using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Ustawienia wykonawcy dla mostu TeklaBridge (Dominik, Tekla Structures 2019.1).</summary>
    public sealed class TeklaBridgeOptions
    {
        /// <summary>Folder wymiany mostu (tam most szuka plików <c>cmd_*.cs</c>).</summary>
        public string ExchangeFolder { get; set; }

        /// <summary>Folder profilu z szablonami (<c>szablony\*.cs</c>).</summary>
        public string ProfileFolder { get; set; }

        /// <summary>Jak długo czekać na wynik skryptu.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Wartość wstawiana za niepodaną liczbę opcjonalną. Szablony profilu „tekla” sprawdzają <c>double.IsNaN</c>.</summary>
        public string MissingNumberLiteral { get; set; } = "double.NaN";

        /// <summary>Most uznaje się za żywy, gdy <c>bridge_status.txt</c> jest młodszy niż ten czas.</summary>
        public TimeSpan StatusMaxAge { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Wykonuje polecenia przez most TeklaBridge: wypełnia szablon C# argumentami, zapisuje
    /// <c>cmd_lokalneai_*.cs</c> w folderze wymiany i czeka na <c>.result.txt</c> (pierwsza linia OK albo ERROR).
    /// Kod skryptu pochodzi WYŁĄCZNIE ze sprawdzonych szablonów — model podaje tylko argumenty.
    /// </summary>
    public sealed class TeklaBridgeExecutor : IToolExecutor
    {
        private static readonly Regex Placeholder = new Regex(@"\{\{([a-z0-9_]+)\}\}", RegexOptions.Compiled);
        private readonly TeklaBridgeOptions _options;

        public TeklaBridgeExecutor(TeklaBridgeOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Sprawdza <c>bridge_status.txt</c>: null = most działa i ma model; inaczej opis problemu.</summary>
        public string CheckBridge()
        {
            var status = Path.Combine(_options.ExchangeFolder ?? "", "bridge_status.txt");
            if (!File.Exists(status))
            {
                return "Most TeklaBridge nie działa (brak " + status + "). Uruchom Teklę z modelem i TeklaBridge.exe.";
            }

            var age = DateTime.Now - File.GetLastWriteTime(status);
            if (age > _options.StatusMaxAge)
            {
                return "Most TeklaBridge nie odpowiada (status sprzed " + (int)age.TotalSeconds + " s).";
            }

            var text = File.ReadAllText(status);
            return text.IndexOf("CONNECTED=True", StringComparison.OrdinalIgnoreCase) >= 0
                ? null
                : "Most działa, ale nie ma połączenia z modelem Tekli.";
        }

        /// <summary>Wypełnia szablon argumentami (bez wykonywania) — do podglądu i testów.</summary>
        public string Render(ToolCall call)
        {
            if (string.IsNullOrEmpty(call.Definition.Template))
            {
                throw new InvalidOperationException("Polecenie „" + call.Name + "” nie ma szablonu.");
            }

            var path = Path.Combine(_options.ProfileFolder ?? "", call.Definition.Template);
            var template = File.ReadAllText(path, Encoding.UTF8);
            return Fill(template, call);
        }

        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            var problem = CheckBridge();
            if (problem != null)
            {
                return ToolResult.Error("BLAD: " + problem);
            }

            var script = Render(call);
            var name = "cmd_lokalneai_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + call.Name;
            var scriptPath = Path.Combine(_options.ExchangeFolder, name + ".cs");
            var resultPath = Path.Combine(_options.ExchangeFolder, name + ".result.txt");

            // Zapis przez plik tymczasowy z „_” (most go pomija), potem zmiana nazwy — most nie przeczyta połowy pliku.
            var temporary = Path.Combine(_options.ExchangeFolder, "_" + name + ".tmp");
            File.WriteAllText(temporary, script, new UTF8Encoding(false));
            File.Move(temporary, scriptPath);

            var deadline = DateTime.UtcNow + _options.Timeout;
            while (!File.Exists(resultPath))
            {
                if (DateTime.UtcNow > deadline || cancellationToken.IsCancellationRequested)
                {
                    // Skrypt jeszcze nie podjęty — zabieramy go, żeby most nie wykonał go później (podwójny słup!).
                    if (TryWithdraw(scriptPath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return ToolResult.Error("BLAD: most nie podjął skryptu w ciągu " + (int)_options.Timeout.TotalSeconds + " s — nic nie wykonano.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return ToolResult.Error("BLAD: most podjął skrypt, ale nie zwrócił wyniku. Stan modelu nieznany — sprawdź model w Tekli i NIE ponawiaj polecenia.");
                }

                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Pętla sprawdzi stan skryptu i dopiero wtedy przerwie.
                }
            }

            string text = null;
            for (var attempt = 0; attempt < 20 && text == null; attempt++)
            {
                try
                {
                    text = File.ReadAllText(resultPath, Encoding.UTF8);
                }
                catch (IOException)
                {
                    // Most jeszcze pisze plik.
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }

            text = (text ?? "").Trim();
            var firstLine = text.Split('\n').FirstOrDefault()?.Trim() ?? "";
            var rest = text.Length > firstLine.Length ? text.Substring(firstLine.Length).Trim() : "";
            // Most pisze OK, gdy skrypt nie rzucił wyjątku; szablony zgłaszają własne błędy tekstem „BLAD …”.
            var scriptFailed = rest.StartsWith("BLAD", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("BŁĄD", StringComparison.OrdinalIgnoreCase);
            return firstLine.Equals("OK", StringComparison.OrdinalIgnoreCase) && !scriptFailed
                ? ToolResult.Ok(rest.Length == 0 ? "OK" : rest)
                : ToolResult.Error(rest.Length == 0 ? text : rest);
        }

        internal string Fill(string template, ToolCall call)
        {
            var properties = call.Definition.Parameters.TryGetProperty("properties", out var p) ? p : default;
            return Placeholder.Replace(template, match =>
            {
                var name = match.Groups[1].Value;
                if (call.Arguments.TryGetProperty(name, out var value))
                {
                    switch (value.ValueKind)
                    {
                        case JsonValueKind.Number:
                            return value.GetDouble().ToString("R", CultureInfo.InvariantCulture);
                        case JsonValueKind.True:
                            return "true";
                        case JsonValueKind.False:
                            return "false";
                        case JsonValueKind.String:
                            return EscapeCSharp(value.GetString());
                        default:
                            return EscapeCSharp(value.GetRawText());
                    }
                }

                var type = properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(name, out var schema)
                    && schema.TryGetProperty("type", out var t) ? t.GetString() : "string";
                switch (type)
                {
                    case "number":
                    case "integer":
                        return _options.MissingNumberLiteral;
                    case "boolean":
                        return "false";
                    default:
                        return "";
                }
            });
        }

        private static bool TryWithdraw(string scriptPath)
        {
            try
            {
                if (!File.Exists(scriptPath))
                {
                    return false;
                }

                File.Delete(scriptPath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string EscapeCSharp(string text)
        {
            var builder = new StringBuilder();
            foreach (var c in text ?? "")
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\u0085':
                    case '\u2028':
                    case '\u2029':
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
