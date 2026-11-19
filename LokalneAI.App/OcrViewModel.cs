using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows.Input;
using LokalneAI.Core.Storage;
using LokalneAI.Harness.Ocr;
using Microsoft.Win32;

namespace LokalneAI.App;

/// <summary>
/// Ustawienia → „OCR”: języki Tesseracta, obrót i układ strony, próg pewności, reguły ignorowania, rozdzielczość PDF,
/// folder danych języków z pobieraniem, próba na pliku. Te same ustawienia dostaje wykonawca umiejętności „ocr”
/// i poleceń PDF umiejętności „office” (<see cref="ToOptions"/>) oraz OCR obrazka wklejonego do pola rozmowy
/// (<see cref="RecognizePastedAsync"/>).
/// </summary>
public sealed class OcrViewModel : ObservableObject
{
    /// <summary>Wybór obrotu: etykieta → wartość w ustawieniach.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> RotationChoices =
    [
        new("wykryj automatycznie", "auto"),
        new("bez obrotu", "0"),
        new("90° w prawo", "90"),
        new("180°", "180"),
        new("90° w lewo", "270"),
    ];

    /// <summary>Wybór układu strony: etykieta → wartość w ustawieniach.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> LayoutChoices =
    [
        new("automatyczny (akapity, kolumny)", "auto"),
        new("tabela (zachowane odstępy kolumn)", "tabela"),
        new("jedna kolumna", "kolumna"),
        new("luźny tekst (pieczątki, rysunki)", "luzny"),
    ];

    public static readonly IReadOnlyList<int> DpiChoices = [150, 200, 300, 400, 600];

    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private string _status = "";
    private bool _busy;

