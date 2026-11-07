using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Validation;
using Npgsql;

namespace LokalneAI.Harness.Postgres
{
    /// <summary>Ustawienia wykonawcy SQL.</summary>
    public sealed class PostgresReadOnlyOptions
    {
        /// <summary>Np. <c>Host=10.0.0.214;Port=5441;Database=cde;Username=…;Password=…</c>. Najlepiej rola tylko do odczytu.</summary>
        public string ConnectionString { get; set; }

        /// <summary>Ile wierszy wyniku najwyżej trafia do modelu (resztę zastępuje informacja o ucięciu).</summary>
        public int MaxRows { get; set; } = 40;

        /// <summary>Limit czasu jednego zapytania w bazie.</summary>
        public TimeSpan StatementTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>Nazwa argumentu z zapytaniem.</summary>
        public string SqlArgument { get; set; } = "sql";
    }

    /// <summary>Wynik zapytania: kolumny i wiersze.</summary>
    public sealed class QueryResult
    {
        /// <summary>Najdłuższa komórka tekstowa w wyniku dla modelu (dłuższe są ucinane, żeby nie zapchać kontekstu).</summary>
        public const int MaxCellLength = 300;

        /// <summary>Nazwy kolumn.</summary>
        public List<string> Columns { get; } = new List<string>();

        /// <summary>Wiersze z wartościami z Npgsql.</summary>
        public List<object[]> Rows { get; } = new List<object[]>();

        /// <summary>Wynik ucięty do <see cref="PostgresReadOnlyOptions.MaxRows"/> wierszy.</summary>
        public bool Truncated { get; set; }

        /// <summary>Wszystkie wartości liczbowe wyniku (do porównań w testach).</summary>
        public IEnumerable<decimal> Numbers()
        {
            foreach (var row in Rows)
            {
                foreach (var value in row)
                {
                    switch (value)
                    {
                        case decimal d: yield return d; break;
                        case double f: yield return (decimal)f; break;
                        case float g: yield return (decimal)g; break;
                        case long l: yield return l; break;
                        case int i: yield return i; break;
                        case short s: yield return s; break;
                    }
                }
            }
        }

        /// <summary>Tekst dla modelu: nagłówek, wiersze rozdzielone „ | ”, liczba wierszy.</summary>
        public string ToText()
        {
            var builder = new StringBuilder();
            builder.Append("kolumny: ").Append(string.Join(" | ", Columns));
            foreach (var row in Rows)
            {
                builder.AppendLine().Append(string.Join(" | ", row.Select(Format)));
            }

            builder.AppendLine().Append(Truncated
                ? "(pokazano pierwsze " + Rows.Count + " wierszy — jest ich więcej; zawęź zapytanie albo policz w SQL)"
                : "(" + Rows.Count + " " + (Rows.Count == 1 ? "wiersz" : "wierszy") + ")");
            return builder.ToString();
        }

