using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Tools
{
    /// <summary>
    /// Małe modele piszą w JSON-ie „\\n” (ukośnik i n) tam, gdzie chodzi o nową linię, np. w treści skryptu albo w poleceniu
    /// PowerShella (w którym „\n” w ogóle nie jest nową linią). Tu jest rozpoznawanie takich miejsc i naprawa treści pliku.
    /// Ścieżki typu <c>C:\nowy</c> nie są ruszane.
    /// </summary>
    internal static class EscapedNewlines
    {
        private static readonly Regex PathStart = new Regex(@"^([A-Za-z]:\\|\\\\|\.{1,2}\\|%\w+%\\|\$env:\w+\\)", RegexOptions.CultureInvariant);

        private static readonly Regex RelativeFile = new Regex(@"^[\w\-. ]+(\\[\w\-. ]+)+\.[A-Za-z0-9]{1,5}$", RegexOptions.CultureInvariant);

        private static readonly Regex WritesFile = new Regex(
            @"(?i)(^|[\s;|({])(Set-Content|Add-Content|Out-File|Tee-Object|New-Item|sc|ac)\b|WriteAll(Text|Lines)|(?<![\d*])>>?(?!&)",
            RegexOptions.CultureInvariant);

        private static readonly Regex UsesRegex = new Regex(
            @"(?i)-[ci]?(replace|split|match|notmatch)\b|\[regex\]|Select-String|-Pattern\b", RegexOptions.CultureInvariant);

        /// <summary>
        /// Treść bez ani jednej prawdziwej nowej linii, za to z „\n” poza ścieżkami → „\n” (i „\r\n”) zamienione na nowe linie.
        /// Treść z prawdziwymi nowymi liniami zostaje bez zmian (wtedy „\n” to zamierzony znak, np. w napisie w kodzie).
        /// </summary>
        public static string Fix(string text, out int replaced)
        {
            replaced = 0;
            if (string.IsNullOrEmpty(text) || text.IndexOf('\n') >= 0 || text.IndexOf("\\n", System.StringComparison.Ordinal) < 0)
            {
                return text;
            }

            var result = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (IsEscapedNewline(text, i))
                {
                    result.Append('\n');
                    replaced++;
                    i++;
                    continue;
                }

                if (i + 3 < text.Length && text[i] == '\\' && text[i + 1] == 'r' && IsEscapedNewline(text, i + 2) && OddBackslashes(text, i))
                {
                    result.Append('\n');
                    replaced++;
                    i += 3;
                    continue;
                }

                result.Append(text[i]);
            }

            return result.ToString();
        }

        /// <summary>
        /// Opis problemu, gdy polecenie PowerShella zapisuje plik, a nowe linie są w nim jako „\n” (PowerShell zapisałby znaki \ i n);
        /// null, gdy polecenie jest w porządku. Polecenia z wyrażeniami regularnymi (<c>-replace '\n'</c>) i z prawdziwymi nowymi
        /// liniami (here-string) przechodzą.
        /// </summary>
        public static string PowerShellProblem(string script)
        {
            if (string.IsNullOrEmpty(script) || script.IndexOf('\n') >= 0 || !WritesFile.IsMatch(script) || UsesRegex.IsMatch(script))
            {
                return null;
            }

            for (var i = 0; i < script.Length; i++)
            {
                if (IsEscapedNewline(script, i))
                {
                    return "BŁĄD: w PowerShellu „\\n” nie jest nową linią — do pliku trafiłyby znaki \\ i n, a skrypt by nie działał. "
                        + "Plik z kilkoma liniami (skrypt, tekst) zapisz narzędziem zapisz_plik: sciezka i cała tresc z nowymi liniami.";
                }
            }

            return null;
        }

        /// <summary>„\n” na pozycji <paramref name="i"/>, które jest znakiem ucieczki (nie „\\n”) i nie leży w ścieżce.</summary>
        private static bool IsEscapedNewline(string text, int i)
            => i + 1 < text.Length && text[i] == '\\' && text[i + 1] == 'n' && OddBackslashes(text, i) && !InPath(text, i);

        /// <summary>Ukośnik na pozycji <paramref name="i"/> zaczyna znak ucieczki: przed nim parzysta liczba ukośników.</summary>
        private static bool OddBackslashes(string text, int i)
        {
            var count = 0;
            for (var j = i; j >= 0 && text[j] == '\\'; j--)
            {
                count++;
            }

            return count % 2 == 1;
        }

        /// <summary>Czy „\n” na pozycji <paramref name="i"/> jest częścią ścieżki (<c>C:\ODS\nowy</c>, <c>Raporty\notatki.txt</c>).</summary>
        private static bool InPath(string text, int i)
        {
            var start = i;
            while (start > 0 && !IsWordBreak(text[start - 1]))
            {
                start--;
            }

            var end = i + 2;
            while (end < text.Length && !IsWordBreak(text[end]))
            {
                end++;
            }

            var word = text.Substring(start, end - start);
            if (PathStart.IsMatch(word) || RelativeFile.IsMatch(word))
            {
                return true;
            }

            var quoted = QuotedAround(text, i);
            if (quoted == null)
            {
                return false;
            }

            // 'C:\Program Files\nowy' (ze spacją) albo 'Projekty\nowy' (jedno słowo ze znaków ścieżki).
            return PathStart.IsMatch(quoted) && quoted.IndexOfAny(new[] { '(', ')', '=', ';', '#', '{', '}' }) < 0
                || QuotedPath.IsMatch(quoted);
        }

        private static readonly Regex QuotedPath = new Regex(@"^[\w\-.:$%]+(\\[\w\-.:$%]+)+$", RegexOptions.CultureInvariant);

        /// <summary>Treść napisu w cudzysłowie (' albo "), w którym leży pozycja <paramref name="i"/>; null, gdy poza cudzysłowem.</summary>
        private static string QuotedAround(string text, int i)
        {
            var open = -1;
            for (var j = 0; j < i; j++)
            {
                var c = text[j];
                if (open >= 0)
                {
                    if (c == text[open])
                    {
                        open = -1;
                    }
                }
                else if (c == '\'' || c == '"')
                {
                    open = j;
                }
            }

            if (open < 0)
            {
                return null;
            }

            var close = text.IndexOf(text[open], i);
            return text.Substring(open + 1, (close < 0 ? text.Length : close) - open - 1);
        }

        private static bool IsWordBreak(char c)
            => char.IsWhiteSpace(c) || c == '\'' || c == '"' || c == '`' || c == '(' || c == ')' || c == ',' || c == ';' || c == '='
               || c == '|' || c == '<' || c == '>';
    }
}
