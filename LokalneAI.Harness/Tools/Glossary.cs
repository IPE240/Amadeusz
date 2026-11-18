using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Jedna para terminów glosariusza, np. <c>expansion joint – kompensator</c>.</summary>
    public sealed class GlossaryEntry
    {
        /// <summary>Tworzy wpis.</summary>
        public GlossaryEntry(string left, string right, string source)
        {
            Left = left ?? "";
            Right = right ?? "";
            Source = source ?? "";
        }

        /// <summary>Termin przed separatorem (w glosariuszu biura: angielski).</summary>
        public string Left { get; }

        /// <summary>Termin po separatorze, z uwagami w nawiasach (np. „(niepotwierdzone)”).</summary>
        public string Right { get; }

        /// <summary>Plik, z którego pochodzi wpis.</summary>
        public string Source { get; }

        /// <summary>„expansion joint – kompensator”.</summary>
        public override string ToString() => Left + " – " + Right;
    }

    /// <summary>
    /// Glosariusz: pary terminów w pliku tekstowym, jedna para w wierszu (<c>expansion joint – kompensator</c>; separator
    /// „ – ”, „ — ”, „ - ”, tabulator albo „ = ”). Wiersze bez separatora (np. „Dokumentacja i organizacja”) to nagłówki grup — pomijane.
    /// W odróżnieniu od <see cref="KnowledgeIndex"/> model nie musi o nic pytać: <see cref="BuildHint"/> sam znajduje w tekście
    /// terminy z glosariusza (w dowolnej stronie, także odmienione: „kompensatora”, „kompensatorem”) i podaje modelowi ich odpowiedniki.
    /// </summary>
    public sealed class Glossary
    {
        private static readonly Regex WordPattern = new Regex(@"[\p{L}\d]+", RegexOptions.Compiled);
        private static readonly Regex Separator = new Regex(@"\s+[–—=-]\s+|\t+", RegexOptions.Compiled);
        private static readonly Regex Parentheses = new Regex(@"\([^)]*\)", RegexOptions.Compiled);
        private static readonly HashSet<string> StopWords = new HashSet<string>(
            "i w z na do od po za ze dla przy oraz lub albo a o the of and or to in on for from with by an".Split(' '));

        private readonly List<GlossaryEntry> _entries = new List<GlossaryEntry>();
        private readonly List<string[]> _leftWords = new List<string[]>();
        private readonly List<string[]> _rightWords = new List<string[]>();

        /// <summary>Wpisy w kolejności wczytania.</summary>
        public IReadOnlyList<GlossaryEntry> Entries => _entries;

        /// <summary>Liczba wpisów; 0 = brak glosariusza.</summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Czy tekst wygląda na glosariusz: co najmniej 3 wiersze z separatorem i to co najmniej połowa niepustych wierszy.
        /// Notatki Markdown (akapity, tabele z „|”) — nie.
        /// </summary>
        public static bool LooksLikeGlossary(string text)
        {
            var lines = Lines(text).ToList();
            var pairs = lines.Count(l => !l.Contains("|") && Split(l) != null);
            return pairs >= 3 && pairs * 2 >= lines.Count;
        }

        /// <summary>Dokłada wpisy z tekstu glosariusza (wiersze bez separatora są pomijane).</summary>
        public void Add(string text, string source)
        {
            foreach (var line in Lines(text))
            {
                var pair = Split(line);
                if (pair != null)
                {
                    AddEntry(new GlossaryEntry(pair[0], pair[1], source));
                }
            }
        }

        /// <summary>Dokłada plik, jeśli wygląda na glosariusz (<see cref="LooksLikeGlossary"/>); zwraca, czy dołożył.</summary>
        public bool AddFile(string path, string source = null)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (!LooksLikeGlossary(text))
            {
                return false;
            }

            Add(text, source ?? Path.GetFileName(path));
            return true;
        }

        /// <summary>
        /// Glosariusze z folderu wiedzy profilu: pliki <c>*.txt</c>, <c>*.tsv</c>, <c>*.csv</c> (z podfolderami), które wyglądają
        /// na glosariusz. Brak folderu = pusty glosariusz.
        /// </summary>
        public static Glossary FromFolder(string folder)
        {
            var glossary = new Glossary();
            if (!Directory.Exists(folder))
            {
                return glossary;
            }

            foreach (var file in new[] { "*.txt", "*.tsv", "*.csv" }
                .SelectMany(p => Directory.GetFiles(folder, p, SearchOption.AllDirectories))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                glossary.AddFile(file, file.Substring(folder.Length).TrimStart('\\', '/'));
            }

            return glossary;
        }

        /// <summary>Dokłada wpisy innego glosariusza (np. przy łączeniu profili); te same pary nie powtarzają się.</summary>
        public void AddFrom(Glossary other)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return;
            }

            foreach (var entry in other._entries)
            {
                AddEntry(entry);
            }
        }

        /// <summary>
        /// Wpisy, których termin (z którejkolwiek strony) występuje w tekście — wszystkie słowa terminu, także odmienione.
        /// Najpierw dłuższe terminy („flue gas duct” przed „duct”); najwyżej <paramref name="max"/>.
        /// </summary>
        public IList<GlossaryEntry> Find(string text, int max = 40)
        {
            var found = new List<KeyValuePair<GlossaryEntry, int>>();
            if (string.IsNullOrWhiteSpace(text) || _entries.Count == 0)
            {
                return new List<GlossaryEntry>();
            }

            // Słowa tekstu pogrupowane po pierwszych 3 literach — szybkie sprawdzanie także przy długim dokumencie i dużym słowniku.
            var byStart = new Dictionary<string, List<string>>();
            foreach (var word in Words(text).Distinct())
            {
                var key = word.Length >= 3 ? word.Substring(0, 3) : word;
                if (!byStart.TryGetValue(key, out var list))
                {
                    byStart[key] = list = new List<string>();
                }

                list.Add(word);
            }

            for (var i = 0; i < _entries.Count; i++)
            {
                var left = AllPresent(_leftWords[i], byStart);
                var right = AllPresent(_rightWords[i], byStart);
                if (left || right)
                {
                    found.Add(new KeyValuePair<GlossaryEntry, int>(_entries[i], Math.Max(left ? _leftWords[i].Length : 0, right ? _rightWords[i].Length : 0)));
                }
            }

            return found.OrderByDescending(f => f.Value).Take(max).Select(f => f.Key).ToList();
        }

        /// <summary>
        /// Blok dla modelu z terminami glosariusza znalezionymi w tekście; pusty, gdy nic nie pasuje.
        /// Biblioteka dokleja go do polecenia użytkownika i do wyników narzędzi gospodarza (np. tekst z dokumentu Word).
        /// </summary>
        public string BuildHint(string text, int max = 40)
        {
            var entries = Find(text, max);
            if (entries.Count == 0)
            {
                return "";
            }

            var hint = new StringBuilder();
            hint.AppendLine("[Glosariusz biura — terminy z tego tekstu. Używaj tych odpowiedników (mają pierwszeństwo przed słownikiem i Twoją pamięcią);")
                .AppendLine("wpis z uwagą „niepotwierdzone” albo z dwiema wersjami — użyj, ale gdy użytkownik prosi o uwagi, wspomnij o nim.]");
            foreach (var entry in entries)
            {
                hint.AppendLine("- " + entry);
            }

            return hint.ToString().TrimEnd();
        }

        private void AddEntry(GlossaryEntry entry)
        {
            if (_entries.Any(e => string.Equals(e.Left, entry.Left, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Right, entry.Right, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var left = TermWords(entry.Left);
            var right = TermWords(entry.Right);
            if (left.Length == 0 && right.Length == 0)
            {
                return;
            }

            _entries.Add(entry);
            _leftWords.Add(left);
            _rightWords.Add(right);
        }

        /// <summary>Słowa terminu do szukania: bez uwag w nawiasach i bez wariantów po przecinku („śruba rzymska, nakrętka rzymska” → pierwszy).</summary>
        private static string[] TermWords(string term)
        {
            var main = Parentheses.Replace(term ?? "", " ").Split(',', ';')[0];
            return Words(main).Where(w => !StopWords.Contains(w)).ToArray();
        }

        private static bool AllPresent(string[] termWords, Dictionary<string, List<string>> byStart)
        {
            if (termWords.Length == 0)
            {
                return false;
            }

            foreach (var term in termWords)
            {
                var key = term.Length >= 3 ? term.Substring(0, 3) : term;
                if (!byStart.TryGetValue(key, out var candidates) || !candidates.Any(w => Matches(term, w)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Słowo tekstu pasuje do słowa terminu: krótkie (do 3 liter) dokładnie albo z angielskim „-s”/„-es”; dłuższe — wspólny początek
        /// min(długość−1, 5) liter, ale nie krótszy niż 3 (kanał/kanału, klapa/klapy, kompensator/kompensatora).
        /// </summary>
        internal static bool Matches(string term, string word)
        {
            if (term.Length <= 3)
            {
                return word == term || word == term + "s" || word == term + "es"; // rod/rods, tee/tees; „teeth” — nie
            }

            var need = Math.Max(3, Math.Min(term.Length - 1, 5));
            if (word.Length < need)
            {
                return false;
            }

            return string.CompareOrdinal(term, 0, word, 0, need) == 0;
        }

        private static IEnumerable<string> Words(string text)
        {
            foreach (Match match in WordPattern.Matches(Text.Fold(text ?? "")))
            {
                if (match.Value.Length >= 2)
                {
                    yield return match.Value;
                }
            }
        }

        private static IEnumerable<string> Lines(string text)
            => (text ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal));

        private static string[] Split(string line)
        {
            var match = Separator.Match(line);
            if (!match.Success || match.Index == 0)
            {
                return null;
            }

            var left = line.Substring(0, match.Index).Trim();
            var right = line.Substring(match.Index + match.Length).Trim();
            return left.Length == 0 || right.Length == 0 ? null : new[] { left, right };
        }
    }
}