    public OcrViewModel(AppSettings settings, Action saveSettings)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        DownloadCommand = new RelayCommand(DownloadAsync, () => !_busy);
        OpenFolderCommand = new RelayCommand(() => OpenFolder(TessdataPath));
        TestCommand = new RelayCommand(TestOnFileAsync, () => !_busy);
        RefreshLanguagesStatus();
    }

    public RelayCommand DownloadCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public RelayCommand TestCommand { get; }

    /// <summary>Do nagłówka sekcji: „pol+eng · obrót auto · 300 dpi”.</summary>
    public string Summary => Languages + " · obrót " + RotationLabel + " · " + Dpi + " dpi" + (OnPaste ? " · wklejony obrazek → OCR" : "");

    public string Languages
    {
        get => _settings.OcrLanguages;
        set => Update(() => _settings.OcrLanguages = NormalizeLanguages(value), nameof(Languages));
    }

    public string Rotation
    {
        get => _settings.OcrRotation;
        set => Update(() => _settings.OcrRotation = RotationChoices.Any(c => c.Value == value) ? value : "auto", nameof(Rotation));
    }

    public string Layout
    {
        get => _settings.OcrLayout;
        set => Update(() => _settings.OcrLayout = LayoutChoices.Any(c => c.Value == value) ? value : "auto", nameof(Layout));
    }

    public string MinConfidenceText
    {
        get => _settings.OcrMinConfidence.ToString(CultureInfo.InvariantCulture);
        set => Update(() => _settings.OcrMinConfidence = int.TryParse(value?.Trim(), out var v) ? Math.Max(0, Math.Min(100, v)) : 0, nameof(MinConfidenceText));
    }

    public string IgnorePatterns
    {
        get => _settings.OcrIgnorePatterns ?? "";
        set => Update(() => _settings.OcrIgnorePatterns = string.IsNullOrWhiteSpace(value) ? null : value, nameof(IgnorePatterns));
    }

    public int Dpi
    {
        get => _settings.OcrDpi;
        set => Update(() => _settings.OcrDpi = DpiChoices.Contains(value) ? value : 300, nameof(Dpi));
    }

    public bool WhenNoText
    {
        get => _settings.OcrWhenNoText;
        set => Update(() => _settings.OcrWhenNoText = value, nameof(WhenNoText));
    }

    public bool OnPaste
    {
        get => _settings.OcrOnPaste;
        set => Update(() => _settings.OcrOnPaste = value, nameof(OnPaste));
    }

    public string TessdataFolder
    {
        get => _settings.OcrTessdataFolder ?? "";
        set => Update(() => _settings.OcrTessdataFolder = string.IsNullOrWhiteSpace(value) ? null : value.Trim(), nameof(TessdataFolder));
    }

    /// <summary>Folder danych języków po rozwinięciu zmiennych; domyślny, gdy pole puste.</summary>
    public string TessdataPath => string.IsNullOrWhiteSpace(_settings.OcrTessdataFolder) ? OcrLanguages.DefaultFolder : Environment.ExpandEnvironmentVariables(_settings.OcrTessdataFolder);

    /// <summary>Podpowiedź pod polem folderu: które języki są pobrane, a których brakuje.</summary>
    public string LanguagesStatus { get; private set; } = "";

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private string RotationLabel => RotationChoices.FirstOrDefault(c => c.Value == Rotation).Key ?? Rotation;

    /// <summary>Ustawienia OCR z okna jako opcje wykonawcy (foldery z zakładki Code).</summary>
    public static OcrOptions ToOptions(AppSettings settings, IList<string> roots, string? workingFolder)
    {
        var options = new OcrOptions
        {
            AllowedRoots = roots,
            WorkingFolder = workingFolder,
            Languages = NormalizeLanguages(settings.OcrLanguages),
            Rotation = settings.OcrRotation switch
            {
                "0" => OcrRotation.None,
                "90" => OcrRotation.Clockwise90,
                "180" => OcrRotation.Rotate180,
                "270" => OcrRotation.Clockwise270,
                _ => OcrRotation.Auto,
            },
            Layout = settings.OcrLayout switch
            {
                "tabela" => OcrLayout.PreserveLayout,
                "kolumna" => OcrLayout.SingleColumn,
                "luzny" => OcrLayout.SparseText,
                _ => OcrLayout.Auto,
            },
            MinConfidence = Math.Max(0, Math.Min(100, settings.OcrMinConfidence)),
            Dpi = DpiChoices.Contains(settings.OcrDpi) ? settings.OcrDpi : 300,
            OcrWhenNoText = settings.OcrWhenNoText,
            TessdataFolder = string.IsNullOrWhiteSpace(settings.OcrTessdataFolder) ? OcrLanguages.DefaultFolder : settings.OcrTessdataFolder,
        };
        foreach (var line in (settings.OcrIgnorePatterns ?? "").Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                options.IgnorePatterns.Add(line.Trim());
            }
        }

        return options;
    }

    /// <summary>
    /// Obrazek ze schowka (PNG): zapis do folderu i OCR z ustawieniami okna. Zwraca blok do wstawienia w pole rozmowy:
    /// nagłówek z nazwą pliku i pewnością, potem rozpoznany tekst. Błąd OCR też wraca jako tekst (model go nie dostaje —
    /// użytkownik widzi w polu i usuwa).
    /// </summary>
    public async Task<string> RecognizePastedAsync(byte[] png, string folder)
    {
        Directory.CreateDirectory(folder);
        var name = "wklejony_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + ".png";
        var path = Path.Combine(folder, name);
        for (var i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + "_" + i + ".png");
        }

        await File.WriteAllBytesAsync(path, png);
        var options = ToOptions(_settings, [folder], folder);
        try
        {
            var result = await Task.Run(() => new OcrEngine(options).RecognizePng(png));
            var header = "[obraz wklejony: " + Path.GetFileName(path) + " — OCR " + Math.Round(result.MeanConfidence * 100) + "%, " + result.Words + " słów"
                + (result.RotationApplied != 0 ? ", obrót " + result.RotationApplied + "°" : "") + "]";
            return header + "\n" + (result.Text.Length == 0 ? "(nie rozpoznano tekstu)" : result.Text) + "\n\n";
        }
        catch (Exception e) when (e is OcrException || e is IOException || e is InvalidOperationException || e is DllNotFoundException || e is System.Reflection.TargetInvocationException || e is TypeInitializationException)
        {
            return "[obraz wklejony: " + Path.GetFileName(path) + " — OCR nie powiódł się: " + e.Message + "]\n";
        }
    }

    private async Task DownloadAsync()
    {
        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var progress = new Progress<string>(text => Status = text);
            var downloaded = await OcrLanguages.DownloadMissingAsync(TessdataPath, Languages, progress, CancellationToken.None);
            Status = downloaded.Count == 0 ? "Wszystkie dane języków już są w " + TessdataPath + "." : "Pobrano: " + string.Join(", ", downloaded) + " do " + TessdataPath + ".";
        }
        catch (Exception e) when (e is HttpRequestException || e is IOException || e is TaskCanceledException || e is UnauthorizedAccessException)
        {
            Status = "Nie udało się pobrać danych języków: " + e.Message + " Pliki *.traineddata można też wgrać ręcznie do " + TessdataPath + ".";
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
            RefreshLanguagesStatus();
        }
    }

    private async Task TestOnFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Próba OCR — wybierz obraz albo PDF",
            Filter = "Obrazy i PDF|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif;*.webp;*.pdf|Wszystkie pliki|*.*",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        Status = "OCR pliku " + Path.GetFileName(dialog.FileName) + "…";
        try
        {
            var file = dialog.FileName;
            var options = ToOptions(_settings, [Path.GetDirectoryName(file) ?? ""], Path.GetDirectoryName(file));
            var result = await Task.Run(() =>
            {
                var engine = new OcrEngine(options);
                return OcrOptions.IsPdf(file) ? engine.RecognizePdf(file, [1]) : engine.RecognizeFile(file);
            });
            var preview = new StringBuilder(result.Summary());
            foreach (var note in result.Notes)
            {
                preview.Append(" · ").Append(note);
            }

            var lines = result.Text.Split('\n').Where(l => l.Trim().Length > 0).Take(6).ToList();
            preview.Append('\n').Append(string.Join("\n", lines));
            if (result.Text.Split('\n').Length > 6)
            {
                preview.Append("\n…");
            }

            Status = preview.ToString();
        }
        catch (Exception e) when (e is OcrException || e is IOException || e is InvalidOperationException || e is DllNotFoundException || e is System.Reflection.TargetInvocationException || e is TypeInitializationException)
        {
            Status = "Błąd OCR: " + e.Message;
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
            RefreshLanguagesStatus();
        }
    }

    private void RefreshLanguagesStatus()
    {
        var available = OcrLanguages.Available(TessdataPath);
        var missing = OcrLanguages.Missing(TessdataPath, Languages, withOrientation: Rotation == "auto");
        LanguagesStatus = (available.Count == 0 ? "Brak pobranych języków." : "Pobrane: " + string.Join(", ", available) + ".")
            + (missing.Count == 0 ? " Wszystko gotowe." : " Brakuje: " + string.Join(", ", missing.Select(OcrLanguages.Describe)) + " — pobierz przyciskiem (kilka MB na język) albo program pobierze przy pierwszym użyciu.");
        OnPropertyChanged(nameof(LanguagesStatus));
    }

    private static string NormalizeLanguages(string? value)
    {
        var codes = OcrLanguages.Split(value ?? "").Where(c => c.Length == 3 && c.All(char.IsLetter)).ToList();
        return codes.Count == 0 ? "pol+eng" : string.Join("+", codes);
    }

    private void Update(Action apply, string property)
    {
        apply();
        _saveSettings();
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(Summary));
        RefreshLanguagesStatus();
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.ComponentModel.Win32Exception)
        {
            // Eksplorator się nie otworzył — ścieżka jest w polu.
        }
    }
}
