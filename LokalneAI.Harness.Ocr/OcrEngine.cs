using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tesseract;

namespace LokalneAI.Harness.Ocr
{
    /// <summary>Błąd OCR albo PDF z komunikatem dla modelu (po polsku).</summary>
    public sealed class OcrException : Exception
    {
        /// <summary>Tworzy błąd z komunikatem dla modelu.</summary>
        public OcrException(string message) : base(message)
        {
        }

        /// <summary>Tworzy błąd z komunikatem i przyczyną.</summary>
        public OcrException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>Wynik rozpoznania tekstu.</summary>
    public sealed class OcrResult
    {
        /// <summary>Rozpoznany tekst (linie jak na stronie; strony PDF rozdzielone nagłówkiem „--- strona N ---”).</summary>
        public string Text { get; set; } = "";

        /// <summary>Średnia pewność Tesseracta 0–1.</summary>
        public double MeanConfidence { get; set; }

        /// <summary>Rozpoznane słowa (po odfiltrowaniu).</summary>
        public int Words { get; set; }

        /// <summary>Słowa pominięte przez próg pewności.</summary>
        public int DroppedWords { get; set; }

        /// <summary>Linie pominięte przez reguły ignorowania.</summary>
        public int DroppedLines { get; set; }

        /// <summary>O ile stopni (w prawo) obrócono obraz przed OCR.</summary>
        public int RotationApplied { get; set; }

        /// <summary>Czy orientację wykryto automatycznie (dane <c>osd</c>).</summary>
        public bool RotationDetected { get; set; }

        /// <summary>Ile stron przetworzono (obraz = 1).</summary>
        public int Pages { get; set; }

        /// <summary>Czas całego rozpoznania.</summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>Użyte języki, np. <c>pol+eng</c>.</summary>
        public string Languages { get; set; }

        /// <summary>Uwagi dla użytkownika (pobrane dane języka, brak OSD…).</summary>
        public IList<string> Notes { get; } = new List<string>();

