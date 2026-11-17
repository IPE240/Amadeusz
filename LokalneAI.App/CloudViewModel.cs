using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;
using LokalneAI.Core.Storage;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;

namespace LokalneAI.App;

/// <summary>
/// Ustawienia → „Modele w chmurze (test)”: klucze API (zaszyfrowane kontem Windows) i modele Claude, GPT, Gemini,
/// które mają być na liście modeli. Lista dostawców jest zamknięta (<see cref="CloudProvider.All"/>).
/// </summary>
public sealed class CloudViewModel : ObservableObject
{
    public CloudViewModel(AppSettings settings, CloudKeyStore keys, Action modelsChanged, Action save)
    {
        var profilesRoot = ProfilesFolder.Find();
        foreach (var provider in CloudProvider.All)
        {
            var row = new CloudProviderRow(provider, settings, keys, modelsChanged, save, AllowedIn(profilesRoot, provider));
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(CloudProviderRow.HasKey) or nameof(CloudProviderRow.AddedCount))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            };
            Providers.Add(row);
        }
    }

    public ObservableCollection<CloudProviderRow> Providers { get; } = [];

    /// <summary>Nagłówek zwiniętej sekcji: „Claude: 2 na liście · GPT: brak klucza · Gemini: brak klucza”.</summary>
    public string Summary => string.Join(" · ", Providers.Select(p => p.Provider.Family + ": "
        + (!p.HasKey ? "brak klucza" : p.AddedCount == 0 ? "klucz, brak modeli na liście" : p.AddedCount + " na liście")));

    /// <summary>Klucz API dostawcy: z programu (zaszyfrowany), a gdy go nie ma — ze zmiennej środowiskowej.</summary>
    public static string? ResolveKey(CloudKeyStore keys, CloudProvider provider)
        => keys.Get(provider.Id) is { Length: > 0 } key ? key : Environment.GetEnvironmentVariable(provider.KeyVariable);

    /// <summary>Ustawienia profilu albo null, gdy <c>profil.json</c> jest uszkodzony (wtedy profil nie dopuszcza chmury).</summary>
    internal static ProfileSettings? SafeSettings(string folder)
    {
        try
        {
            return HarnessProfile.LoadSettings(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Opis profilu (pole „opis” w profil.json) do podpowiedzi; null, gdy go nie ma albo plik jest uszkodzony.</summary>
    internal static string? SafeDescription(string folder)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "profil.json")));
            return document.RootElement.TryGetProperty("opis", out var opis) && opis.ValueKind == System.Text.Json.JsonValueKind.String ? opis.GetString() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Profile zakładki Code, które dopuszczają dostawcę (pole „chmura” w profil.json).</summary>
    private static string AllowedIn(string root, CloudProvider provider)
    {
        var allowed = ProfilesFolder.List(root)
            .Where(name => SafeSettings(Path.Combine(root, name))?.AllowsCloud(provider.Id) == true)
            .ToList();
        var denied = ProfilesFolder.List(root).Except(allowed).ToList();
        return "Zakładka Code: " + (allowed.Count == 0 ? "żaden profil" : "profile " + string.Join(", ", allowed))
            + (denied.Count == 0 ? "" : " (nie: " + string.Join(", ", denied) + ")") + ". Czat: tak.";
    }
}

