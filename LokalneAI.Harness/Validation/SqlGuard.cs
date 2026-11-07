using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Validation
{
    /// <summary>
    /// Pierwsza zapora dla SQL od modelu: jedno zapytanie, tylko SELECT/WITH, bez słów zmieniających dane
    /// i bez funkcji niebezpiecznych. To NIE zastępuje wykonania w transakcji READ ONLY na roli tylko
    /// do odczytu — to kolejne, obowiązkowe warstwy po stronie wykonawcy.
    /// </summary>
    public static class SqlGuard
    {
        // Słowa, które w jednym zapytaniu SELECT mogą zmieniać dane albo kończyć transakcję tylko do odczytu.
        private static readonly string[] ForbiddenWords =
        {
            "insert", "update", "delete", "merge", "upsert", "into", "drop", "alter", "create", "truncate", "grant", "revoke",
            "copy", "call", "do", "execute", "prepare", "listen", "notify", "vacuum", "lock", "commit", "rollback", "begin",
            "savepoint", "set", "reset", "discard",
        };

        private static readonly string[] ForbiddenFunctions =
        {
            "pg_terminate_backend", "pg_cancel_backend", "pg_reload_conf", "pg_stat_file", "set_config", "nextval", "setval",
            "ts_stat", "pg_logdir_ls", "pg_rotate_logfile", "pg_switch_wal", "pg_create_restore_point", "txid_current",
            "pg_current_xact_id", "pg_notify",
        };

        // Rodziny funkcji: sen, blokady, pliki serwera, duże obiekty, dblink, zrzuty tabel do XML.
        private static readonly string[] ForbiddenFunctionPrefixes =
        {
            "pg_sleep", "pg_advisory", "pg_try_advisory", "pg_read", "pg_ls_", "lo_", "dblink", "pg_file", "pg_promote",
        };

        private static readonly string[] ForbiddenFunctionInfixes = { "_to_xml" };

        private static readonly Regex Word = new Regex(@"[a-z_][a-z0-9_$]*", RegexOptions.Compiled);
        private static readonly Regex SpacesAroundDot = new Regex(@"\s*\.\s*", RegexOptions.Compiled);

        /// <summary>Zwraca null, gdy zapytanie przechodzi, albo opis problemu dla modelu.</summary>
        public static string Check(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return "Puste zapytanie.";
            }

            string stripped;
            try
            {
                stripped = StripLiteralsAndComments(sql);
            }
            catch (FormatException e)
            {
                return e.Message;
            }

            var body = stripped.Trim();
            while (body.EndsWith(";", StringComparison.Ordinal))
            {
                body = body.Substring(0, body.Length - 1).TrimEnd();
            }

            if (body.IndexOf(';') >= 0)
            {
                return "Dozwolone jest jedno zapytanie (bez średników w środku).";
            }

            var lower = body.ToLowerInvariant().TrimStart('(', ' ', '\t', '\r', '\n');
            if (!(lower.StartsWith("select", StringComparison.Ordinal) || lower.StartsWith("with", StringComparison.Ordinal)))
            {
                return "Dozwolone są tylko zapytania SELECT (albo WITH … SELECT).";
            }

            var words = Word.Matches(body.ToLowerInvariant()).Cast<Match>().Select(m => m.Value).ToList();
            var bad = words.FirstOrDefault(w => ForbiddenWords.Contains(w));
            if (bad != null)
            {
                return "Zapytanie zawiera niedozwolone słowo „" + bad.ToUpperInvariant() + "”. Wolno tylko czytać dane.";
            }

            var function = words.FirstOrDefault(IsForbiddenFunction);
            if (function != null)
            {
                return "Funkcja „" + function + "” jest niedozwolona.";
            }

            for (var i = 0; i + 1 < words.Count; i++)
            {
                if (words[i] == "for" && (words[i + 1] == "update" || words[i + 1] == "share" || words[i + 1] == "no" || words[i + 1] == "key"))
                {
                    return "Blokady (FOR UPDATE/SHARE) są niedozwolone.";
                }
            }

            return null;
        }

        /// <summary>Tekst do sprawdzania relacji: bez literałów i komentarzy, bez cudzysłowów, kropki bez spacji, małe litery.</summary>
        public static string Normalize(string sql)
            => SpacesAroundDot.Replace(StripLiteralsAndComments(sql), ".").ToLowerInvariant();

        private static bool IsForbiddenFunction(string word)
            => ForbiddenFunctions.Contains(word)
               || ForbiddenFunctionPrefixes.Any(p => word.StartsWith(p, StringComparison.Ordinal))
               || ForbiddenFunctionInfixes.Any(p => word.IndexOf(p, StringComparison.Ordinal) >= 0);

        /// <summary>
        /// Zastępuje teksty w apostrofach i komentarze spacjami; identyfikatory w cudzysłowach zostawia bez cudzysłowów
        /// (żeby czarne listy działały także na <c>"pg_read_file"</c> czy <c>"timesheet"."entry"</c>).
        /// Literały <c>E'…'</c> (z ucieczkami przez \) są odrzucane — tak najprościej nie dać się oszukać.
        /// </summary>
        /// <param name="sql">Zapytanie.</param>
        /// <param name="keepLiterals">True = zostaw literały (do sprawdzania reguł typu „person_id = '{{ja}}'”), usuń tylko komentarze.</param>
        internal static string StripLiteralsAndComments(string sql, bool keepLiterals = false)
        {
            var output = new StringBuilder(sql.Length);
            var i = 0;
            while (i < sql.Length)
            {
                var c = sql[i];
                var previous = i > 0 ? sql[i - 1] : ' ';
                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    // Komentarz do końca linii — PostgreSQL kończy go na \n albo \r.
                    while (i < sql.Length && sql[i] != '\n' && sql[i] != '\r')
                    {
                        i++;
                    }

                    output.Append(' ');
                }
                else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    // Komentarze blokowe w PostgreSQL mogą być zagnieżdżone.
                    var depth = 1;
                    i += 2;
                    while (depth > 0)
                    {
                        if (i + 1 >= sql.Length)
                        {
                            throw new FormatException("Niezamknięty komentarz /* … */.");
                        }

                        if (sql[i] == '/' && sql[i + 1] == '*')
                        {
                            depth++;
                            i += 2;
                        }
                        else if (sql[i] == '*' && sql[i + 1] == '/')
                        {
                            depth--;
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }
                    }

                    output.Append(' ');
                }
                else if (c == '\'')
                {
                    if ((previous == 'e' || previous == 'E') && (i < 2 || !IsIdentifierChar(sql[i - 2])))
                    {
                        throw new FormatException("Literały E'…' (z ucieczkami \\) są niedozwolone — użyj zwykłego '…'.");
                    }

                    var j = i + 1;
                    while (true)
                    {
                        if (j >= sql.Length)
                        {
                            throw new FormatException("Niezamknięty tekst w apostrofach.");
                        }

                        if (sql[j] == '\'')
                        {
                            if (j + 1 < sql.Length && sql[j + 1] == '\'')
                            {
                                j += 2;
                                continue;
                            }

                            break;
                        }

                        j++;
                    }

                    output.Append(keepLiterals ? sql.Substring(i, j - i + 1) : " 'x' ");
                    i = j + 1;
                }
                else if (c == '"')
                {
                    var j = i + 1;
                    var name = new StringBuilder();
                    while (true)
                    {
                        if (j >= sql.Length)
                        {
                            throw new FormatException("Niezamknięty identyfikator w cudzysłowie.");
                        }

                        if (sql[j] == '"')
                        {
                            if (j + 1 < sql.Length && sql[j + 1] == '"')
                            {
                                name.Append('"');
                                j += 2;
                                continue;
                            }

                            break;
                        }

                        name.Append(sql[j]);
                        j++;
                    }

                    output.Append(keepLiterals ? sql.Substring(i, j - i + 1) : name.ToString());
                    i = j + 1;
                }
                else if (c == '$' && !IsIdentifierChar(previous) && i + 1 < sql.Length && (sql[i + 1] == '$' || char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
                {
                    var tagEnd = sql.IndexOf('$', i + 1);
                    if (tagEnd < 0)
                    {
                        output.Append(c);
                        i++;
                        continue;
                    }

                    var tag = sql.Substring(i, tagEnd - i + 1);
                    var close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        throw new FormatException("Niezamknięty tekst w znacznikach $$.");
                    }

                    output.Append(keepLiterals ? sql.Substring(i, close + tag.Length - i) : " 'x' ");
                    i = close + tag.Length;
                }
                else
                {
                    output.Append(c);
                    i++;
                }
            }

            return output.ToString();
        }

        private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
    }
}
