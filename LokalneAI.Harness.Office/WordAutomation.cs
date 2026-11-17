using System;
using System.Collections.Generic;
using System.Text;

namespace LokalneAI.Harness.Office
{
    /// <summary>Operacje na dokumentach Worda (wątek STA).</summary>
    internal static class WordAutomation
    {
        /// <summary>Wbudowane style Worda (niezależne od języka Office’a): nazwa z profilu → stała <c>WdBuiltinStyle</c>.</summary>
        private static readonly Dictionary<string, int> Styles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["zwykly"] = -1,        // wdStyleNormal
            ["naglowek1"] = -2,     // wdStyleHeading1
            ["naglowek2"] = -3,     // wdStyleHeading2
            ["naglowek3"] = -4,     // wdStyleHeading3
            ["tytul"] = -63,        // wdStyleTitle
            ["lista"] = -49,        // wdStyleListBullet
            ["lista_numerowana"] = -50, // wdStyleListNumber
        };

        /// <summary>Nazwy stylów przyjmowane w <c>styl</c>.</summary>
        public static IEnumerable<string> StyleNames => Styles.Keys;

        /// <summary>
        /// Tekst dokumentu z numerami akapitów. Numeracja zgadza się z kolekcją <c>Paragraphs</c> (komórki tabel i końce wierszy
        /// liczą się jako akapity), więc model może wskazać akapit w <c>word_dopisz</c>. Jeden odczyt <c>Content.Text</c> zamiast
        /// wywołania na akapit — dokument z tysiącem akapitów czyta się w ułamku sekundy.
        /// </summary>
        public static string Read(OfficeHost host, string path, int from, int count, int maxChars)
        {
            host.Open(path, readOnly: true, visible: false);
            var content = host.Track(Com.Get(host.Document, "Content"));
            var text = Com.Text(content, "Text");
            var paragraphs = text.Split('\r');
            var total = paragraphs.Length;
            if (total > 0 && paragraphs[total - 1].Length == 0)
            {
                total--; // za ostatnim znakiem akapitu nic nie ma
            }

            var result = new StringBuilder("OK ").Append(path).Append(" (akapitów: ").Append(total);
            var shown = 0;
            var body = new StringBuilder();
            var last = Math.Min(total, from + count - 1);
            for (var i = from; i <= last; i++)
            {
                var paragraph = paragraphs[i - 1].Replace("\a", " | ").Replace("\v", " ").Trim();
                if (paragraph.Length == 0 || paragraph == "|")
                {
                    continue; // pusty akapit albo sam koniec wiersza tabeli
                }

                body.Append('\n').Append(i).Append(": ").Append(paragraph);
                shown++;
                if (body.Length > maxChars)
                {
                    last = i;
                    break;
                }
            }

            if (last >= from)
            {
                result.Append(", pokazano ").Append(from).Append('–').Append(last);
            }

            result.Append(')');
            if (shown == 0)
            {
                result.Append("\n(brak tekstu od akapitu ").Append(from).Append(')');
            }
            else
            {
                result.Append(Tools.FileSystemExecutor.Truncate(body.ToString(), maxChars));
            }

            if (last < total)
            {
                result.Append("\n… dalsze akapity: word_czytaj z od_akapitu=").Append(last + 1);
            }

            return result.ToString();
        }

        /// <summary>Ile razy <paramref name="needle"/> występuje w tekście (bez wielkości liter, gdy <paramref name="matchCase"/> = false).</summary>
        public static int CountOccurrences(string haystack, string needle, bool matchCase)
        {
            if (string.IsNullOrEmpty(needle))
            {
                return 0;
            }

            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, comparison)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        /// <summary>Zamiana tekstu w całym dokumencie (treść główna; nagłówki i stopki osobno — też przez <c>Find</c>).</summary>
        public static string Replace(OfficeHost host, string path, string find, string replaceWith, bool all, bool matchCase, bool keepOpen)
        {
            host.Open(path, readOnly: false, visible: keepOpen);
            var content = host.Track(Com.Get(host.Document, "Content"));
            var before = CountOccurrences(Com.Text(content, "Text"), find, matchCase);
            if (before == 0)
            {
                host.Finish(keepOpen: false);
                return "BŁĄD: w dokumencie nie ma tekstu „" + find + "” (sprawdź pisownię; word_czytaj pokazuje treść).";
            }

            var replaced = ReplaceIn(content, find, replaceWith, all, matchCase);
            var inHeaders = 0;
            if (all)
            {
                inHeaders = ReplaceInHeadersAndFooters(host, find, replaceWith, matchCase);
            }

            host.Save();
            host.Finish(keepOpen);
            var count = all ? before : 1;
            return "OK zamieniono " + count + "× „" + find + "” → „" + replaceWith + "”" + (inHeaders > 0 ? " (także w nagłówkach/stopkach)" : "")
                + " i zapisano " + path + (replaced ? "" : " (Word nie potwierdził zamiany — sprawdź dokument)");
        }

        private static bool ReplaceIn(object range, string find, string replaceWith, bool all, bool matchCase)
        {
            var finder = Com.Get(range, "Find");
            try
            {
                Com.Call(finder, "ClearFormatting");
                var replacement = Com.Get(finder, "Replacement");
                Com.Call(replacement, "ClearFormatting");
                Com.Release(replacement);
                // Execute(FindText, MatchCase, MatchWholeWord, MatchWildcards, MatchSoundsLike, MatchAllWordForms, Forward, Wrap, Format,
                //         ReplaceWith, Replace) — Wrap: wdFindContinue = 1; Replace: wdReplaceOne = 1, wdReplaceAll = 2
                var result = Com.Call(finder, "Execute", find, matchCase, false, false, false, false, true, 1, false, replaceWith, all ? 2 : 1);
                return Convert.ToBoolean(result ?? false, System.Globalization.CultureInfo.InvariantCulture);
            }
            finally
            {
                Com.Release(finder);
            }
        }

