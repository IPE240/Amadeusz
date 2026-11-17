using System.Collections.ObjectModel;
using System.IO;
using LokalneAI.Core.Storage;

namespace LokalneAI.App;

/// <summary>
/// Ustawienia → „Profile”: nazwane zestawy umiejętności (<see cref="AppSettings.WorkProfiles"/>). Jeden wiersz = jeden
/// profil: nazwa, pola wyboru umiejętności (foldery z <c>profile\</c>) i ×. Każda zmiana zapisuje się od razu,
/// a zakładka Code odświeża listę profili (<see cref="Changed"/>).
/// </summary>
public sealed class WorkProfilesViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly string _skillsRoot;
    private bool _loading;

    public WorkProfilesViewModel(AppSettings settings, Action saveSettings, string skillsRoot)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _skillsRoot = skillsRoot;
        AddCommand = new RelayCommand(Add);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        Reload();
    }

    public ObservableCollection<WorkProfileRow> Rows { get; } = [];

    public RelayCommand AddCommand { get; }

    public RelayCommand RestoreDefaultsCommand { get; }

    /// <summary>Do nagłówka sekcji: „4 profile”.</summary>
    public string Summary => Rows.Count == 0 ? "brak profili"
        : Rows.Count == 1 ? "1 profil"
        : Rows.Count is >= 2 and <= 4 ? Rows.Count + " profile"
        : Rows.Count + " profili";

    /// <summary>Lista profili się zmieniła (zakładka Code ją przeładowuje).</summary>
    public event EventHandler? Changed;

    /// <summary>Wczytuje profile z ustawień (np. po „Zapisz jako profil…” w zakładce Code).</summary>
    public void Reload()
    {
        _loading = true;
        Rows.Clear();
        var skills = ProfilesFolder.List(_skillsRoot).ToList();
        foreach (var profile in _settings.WorkProfiles ?? WorkProfileSetting.Defaults())
        {
            Rows.Add(new WorkProfileRow(this, profile.Name, skills, profile.Skills));
        }

        _loading = false;
        OnPropertyChanged(nameof(Summary));
    }

    private void Add()
    {
        var skills = ProfilesFolder.List(_skillsRoot).ToList();
        Rows.Add(new WorkProfileRow(this, UniqueName("Nowy profil", null), skills, []));
        Commit();
    }

    /// <summary>Dopisuje domyślne profile, których nazw nie ma na liście (istniejących nie zmienia).</summary>
    private void RestoreDefaults()
    {
        var skills = ProfilesFolder.List(_skillsRoot).ToList();
        foreach (var profile in WorkProfileSetting.Defaults())
        {
            if (!Rows.Any(r => string.Equals(r.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                Rows.Add(new WorkProfileRow(this, profile.Name, skills, profile.Skills));
            }
        }

        Commit();
    }

    internal void Remove(WorkProfileRow row)
    {
        Rows.Remove(row);
        Commit();
    }

    /// <summary>Nazwa bez powtórzeń na liście: „Biuro”, „Biuro 2”…</summary>
    internal string UniqueName(string wanted, WorkProfileRow? except)
    {
        var name = wanted.Trim();
        var candidate = name;
        for (var i = 2; Rows.Any(r => r != except && string.Equals(r.Name, candidate, StringComparison.OrdinalIgnoreCase)); i++)
        {
            candidate = name + " " + i;
        }

        return candidate;
    }

    /// <summary>Zapisuje listę w ustawieniach. Umiejętności, których folderu tu nie ma, zostają w profilu (inny komputer może je mieć).</summary>
    internal void Commit()
    {
        if (_loading)
        {
            return;
        }

        _settings.WorkProfiles = Rows.Select(r => new WorkProfileSetting { Name = r.Name, Skills = r.SelectedSkills }).ToList();
        _saveSettings();
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal string? Describe(string skill) => CloudViewModel.SafeDescription(Path.Combine(_skillsRoot, skill));
}

/// <summary>Wiersz sekcji „Profile”.</summary>
public sealed class WorkProfileRow : ObservableObject
{
    private readonly WorkProfilesViewModel _owner;
    private readonly List<string> _unknownSkills;
    private string _name;

    public WorkProfileRow(WorkProfilesViewModel owner, string name, IReadOnlyList<string> allSkills, IReadOnlyCollection<string> chosen)
    {
        _owner = owner;
        _name = name;
        Skills = new ObservableCollection<SkillChoice>(allSkills.Select(s => new SkillChoice(this, s, chosen.Contains(s), owner.Describe(s))));
        _unknownSkills = chosen.Where(s => !allSkills.Contains(s)).ToList();
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    /// <summary>Nazwa profilu; zmiana zapisuje się po wyjściu z pola. Pusta = zostaje poprzednia.</summary>
    public string Name
    {
        get => _name;
        set
        {
            var name = string.IsNullOrWhiteSpace(value) ? _name : _owner.UniqueName(value, this);
            if (_name != name)
            {
                _name = name;
                _owner.Commit();
            }

            OnPropertyChanged(); // także gdy nazwa wróciła do poprzedniej albo dostała numer
        }
    }

    public ObservableCollection<SkillChoice> Skills { get; }

    /// <summary>Umiejętności profilu w kolejności listy (plus te, których na tym komputerze nie ma).</summary>
    public List<string> SelectedSkills => Skills.Where(s => s.IsChecked).Select(s => s.Name).Concat(_unknownSkills).ToList();

    /// <summary>Podpowiedź o umiejętnościach z profilu, których tu nie ma.</summary>
    public string UnknownText => _unknownSkills.Count == 0 ? "" : "(brak tutaj: " + string.Join(", ", _unknownSkills) + ")";

    public RelayCommand RemoveCommand { get; }

    internal void SkillToggled() => _owner.Commit();
}

/// <summary>Pole wyboru jednej umiejętności w wierszu profilu.</summary>
public sealed class SkillChoice : ObservableObject
{
    private readonly WorkProfileRow _row;
    private bool _isChecked;

    public SkillChoice(WorkProfileRow row, string name, bool isChecked, string? description)
    {
        _row = row;
        Name = name;
        _isChecked = isChecked;
        Description = description;
    }

    public string Name { get; }

    public string? Description { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
            {
                _row.SkillToggled();
            }
        }
    }
}
