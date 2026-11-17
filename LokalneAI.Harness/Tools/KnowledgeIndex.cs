using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Tools
{
    /// <summary>
    /// Wyszukiwarka w notatkach profilu (<c>wiedza\*.md</c>) — narzędzie <c>szukaj_wiedzy</c>.
    /// Prosty BM25 na słowach bez polskich znaków, z ucięciem końcówek (pierwsze 6 liter),
    /// bez embeddingów: działa od ręki i na słabym laptopie.
    /// </summary>
    public sealed class KnowledgeIndex
    {
        private static readonly Regex WordPattern = new Regex(@"[\p{L}\d]+", RegexOptions.Compiled);
        private static readonly HashSet<string> StopWords = new HashSet<string>(
            "i w z na do to jest sie nie a o od po za ze co jak czy ten ta te tego tej dla przy oraz lub albo jesli gdy tak juz".Split(' '));

        private readonly List<Chunk> _chunks = new List<Chunk>();
        private readonly Dictionary<string, int> _documentFrequency = new Dictionary<string, int>();
        private double _averageLength;

        /// <summary>Plik z opisem folderu dla ludzi; pomijany w indeksie.</summary>
        public const string ReadMeFile = "CZYTAJ.md";

        /// <summary>Liczba fragmentów w indeksie; 0 = profil bez wiedzy.</summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>
        /// Indeks wszystkich <c>*.md</c> i <c>*.txt</c> z folderu i podfolderów, bez plików <c>CZYTAJ.md</c> (opisy dla ludzi).
        /// Brak folderu = pusty indeks.
        /// </summary>
        public static KnowledgeIndex FromFolder(string folder)
        {
            var index = new KnowledgeIndex();
            if (Directory.Exists(folder))
            {
                // Z podfolderami — także dokumentacja pobrana automatycznie do wiedza\zrodla\.
                // CZYTAJ.md to opis folderu dla ludzi („co tu wkładać”) — model go nie dostaje.
                var files = Directory.GetFiles(folder, "*.md", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(folder, "*.txt", SearchOption.AllDirectories))
                    .Where(f => !string.Equals(Path.GetFileName(f), ReadMeFile, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    var name = file.Substring(folder.Length).TrimStart('\\', '/');
                    index.Add(name, File.ReadAllText(file, Encoding.UTF8));
                }
            }

            return index;
        }

        /// <summary>Dodaje dokument, dzieląc go na fragmenty po nagłówkach Markdown.</summary>
        public void Add(string source, string markdown)
        {
            var heading = source;
            var buffer = new StringBuilder();
            foreach (var line in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    Flush(source, heading, buffer);
                    heading = line.TrimStart('#', ' ');
                    continue;
                }

                buffer.AppendLine(line);
                if (buffer.Length > 1500)
                {
                    Flush(source, heading, buffer);
                }
            }

            Flush(source, heading, buffer);
            Recalculate();
        }

        /// <summary>
        /// Dokłada wszystkie fragmenty innego indeksu (np. przy łączeniu profili). Źródła dostają przedrostek, np. nazwę profilu
        /// (<c>cde/wiedza.md</c>), żeby w wyniku <c>szukaj_wiedzy</c> było widać, skąd fragment.
        /// </summary>
        public void AddFrom(KnowledgeIndex other, string prefix = null)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return;
            }

            foreach (var chunk in other._chunks)
            {
                _chunks.Add(new Chunk
                {
                    Source = string.IsNullOrEmpty(prefix) ? chunk.Source : prefix + "/" + chunk.Source,
                    Heading = chunk.Heading,
                    Body = chunk.Body,
                    Frequencies = chunk.Frequencies,
                    Length = chunk.Length,
                });
            }

            Recalculate();
        }

        /// <summary>Najlepsze fragmenty dla zapytania (najwyżej <paramref name="top"/>).</summary>
        public IList<string> Search(string query, int top = 3, int maxChars = 900)
        {
            var terms = Terms(query).Distinct().ToList();
            if (terms.Count == 0 || _chunks.Count == 0)
            {
                return new List<string>();
            }

            const double k1 = 1.2, b = 0.75;
            var scored = new List<KeyValuePair<Chunk, double>>();
            foreach (var chunk in _chunks)
            {
                double score = 0;
                foreach (var term in terms)
                {
                    if (!chunk.Frequencies.TryGetValue(term, out var tf))
                    {
                        continue;
                    }

                    var df = _documentFrequency[term];
                    var idf = Math.Log(1 + ((_chunks.Count - df + 0.5) / (df + 0.5)));
                    score += idf * tf * (k1 + 1) / (tf + (k1 * (1 - b + (b * chunk.Length / _averageLength))));
                }

                if (score > 0)
                {
                    scored.Add(new KeyValuePair<Chunk, double>(chunk, score));
                }
            }

            return scored
                .OrderByDescending(s => s.Value)
                .Take(top)
                .Select(s => "[" + s.Key.Source + " › " + s.Key.Heading + "]\n" + Text.Shorten(s.Key.Body.Trim(), maxChars))
                .ToList();
        }

        private static IEnumerable<string> Terms(string text)
        {
            foreach (Match match in WordPattern.Matches(Text.Fold(text)))
            {
                var word = match.Value;
                if (word.Length < 2 || StopWords.Contains(word))
                {
                    continue;
                }

                yield return word.Length > 6 ? word.Substring(0, 6) : word;
            }
        }

        private void Flush(string source, string heading, StringBuilder buffer)
        {
            var body = buffer.ToString();
            buffer.Clear();
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            var terms = Terms(heading + " " + body).ToList();
            var frequencies = new Dictionary<string, int>();
            foreach (var term in terms)
            {
                frequencies.TryGetValue(term, out var count);
                frequencies[term] = count + 1;
            }

            _chunks.Add(new Chunk { Source = source, Heading = heading, Body = body, Frequencies = frequencies, Length = Math.Max(1, terms.Count) });
        }

        private void Recalculate()
        {
            _documentFrequency.Clear();
            foreach (var chunk in _chunks)
            {
                foreach (var term in chunk.Frequencies.Keys)
                {
                    _documentFrequency.TryGetValue(term, out var count);
                    _documentFrequency[term] = count + 1;
                }
            }

            _averageLength = _chunks.Count == 0 ? 1 : _chunks.Average(c => c.Length);
        }

        private sealed class Chunk
        {
            public string Source { get; set; }

            public string Heading { get; set; }

            public string Body { get; set; }

            public Dictionary<string, int> Frequencies { get; set; }

            public int Length { get; set; }
        }
    }
}
