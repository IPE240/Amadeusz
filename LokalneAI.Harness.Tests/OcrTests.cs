using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LokalneAI.Harness.Ocr;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Validation;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace LokalneAI.Harness.Tests;

/// <summary>OCR i PDF bez Tesseracta: koder PNG, zakresy stron, obróbka tekstu, decyzje wykonawcy, pliki umiejętności.</summary>
public sealed class OcrUnitTests : IDisposable
{
    private static readonly HarnessProfile OcrProfile = HarnessProfile.Load(Paths.Profile("ocr"));
    private static readonly HarnessProfile OfficeProfile = HarnessProfile.Load(Paths.Profile("office"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lokalneai_ocr_" + Guid.NewGuid().ToString("N"));

    public OcrUnitTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "skany"));
        File.WriteAllBytes(Path.Combine(_root, "skan.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.WriteAllBytes(Path.Combine(_root, "raport.pdf"), Encoding.ASCII.GetBytes("%PDF-1.4"));
        File.WriteAllBytes(Path.Combine(_root, "drugi.pdf"), Encoding.ASCII.GetBytes("%PDF-1.4"));
        File.WriteAllText(Path.Combine(_root, "notatka.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private OcrExecutor Executor() => new OcrExecutor(new OcrOptions { AllowedRoots = new List<string> { _root }, WorkingFolder = _root });

    private static ToolCall Call(HarnessProfile profile, string tool, object args)
        => new ToolCall(profile.FindTool(tool), Paths.Json(JsonSerializer.Serialize(args)), 1, 1);

    [Fact]
    public void Png_SkladaAlfeNaBialymTle()
    {
        // 2 piksele: nieprzezroczysta czerwień i w pełni przezroczysty (PDFium renderuje strony z przezroczystym tłem).
        var bgra = new byte[] { 0, 0, 255, 255, 10, 20, 30, 0 };

        var png = PngWriter.FromBgra(bgra, 2, 1, 300);
        var raw = Inflate(png, out var width, out var height);

        Assert.Equal(2, width);
        Assert.Equal(1, height);
        Assert.Equal(new byte[] { 0, 255, 0, 0, 255, 255, 255 }, raw);   // filtr 0, potem RGB RGB
        Assert.Contains("pHYs", Encoding.ASCII.GetString(png));
    }

    [Theory]
    [InlineData("", 5, new[] { 1, 2, 3, 4, 5 })]
    [InlineData("1-3,5", 5, new[] { 1, 2, 3, 5 })]
    [InlineData("4-", 6, new[] { 4, 5, 6 })]
    [InlineData("3, 1, 3", 4, new[] { 3, 1 })]
    [InlineData("2–3", 4, new[] { 2, 3 })]
    public void ZakresStron(string spec, int count, int[] expected)
        => Assert.Equal(expected, PdfTools.ParsePages(spec, count));

    [Theory]
    [InlineData("0-2", 5)]
    [InlineData("7", 5)]
    [InlineData("a-b", 5)]
    [InlineData("3-1", 5)]
    public void ZakresStron_Bledy(string spec, int count)
    {
        var e = Assert.Throws<OcrException>(() => PdfTools.ParsePages(spec, count));
        Assert.StartsWith("BŁĄD", e.Message);
    }

    [Fact]
    public void ObrobkaTekstu_RegulyIgnorowaniaIDzielenieWyrazow()
    {
        var engine = new OcrEngine(new OcrOptions { IgnorePatterns = new List<string> { @"^Strona \d+ z \d+$", "ODS Sp. z o.o.", "[" } });
        var result = new OcrResult();

        var text = engine.PostProcess("Nagłówek ODS Sp. z o.o.\nSłupy główne kon-\nstrukcji z HEB300.\n\n\n\nStrona 3 z 12\nKoniec", result);

        Assert.Equal("Słupy główne konstrukcji z HEB300.\n\nKoniec", text);
        Assert.Equal(2, result.DroppedLines);   // niepoprawny wzorzec „[” jest traktowany jak zwykły tekst i niczego nie zrzuca
    }

    [Fact]
    public void Tsv_LinieAkapityIProgPewnosci()
    {
        const string tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
            + "1\t1\t0\t0\t0\t0\t0\t0\t100\t100\t-1\t\n"
            + "5\t1\t1\t1\t1\t1\t10\t10\t50\t10\t96.5\tSłupy\n"
            + "5\t1\t1\t1\t1\t2\t70\t10\t50\t10\t91.0\tHEB300\n"
            + "5\t1\t1\t1\t2\t1\t10\t30\t50\t10\t40.2\t|ę\n"
            + "5\t1\t1\t1\t2\t2\t70\t30\t50\t10\t88.0\tkotwy\n"
            + "5\t1\t1\t2\t1\t1\t10\t60\t50\t10\t95.0\tKoniec\n";
        var engine = new OcrEngine(new OcrOptions { MinConfidence = 60 });
        var result = new OcrResult();

        var text = engine.FromTsv(tsv, result);

        Assert.Equal("Słupy HEB300\nkotwy\n\nKoniec\n", text);
        Assert.Equal(4, result.Words);
        Assert.Equal(1, result.DroppedWords);
    }

    [Fact]
    public void Decyzje_CzytanieBezPytaniaZapisZPytaniemZlyTypOdrzucony()
    {
        var executor = Executor();

        Assert.Equal(ConfirmationKind.None, executor.Decide(Call(OcrProfile, "ocr_obraz", new { sciezka = "skan.png" })).Kind);
        Assert.Equal(ConfirmationKind.None, executor.Decide(Call(OcrProfile, "pdf_info", new { sciezka = "raport.pdf" })).Kind);
        Assert.Equal(ConfirmationKind.None, executor.Decide(Call(OfficeProfile, "pdf_czytaj", new { sciezka = "raport.pdf", strony = "1-2" })).Kind);

        var save = executor.Decide(Call(OcrProfile, "ocr_obraz", new { sciezka = "skan.png", zapisz_txt = true }));
        Assert.Equal(ConfirmationKind.Ask, save.Kind);
        Assert.Contains("skan.ocr.txt", save.Message);

        var wrong = executor.Decide(Call(OcrProfile, "ocr_obraz", new { sciezka = "raport.pdf" }));
        Assert.Equal(ConfirmationKind.Reject, wrong.Kind);
        Assert.Contains("ocr_pdf", wrong.Message);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call(OcrProfile, "ocr_pdf", new { sciezka = "notatka.txt" })).Kind);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call(OcrProfile, "ocr_obraz", new { sciezka = "C:\\Windows\\win.ini" })).Kind);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call(OcrProfile, "ocr_folder", new { sciezka = "skany" })).Kind);   // pusty folder
    }

    [Fact]
    public void Decyzje_PdfNowyPlikZPytaniemIstniejacyOdrzucony()
    {
        var executor = Executor();

        var merge = executor.Decide(Call(OfficeProfile, "pdf_polacz", new { pliki = new[] { "raport.pdf", "drugi.pdf" }, wynik = "komplet.pdf" }));
        Assert.Equal(ConfirmationKind.Ask, merge.Kind);
        Assert.Contains("2 plików", merge.Message);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call(OfficeProfile, "pdf_polacz", new { pliki = new[] { "raport.pdf", "drugi.pdf" }, wynik = "drugi.pdf" })).Kind);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call(OfficeProfile, "pdf_polacz", new { pliki = new[] { "raport.pdf" }, wynik = "x.pdf" })).Kind);
        var rotate = executor.Decide(Call(OfficeProfile, "pdf_obroc", new { sciezka = "raport.pdf", stopnie = 90 }));
        Assert.Equal(ConfirmationKind.Ask, rotate.Kind);
        Assert.Contains("raport_obrocony.pdf", rotate.Message);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call(OfficeProfile, "pdf_wytnij", new { sciezka = "raport.pdf", strony = "1", wynik = "wyciete.txt" })).Kind);
    }

    [Fact]
    public async Task SzukajPlikow_PrzezWykonawcePlikow()
    {
        var found = await Executor().ExecuteAsync(Call(OcrProfile, "szukaj_plikow", new { wzorzec = "*.pdf" }), CancellationToken.None);

        Assert.True(found.Success, found.Text);
        Assert.Contains("raport.pdf", found.Text);
    }

    [Theory]
    [InlineData("ocr")]
    public void Umiejetnosc_PrzykladyITestyPasujaDoNarzedzi(string name)
    {
        var profile = HarnessProfile.Load(Paths.Profile(name));
        var problems = new List<string>();
        foreach (var example in profile.Examples.Where(e => e.CallsJson != null))
        {
            foreach (var call in Paths.Json(example.CallsJson).EnumerateArray())
            {
                var tool = profile.FindTool(call.GetProperty("narzedzie").GetString());
                if (tool == null)
                {
                    problems.Add("przyklady: brak narzędzia " + call.GetProperty("narzedzie").GetString());
                    continue;
                }

                problems.AddRange(SchemaValidator.Validate(call.GetProperty("argumenty"), tool.Parameters).Select(e => "przyklady: " + e));
            }
        }

        foreach (var line in File.ReadAllLines(Path.Combine(profile.Folder, "testy.jsonl"), Encoding.UTF8).Where(l => l.Trim().Length > 0))
        {
            var test = Paths.Json(line);
            foreach (var step in test.GetProperty("kroki").EnumerateArray())
            {
                foreach (var call in step.GetProperty("oczekiwane").EnumerateArray())
                {
                    var tool = profile.FindTool(call.GetProperty("narzedzie").GetString());
                    if (tool == null)
                    {
                        problems.Add(test.GetProperty("id").GetString() + ": brak narzędzia " + call.GetProperty("narzedzie").GetString());
                        continue;
                    }

                    var properties = tool.Parameters.GetProperty("properties");
                    foreach (var argument in call.GetProperty("argumenty").EnumerateObject())
                    {
                        if (!properties.TryGetProperty(argument.Name, out var schema))
                        {
                            problems.Add(test.GetProperty("id").GetString() + ": " + tool.Name + " nie ma parametru " + argument.Name);
                            continue;
                        }

                        problems.AddRange(SchemaValidator.Validate(argument.Value, schema).Select(e => test.GetProperty("id").GetString() + ": " + e));
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Empty(profile.Settings.CloudProviders);
        Assert.False(profile.Settings.Internet);
    }

    [Fact]
    public void Office_MaPoleceniaPdf()
    {
        foreach (var tool in OcrExecutor.PdfTools)
        {
            Assert.NotNull(OfficeProfile.FindTool(tool));
        }

        Assert.True(OfficeProfile.FindTool("pdf_polacz")!.Mutates);
        Assert.False(OfficeProfile.FindTool("pdf_czytaj")!.Mutates);
    }

    /// <summary>Rozpakowuje IDAT (zlib) i zwraca surowe bajty obrazu; czyta wymiary z IHDR.</summary>
    private static byte[] Inflate(byte[] png, out int width, out int height)
    {
        width = ReadInt(png, 16);
        height = ReadInt(png, 20);
        var position = 8;
        while (position < png.Length)
        {
            var length = ReadInt(png, position);
            var type = Encoding.ASCII.GetString(png, position + 4, 4);
            if (type == "IDAT")
            {
                using var stream = new MemoryStream(png, position + 8 + 2, length - 2 - 4);   // bez nagłówka zlib i adlera
                using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                return output.ToArray();
            }

            position += 12 + length;
        }

        throw new InvalidDataException("brak IDAT");
    }

    private static int ReadInt(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
}

/// <summary>
/// Próby na prawdziwym Tesseracie i PDFium: PDF z polskim tekstem powstaje w PDFsharp (czcionka Arial z Windows), potem
/// czytanie warstwy tekstowej, render, OCR, wykrywanie obrotu, scalanie i wycinanie. Bez danych języków
/// (<c>%LOCALAPPDATA%\LokalneAI\tessdata</c>) próby przechodzą bez sprawdzania.
/// </summary>
public sealed class OcrLiveTests : IDisposable
{
    private static readonly HarnessProfile OcrProfile = HarnessProfile.Load(Paths.Profile("ocr"));
    private static readonly HarnessProfile OfficeProfile = HarnessProfile.Load(Paths.Profile("office"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lokalneai_ocr_live_" + Guid.NewGuid().ToString("N"));

    private static readonly string[] Lines =
    {
        "Protokół odbioru konstrukcji stalowej hali",
        "Słupy główne HEB300 ze stali S355J2, kotwy M30 długości 800 mm.",
        "Zestawienie stali: 12 500 kg, gatunek S355, rewizja R02.",
        "Płatwie z IPE200 w rozstawie 2,5 m, stężenia z prętów Ø20.",
        "Uwagi inwestora z 2026-11-10 uwzględnione w dokumentacji.",
        "Termin oddania projektu wykonawczego: 30 listopada 2026.",
        "Następne spotkanie w biurze projektowym: 24 listopada.",
        "Podpisy: kierownik budowy, inspektor nadzoru, projektant.",
    };

    public OcrLiveTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // PDFium bywa jeszcze w folderze.
        }
    }

    private static bool Ready => OcrEngine.IsAvailable() && OcrLanguages.Missing(OcrLanguages.DefaultFolder, "pol+eng").Count == 0;

    private OcrExecutor Executor() => new OcrExecutor(new OcrOptions { AllowedRoots = new List<string> { _root }, WorkingFolder = _root });

    private static ToolCall Call(HarnessProfile profile, string tool, object args)
        => new ToolCall(profile.FindTool(tool), Paths.Json(JsonSerializer.Serialize(args)), 1, 1);

    private string MakePdf(string name, int pages = 1)
    {
        GlobalFontSettings.FontResolver ??= new WindowsFontResolver();
        var path = Path.Combine(_root, name);
        using var document = new PdfDocument();
        document.Info.Title = "Protokół próbny";
        for (var p = 0; p < pages; p++)
        {
            var page = document.AddPage();
            using var graphics = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 13);
            for (var i = 0; i < Lines.Length; i++)
            {
                graphics.DrawString((pages > 1 ? "Strona " + (p + 1) + ". " : "") + Lines[i], font, XBrushes.Black, new XPoint(60, 90 + i * 26));
            }
        }

        document.Save(path);
        return path;
    }

    [Fact]
    public void Pdf_TekstInfoScalanieWycinanieObrot()
    {
        var pdf = MakePdf("protokol.pdf", pages: 3);

        var text = PdfTools.GetText(pdf, 2);
        var info = PdfTools.Info(pdf);
        var merged = Path.Combine(_root, "scalony.pdf");
        var cut = Path.Combine(_root, "wyciete.pdf");
        var rotated = Path.Combine(_root, "obrocony.pdf");

        Assert.Contains("Strona 2. Protokół odbioru", text);
        Assert.Contains("kotwy M30", text);
        Assert.Equal(3, info.Pages);
        Assert.Equal(3, info.PagesWithText);
        Assert.Equal(210, info.WidthMm);
        Assert.Equal("Protokół próbny", info.Title);
        Assert.Equal(6, PdfTools.Merge(new[] { pdf, pdf }, merged));
        Assert.Equal(2, PdfTools.ExtractPages(pdf, new[] { 3, 1 }, cut));
        Assert.Contains("Strona 3.", PdfTools.GetText(cut, 1));
        Assert.Equal(1, PdfTools.Rotate(pdf, new[] { 2 }, 90, rotated));
        Assert.Equal(3, PdfTools.PageCount(rotated));
    }

    [Fact]
    public void Ocr_StronaPdfIObrazZWykryciemObrotu()
    {
        if (!Ready)
        {
            return;
        }

        var pdf = MakePdf("skan_zrodlo.pdf");
        var engine = new OcrEngine(new OcrOptions { AllowedRoots = new List<string> { _root }, Dpi = 300 });

        var upright = engine.RecognizePng(PdfTools.RenderPng(pdf, 1, 300));
        Assert.True(upright.MeanConfidence > 0.8, upright.Summary());
        Assert.Contains("Protokół odbioru konstrukcji stalowej hali", upright.Text);
        Assert.Contains("HEB300", upright.Text);
        Assert.Contains("12 500 kg", upright.Text);
        Assert.Equal(0, upright.RotationApplied);

        // Obraz obrócony o 90° w prawo: OSD ma wykryć orientację, a poprawka ustawić tekst prosto.
        var png = Path.Combine(_root, "strona.png");
        File.WriteAllBytes(png, PdfTools.RenderPng(pdf, 1, 200));
        using (var pix = Tesseract.Pix.LoadFromFile(png))
        using (var turned = pix.Rotate90(1))
        {
            turned.Save(Path.Combine(_root, "obrocony.png"), Tesseract.ImageFormat.Png);
        }

        var rotated = engine.RecognizeFile(Path.Combine(_root, "obrocony.png"));
        Assert.True(rotated.RotationDetected, string.Join(" ", rotated.Notes));
        Assert.NotEqual(0, rotated.RotationApplied);
        Assert.Contains("Protokół odbioru konstrukcji stalowej hali", rotated.Text);
    }

    [Fact]
    public async Task Wykonawca_OcrObrazuPdfInfoICzytanieZOcr()
    {
        if (!Ready)
        {
            return;
        }

        var pdf = MakePdf("dokument.pdf", pages: 2);
        File.WriteAllBytes(Path.Combine(_root, "skan.png"), PdfTools.RenderPng(pdf, 1, 250));
        var executor = Executor();

        var image = await executor.ExecuteAsync(Call(OcrProfile, "ocr_obraz", new { sciezka = "skan.png", zapisz_txt = true }), CancellationToken.None);
        var info = await executor.ExecuteAsync(Call(OfficeProfile, "pdf_info", new { sciezka = "dokument.pdf" }), CancellationToken.None);
        var read = await executor.ExecuteAsync(Call(OfficeProfile, "pdf_czytaj", new { sciezka = "dokument.pdf", strony = "2" }), CancellationToken.None);
        var ocrPdf = await executor.ExecuteAsync(Call(OcrProfile, "ocr_pdf", new { sciezka = "dokument.pdf", strony = "1", uklad = "tabela" }), CancellationToken.None);
        var folder = await executor.ExecuteAsync(Call(OcrProfile, "ocr_folder", new { wzorzec = "*.png" }), CancellationToken.None);

        Assert.True(image.Success, image.Text);
        Assert.Contains("% pewności", image.Text);
        Assert.Contains("zapisano", image.Text);
        Assert.Contains("HEB300", File.ReadAllText(Path.Combine(_root, "skan.ocr.txt")));
        Assert.True(info.Success, info.Text);
        Assert.Contains("stron 2", info.Text);
        Assert.Contains("dokument tekstowy", info.Text);
        Assert.True(read.Success, read.Text);
        Assert.Contains("--- strona 2 ---", read.Text);
        Assert.Contains("Strona 2. Protokół", read.Text);
        Assert.True(ocrPdf.Success, ocrPdf.Text);
        Assert.Contains("--- strona 1 ---", ocrPdf.Text);
        Assert.Contains("S355J2", ocrPdf.Text);
        Assert.True(folder.Success, folder.Text);
        Assert.Contains("skan.png → skan.ocr.txt", folder.Text);
    }

    /// <summary>PDFsharp (wersja Core) nie zna czcionek Windows — podajemy Arial z folderu Fonts.</summary>
    private sealed class WindowsFontResolver : IFontResolver
    {
        public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), faceName));

        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new FontResolverInfo(bold ? "arialbd.ttf" : "arial.ttf");
    }
}
