using System.Net.Http;
using System.Collections.ObjectModel;
using System.Globalization;
using LokalneAI.Core;
using LokalneAI.Core.Ollama;
using LokalneAI.Core.Storage;
using LokalneAI.Gateway;

namespace LokalneAI.App;

/// <summary>
/// Ustawienia → Gdzie liczyć → „Komputery dla podagentów”: lista jak w Menedżerze poświadczeń (wiersz = komputer,
/// „+” dodaje, × usuwa, „Sprawdź” pyta bramę tamtego komputera o kartę i modele) plus przełącznik „Ten komputer”.
/// Zakładka Code buduje z tej listy gospodarza podagentów (<see cref="SubAgentHost"/>); zmiana listy przebudowuje sesję.
/// </summary>
public sealed class AgentMachinesViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _save;
    private string _thisComputerText = "Ten komputer";

    public AgentMachinesViewModel(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        AddCommand = new RelayCommand(() => Rows.Add(new AgentMachineRow(this, "", null)));
        foreach (var machine in settings.AgentMachines)
        {
            Rows.Add(new AgentMachineRow(this, machine.Address, machine.Name));
        }

        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Wiersze listy (zapisane i nowe, jeszcze bez adresu).</summary>
    public ObservableCollection<AgentMachineRow> Rows { get; } = [];

    public RelayCommand AddCommand { get; }

    /// <summary>Zmieniła się lista albo przełącznik (zakładka Code przebudowuje sesję).</summary>
    public event EventHandler? Changed;

    /// <summary>„Ten komputer (NVIDIA GeForce RTX 4060 (8 GB))” — opis karty dopisuje okno po jej wykryciu.</summary>
    public string ThisComputerText
    {
        get => _thisComputerText;
        set => SetProperty(ref _thisComputerText, value);
    }

    /// <summary>Podagenci mogą liczyć także na tym komputerze.</summary>
    public bool UseThisComputer
    {
        get => _settings.AgentsUseThisComputer;
        set
        {
            if (_settings.AgentsUseThisComputer == value)
            {
                return;
            }

            _settings.AgentsUseThisComputer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            _save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Do nagłówka sekcji: „podagenci: ten komputer + 2 inne”.</summary>
    public string Summary
    {
        get
        {
            var others = _settings.AgentMachines.Count;
            var parts = new List<string>();
            if (_settings.AgentsUseThisComputer)
            {
                parts.Add("ten komputer");
            }

            if (others > 0)
            {
                parts.Add(others == 1 ? "1 inny" : others + " inne");
            }

            return "podagenci: " + (parts.Count == 0 ? "wyłączeni" : string.Join(" + ", parts));
        }
    }

    /// <summary>Zapisuje listę z wierszy (puste adresy pomijane) i powiadamia zakładkę Code.</summary>
    internal void Commit()
    {
        _settings.AgentMachines = Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Address))
            .Select(r => new AgentMachineSetting { Address = r.Address.Trim(), Name = r.Name })
            .ToList();
        _settings.Normalize();
        OnPropertyChanged(nameof(Summary));
        _save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Remove(AgentMachineRow row)
    {
        Rows.Remove(row);
        Commit();
    }

    /// <summary>Sprawdza wszystkie wpisane komputery (po starcie okna, w tle).</summary>
    public async Task CheckAllAsync()
    {
        foreach (var row in Rows.Where(r => !string.IsNullOrWhiteSpace(r.Address)).ToList())
        {
            await row.CheckAsync();
        }
    }

    /// <summary>
    /// Pyta bramę komputera: czy jest, czy udostępnia, jaka karta i ile modeli. Zwraca opis do wiersza (✓/✗) i nazwę komputera.
    /// </summary>
    internal static async Task<(bool Ok, string Text, string? Name)> ProbeAsync(string address)
    {
        string url;
        try
        {
            url = GatewayServer.NormalizeUrl(address);
        }
        catch (ArgumentException e)
        {
            return (false, "✗ " + e.Message, null);
        }

        var probe = await GatewayServer.ProbeAsync(url, TimeSpan.FromSeconds(4));
        if (probe.Info is null)
        {
            return (false, "✗ " + probe.Error, null);
        }

        if (probe.Info.Computer == Environment.MachineName)
        {
            return (false, "✗ To ten komputer — zaznacz „Ten komputer” zamiast wpisywać jego adres.", probe.Info.Computer);
        }

        if (!probe.Info.Sharing)
        {
            return (false, $"✗ {probe.Info.Computer} ma LokalneAI, ale nie udostępnia modelu innym (przełącznik w jego Ustawieniach).", probe.Info.Computer);
        }

        var models = "";
        try
        {
            using var ollama = new OllamaClient(url) { ClientName = "Podagenci", MetadataTimeout = TimeSpan.FromSeconds(5) };
            var list = (await ollama.ListModelsAsync()).Where(ModelChooser.IsChatModel).ToList();
            models = list.Count == 0 ? " · brak modeli do rozmowy" : " · modeli: " + list.Count.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is OllamaException or HttpRequestException or TaskCanceledException)
        {
            models = " · lista modeli: " + e.Message;
        }

        var gpu = string.IsNullOrWhiteSpace(probe.Info.Gpu) ? "karta nieznana" : probe.Info.Gpu;
        return (true, $"✓ {probe.Info.Computer} · {gpu} · kontekst do {probe.Info.MaxContext}{models}", probe.Info.Computer);
    }
}

/// <summary>Wiersz listy komputerów dla podagentów.</summary>
public sealed class AgentMachineRow : ObservableObject
{
    private readonly AgentMachinesViewModel _owner;
    private string _address;
    private string? _name;
    private string _state = "";
    private bool _stateIsError;
    private bool _busy;

    public AgentMachineRow(AgentMachinesViewModel owner, string address, string? name)
    {
        _owner = owner;
        _address = address;
        _name = name;
        CheckCommand = new RelayCommand(CheckAsync, () => !_busy && Address.Trim().Length > 0);
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    /// <summary>Nazwa komputera (np. PC-ODS12) albo IP, port tylko gdy inny niż 11435. Zapis po wyjściu z pola.</summary>
    public string Address
    {
        get => _address;
        set
        {
            if (SetProperty(ref _address, value ?? ""))
            {
                _name = null;
                State = "";
                _owner.Commit();
            }
        }
    }

    /// <summary>Nazwa komputera z jego odpowiedzi; null przed sprawdzeniem.</summary>
    public string? Name => _name;

    /// <summary>Wynik sprawdzenia: „✓ PC-ODS12 · RTX 4090 (24 GB) · kontekst do 16384 · modeli: 5” albo „✗ …”.</summary>
    public string State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public bool StateIsError
    {
        get => _stateIsError;
        private set => SetProperty(ref _stateIsError, value);
    }

    public RelayCommand CheckCommand { get; }

    public RelayCommand RemoveCommand { get; }

    /// <summary>Pyta bramę komputera o kartę, kontekst i modele; nazwę zapisuje w ustawieniach.</summary>
    public async Task CheckAsync()
    {
        if (string.IsNullOrWhiteSpace(Address))
        {
            return;
        }

        _busy = true;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        StateIsError = false;
        State = "Sprawdzam…";
        try
        {
            var (ok, text, name) = await AgentMachinesViewModel.ProbeAsync(Address);
            StateIsError = !ok;
            State = text;
            if (ok && name is not null && name != _name)
            {
                _name = name;
                _owner.Commit();
            }
        }
        finally
        {
            _busy = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
}
