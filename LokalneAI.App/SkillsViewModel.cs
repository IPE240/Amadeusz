using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using LokalneAI.Core.Storage;
using LokalneAI.Harness.Tools;

namespace LokalneAI.App;

/// <summary>
/// Ustawienia → „Umiejętności”: lista umiejętności (foldery z <c>profile\</c>) z opisem i stanem wiedzy, a przy każdej
/// „Źródła wiedzy…” (repozytoria i pliki na dysku), „Otwórz folder” i „Aktualizuj” (pobranie dokumentacji z <c>zrodla.json</c>).
/// Po zmianie zakładka Code buduje sesję od nowa (<see cref="Changed"/>).
/// </summary>
public sealed class SkillsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private string _status = "";
    private bool _busy;

    public SkillsViewModel(AppSettings settings, Action saveSettings, string skillsRoot)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        SkillsRoot = skillsRoot;
        SyncAllCommand = new RelayCommand(SyncAllAsync, () => !_busy);
        OpenRootCommand = new RelayCommand(() => OpenFolder(SkillsRoot));
        Reload();
    }

    /// <summary>Folder z umiejętnościami (<c>profile\</c> obok programu).</summary>
    public string SkillsRoot { get; }

    public ObservableCollection<SkillRow> Rows { get; } = [];

    public RelayCommand SyncAllCommand { get; }

    public RelayCommand OpenRootCommand { get; }

    /// <summary>Do nagłówka sekcji: „8 umiejętności”.</summary>
    public string Summary => Rows.Count == 1 ? "1 umiejętność" : Rows.Count + " umiejętności";

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Zmieniła się wiedza albo jej źródła — zakładka Code zbuduje sesję od nowa.</summary>
    public event EventHandler? Changed;

    /// <summary>Folder z kopiami repozytoriów (<c>%LOCALAPPDATA%\LokalneAI\repo</c>).</summary>
    public static string RepoCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "repo");

    public void Reload()
    {
        Rows.Clear();
        foreach (var name in ProfilesFolder.List(SkillsRoot))
        {
            Rows.Add(new SkillRow(this, name, Path.Combine(SkillsRoot, name)));
        }

        OnPropertyChanged(nameof(Summary));
    }

    internal List<string> DiskPaths(string skill)
        => _settings.DiskKnowledge is { } map && map.TryGetValue(skill, out var list) ? list.ToList() : [];

    internal void OpenSources(SkillRow row)
    {
        var window = new SourcesWindow(row.Name, row.Folder, RepoCache, DiskPaths(row.Name), updated =>
        {
            _settings.DiskKnowledge ??= [];
            if (updated.Count == 0)
            {
                _settings.DiskKnowledge.Remove(row.Name);
            }
            else
            {
                _settings.DiskKnowledge[row.Name] = updated;
            }

            _saveSettings();
        })
        { Owner = Application.Current?.MainWindow };
        window.ShowDialog();
        row.Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal static void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(e.Message, "Eksplorator");
        }
    }

    /// <summary>Pobiera dokumentację umiejętności z jej <c>zrodla.json</c>.</summary>
    internal async Task SyncAsync(SkillRow row)
    {
        if (!row.HasSources)
        {
            Status = "Umiejętność " + row.Name + " nie ma źródeł w repozytoriach — dodaj je przyciskiem „Źródła wiedzy…”.";
            return;
        }

        _busy = true;
        row.IsSyncing = true;
        Status = "Aktualizuję wiedzę umiejętności " + row.Name + "…";
        try
        {
            var result = await Task.Run(() => KnowledgeSync.SyncAsync(row.Folder, RepoCache));
            Status = "Wiedza umiejętności " + row.Name + ": " + result + ". Następne polecenie w zakładce Code użyje nowych plików.";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Status = "Aktualizacja wiedzy " + row.Name + " nie powiodła się: " + e.Message;
        }
        finally
        {
            row.IsSyncing = false;
            row.Refresh();
            _busy = false;
        }
    }

    private async Task SyncAllAsync()
    {
        var withSources = Rows.Where(r => r.HasSources).ToList();
        if (withSources.Count == 0)
        {
            Status = "Żadna umiejętność nie ma źródeł w repozytoriach.";
            return;
        }

        foreach (var row in withSources)
        {
            await SyncAsync(row);
        }
    }
}

/// <summary>Wiersz sekcji „Umiejętności”.</summary>
public sealed class SkillRow : ObservableObject
{
    private readonly SkillsViewModel _owner;
    private bool _isSyncing;

    public SkillRow(SkillsViewModel owner, string name, string folder)
    {
        _owner = owner;
        Name = name;
        Folder = folder;
        Description = CloudViewModel.SafeDescription(folder) ?? "";
        SourcesCommand = new RelayCommand(() => _owner.OpenSources(this));
        OpenFolderCommand = new RelayCommand(() => SkillsViewModel.OpenFolder(Folder));
        SyncCommand = new RelayCommand(() => _owner.SyncAsync(this), () => !IsSyncing && HasSources);
    }

    public string Name { get; }

    public string Folder { get; }

    public string Description { get; }

    public RelayCommand SourcesCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public RelayCommand SyncCommand { get; }

    /// <summary>Czy umiejętność ma repozytoria albo adresy w <c>zrodla.json</c>.</summary>
    public bool HasSources => SafeSources().Count > 0;

    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetProperty(ref _isSyncing, value);
    }

    /// <summary>„2 źródła · 1 plik z dysku · aktualizacja 17.11 20:05” albo „bez źródeł”.</summary>
    public string SourcesSummary
    {
        get
        {
            var parts = new List<string>();
            var sources = SafeSources().Count;
            if (sources > 0)
            {
                parts.Add(sources == 1 ? "1 źródło" : sources + " źródła");
                var last = KnowledgeSync.LastSync(Folder);
                parts.Add(last is null ? "jeszcze nie pobrane" : "aktualizacja " + last.Value.ToString("dd.MM HH:mm"));
            }

            var disk = _owner.DiskPaths(Name).Count;
            if (disk > 0)
            {
                parts.Add(disk == 1 ? "1 ścieżka na dysku" : disk + " ścieżki na dysku");
            }

            return parts.Count == 0 ? "bez źródeł (sama wiedza z folderu)" : string.Join(" · ", parts);
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(SourcesSummary));
        OnPropertyChanged(nameof(HasSources));
    }

    private IList<KnowledgeSource> SafeSources()
    {
        try
        {
            return KnowledgeSync.ReadSources(Folder);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            return [];
        }
    }
}