        /// <summary>Np. „94% pewności, 312 słów, 2,1 s, obrót 90°”.</summary>
        public string Summary()
        {
            var parts = new List<string>
            {
                Math.Round(MeanConfidence * 100).ToString(CultureInfo.InvariantCulture) + "% pewności",
                Words + " słów",
                Elapsed.TotalSeconds.ToString("0.0", CultureInfo.GetCultureInfo("pl-PL")) + " s",
            };
            if (Pages > 1)
            {
                parts.Insert(0, Pages + " stron");
            }

            if (RotationApplied != 0)
            {
                parts.Add("obrót " + RotationApplied + "°" + (RotationDetected ? " (wykryty)" : ""));
            }

            if (DroppedWords > 0)
            {
                parts.Add("pominięto słów o niskiej pewności: " + DroppedWords);
            }

            if (DroppedLines > 0)
            {
                parts.Add("pominięto linii z reguł: " + DroppedLines);
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// OCR skryptem: Tesseract 5 (LSTM, dane tessdata_fast) na obrazie z pliku, z pamięci (PNG) albo na stronach PDF
    /// zrenderowanych przez PDFium. Obrót strony (wykryty przez <c>osd</c> albo stały), układ (tabele z zachowanymi
    /// odstępami), próg pewności słów, reguły ignorowania linii, łączenie wyrazów dzielonych na końcu linii.
    /// Bez modelu językowego — model dostaje gotowy tekst.
    /// </summary>
    public sealed class OcrEngine
    {
        private readonly OcrOptions _options;

        /// <summary>Tworzy silnik z ustawieniami (języki, obrót, układ, filtry).</summary>
        public OcrEngine(OcrOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Czy Tesseract (natywne biblioteki) daje się załadować na tym komputerze.</summary>
        public static bool IsAvailable()
        {
            try
            {
                using (var pix = Pix.Create(8, 8, 8))
                {
                    return pix.Width == 8;
                }
            }
            catch (Exception e) when (e is DllNotFoundException || e is TypeInitializationException || e is BadImageFormatException || e is TesseractException)
            {
                return false;
            }
        }

        /// <summary>
        /// Brakujące dane języków: pobiera je (gdy <paramref name="download"/>) albo zgłasza błąd z podpowiedzią.
        /// Zwraca listę pobranych języków (do uwagi w wyniku).
        /// </summary>
        public IList<string> EnsureLanguages(bool download, string languages = null)
        {
            var wanted = languages ?? _options.Languages;
            var missing = OcrLanguages.Missing(_options.TessdataPath, wanted, withOrientation: _options.Rotation == OcrRotation.Auto);
            if (missing.Count == 0)
            {
                return new List<string>();
            }

            if (!download)
            {
                throw new OcrException("BŁĄD: brak danych języka OCR: " + string.Join(", ", missing.Select(OcrLanguages.Describe))
                    + " w " + _options.TessdataPath + ". Pobierz je w Ustawieniach → OCR.");
            }

            try
            {
                return OcrLanguages.DownloadMissingAsync(_options.TessdataPath, wanted, null, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is IOException || e is TaskCanceledException)
            {
                throw new OcrException("BŁĄD: nie udało się pobrać danych języka OCR (" + string.Join(", ", missing) + "): " + e.Message
                    + ". Sprawdź internet albo wgraj pliki *.traineddata do " + _options.TessdataPath + ".", e);
            }
        }

        /// <summary>OCR obrazu z pliku (PNG, JPG, TIFF, BMP, GIF, WebP).</summary>
        public OcrResult RecognizeFile(string imagePath, string languages = null, OcrLayout? layout = null)
        {
            if (!File.Exists(imagePath))
            {
                throw new OcrException("BŁĄD: nie ma pliku " + imagePath);
            }

            Pix pix;
            try
            {
                pix = Pix.LoadFromFile(imagePath);
            }
            catch (Exception e) when (e is IOException || e is TesseractException || e is ArgumentException)
            {
                throw new OcrException("BŁĄD: nie udało się wczytać obrazu " + imagePath + " (" + e.Message + "). Obsługiwane: PNG, JPG, TIFF, BMP, GIF, WebP.", e);
            }

            using (pix)
            {
                return Recognize(pix, languages, layout);
            }
        }

        /// <summary>OCR obrazu PNG z pamięci (np. zrzut ze schowka albo strona PDF).</summary>
        public OcrResult RecognizePng(byte[] png, string languages = null, OcrLayout? layout = null)
        {
            using (var pix = Pix.LoadFromMemory(png))
            {
                return Recognize(pix, languages, layout);
            }
        }

        /// <summary>OCR stron PDF (numery od 1) zrenderowanych w <see cref="OcrOptions.Dpi"/>; strony rozdzielone nagłówkami.</summary>
        public OcrResult RecognizePdf(string pdfPath, IList<int> pages, string languages = null, OcrLayout? layout = null)
        {
            var clock = Stopwatch.StartNew();
            var text = new StringBuilder();
            var total = new OcrResult { Languages = languages ?? _options.Languages };
            double confidenceSum = 0;
            foreach (var pageNumber in pages)
            {
                var png = PdfTools.RenderPng(pdfPath, pageNumber, _options.Dpi);
                var result = RecognizePng(png, languages, layout);
                text.Append("--- strona ").Append(pageNumber).Append(" ---\n").Append(result.Text.TrimEnd()).Append("\n\n");
                total.Words += result.Words;
                total.DroppedWords += result.DroppedWords;
                total.DroppedLines += result.DroppedLines;
                total.Pages++;
                confidenceSum += result.MeanConfidence;
                if (result.RotationApplied != 0)
                {
                    total.RotationApplied = result.RotationApplied;
                    total.RotationDetected = result.RotationDetected;
                }

                foreach (var note in result.Notes.Where(n => !total.Notes.Contains(n)))
                {
                    total.Notes.Add(note);
                }
            }

            total.Text = text.ToString().TrimEnd();
            total.MeanConfidence = total.Pages == 0 ? 0 : confidenceSum / total.Pages;
            total.Elapsed = clock.Elapsed;
            return total;
        }

        /// <summary>OCR gotowego obrazu Leptoniki.</summary>
        internal OcrResult Recognize(Pix source, string languages, OcrLayout? layoutOverride)
        {
            var clock = Stopwatch.StartNew();
            var usedLanguages = string.IsNullOrWhiteSpace(languages) ? _options.Languages : languages;
            var layout = layoutOverride ?? _options.Layout;
            var result = new OcrResult { Languages = usedLanguages, Pages = 1 };
            foreach (var downloaded in EnsureLanguages(true, usedLanguages))
            {
                result.Notes.Add("Pobrano dane języka " + OcrLanguages.Describe(downloaded) + ".");
            }

            if (source.XRes <= 0 || source.YRes <= 0)
            {
                source.XRes = _options.Dpi;
                source.YRes = _options.Dpi;
            }

            Pix working = null;
            try
            {
                var rotation = _options.Rotation;
                if (rotation == OcrRotation.Auto)
                {
                    var detected = DetectRotation(source, out var detectedOk);
                    result.RotationDetected = detectedOk;
                    rotation = (OcrRotation)detected;
                    if (!detectedOk)
                    {
                        result.Notes.Add("Orientacji strony nie dało się wykryć (za mało tekstu) — bez obrotu.");
                    }
                }

                working = Rotate(source, (int)rotation);
                result.RotationApplied = (int)rotation;
                var pix = working ?? source;
                using (var engine = CreateEngine(usedLanguages, layout))
                using (var page = engine.Process(pix, PageSegMode(layout)))
                {
                    result.MeanConfidence = page.GetMeanConfidence();
                    string text;
                    if (layout == OcrLayout.PreserveLayout)
                    {
                        text = page.GetText();
                        result.Words = text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    }
                    else
                    {
                        text = FromTsv(page.GetTsvText(0), result);
                    }

                    result.Text = PostProcess(text, result);
                }
            }
            finally
            {
                working?.Dispose();
            }

            result.Elapsed = clock.Elapsed;
            return result;
        }

        private TesseractEngine CreateEngine(string languages, OcrLayout layout)
        {
            TesseractEngine engine;
            try
            {
                engine = new TesseractEngine(_options.TessdataPath, languages, EngineMode.LstmOnly);
            }
            catch (Exception e) when (e is TesseractException || e is DllNotFoundException || e is TypeInitializationException || e is BadImageFormatException)
            {
                throw new OcrException("BŁĄD: nie udało się uruchomić Tesseracta (języki " + languages + ", dane " + _options.TessdataPath + "): " + e.Message, e);
            }

            if (layout == OcrLayout.PreserveLayout)
            {
                engine.SetVariable("preserve_interword_spaces", "1");
            }

            return engine;
        }

        private static PageSegMode PageSegMode(OcrLayout layout)
        {
            switch (layout)
            {
                case OcrLayout.PreserveLayout: return Tesseract.PageSegMode.SingleBlock;
                case OcrLayout.SingleColumn: return Tesseract.PageSegMode.SingleColumn;
                case OcrLayout.SparseText: return Tesseract.PageSegMode.SparseText;
                default: return Tesseract.PageSegMode.Auto;
            }
        }

        /// <summary>
        /// Wykrywa, o ile stopni w prawo obrócić obraz, żeby tekst był prosto (dane <c>osd</c>). Tesseract podaje obrót obrazu
        /// w prawo względem pionu, więc poprawka to dopełnienie do 360°. Przy zbyt małej ilości tekstu zwraca 0 i <paramref name="ok"/> = false.
        /// </summary>
        internal int DetectRotation(Pix pix, out bool ok)
        {
            ok = false;
            if (!File.Exists(Path.Combine(_options.TessdataPath, OcrLanguages.OrientationData + ".traineddata")))
            {
                return 0;
            }

            try
            {
                using (var osd = new TesseractEngine(_options.TessdataPath, OcrLanguages.OrientationData, EngineMode.TesseractOnly))
                using (var page = osd.Process(pix, Tesseract.PageSegMode.OsdOnly))
                {
                    page.DetectBestOrientation(out int orientation, out float confidence);
                    ok = true;
                    var normalized = ((orientation % 360) + 360) % 360;
                    return (360 - normalized) % 360;
                }
            }
            catch (TesseractException)
            {
                return 0; // „Too few characters” — bez obrotu
            }
        }

        /// <summary>Obraz obrócony w prawo o 90, 180 albo 270 stopni; null przy 0 (używa się oryginału).</summary>
        internal static Pix Rotate(Pix pix, int degreesClockwise)
        {
            switch (((degreesClockwise % 360) + 360) % 360)
            {
                case 90: return pix.Rotate90(1);
                case 180:
                    using (var quarter = pix.Rotate90(1))
                    {
                        return quarter.Rotate90(1);
                    }

                case 270: return pix.Rotate90(-1);
                default: return null;
            }
        }

        /// <summary>
        /// Tekst z TSV Tesseracta: linie wg bloków i akapitów, słowa o pewności poniżej progu pominięte.
        /// Kolumny TSV: level page block par line word left top width height conf text.
        /// </summary>
        internal string FromTsv(string tsv, OcrResult result)
        {
            var text = new StringBuilder();
            var line = new List<string>();
            string current = null;
            string currentParagraph = null;
            foreach (var row in tsv.Split('\n'))
            {
                var cells = row.TrimEnd('\r').Split('\t');
                if (cells.Length < 12 || cells[0] != "5")
                {
                    continue;
                }

                var key = cells[2] + ":" + cells[3] + ":" + cells[4];
                var paragraph = cells[2] + ":" + cells[3];
                if (current != null && key != current)
                {
                    Flush(text, line, currentParagraph != paragraph);
                }

                current = key;
                currentParagraph = paragraph;
                var word = cells[11].Trim();
                if (word.Length == 0)
                {
                    continue;
                }

                var confidence = double.TryParse(cells[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? c : 100;
                if (_options.MinConfidence > 0 && confidence >= 0 && confidence < _options.MinConfidence)
                {
                    result.DroppedWords++;
                    continue;
                }

                line.Add(word);
                result.Words++;
            }

            Flush(text, line, false);
            return text.ToString();
        }

        private static void Flush(StringBuilder text, List<string> line, bool paragraphBreak)
        {
            if (line.Count > 0)
            {
                text.Append(string.Join(" ", line)).Append('\n');
                line.Clear();
            }

            if (paragraphBreak && text.Length > 0 && !text.ToString().EndsWith("\n\n", StringComparison.Ordinal))
            {
                text.Append('\n');
            }
        }

        /// <summary>Reguły ignorowania linii i łączenie wyrazów dzielonych; usuwa nadmiar pustych linii.</summary>
        internal string PostProcess(string text, OcrResult result)
        {
            var patterns = _options.IgnorePatterns
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p =>
                {
                    try
                    {
                        return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    }
                    catch (ArgumentException)
                    {
                        return new Regex(Regex.Escape(p), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    }
                })
                .ToList();

            var lines = new List<string>();
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Trim().Length > 0 && patterns.Any(p => SafeMatch(p, line)))
                {
                    result.DroppedLines++;
                    continue;
                }

                lines.Add(line);
            }

            if (_options.JoinHyphenation)
            {
                for (var i = 0; i < lines.Count - 1; i++)
                {
                    var next = lines.Skip(i + 1).FirstOrDefault(l => l.Trim().Length > 0);
                    if (lines[i].Length > 1 && lines[i].EndsWith("-", StringComparison.Ordinal) && next != null && char.IsLower(next.TrimStart()[0])
                        && lines[i + 1].Trim().Length > 0)
                    {
                        lines[i + 1] = lines[i].Substring(0, lines[i].Length - 1) + lines[i + 1].TrimStart();
                        lines[i] = "";
                    }
                }
            }

            var output = new StringBuilder();
            var blank = 0;
            foreach (var line in lines)
            {
                if (line.Trim().Length == 0)
                {
                    blank++;
                    continue;
                }

                if (output.Length > 0)
                {
                    output.Append(blank > 0 ? "\n\n" : "\n");
                }

                blank = 0;
                output.Append(line);
            }

            return output.ToString();
        }

        private static bool SafeMatch(Regex pattern, string line)
        {
            try
            {
                return pattern.IsMatch(line);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