        private static int ReplaceInHeadersAndFooters(OfficeHost host, string find, string replaceWith, bool matchCase)
        {
            var replaced = 0;
            var sections = host.Track(Com.Get(host.Document, "Sections"));
            var count = Com.Int(sections, "Count");
            for (var s = 1; s <= count; s++)
            {
                var section = Com.Get(sections, "Item", s);
                foreach (var collectionName in new[] { "Headers", "Footers" })
                {
                    var collection = Com.Get(section, collectionName);
                    for (var i = 1; i <= 3; i++) // wdHeaderFooterPrimary, FirstPage, EvenPages
                    {
                        var part = Com.Get(collection, "Item", i);
                        try
                        {
                            if (Com.Bool(part, "Exists"))
                            {
                                var range = Com.Get(part, "Range");
                                if (CountOccurrences(Com.Text(range, "Text"), find, matchCase) > 0 && ReplaceIn(range, find, replaceWith, true, matchCase))
                                {
                                    replaced++;
                                }

                                Com.Release(range);
                            }
                        }
                        finally
                        {
                            Com.Release(part);
                        }
                    }

                    Com.Release(collection);
                }

                Com.Release(section);
            }

            return replaced;
        }

        /// <summary>
        /// Dopisuje akapity: na końcu, na początku albo po wskazanym akapicie. Nowe linie w tekście = nowe akapity.
        /// Nieistniejący dokument powstaje (folder musi istnieć).
        /// </summary>
        public static string Append(OfficeHost host, string path, string text, string where, int afterParagraph, string styleName, bool keepOpen)
        {
            var created = !System.IO.File.Exists(path);
            if (created)
            {
                host.CreateNew(path, visible: keepOpen);
            }
            else
            {
                host.Open(path, readOnly: false, visible: keepOpen);
            }

            var paragraphs = host.Track(Com.Get(host.Document, "Paragraphs"));
            var content = host.Track(Com.Get(host.Document, "Content"));
            var countBefore = Com.Int(paragraphs, "Count");
            var isEmpty = Com.Text(content, "Text").Trim('\r', '\n', ' ', '\a') == "";
            var lines = text.Replace("\r\n", "\n").Replace('\n', '\r').TrimEnd('\r');
            var added = lines.Split('\r').Length;
            int firstNew;
            if (where == "poczatek" && !isEmpty)
            {
                Com.Call(content, "InsertBefore", lines + "\r");
                firstNew = 1;
            }
            else if (where == "po_akapicie" && !isEmpty)
            {
                if (afterParagraph < 1 || afterParagraph > countBefore)
                {
                    host.Finish(keepOpen: false);
                    return "BŁĄD: dokument ma " + countBefore + " akapitów, nie ma akapitu " + afterParagraph + ".";
                }

                var paragraph = Com.Get(paragraphs, "Item", afterParagraph);
                var range = Com.Get(paragraph, "Range");
                Com.Call(range, "InsertParagraphAfter");
                var next = Com.Get(paragraphs, "Item", afterParagraph + 1);
                var nextRange = Com.Get(next, "Range");
                Com.Call(nextRange, "InsertBefore", lines);
                Com.Release(new List<object> { paragraph, range, next, nextRange });
                firstNew = afterParagraph + 1;
            }
            else
            {
                if (!isEmpty)
                {
                    Com.Call(content, "InsertParagraphAfter");
                }

                Com.Call(content, "InsertAfter", lines);
                firstNew = isEmpty ? 1 : countBefore + 1;
            }

            var styleNote = "";
            if (!string.IsNullOrWhiteSpace(styleName))
            {
                if (!Styles.TryGetValue(styleName.Trim(), out var style))
                {
                    styleNote = " (nieznany styl „" + styleName + "”, zostawiono zwykły; dostępne: " + string.Join(", ", Styles.Keys) + ")";
                }
                else
                {
                    for (var i = firstNew; i < firstNew + added; i++)
                    {
                        var paragraph = Com.Get(paragraphs, "Item", i);
                        var range = Com.Get(paragraph, "Range");
                        Com.Set(range, "Style", style);
                        Com.Release(range);
                        Com.Release(paragraph);
                    }
                }
            }

            host.Save();
            var countAfter = Com.Int(paragraphs, "Count");
            host.Finish(keepOpen);
            return "OK " + (created ? "utworzono dokument " : "dopisano do ") + path + ": akapity " + firstNew + "–" + (firstNew + added - 1)
                + " (razem akapitów: " + countAfter + ")" + styleNote;
        }

        /// <summary>Eksport do PDF (<c>ExportAsFixedFormat</c>) albo zapis kopii w innym formacie (<c>SaveAs2</c>).</summary>
        public static string SaveAs(OfficeHost host, string path, string target)
        {
            host.Open(path, readOnly: true, visible: false);
            if (string.Equals(System.IO.Path.GetExtension(target), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                Com.Call(host.Document, "ExportAsFixedFormat", target, 17); // wdExportFormatPDF
            }
            else
            {
                Com.Call(host.Document, "SaveAs2", target, OfficeHost.WordFormat(target));
            }

            host.Finish(keepOpen: false);
            return "OK zapisano " + target;
        }
    }
}