/// <summary>Jeden dostawca: klucz, stan, lista modeli z pola wyboru „na liście”.</summary>
public sealed class CloudProviderRow : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly CloudKeyStore _keys;
    private readonly Action _modelsChanged;
    private readonly Action _save;
    private string _keyStatus = "";
    private string _status = "";
    private bool _statusIsError;
    private bool _isBusy;
    private string _manualModel = "";

    public CloudProviderRow(CloudProvider provider, AppSettings settings, CloudKeyStore keys, Action modelsChanged, Action save, string allowedIn)
    {
        Provider = provider;
        _settings = settings;
        _keys = keys;
        _modelsChanged = modelsChanged;
        _save = save;
        AllowedIn = allowedIn;
        Brush = ModelItem.BrushFor(provider);
        foreach (var chosen in settings.CloudModels.Where(m => m.Provider == provider.Id))
        {
            Models.Add(new CloudModelRow(this, chosen.Id, chosen.Name, isAdded: true));
        }

        SaveKeyCommand = new RelayCommand<PasswordBox>(SaveKeyAsync);
        RemoveKeyCommand = new RelayCommand(RemoveKey);
        FetchCommand = new RelayCommand(FetchAsync, () => !IsBusy);
        AddManualCommand = new RelayCommand(AddManual);
        OpenKeysPageCommand = new RelayCommand(OpenKeysPage);
        RefreshKeyStatus();
    }

    public CloudProvider Provider { get; }

    public string Title => Provider.Family + " (" + Provider.Name + ")";

    public Brush Brush { get; }

    /// <summary>Gdzie wolno tego dostawcy (profile zakładki Code).</summary>
    public string AllowedIn { get; }

    public string KeysPage => Provider.KeysPage;

    public ObservableCollection<CloudModelRow> Models { get; } = [];

    public RelayCommand<PasswordBox> SaveKeyCommand { get; }

    public RelayCommand RemoveKeyCommand { get; }

    public RelayCommand FetchCommand { get; }

    public RelayCommand AddManualCommand { get; }

    public RelayCommand OpenKeysPageCommand { get; }

    public string KeyStatus
    {
        get => _keyStatus;
        private set => SetProperty(ref _keyStatus, value);
    }

    public bool HasKey => CloudViewModel.ResolveKey(_keys, Provider) is { Length: > 0 };

    /// <summary>Ile modeli tego dostawcy jest na liście modeli okna.</summary>
    public int AddedCount => _settings.CloudModels.Count(m => m.Provider == Provider.Id);

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>Id modelu wpisane ręcznie (gdy nie ma go na liście dostawcy).</summary>
    public string ManualModel
    {
        get => _manualModel;
        set => SetProperty(ref _manualModel, value);
    }

    /// <summary>Pole wyboru „na liście” zmieniło się: dopisz albo usuń model z listy modeli okna.</summary>
    internal void OnAddedChanged(CloudModelRow row)
    {
        var existing = _settings.CloudModels.FirstOrDefault(m => m.Provider == Provider.Id && string.Equals(m.Id, row.Id, StringComparison.OrdinalIgnoreCase));
        if (row.IsAdded && existing is null)
        {
            _settings.CloudModels.Add(new CloudModelChoice { Provider = Provider.Id, Id = row.Id, Name = row.Name });
        }
        else if (!row.IsAdded && existing is not null)
        {
            _settings.CloudModels.Remove(existing);
            _settings.FavoriteModels.RemoveAll(f => string.Equals(f, AppSettings.ModelKey(Provider.Id, row.Id), StringComparison.OrdinalIgnoreCase));
        }

        OnPropertyChanged(nameof(AddedCount));
        _modelsChanged();
        _save();
    }

    private async Task SaveKeyAsync(PasswordBox? box)
    {
        var key = box?.Password.Trim() ?? "";
        if (key.Length == 0)
        {
            SetStatus("Wklej klucz API w pole obok.", error: true);
            return;
        }

        try
        {
            _keys.Set(Provider.Id, key);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            SetStatus("Nie zapisałem klucza: " + e.Message, error: true);
            return;
        }

        box!.Clear();
        RefreshKeyStatus();
        await FetchAsync();
    }

    private void RemoveKey()
    {
        try
        {
            _keys.Set(Provider.Id, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatus("Nie usunąłem klucza: " + e.Message, error: true);
            return;
        }

        RefreshKeyStatus();
        SetStatus(HasKey ? "Usunięto klucz z programu; zostaje klucz ze zmiennej " + Provider.KeyVariable + "." : "Usunięto klucz.", error: false);
        _modelsChanged();
    }

    /// <summary>Pobiera listę modeli dostawcy — to też sprawdza klucz. Nic nie kosztuje.</summary>
    private async Task FetchAsync()
    {
        var key = CloudViewModel.ResolveKey(_keys, Provider);
        if (string.IsNullOrEmpty(key))
        {
            SetStatus("Najpierw zapisz klucz API.", error: true);
            return;
        }

        IsBusy = true;
        SetStatus("Sprawdzam klucz i pobieram listę modeli…", error: false);
        try
        {
            var models = await CloudModels.ListAsync(Provider, key);
            foreach (var model in models)
            {
                var row = Models.FirstOrDefault(m => string.Equals(m.Id, model.Id, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    Models.Add(new CloudModelRow(this, model.Id, model.DisplayName == model.Id ? null : model.DisplayName, isAdded: false));
                }
            }

            SetStatus($"Klucz działa: {models.Count} modeli. Zaznacz te, które mają być na liście modeli.", error: false);
        }
        catch (LlmException e)
        {
            SetStatus(e.Message, error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddManual()
    {
        var id = ManualModel.Trim();
        if (id.Length == 0)
        {
            return;
        }

        var row = Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            row = new CloudModelRow(this, id, null, isAdded: false);
            Models.Insert(0, row);
        }

        row.IsAdded = true;
        ManualModel = "";
    }

    private void OpenKeysPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Provider.KeysPage) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus("Nie otworzyłem przeglądarki: " + e.Message, error: true);
        }
    }

    private void RefreshKeyStatus()
    {
        KeyStatus = _keys.Has(Provider.Id) ? "Klucz zapisany (zaszyfrowany kontem Windows)."
            : Environment.GetEnvironmentVariable(Provider.KeyVariable) is { Length: > 0 } ? "Klucz ze zmiennej środowiskowej " + Provider.KeyVariable + "."
            : "Brak klucza.";
        OnPropertyChanged(nameof(HasKey));
    }

    private void SetStatus(string text, bool error)
    {
        Status = text;
        StatusIsError = error;
    }
}

/// <summary>Model dostawcy z polem wyboru „na liście modeli”.</summary>
public sealed class CloudModelRow : ObservableObject
{
    private readonly CloudProviderRow _owner;
    private bool _isAdded;

    public CloudModelRow(CloudProviderRow owner, string id, string? name, bool isAdded)
    {
        _owner = owner;
        Id = id;
        Name = name;
        _isAdded = isAdded;
    }

    public string Id { get; }

    public string? Name { get; }

    public string Display => Name is null ? Id : Name + "  (" + Id + ")";

    public bool IsAdded
    {
        get => _isAdded;
        set
        {
            if (SetProperty(ref _isAdded, value))
            {
                _owner.OnAddedChanged(this);
            }
        }
    }
}