        /// <summary>Wartość jako tekst dla modelu: liczby z kropką, daty ISO, <c>NULL</c>, tak/nie; długie teksty ucięte.</summary>
        public static string Format(object value)
        {
            switch (value)
            {
                case null:
                case DBNull _:
                    return "NULL";
                case decimal d:
                    return d.ToString(CultureInfo.InvariantCulture);
                case double f:
                    return f.ToString("0.######", CultureInfo.InvariantCulture);
                case float g:
                    return g.ToString("0.######", CultureInfo.InvariantCulture);
                case DateTime t:
                    return t.TimeOfDay == TimeSpan.Zero ? t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                case DateTimeOffset o:
                    return o.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                case bool b:
                    return b ? "tak" : "nie";
                case string s:
                    var flat = s.Replace("\r", " ").Replace("\n", " ");
                    return flat.Length <= MaxCellLength ? flat : flat.Substring(0, MaxCellLength) + "…";
                case byte[] bytes:
                    return "<dane binarne: " + bytes.Length + " B>";
                case IEnumerable e:
                    return "{" + string.Join(", ", e.Cast<object>().Select(Format)) + "}";
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Wykonuje SQL od modelu w transakcji READ ONLY z limitem czasu i zawsze ją wycofuje.
    /// Przed bazą zapytanie przechodzi jeszcze raz przez <see cref="SqlGuard"/>.
    /// Druga warstwa ochrony to rola bazy — dla asystenta powinna być tylko do odczytu.
    /// </summary>
    public sealed class PostgresReadOnlyExecutor : IToolExecutor
    {
        private readonly PostgresReadOnlyOptions _options;

        /// <summary>Tworzy wykonawcę. Każda transakcja na tym połączeniu jest tylko do odczytu i ma limit czasu.</summary>
        public PostgresReadOnlyExecutor(PostgresReadOnlyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                throw new ArgumentException("Brak danych połączenia z bazą.", nameof(options));
            }

            // Trzecia warstwa: KAŻDA transakcja na tym połączeniu jest domyślnie tylko do odczytu — także po
            // ewentualnym przemyconym COMMIT. Do tego limit czasu na poziomie sesji.
            var builder = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
            var extra = "-c default_transaction_read_only=on -c statement_timeout="
                + ((int)_options.StatementTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
            builder.Options = string.IsNullOrWhiteSpace(builder.Options) ? extra : builder.Options + " " + extra;
            _connectionString = builder.ConnectionString;
        }

        private readonly string _connectionString;

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            var sql = call.GetString(_options.SqlArgument);
            var problem = SqlGuard.Check(sql);
            if (problem != null)
            {
                return ToolResult.Error(problem);
            }

            try
            {
                var result = await QueryAsync(sql, _options.MaxRows, cancellationToken).ConfigureAwait(false);
                return ToolResult.Ok(result.ToText());
            }
            catch (PostgresException e)
            {
                var text = new StringBuilder("Błąd bazy: ").Append(e.MessageText);
                if (!string.IsNullOrEmpty(e.Hint))
                {
                    text.Append(" Wskazówka: ").Append(e.Hint);
                }

                if (e.Position > 0 && e.Position <= sql.Length)
                {
                    text.Append(" (przy: „").Append(Text(sql, e.Position - 1)).Append("”)");
                }

                return ToolResult.Error(text.ToString());
            }
            catch (NpgsqlException e)
            {
                return ToolResult.Error("Brak połączenia z bazą: " + e.Message);
            }
        }

        /// <summary>
        /// Zapytanie tylko do odczytu; najwyżej <paramref name="maxRows"/> wierszy. Przy zerwanym połączeniu
        /// (zimny start bazy CDE, połączenie z puli zamknięte przez serwer) jedno ponowienie — bezpieczne, bo nic nie zapisujemy.
        /// </summary>
        public async Task<QueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
        {
            try
            {
                return await QueryOnceAsync(sql, maxRows, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (IsTransient(e) && !cancellationToken.IsCancellationRequested)
            {
                using (var stale = new NpgsqlConnection(_connectionString))
                {
                    NpgsqlConnection.ClearPool(stale);
                }

                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                return await QueryOnceAsync(sql, maxRows, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool IsTransient(Exception e)
            => (e is NpgsqlException && !(e is PostgresException)) || e is System.IO.IOException || e is TimeoutException;

        private async Task<QueryResult> QueryOnceAsync(string sql, int maxRows, CancellationToken cancellationToken)
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                {
                    using (var setup = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = "
                        + ((int)_options.StatementTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture), connection, transaction))
                    {
                        await setup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    var result = new QueryResult();
                    using (var command = new NpgsqlCommand(sql, connection, transaction))
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            result.Columns.Add(reader.GetName(i));
                        }

                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            if (result.Rows.Count >= maxRows)
                            {
                                result.Truncated = true;
                                break;
                            }

                            var row = new object[reader.FieldCount];
                            reader.GetValues(row);
                            result.Rows.Add(row);
                        }
                    }

                    transaction.Rollback();
                    return result;
                }
            }
        }

        private static string Text(string sql, int position)
        {
            var start = Math.Max(0, position - 20);
            var length = Math.Min(sql.Length - start, 50);
            return sql.Substring(start, length).Replace("\n", " ");
        }
    }
}
