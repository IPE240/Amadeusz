using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Ocr
{
    /// <summary>
    /// Polecenia umiejętności „ocr” (<c>ocr_obraz</c>, <c>ocr_pdf</c>, <c>ocr_folder</c>) i PDF dla umiejętności „office”
    /// (<c>pdf_czytaj</c>, <c>pdf_info</c>, <c>pdf_polacz</c>, <c>pdf_wytnij</c>, <c>pdf_obroc</c>) oraz <c>szukaj_plikow</c>
    /// i <c>pokaz_zawartosc</c> jak w „komputer”. OCR robi Tesseract (skrypt, bez modelu); strony PDF renderuje PDFium;
    /// scalanie i obracanie — PDFsharp. Ścieżki tylko w dozwolonych folderach; czytanie bez pytania, każdy nowy plik z pytaniem,
    /// istniejących plików nie nadpisuje.
    /// </summary>
    public sealed class OcrExecutor : IToolExecutor, IConfirmingExecutor, IWorkingFolderProvider, IFolderAccess
    {
        /// <summary>Dodaje folder do dozwolonych (po zgodzie użytkownika w rozmowie, patrz <see cref="IFolderAccess"/>).</summary>
        public bool AllowFolder(string folder) => FolderAccess.AddTo(_options.AllowedRoots, folder);

        private readonly OcrOptions _options;
        private readonly OcrEngine _engine;
        private readonly FileSystemExecutor _files;

        /// <summary>Folder ścieżek względnych — dla strażnika bezpieczeństwa.</summary>
        public string WorkingFolderFor(ToolCall call) => _options.WorkingFolder;

        /// <summary>Tworzy wykonawcę. Pusta lista <see cref="OcrOptions.AllowedRoots"/> = żadne polecenie na plikach nie przejdzie.</summary>
        public OcrExecutor(OcrOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _engine = new OcrEngine(_options);
            _files = new FileSystemExecutor(new FileSystemOptions
            {
                AllowedRoots = _options.AllowedRoots,
                WorkingFolder = _options.WorkingFolder,
                MaxOutputChars = _options.MaxOutputChars,
            });
        }

        /// <summary>Silnik OCR z tymi samymi ustawieniami — np. dla obrazka wklejonego do pola rozmowy.</summary>
        public OcrEngine Engine => _engine;

        /// <summary>Nazwy poleceń PDF (umiejętność „office” kieruje je tutaj).</summary>
        public static readonly IReadOnlyList<string> PdfTools = new[] { "pdf_czytaj", "pdf_info", "pdf_polacz", "pdf_wytnij", "pdf_obroc" };

        /// <summary>Nazwy poleceń OCR (umiejętność „ocr”).</summary>
        public static readonly IReadOnlyList<string> OcrTools = new[] { "ocr_obraz", "ocr_pdf", "ocr_folder" };

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            switch (call.Name)
            {
                case "szukaj_plikow":
                case "pokaz_zawartosc":
                    return await _files.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var text = await Task.Run(() => Execute(call, cancellationToken), cancellationToken).ConfigureAwait(false);
                return text.StartsWith("BŁĄD", StringComparison.Ordinal) ? ToolResult.Error(text) : ToolResult.Ok(text);
            }
            catch (OcrException e)
            {
                return ToolResult.Error(e.Message);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                                      || e is InvalidOperationException || e is Tesseract.TesseractException || e is DllNotFoundException)
            {
                return ToolResult.Error("BŁĄD: " + e.Message);
            }
        }

        /// <summary>Czytanie i OCR bez pytania; zapis pliku z pytaniem; ścieżka spoza dozwolonych folderów albo zły typ pliku — odrzucenie.</summary>
        public ConfirmationDecision Decide(ToolCall call)
        {
            string problem;
            switch (call.Name)
            {
                case "szukaj_plikow":
                case "pokaz_zawartosc":
                    return _files.Decide(call);

                case "ocr_obraz":
                case "ocr_pdf":
                {
                    var path = ResolveExisting(call, call.Name == "ocr_pdf" ? "pdf" : "obraz", out problem);
                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    if (call.GetBool("zapisz_txt") != true)
                    {
                        return ConfirmationDecision.NotNeeded;
                    }

                    var txt = TextFileFor(path);
                    return ConfirmationDecision.Ask((File.Exists(txt) ? "Nadpisać plik z tekstem OCR?\n\n" : "Zapisać tekst OCR do pliku?\n\n") + txt, File.Exists(txt));
                }

                case "ocr_folder":
                {
                    var files = FolderFiles(call, out problem);
                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    var existing = files.Count(f => File.Exists(TextFileFor(f)));
                    return ConfirmationDecision.Ask("Zrobić OCR " + files.Count + " plików i zapisać teksty obok (.ocr.txt)?\n\n"
                        + string.Join("\n", files.Take(12).Select(Path.GetFileName)) + (files.Count > 12 ? "\n… i " + (files.Count - 12) + " więcej" : "")
                        + (existing > 0 ? "\n\nUWAGA: " + existing + " plików .ocr.txt już istnieje i zostanie nadpisanych." : ""), existing > 0);
                }

                case "pdf_czytaj":
                case "pdf_info":
                {
                    ResolveExisting(call, "pdf", out problem);
                    return problem != null ? ConfirmationDecision.Reject(problem) : ConfirmationDecision.NotNeeded;
                }

                case "pdf_polacz":
                {
                    var inputs = InputFiles(call, out problem);
                    var output = problem == null ? ResolveNew(call.GetString("wynik"), out problem) : null;
                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    return ConfirmationDecision.Ask("Scalić " + inputs.Count + " plików PDF w nowy plik?\n\n" + string.Join("\n", inputs.Select(Path.GetFileName)) + "\n→ " + output);
                }

                case "pdf_wytnij":
                {
                    var path = ResolveExisting(call, "pdf", out problem);
                    var output = problem == null ? ResolveNew(call.GetString("wynik"), out problem) : null;
                    if (problem == null && string.IsNullOrWhiteSpace(call.GetString("strony")))
                    {
                        problem = "BŁĄD: podaj strony do wycięcia, np. 1-3,5.";
                    }

                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    return ConfirmationDecision.Ask("Wyciąć strony " + call.GetString("strony") + " do nowego pliku?\n\n" + path + "\n→ " + output);
                }

                case "pdf_obroc":
                {
                    var path = ResolveExisting(call, "pdf", out problem);
                    var output = problem == null ? ResolveNew(call.GetString("wynik") ?? DefaultRotatedName(path), out problem) : null;
                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    var pages = call.GetString("strony");
                    return ConfirmationDecision.Ask("Obrócić " + (string.IsNullOrWhiteSpace(pages) ? "wszystkie strony" : "strony " + pages) + " o "
                        + (int)(call.GetNumber("stopnie") ?? 90) + "° w prawo i zapisać nowy plik?\n\n" + path + "\n→ " + output);
                }

                default:
                    return ConfirmationDecision.NotNeeded;
            }
        }

        private string Execute(ToolCall call, CancellationToken cancellationToken)
        {
            string problem;
            switch (call.Name)
            {
                case "ocr_obraz":
                {
                    var path = ResolveExisting(call, "obraz", out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var result = _engine.RecognizeFile(path, Language(call), Layout(call, out problem));
                    if (problem != null)
                    {
                        return problem;
                    }

                    return Report(path, result, call.GetBool("zapisz_txt") == true);
                }

                case "ocr_pdf":
                {
                    var path = ResolveExisting(call, "pdf", out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var count = Ocr.PdfTools.PageCount(path);
                    var pages = Ocr.PdfTools.ParsePages(call.GetString("strony"), count);
                    var note = "";
                    if (pages.Count > _options.MaxPagesPerCall)
                    {
                        note = " (OCR najwyżej " + _options.MaxPagesPerCall + " stron naraz — dalsze: strony=" + pages[_options.MaxPagesPerCall] + "-)";
                        pages = pages.Take(_options.MaxPagesPerCall).ToList();
                    }

                    var layout = Layout(call, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var result = _engine.RecognizePdf(path, pages, Language(call), layout);
                    return Report(path, result, call.GetBool("zapisz_txt") == true, note);
                }

                case "ocr_folder":
                {
                    var files = FolderFiles(call, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var language = Language(call);
                    var lines = new StringBuilder("OK OCR " + files.Count + " plików (" + (language ?? _options.Languages) + "):");
                    var failures = 0;
                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            OcrResult result;
                            if (OcrOptions.IsPdf(file))
                            {
                                var count = Ocr.PdfTools.PageCount(file);
                                result = _engine.RecognizePdf(file, Enumerable.Range(1, Math.Min(count, _options.MaxPagesPerCall)).ToList(), language, null);
                            }
                            else
                            {
                                result = _engine.RecognizeFile(file, language, null);
                            }

                            var txt = TextFileFor(file);
                            File.WriteAllText(txt, result.Text, new UTF8Encoding(false));
                            lines.Append('\n').Append(Path.GetFileName(file)).Append(" → ").Append(Path.GetFileName(txt)).Append(": ").Append(result.Summary());
                        }
                        catch (OcrException e)
                        {
                            failures++;
                            lines.Append('\n').Append(Path.GetFileName(file)).Append(": ").Append(e.Message);
                        }
                    }

                    if (failures > 0)
                    {
                        lines.Append("\nNie udało się: ").Append(failures).Append(" plików.");
                    }

                    return lines.ToString();
                }

                case "pdf_czytaj":
                    return ReadPdf(call);

                case "pdf_info":
                {
                    var path = ResolveExisting(call, "pdf", out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var info = Ocr.PdfTools.Info(path);
                    var text = new StringBuilder("OK ").Append(path).Append(": stron ").Append(info.Pages)
                        .Append(", strona ").Append(info.WidthMm.ToString("0", CultureInfo.InvariantCulture)).Append('×').Append(info.HeightMm.ToString("0", CultureInfo.InvariantCulture))
                        .Append(" mm (").Append(PaperName(info.WidthMm, info.HeightMm)).Append("), ").Append(info.SizeKb).Append(" kB");
                    if (!string.IsNullOrWhiteSpace(info.Title))
                    {
                        text.Append(", tytuł „").Append(info.Title).Append('”');
                    }

                    if (!string.IsNullOrWhiteSpace(info.Author))
                    {
                        text.Append(", autor ").Append(info.Author);
                    }

                    text.Append(". Warstwa tekstowa: ").Append(info.PagesWithText).Append('/').Append(info.PagesChecked).Append(" sprawdzonych stron");
                    text.Append(info.PagesWithText == 0 ? " — skan bez tekstu, do czytania użyj ocr_pdf." : info.PagesWithText < info.PagesChecked ? " — część stron to skany (pdf_czytaj robi na nich OCR)." : " — dokument tekstowy (pdf_czytaj).");
                    if (info.Encrypted)
                    {
                        text.Append(" Plik jest zabezpieczony hasłem.");
                    }

                    return text.ToString();
                }

                case "pdf_polacz":
                {
                    var inputs = InputFiles(call, out problem);
                    var output = problem == null ? ResolveNew(call.GetString("wynik"), out problem) : null;
                    if (problem != null)
                    {
                        return problem;
                    }

                    var pages = Ocr.PdfTools.Merge(inputs, output);
                    return "OK scalono " + inputs.Count + " plików → " + output + " (stron: " + pages + ")";
                }

                case "pdf_wytnij":
                {
                    var path = ResolveExisting(call, "pdf", out problem);
                    var output = problem == null ? ResolveNew(call.GetString("wynik"), out problem) : null;
                    if (problem != null)
                    {
                        return problem;
                    }

                    var pages = Ocr.PdfTools.ParsePages(call.GetString("strony"), Ocr.PdfTools.PageCount(path));
                    var count = Ocr.PdfTools.ExtractPages(path, pages, output);
                    return "OK wycięto strony " + call.GetString("strony") + " → " + output + " (stron: " + count + ")";
                }

                case "pdf_obroc":
                {
                    var path = ResolveExisting(call, "pdf", out problem);
                    var output = problem == null ? ResolveNew(call.GetString("wynik") ?? DefaultRotatedName(path), out problem) : null;
                    if (problem != null)
                    {
                        return problem;
                    }

                    var degrees = (int)(call.GetNumber("stopnie") ?? 90);
                    var spec = call.GetString("strony");
                    var pages = string.IsNullOrWhiteSpace(spec) ? new List<int>() : Ocr.PdfTools.ParsePages(spec, Ocr.PdfTools.PageCount(path));
                    var rotated = Ocr.PdfTools.Rotate(path, pages, degrees, output);
                    return "OK obrócono " + rotated + " stron o " + degrees + "° → " + output;
                }

                default:
                    return "BŁĄD: nieobsługiwane polecenie: " + call.Name;
            }
        }

        private string ReadPdf(ToolCall call)
        {
            var path = ResolveExisting(call, "pdf", out var problem);
            if (problem != null)
            {
                return problem;
            }

            var count = Ocr.PdfTools.PageCount(path);
            var spec = call.GetString("strony");
            var from = Math.Max(1, (int)(call.GetNumber("od_strony") ?? 1));
            var pages = string.IsNullOrWhiteSpace(spec) ? Enumerable.Range(from, Math.Max(0, count - from + 1)).ToList() : Ocr.PdfTools.ParsePages(spec, count);
            var text = new StringBuilder();
            var shown = new List<int>();
            var ocrPages = 0;
            var budget = _options.MaxOutputChars;
            foreach (var page in pages)
            {
                if (text.Length >= budget)
                {
                    break;
                }

                var content = Ocr.PdfTools.GetText(path, page);
                var marker = "";
                if (content.Length < 20 && _options.OcrWhenNoText)
                {
                    var result = _engine.RecognizePdf(path, new[] { page }, null, null);
                    content = result.Text.Replace("--- strona " + page + " ---\n", "").Trim();
                    marker = " (OCR, " + Math.Round(result.MeanConfidence * 100) + "% pewności)";
                    ocrPages++;
                }

                text.Append("--- strona ").Append(page).Append(marker).Append(" ---\n").Append(content.Length == 0 ? "(pusta)" : content).Append("\n\n");
                shown.Add(page);
            }

            var header = new StringBuilder("OK ").Append(path).Append(" (stron: ").Append(count);
            if (shown.Count > 0)
            {
                header.Append(", pokazano ").Append(shown.Count == 1 ? shown[0].ToString(CultureInfo.InvariantCulture) : shown[0] + "–" + shown[shown.Count - 1]);
            }

            if (ocrPages > 0)
            {
                header.Append(", OCR stron bez tekstu: ").Append(ocrPages);
            }

            header.Append(')');
            var body = FileSystemExecutor.Truncate(text.ToString().TrimEnd(), _options.MaxOutputChars);
            var last = shown.Count > 0 ? shown[shown.Count - 1] : 0;
            return header + "\n" + body + (last > 0 && last < count && string.IsNullOrWhiteSpace(spec) ? "\n… dalsze strony: pdf_czytaj z od_strony=" + (last + 1) : "");
        }

        private string Report(string path, OcrResult result, bool save, string note = "")
        {
            var text = new StringBuilder("OK ").Append(path).Append(" — OCR (").Append(result.Languages).Append("): ").Append(result.Summary()).Append(note);
            if (save)
            {
                var txt = TextFileFor(path);
                File.WriteAllText(txt, result.Text, new UTF8Encoding(false));
                text.Append(", zapisano ").Append(txt);
            }

            foreach (var remark in result.Notes)
            {
                text.Append("\nUwaga: ").Append(remark);
            }

            text.Append('\n').Append(result.Text.Length == 0 ? "(nie rozpoznano tekstu)" : FileSystemExecutor.Truncate(result.Text, _options.MaxOutputChars));
            return text.ToString();
        }

        private string Language(ToolCall call)
        {
            var language = call.GetString("jezyk");
            if (string.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            var codes = OcrLanguages.Split(language);
            foreach (var code in codes)
            {
                if (code.Length != 3 || !code.All(char.IsLetter))
                {
                    throw new OcrException("BŁĄD: język podaj kodem Tesseracta (pol, eng, deu…), np. pol+eng; „" + code + "” nie pasuje.");
                }
            }

            return string.Join("+", codes);
        }

        private static OcrLayout? Layout(ToolCall call, out string problem)
        {
            problem = null;
            switch ((call.GetString("uklad") ?? "").Trim().ToLowerInvariant())
            {
                case "": return null;
                case "auto": return OcrLayout.Auto;
                case "tabela": return OcrLayout.PreserveLayout;
                case "kolumna": return OcrLayout.SingleColumn;
                case "luzny": return OcrLayout.SparseText;
                default:
                    problem = "BŁĄD: uklad może być: auto, tabela (zachowane odstępy kolumn), kolumna albo luzny (pieczątki, rysunki).";
                    return null;
            }
        }

        private static string TextFileFor(string path) => Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + ".ocr.txt");

        private static string DefaultRotatedName(string path)
            => path == null ? null : Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "_obrocony.pdf");

        private static string PaperName(double widthMm, double heightMm)
        {
            var portrait = widthMm <= heightMm;
            var shorter = Math.Min(widthMm, heightMm);
            var longer = Math.Max(widthMm, heightMm);
            string name;
            if (Near(shorter, 210) && Near(longer, 297))
            {
                name = "A4";
            }
            else if (Near(shorter, 297) && Near(longer, 420))
            {
                name = "A3";
            }
            else if (Near(shorter, 420) && Near(longer, 594))
            {
                name = "A2";
            }
            else if (Near(shorter, 594) && Near(longer, 841))
            {
                name = "A1";
            }
            else if (Near(shorter, 841) && Near(longer, 1189))
            {
                name = "A0";
            }
            else if (Near(shorter, 216) && Near(longer, 279))
            {
                name = "Letter";
            }
            else
            {
                name = "format własny";
            }

            return name + (portrait ? ", pionowo" : ", poziomo");
        }

        private static bool Near(double value, double expected) => Math.Abs(value - expected) <= 3;

        /// <summary>Istniejący plik w dozwolonym folderze o właściwym typie („pdf”, „obraz” albo null = dowolny).</summary>
        private string ResolveExisting(ToolCall call, string kind, out string problem)
        {
            var path = _files.Resolve(call.GetString("sciezka"), out problem);
            if (problem != null)
            {
                return null;
            }

            if (!File.Exists(path))
            {
                problem = Directory.Exists(path) ? "BŁĄD: " + path + " to folder, nie plik (do folderu: ocr_folder)." : "BŁĄD: nie ma pliku " + path + " (szukaj_plikow pokaże, co jest w folderze).";
                return null;
            }

            if (kind == "pdf" && !OcrOptions.IsPdf(path))
            {
                problem = "BŁĄD: " + Path.GetFileName(path) + " nie jest plikiem PDF" + (OcrOptions.IsImage(path) ? " — do obrazu użyj ocr_obraz." : ".");
                return null;
            }

            if (kind == "obraz" && !OcrOptions.IsImage(path))
            {
                problem = "BŁĄD: " + Path.GetFileName(path) + " nie jest obrazem (PNG, JPG, TIFF, BMP, GIF, WebP)" + (OcrOptions.IsPdf(path) ? " — do PDF użyj ocr_pdf." : ".");
                return null;
            }

            return path;
        }

        /// <summary>Nowy plik PDF w dozwolonym, istniejącym folderze; istniejącego nie nadpisujemy.</summary>
        private string ResolveNew(string target, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(target))
            {
                problem = "BŁĄD: podaj nazwę pliku wynikowego (wynik), np. scalony.pdf.";
                return null;
            }

            var path = _files.Resolve(target, out problem);
            if (problem != null)
            {
                return null;
            }

            if (!OcrOptions.IsPdf(path))
            {
                problem = "BŁĄD: plik wynikowy ma mieć rozszerzenie .pdf (jest " + Path.GetFileName(path) + ").";
                return null;
            }

            if (File.Exists(path))
            {
                problem = "BŁĄD: " + path + " już istnieje — niczego nie nadpisuję, podaj inną nazwę.";
                return null;
            }

            if (!Directory.Exists(Path.GetDirectoryName(path)))
            {
                problem = "BŁĄD: nie ma folderu " + Path.GetDirectoryName(path) + ".";
                return null;
            }

            return path;
        }

        private List<string> InputFiles(ToolCall call, out string problem)
        {
            problem = null;
            var files = new List<string>();
            if (!call.Arguments.TryGetProperty("pliki", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
            {
                problem = "BŁĄD: podaj listę plików PDF do scalenia (pliki), np. [\"a.pdf\",\"b.pdf\"].";
                return files;
            }

            foreach (var item in array.EnumerateArray())
            {
                var path = _files.Resolve(item.ValueKind == JsonValueKind.String ? item.GetString() : null, out problem);
                if (problem != null)
                {
                    return files;
                }

                if (!File.Exists(path))
                {
                    problem = "BŁĄD: nie ma pliku " + path;
                    return files;
                }

                if (!OcrOptions.IsPdf(path))
                {
                    problem = "BŁĄD: " + Path.GetFileName(path) + " nie jest plikiem PDF.";
                    return files;
                }

                files.Add(path);
            }

            if (files.Count < 2)
            {
                problem = "BŁĄD: do scalenia potrzeba co najmniej dwóch plików.";
            }

            return files;
        }

        private List<string> FolderFiles(ToolCall call, out string problem)
        {
            var folder = _files.Resolve(string.IsNullOrWhiteSpace(call.GetString("sciezka")) ? "." : call.GetString("sciezka"), out problem);
            if (problem != null)
            {
                return new List<string>();
            }

            if (!Directory.Exists(folder))
            {
                problem = "BŁĄD: nie ma folderu " + folder;
                return new List<string>();
            }

            var pattern = (call.GetString("wzorzec") ?? "").Trim();
            var files = Directory.GetFiles(folder, pattern.Length == 0 ? "*" : pattern)
                .Where(f => OcrOptions.IsImage(f) || OcrOptions.IsPdf(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0)
            {
                problem = "BŁĄD: w " + folder + " nie ma obrazów ani PDF-ów" + (pattern.Length > 0 ? " pasujących do " + pattern : "") + ".";
                return files;
            }

            if (files.Count > _options.MaxFilesPerFolder)
            {
                problem = "BŁĄD: w folderze jest " + files.Count + " plików, najwyżej " + _options.MaxFilesPerFolder + " naraz — zawęź wzorzec (np. *.jpg albo skan_2024*).";
                return new List<string>();
            }

            return files;
        }
    }
}
