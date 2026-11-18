using System;
using System.Collections.Generic;
using System.IO;

namespace LokalneAI.Harness.Ocr
{
    /// <summary>Jak obracać stronę przed OCR.</summary>
    public enum OcrRotation
    {
        /// <summary>Wykryj orientację (dane <c>osd</c> Tesseracta) i obróć, gdy trzeba; przy zbyt małej ilości tekstu — bez obrotu.</summary>
        Auto = -1,
        /// <summary>Bez obrotu.</summary>
        None = 0,
        /// <summary>Obróć o 90° w prawo przed OCR.</summary>
        Clockwise90 = 90,
        /// <summary>Obróć o 180°.</summary>
        Rotate180 = 180,
        /// <summary>Obróć o 270° w prawo (90° w lewo).</summary>
        Clockwise270 = 270,
    }

    /// <summary>Analiza układu strony (tryb segmentacji Tesseracta).</summary>
    public enum OcrLayout
    {
        /// <summary>Automatyczna: kolumny, akapity, nagłówki (PSM 3).</summary>
        Auto,
        /// <summary>Jeden blok tekstu, zachowane odstępy między kolumnami — do tabel i zestawień (PSM 6 + <c>preserve_interword_spaces</c>).</summary>
        PreserveLayout,
        /// <summary>Jedna kolumna tekstu o różnych rozmiarach (PSM 4).</summary>
        SingleColumn,
        /// <summary>Luźny tekst bez kolejności (pieczątki, adnotacje, rysunki) (PSM 11).</summary>
        SparseText,
    }

    /// <summary>Ustawienia OCR i PDF (umiejętności „ocr” i „office”; Ustawienia → OCR w oknie).</summary>
    public sealed class OcrOptions
    {
        /// <summary>Foldery, w których wolno czytać i zapisywać pliki (z podfolderami). Pusta lista = nigdzie.</summary>
        public IList<string> AllowedRoots { get; set; } = new List<string>();

        /// <summary>Folder bieżący dla ścieżek względnych.</summary>
        public string WorkingFolder { get; set; }

        /// <summary>Folder z danymi języków Tesseracta (<c>pol.traineddata</c>…). Domyślnie <see cref="OcrLanguages.DefaultFolder"/>.</summary>
        public string TessdataFolder { get; set; } = OcrLanguages.DefaultFolder;

        /// <summary>Języki Tesseracta, np. <c>pol+eng</c> (polski z angielskim) albo <c>deu</c>.</summary>
        public string Languages { get; set; } = "pol+eng";

        /// <summary>Obrót strony przed OCR.</summary>
        public OcrRotation Rotation { get; set; } = OcrRotation.Auto;

        /// <summary>Analiza układu strony.</summary>
        public OcrLayout Layout { get; set; } = OcrLayout.Auto;

        /// <summary>Słowa o pewności poniżej tej wartości (0–100) są pomijane; 0 = nic nie pomijaj.</summary>
        public int MinConfidence { get; set; }

        /// <summary>Wyrażenia regularne: linia pasująca do któregoś jest pomijana (nagłówki stron, numery stron, pieczątki).</summary>
        public IList<string> IgnorePatterns { get; set; } = new List<string>();

        /// <summary>Rozdzielczość renderowania stron PDF do OCR (punkty na cal). 300 = dobre dla druku, 200 = szybciej.</summary>
        public int Dpi { get; set; } = 300;

        /// <summary>Gdy strona PDF nie ma warstwy tekstowej (skan), <c>pdf_czytaj</c> robi OCR tej strony.</summary>
        public bool OcrWhenNoText { get; set; } = true;

        /// <summary>Ile stron PDF najwyżej w jednym wywołaniu OCR (każda to sekundy).</summary>
        public int MaxPagesPerCall { get; set; } = 20;

        /// <summary>Ile znaków wyniku najwyżej trafia do modelu (reszta jest obcinana).</summary>
        public int MaxOutputChars { get; set; } = 4000;

        /// <summary>Ile plików najwyżej w <c>ocr_folder</c>.</summary>
        public int MaxFilesPerFolder { get; set; } = 50;

        /// <summary>Usuwaj podział wyrazów na końcu linii („kon-\nstrukcja” → „konstrukcja”).</summary>
        public bool JoinHyphenation { get; set; } = true;

        /// <summary>Pełna ścieżka danych języków (folder istniejący albo nie).</summary>
        public string TessdataPath => string.IsNullOrWhiteSpace(TessdataFolder) ? OcrLanguages.DefaultFolder : Environment.ExpandEnvironmentVariables(TessdataFolder);

        /// <summary>Kopia ustawień OCR (bez folderów) — do nadpisania w jednym wywołaniu (np. inny język).</summary>
        public OcrOptions Clone()
        {
            return new OcrOptions
            {
                AllowedRoots = new List<string>(AllowedRoots),
                WorkingFolder = WorkingFolder,
                TessdataFolder = TessdataFolder,
                Languages = Languages,
                Rotation = Rotation,
                Layout = Layout,
                MinConfidence = MinConfidence,
                IgnorePatterns = new List<string>(IgnorePatterns),
                Dpi = Dpi,
                OcrWhenNoText = OcrWhenNoText,
                MaxPagesPerCall = MaxPagesPerCall,
                MaxOutputChars = MaxOutputChars,
                MaxFilesPerFolder = MaxFilesPerFolder,
                JoinHyphenation = JoinHyphenation,
            };
        }

        /// <summary>Rozszerzenia obrazów, które czyta Leptonica (Tesseract).</summary>
        public static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif", ".webp",
        };

        /// <summary>Czy ścieżka to obraz do OCR.</summary>
        public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path ?? ""));

        /// <summary>Czy ścieżka to PDF.</summary>
        public static bool IsPdf(string path) => string.Equals(Path.GetExtension(path ?? ""), ".pdf", StringComparison.OrdinalIgnoreCase);
    }
}
