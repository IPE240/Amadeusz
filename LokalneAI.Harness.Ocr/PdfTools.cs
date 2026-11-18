using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Docnet.Core;
using Docnet.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace LokalneAI.Harness.Ocr
{
    /// <summary>Opis pliku PDF.</summary>
    public sealed class PdfInfo
    {
        /// <summary>Liczba stron.</summary>
        public int Pages { get; set; }

        /// <summary>Tytuł z metadanych (może być pusty).</summary>
        public string Title { get; set; }

        /// <summary>Autor z metadanych (może być pusty).</summary>
        public string Author { get; set; }

        /// <summary>Szerokość pierwszej strony w mm.</summary>
        public double WidthMm { get; set; }

        /// <summary>Wysokość pierwszej strony w mm.</summary>
        public double HeightMm { get; set; }

        /// <summary>Ile ze sprawdzonych stron ma warstwę tekstową.</summary>
        public int PagesWithText { get; set; }

        /// <summary>Ile stron sprawdzono pod kątem tekstu (najwyżej kilkanaście, żeby nie czekać).</summary>
        public int PagesChecked { get; set; }

        /// <summary>Rozmiar pliku w kB.</summary>
        public long SizeKb { get; set; }

        /// <summary>Czy PDF jest zaszyfrowany hasłem.</summary>
        public bool Encrypted { get; set; }
    }

    /// <summary>
    /// PDF: tekst i renderowanie stron przez PDFium (Docnet.Core), scalanie, wycinanie i obracanie stron przez PDFsharp (MIT).
    /// Numery stron od 1. Nic nie nadpisuje: wynik zawsze w nowym pliku.
    /// </summary>
    public static class PdfTools
    {
        private static readonly object Gate = new object();

        /// <summary>PDFium nie jest wielowątkowy — jedno wywołanie naraz w procesie.</summary>
        private static T WithPdfium<T>(string path, Func<Docnet.Core.Readers.IDocReader, T> work, double scale = 1.0)
        {
            if (!File.Exists(path))
            {
                throw new OcrException("BŁĄD: nie ma pliku " + path);
            }

            lock (Gate)
            {
                try
                {
                    using (var reader = DocLib.Instance.GetDocReader(path, new PageDimensions(scale)))
                    {
                        return work(reader);
                    }
                }
                catch (Exception e) when (e is Docnet.Core.Exceptions.DocnetException || e is IOException || e is ArgumentException || e is InvalidOperationException)
                {
                    throw new OcrException("BŁĄD: PDFium nie otworzył " + Path.GetFileName(path) + ": " + e.Message + " (plik uszkodzony albo zaszyfrowany?)", e);
                }
            }
        }

        /// <summary>Liczba stron.</summary>
        public static int PageCount(string path) => WithPdfium(path, reader => reader.GetPageCount());

        /// <summary>Warstwa tekstowa strony (PDFium zachowuje linie); pusta = skan bez tekstu.</summary>
        public static string GetText(string path, int pageNumber)
        {
            return WithPdfium(path, reader =>
            {
                CheckPage(reader, pageNumber);
                using (var page = reader.GetPageReader(pageNumber - 1))
                {
                    return (page.GetText() ?? "").Replace("\r\n", "\n").Trim();
                }
            });
        }

        /// <summary>Strona zrenderowana do PNG (RGB na białym tle) w podanej rozdzielczości.</summary>
        public static byte[] RenderPng(string path, int pageNumber, int dpi)
        {
            var scale = Math.Max(36, Math.Min(600, dpi)) / 72.0;
            return WithPdfium(path, reader =>
            {
                CheckPage(reader, pageNumber);
                using (var page = reader.GetPageReader(pageNumber - 1))
                {
                    var width = page.GetPageWidth();
                    var height = page.GetPageHeight();
                    var bgra = page.GetImage();
                    return PngWriter.FromBgra(bgra, width, height, dpi);
                }
            }, scale);
        }

        private static void CheckPage(Docnet.Core.Readers.IDocReader reader, int pageNumber)
        {
            var count = reader.GetPageCount();
            if (pageNumber < 1 || pageNumber > count)
            {
                throw new OcrException("BŁĄD: PDF ma " + count + " stron, nie ma strony " + pageNumber + ".");
            }
        }

        /// <summary>Opis pliku: strony, rozmiar, metadane, ile z pierwszych stron ma tekst.</summary>
        public static PdfInfo Info(string path, int pagesToCheck = 12)
        {
            var info = new PdfInfo { SizeKb = new FileInfo(path).Length / 1024 };
            WithPdfium(path, reader =>
            {
                info.Pages = reader.GetPageCount();
                var toCheck = Math.Min(info.Pages, pagesToCheck);
                for (var i = 0; i < toCheck; i++)
                {
                    using (var page = reader.GetPageReader(i))
                    {
                        if (i == 0)
                        {
                            info.WidthMm = Math.Round(page.GetPageWidth() * 25.4 / 72, 0);
                            info.HeightMm = Math.Round(page.GetPageHeight() * 25.4 / 72, 0);
                        }

                        if ((page.GetText() ?? "").Trim().Length > 0)
                        {
                            info.PagesWithText++;
                        }

                        info.PagesChecked++;
                    }
                }

                return 0;
            });

            try
            {
                using (var document = PdfReader.Open(path, PdfDocumentOpenMode.Import))
                {
                    info.Title = document.Info.Title;
                    info.Author = document.Info.Author;
                }
            }
            catch (Exception e) when (e is PdfSharp.Pdf.IO.PdfReaderException || e is IOException || e is InvalidOperationException || e is NotSupportedException || e is ArgumentException)
            {
                // Metadane nie są konieczne; zaszyfrowany plik PDFsharp odrzuca z komunikatem o haśle.
                info.Encrypted = (e.Message ?? "").IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                    || (e.Message ?? "").IndexOf("hasł", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return info;
        }

        /// <summary>Scala pliki w jeden (kolejność jak na liście). Zwraca liczbę stron wyniku.</summary>
        public static int Merge(IList<string> inputs, string output)
        {
            using (var result = new PdfDocument())
            {
                foreach (var file in inputs)
                {
                    using (var input = OpenImport(file))
                    {
                        for (var i = 0; i < input.PageCount; i++)
                        {
                            result.AddPage(input.Pages[i]);
                        }
                    }
                }

                result.Info.Title = Path.GetFileNameWithoutExtension(output);
                var pages = result.PageCount;   // po Save PDFsharp nie pozwala już czytać dokumentu
                result.Save(output);
                return pages;
            }
        }

        /// <summary>Wycina wybrane strony do nowego pliku. Zwraca liczbę stron wyniku.</summary>
        public static int ExtractPages(string input, IList<int> pages, string output)
        {
            using (var source = OpenImport(input))
            using (var result = new PdfDocument())
            {
                foreach (var pageNumber in pages)
                {
                    if (pageNumber < 1 || pageNumber > source.PageCount)
                    {
                        throw new OcrException("BŁĄD: PDF ma " + source.PageCount + " stron, nie ma strony " + pageNumber + ".");
                    }

                    result.AddPage(source.Pages[pageNumber - 1]);
                }

                var count = result.PageCount;
                result.Save(output);
                return count;
            }
        }

        /// <summary>Obraca strony (wszystkie, gdy lista pusta) o 90, 180 albo 270 stopni w prawo i zapisuje nowy plik. Zwraca liczbę obróconych stron.</summary>
        public static int Rotate(string input, IList<int> pages, int degrees, string output)
        {
            if (degrees != 90 && degrees != 180 && degrees != 270)
            {
                throw new OcrException("BŁĄD: obrót może być tylko o 90, 180 albo 270 stopni.");
            }

            using (var source = OpenImport(input))
            using (var result = new PdfDocument())
            {
                var selected = pages == null || pages.Count == 0 ? Enumerable.Range(1, source.PageCount).ToList() : pages;
                var rotated = 0;
                for (var i = 0; i < source.PageCount; i++)
                {
                    var page = result.AddPage(source.Pages[i]);
                    if (selected.Contains(i + 1))
                    {
                        page.Rotate = (page.Rotate + degrees) % 360;
                        rotated++;
                    }
                }

                result.Save(output);
                return rotated;
            }
        }

        private static PdfDocument OpenImport(string path)
        {
            if (!File.Exists(path))
            {
                throw new OcrException("BŁĄD: nie ma pliku " + path);
            }

            try
            {
                return PdfReader.Open(path, PdfDocumentOpenMode.Import);
            }
            catch (Exception e) when (e is PdfSharp.Pdf.IO.PdfReaderException || e is IOException || e is InvalidOperationException || e is NotSupportedException || e is ArgumentException)
            {
                throw new OcrException("BŁĄD: nie udało się otworzyć " + Path.GetFileName(path) + ": " + e.Message + " (plik uszkodzony albo zaszyfrowany?)", e);
            }
        }

        /// <summary>
        /// Zakres stron z tekstu: „1-3,5,8-” (od 8 do końca), „” = wszystkie. Numery od 1, bez powtórzeń, w kolejności podania.
        /// Błąd zakresu = <see cref="OcrException"/>.
        /// </summary>
        public static IList<int> ParsePages(string spec, int pageCount)
        {
            var pages = new List<int>();
            if (string.IsNullOrWhiteSpace(spec) || spec.Trim() == "*" || spec.Trim().Equals("wszystkie", StringComparison.OrdinalIgnoreCase))
            {
                return Enumerable.Range(1, pageCount).ToList();
            }

            foreach (var part in spec.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var piece = part.Trim().Replace("–", "-");
                int from, to;
                if (piece.Contains("-"))
                {
                    var ends = piece.Split('-');
                    if (ends.Length != 2 || !TryPage(ends[0], 1, out from) || !TryPage(ends[1], pageCount, out to))
                    {
                        throw new OcrException("BŁĄD: niezrozumiały zakres stron „" + piece + "”. Przykład: 1-3,5,8- (PDF ma " + pageCount + " stron).");
                    }
                }
                else if (TryPage(piece, 0, out from))
                {
                    to = from;
                }
                else
                {
                    throw new OcrException("BŁĄD: niezrozumiały numer strony „" + piece + "”. Przykład: 1-3,5,8- (PDF ma " + pageCount + " stron).");
                }

                if (from < 1 || to > pageCount || from > to)
                {
                    throw new OcrException("BŁĄD: strony " + piece + " poza zakresem 1–" + pageCount + ".");
                }

                for (var p = from; p <= to; p++)
                {
                    if (!pages.Contains(p))
                    {
                        pages.Add(p);
                    }
                }
            }

            return pages;
        }

        private static bool TryPage(string text, int whenEmpty, out int page)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                page = whenEmpty;
                return whenEmpty > 0;
            }

            return int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out page);
        }
    }
}
