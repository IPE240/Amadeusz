using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LokalneAI.Core;
using LokalneAI.Core.Ollama;
using LokalneAI.Core.Storage;
using System.Net.Http;
using System.Text.Json;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.App;

/// <summary>Logika okna testowego: pytanie → Ollama (lokalny model) → odpowiedź ze statystykami.</summary>
public sealed class MainViewModel : ObservableObject
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
    // Te same kolory co GoodBrush, BadBrush i MutedBrush w Theme.xaml (styl TransmittalMaster).
    private static readonly Brush GoodBrush = Frozen(0x2E, 0x7D, 0x4F);
    private static readonly Brush BadBrush = Frozen(0xB3, 0x26, 0x1E);
    private static readonly Brush NeutralBrush = Frozen(0x6A, 0x76, 0x86);

    private readonly StartupOptions _options;
    private readonly SettingsStore _settingsStore;
    private readonly HistoryLog _historyLog = new();
    private readonly AppSettings _settings;
    /// <summary>Klucze API modeli w chmurze, zaszyfrowane kontem Windows.</summary>
    private readonly CloudKeyStore _keys;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Czat: brama LokalneAI (kolejka, wybrany komputer), a gdy jej nie ma — Ollama na tym PC.</summary>
    private OllamaClient _client;

    /// <summary>Ollama na tym PC: pobieranie modeli, zwalnianie pamięci, uruchamianie.</summary>
    private OllamaClient _local;
    private ExchangeViewModel? _waitingExchange;
    private CancellationTokenSource? _cts;
    private string? _gpuName;
    private string? _gpuDescription;
    private double? _gpuGb;
    private string? _modelToPull;
    private bool _refreshing;
    /// <summary>Modele z Ollamy (tego albo wybranego komputera), bez modeli „-cloud” liczonych na ollama.com.</summary>
    private List<LocalModel> _localModels = [];

    private ModelItem? _selectedModel;
    private string _question = "";
    private bool _isBusy;
    private string _ollamaStatus = "Sprawdzam Ollamę…";
    private Brush _ollamaStatusBrush = NeutralBrush;
    private string _gpuText = "Sprawdzam kartę graficzną…";
    private string _bannerText = "";
    private bool _canPull;
    private bool _canStartOllama;
    private bool _showInstallHint;
    private bool _isPulling;
    private double _pullPercent;
    private string _systemPrompt;
    private string _temperatureText;
    private int _contextLength;
    private bool _rememberConversation;
    private bool _saveHistory;
    private string _ollamaUrl;
    private string _statusText = "";

    public MainViewModel(StartupOptions options)
    {
        _options = options;
        _settingsStore = new SettingsStore(options.SettingsPath);
        _keys = new CloudKeyStore(options.KeysPath);
        _settings = _settingsStore.Load();
        if (options.Url is not null)
        {
            _settings.OllamaUrl = options.Url;
        }

        if (options.Model is not null)
        {
            _settings.Model = options.Model;
        }

        _systemPrompt = _settings.SystemPrompt;
        _temperatureText = _settings.Temperature.ToString("0.0##", Pl);
        _contextLength = _settings.ContextLength;
        _rememberConversation = _settings.RememberConversation;
        _saveHistory = _settings.SaveHistory;

        try
        {
            _local = new OllamaClient(_settings.OllamaUrl);
        }
        catch (ArgumentException e)
        {
            _local = new OllamaClient();
            _bannerText = e.Message + " Używam adresu domyślnego.";
        }

        _client = _local;
        _ollamaUrl = _local.BaseUrl;
        Network = new NetworkViewModel(_settings, options, OnComputeTargetChangedAsync);
        Network.Queue.CollectionChanged += (_, _) => ShowChatQueuePosition();
        Network.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NetworkViewModel.ComputeWhereText))
            {
                OnPropertyChanged(nameof(ComputeWhereText));
            }
        };
        Cloud = new CloudViewModel(_settings, _keys, RebuildModels, SaveSettings);
        Credentials = new CredentialsViewModel(new CredentialVault(), _settings, _keys, SaveSettings);
        Agents = new AgentMachinesViewModel(_settings, SaveSettings);
        if (_settings.AddDefaultSkills(ProfilesFolder.List(ProfilesFolder.Find())))
        {
            SaveSettings(); // np. nowa umiejętność „formatowanie” dopisana raz do profili i do wyboru w zakładce Code
        }

        WorkProfiles = new WorkProfilesViewModel(_settings, SaveSettings, ProfilesFolder.Find());
        Skills = new SkillsViewModel(_settings, SaveSettings, ProfilesFolder.Find());
        Ocr = new OcrViewModel(_settings, SaveSettings);
        if (_settingsStore.LoadError is { } loadError)
        {
            _statusText = $"Nie odczytałem ustawień ({loadError}), używam domyślnych.";
        }

        SendCommand = new RelayCommand(SendAsync, () => !IsBusy && SelectedModel is not null && Question.Trim().Length > 0);
        StopCommand = new RelayCommand(Stop, () => IsBusy);
        RefreshCommand = new RelayCommand(RefreshAsync, () => !IsBusy);
        PullCommand = new RelayCommand(PullAsync, () => !IsBusy && CanPull);
        StartOllamaCommand = new RelayCommand(StartOllamaAsync, () => !IsBusy && CanStartOllama);
        UnloadCommand = new RelayCommand(UnloadAsync, () => !IsBusy);
        ClearCommand = new RelayCommand(Clear, () => !IsBusy && Exchanges.Count > 0);
        CopyInstallCommand = new RelayCommand(CopyInstallCommandToClipboard);
        ToggleFavoriteCommand = new RelayCommand<ModelItem>(ToggleFavorite);
        Exchanges.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsTranscriptEmpty));
    }

    public ObservableCollection<ModelItem> Models { get; } = [];

    /// <summary>Ustawienia → „Gdzie liczyć”, udostępnianie i kolejka.</summary>
    public NetworkViewModel Network { get; }

    /// <summary>Ustawienia → „Modele w chmurze (test)”: klucze API i modele Claude, GPT, Gemini.</summary>
    public CloudViewModel Cloud { get; }

    /// <summary>Ustawienia → Poświadczenia: wpisy PostgreSQL, FTP, SMB w Menedżerze poświadczeń Windows.</summary>
    public CredentialsViewModel Credentials { get; }

    /// <summary>Ustawienia → „Profile”: nazwane zestawy umiejętności do zakładki Code.</summary>
    public WorkProfilesViewModel WorkProfiles { get; }

    /// <summary>Ustawienia → „Umiejętności”: lista, źródła wiedzy, folder, aktualizacja dokumentacji.</summary>
    public SkillsViewModel Skills { get; }

    /// <summary>Ustawienia → OCR (Tesseract: języki, obrót, układ, reguły; wklejony obrazek → OCR).</summary>
    public OcrViewModel Ocr { get; }

    private readonly LokalneAI.Core.Hardware.TemperatureHistory _temperature = new();
    private System.Windows.Threading.DispatcherTimer? _temperatureTimer;
    private string _temperatureSource = "";
    private bool _temperatureBusy;

    /// <summary>Temperatura karty komputera, który liczy (ostatnie 2 minuty) — wykres na pasku tytułu.</summary>
    public LokalneAI.Core.Hardware.TemperatureHistory Temperature => _temperature;

    /// <summary>Po każdej próbce temperatury (co 5 s).</summary>
    public event EventHandler? TemperatureUpdated;

    /// <summary>Włącza próbkowanie temperatury co 5 s (wołane raz, po otwarciu okna).</summary>
    public void StartTemperature()
    {
        if (_temperatureTimer is not null)
        {
            return;
        }

        _temperatureTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _temperatureTimer.Tick += async (_, _) => await SampleTemperatureAsync();
        _temperatureTimer.Start();
        _ = SampleTemperatureAsync();
    }

    /// <summary>
    /// Jedna próbka: ten komputer — NVML; inny komputer w sieci — pole „temperatura” z jego bramy (<c>/lokalneai/info</c>);
    /// model w chmurze — brak wykresu. Po zmianie komputera historia zaczyna się od nowa.
    /// </summary>
    private async Task SampleTemperatureAsync()
    {
        if (_temperatureBusy)
        {
            return;
        }

        _temperatureBusy = true;
        try
        {
            string source;
            int? celsius = null;
            if (SelectedModel?.Cloud is not null)
            {
                source = "chmura";
            }
            else if (Network.RemoteUrl is { } url)
            {
                source = url;
                var probe = await LokalneAI.Gateway.GatewayServer.ProbeAsync(url, TimeSpan.FromSeconds(3));
                celsius = probe.Info?.Temperature;
            }
            else
            {
                source = "ten komputer";
                celsius = await Task.Run(LokalneAI.Core.Hardware.GpuTemperature.Read);
            }

            if (source != _temperatureSource)
            {
                _temperature.Clear();
                _temperatureSource = source;
            }

            if (celsius is { } value)
            {
                _temperature.Add(DateTime.Now, value);
            }

            TemperatureUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // brak odczytu — wykres po prostu nie dostaje próbki
        }
        finally
        {
            _temperatureBusy = false;
        }
    }

    /// <summary>Jeden klient internetu dla czatu i zakładki Code (DuckDuckGo, strony; sieć biura zablokowana).</summary>
    private readonly WebAccess _web = new();

    /// <summary>Przełącznik „Odblokuj dostęp do internetu” (czat i zakładka Code; zapamiętany).</summary>
    public bool InternetAccess
    {
        get => _settings.InternetAccess;
        set
        {
            if (_settings.InternetAccess == value)
            {
                return;
            }

            _settings.InternetAccess = value;
            OnPropertyChanged();
            StatusText = value
                ? "Internet odblokowany: czat i zakładka Code (poza umiejętnością cde) mogą szukać w sieci i czytać strony. Zapytania wyszukiwania idą do DuckDuckGo."
                : "Internet zablokowany.";
            InternetChanged?.Invoke(this, EventArgs.Empty);
            SaveSettings();
        }
    }

    /// <summary>Zmienił się przełącznik internetu (zakładka Code zaczyna nową sesję).</summary>
    public event EventHandler? InternetChanged;

    /// <summary>Ustawienia → Czat: tokeny i czasy przy wpisach zakładki Code (zapisywane od razu).</summary>
    public bool ShowTokens
    {
        get => _settings.ShowTokens;
        set
        {
            if (_settings.ShowTokens == value)
            {
                return;
            }

            _settings.ShowTokens = value;
            OnPropertyChanged();
            ShowTokensChanged?.Invoke(this, EventArgs.Empty);
            SaveSettings();
        }
    }

    /// <summary>Zmienił się przełącznik „Pokaż tokeny i czasy”.</summary>
    public event EventHandler? ShowTokensChanged;

    /// <summary>Ustawienia → Czat: wpisy „Krok N” w zakładce Code (ten sam przełącznik, co nad rozmową; zapisywany od razu).</summary>
    public bool ShowSteps
    {
        get => _settings.ShowSteps;
        set
        {
            if (_settings.ShowSteps == value)
            {
                return;
            }

            _settings.ShowSteps = value;
            OnPropertyChanged();
            ShowStepsChanged?.Invoke(this, EventArgs.Empty);
            SaveSettings();
        }
    }

    /// <summary>Zmienił się przełącznik „Pokaż kroki modelu” w Ustawieniach.</summary>
    public event EventHandler? ShowStepsChanged;

    /// <summary>Zakładka Code zmieniła przełącznik kroków — odśwież pole w Ustawieniach.</summary>
    public void RefreshShowSteps() => OnPropertyChanged(nameof(ShowSteps));

    /// <summary>Ustawienia → Gdzie liczyć → Komputery dla podagentów.</summary>
    public AgentMachinesViewModel Agents { get; }

    /// <summary>Gwiazdka na liście modeli: ulubione są na górze.</summary>
    public RelayCommand<ModelItem> ToggleFavoriteCommand { get; }

    /// <summary>Pasek stanu: kto liczy — ten komputer, inny komputer albo chmura dostawcy.</summary>
    public string ComputeWhereText => SelectedModel?.Cloud is { } provider ? "Liczy: chmura " + provider.Name : Network.ComputeWhereText;

    /// <summary>Kolor napisu „Liczy…”: dostawcy w chmurze albo akcent okna.</summary>
    public Brush ComputeWhereBrush => SelectedModel?.NameBrush
        ?? Application.Current?.TryFindResource("AccentBrush") as Brush
        ?? Brushes.SteelBlue;

    /// <summary>Zmienił się komputer, który liczy (zakładka Code odświeża opis).</summary>
    public event EventHandler? ComputeTargetChanged;

    /// <summary>Podpowiedź na pustej rozmowie.</summary>
    public string EmptyChatHint => SelectedModel?.Cloud is { } cloud
        ? $"Wpisz pytanie na dole i naciśnij Enter albo „Wyślij”.\nOdpowiada model w chmurze {cloud.Name}: pytanie i rozmowa idą przez internet do {cloud.Name}."
        : Network.IsRemote
        ? $"Wpisz pytanie na dole i naciśnij Enter albo „Wyślij”.\nOdpowiedź policzy komputer {Network.ComputerName}; rozmowa zostaje na tym komputerze."
        : "Wpisz pytanie na dole i naciśnij Enter albo „Wyślij”.\nOdpowiedź liczy model na tym komputerze, bez internetu.";

    /// <summary>Opis bramy w Ustawieniach.</summary>
    public string GatewayHint => Network.GatewayUrl is { } url
        ? $"Brama LokalneAI: {url}. Programy z biblioteką LokalneAI (LocalGateway.ChooseUrl) łączą się przez nią same: jedna kolejka i ten sam komputer co czat."
        : "Brama LokalneAI nie działa — czat łączy się bezpośrednio z Ollamą.";

    public ObservableCollection<ExchangeViewModel> Exchanges { get; } = [];

    public IReadOnlyList<int> ContextChoices => AppSettings.ContextChoices;

    public RelayCommand SendCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand PullCommand { get; }

    public RelayCommand StartOllamaCommand { get; }

    public RelayCommand UnloadCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand CopyInstallCommand { get; }

    /// <summary>Kończy się po pierwszym sprawdzeniu Ollamy i karty graficznej.</summary>
    public Task WhenInitialized => _initialized.Task;

    public string HistoryPath => _historyLog.Path;

    public bool IsTranscriptEmpty => Exchanges.Count == 0;

    public ModelItem? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ComputeWhereText));
            OnPropertyChanged(nameof(ComputeWhereBrush));
            OnPropertyChanged(nameof(EmptyChatHint));
            ComputeTargetChanged?.Invoke(this, EventArgs.Empty);
            if (value is null || _refreshing)
            {
                return;
            }

            // Wybór użytkownika: zapamiętaj go i schowaj baner o brakującym modelu domyślnym.
            _settings.Model = value.Name;
            _settings.ModelProvider = value.Provider;
            if (CanPull)
            {
                CanPull = false;
                BannerText = "";
            }

            _ = UpdateRunningStatusAsync();
        }
    }

    public string Question
    {
        get => _question;
        set => SetProperty(ref _question, value);
    }

    /// <summary>Trwa odpowiedź modelu albo pobieranie: większość przycisków jest wtedy wyłączona.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public string OllamaStatus
    {
        get => _ollamaStatus;
        private set => SetProperty(ref _ollamaStatus, value);
    }

    public Brush OllamaStatusBrush
    {
        get => _ollamaStatusBrush;
        private set => SetProperty(ref _ollamaStatusBrush, value);
    }

    public string GpuText
    {
        get => _gpuText;
        private set => SetProperty(ref _gpuText, value);
    }

    public string BannerText
    {
        get => _bannerText;
        private set
        {
            if (SetProperty(ref _bannerText, value))
            {
                OnPropertyChanged(nameof(HasBanner));
            }
        }
    }

    public bool HasBanner => BannerText.Length > 0;

    public bool CanPull
    {
        get => _canPull;
        private set
        {
            if (SetProperty(ref _canPull, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanStartOllama
    {
        get => _canStartOllama;
        private set
        {
            if (SetProperty(ref _canStartOllama, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool ShowInstallHint
    {
        get => _showInstallHint;
        private set => SetProperty(ref _showInstallHint, value);
    }

    public bool IsPulling
    {
        get => _isPulling;
        private set => SetProperty(ref _isPulling, value);
    }

    public double PullPercent
    {
        get => _pullPercent;
        private set => SetProperty(ref _pullPercent, value);
    }

    public string SystemPrompt
    {
        get => _systemPrompt;
        set => SetProperty(ref _systemPrompt, value);
    }

    public string TemperatureText
    {
        get => _temperatureText;
        set
        {
            if (SetProperty(ref _temperatureText, value))
            {
                OnPropertyChanged(nameof(ChatSummary));
            }
        }
    }

    public int ContextLength
    {
        get => _contextLength;
        set
        {
            if (SetProperty(ref _contextLength, value))
            {
                OnPropertyChanged(nameof(ChatSummary));
            }
        }
    }

    /// <summary>Nagłówek zwiniętej sekcji „Czat” w Ustawieniach.</summary>
    public string ChatSummary => $"temperatura {TemperatureText} · kontekst {ContextLength} · historia {(SaveHistory ? "zapisywana" : "niezapisywana")}";

    public bool RememberConversation
    {
        get => _rememberConversation;
        set => SetProperty(ref _rememberConversation, value);
    }

    public bool SaveHistory
    {
        get => _saveHistory;
        set
        {
            if (SetProperty(ref _saveHistory, value))
            {
                OnPropertyChanged(nameof(ChatSummary));
            }
        }
    }

    /// <summary>Czy sekcja Ustawień (nazwa z okna, np. <c>SectionCloud</c>) jest rozwinięta. Domyślnie tylko „Gdzie liczyć”.</summary>
    public bool IsSectionOpen(string name) => ((IReadOnlyList<string>?)_settings.OpenSections ?? DefaultOpenSections).Contains(name, StringComparer.Ordinal);

    /// <summary>Zapamiętuje zwinięcie albo rozwinięcie sekcji Ustawień.</summary>
    public void SetSectionOpen(string name, bool open)
    {
        var sections = ((IReadOnlyList<string>?)_settings.OpenSections ?? DefaultOpenSections).Where(s => s != name).ToList();
        if (open)
        {
            sections.Add(name);
        }

        _settings.OpenSections = sections;
        SaveSettings();
    }

    private static readonly IReadOnlyList<string> DefaultOpenSections = ["SectionCompute"];

    public string OllamaUrl
    {
        get => _ollamaUrl;
        set
        {
            if (SetProperty(ref _ollamaUrl, value))
            {
                _ = RefreshAsync();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Komunikat na pasku stanu z okna (np. postęp OCR obrazka wklejonego do pytania).</summary>
    internal void ShowStatus(string text) => StatusText = text;

    public async Task InitializeAsync()
    {
        try
        {
            var cards = await Task.Run(() => GpuInfo.Detect());
            var card = cards.FirstOrDefault();
            _gpuName = card?.Name;
            _gpuDescription = card?.Describe();
            _gpuGb = card?.TotalMiB is int mib ? mib / 1024.0 : null;
            Agents.ThisComputerText = card is null ? "Ten komputer (karta nieznana — model policzy procesor)" : "Ten komputer (" + card.Describe() + ")";
            _ = Agents.CheckAllAsync();
            GpuText = card is null ? "Nie wykryłem karty graficznej — model policzy procesor" : "Karta: " + card.Describe();
            await Network.StartAsync(_local.BaseUrl, card?.Describe());
            UseGateway();
            await RefreshAsync();
        }
        finally
        {
            _initialized.TrySetResult();
        }
    }

    /// <summary>Sprawdza Ollamę, odświeża listę modeli i baner z podpowiedzią, co zrobić.</summary>
    public async Task RefreshAsync()
    {
        if (!EnsureClient())
        {
            return;
        }

        var remote = Network.IsRemote;
        OllamaStatus = remote ? $"Sprawdzam {Network.ComputerName}…" : "Sprawdzam Ollamę…";
        OllamaStatusBrush = NeutralBrush;
        GpuText = remote
            ? "Liczy " + Network.ComputerName + (Network.RemoteGpu is null ? "" : ": " + Network.RemoteGpu)
            : _gpuDescription is null ? "Nie wykryłem karty graficznej — model policzy procesor" : "Karta: " + _gpuDescription;
        var ollamaWorks = false;
        _refreshing = true;
        try
        {
            var version = await _client.GetVersionAsync();
            OllamaStatus = remote ? $"● {Network.ComputerName}: Ollama {version}" : $"● Ollama {version} działa";
            OllamaStatusBrush = GoodBrush;
            CanStartOllama = false;
            ShowInstallHint = false;

            var wanted = _settings.Model;
            var all = (await _client.ListModelsAsync())
                .Where(m => m.Capabilities is null || m.Capabilities.Contains("completion"))
                .ToList();

            // Modele „-cloud” liczy ollama.com, nie komputer w biurze — z chmury wolno tylko Claude, GPT i Gemini (Ustawienia).
            _localModels = all.Where(m => !CloudProvider.IsOllamaCloudModel(m.Name))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            RebuildModels();
            var hiddenCloud = all.Count - _localModels.Count;

            if (_settings.ModelProvider is not null || _localModels.Any(m => SameModel(m.Name, wanted)))
            {
                CanPull = false;
                BannerText = hiddenCloud > 0 ? $"Ukryto modele z chmury Ollamy ({hiddenCloud}): liczy je ollama.com, a z chmury wolno tylko Claude, GPT i Gemini." : "";
            }
            else if (remote)
            {
                CanPull = false;
                BannerText = _localModels.Count == 0
                    ? $"Na komputerze {Network.ComputerName} nie ma żadnego modelu."
                    : $"Na komputerze {Network.ComputerName} nie ma modelu {wanted}. Wybierz model z listy (to modele tamtego komputera).";
            }
            else
            {
                _modelToPull = wanted;
                CanPull = true;
                var size = SameModel(wanted, AppSettings.DefaultModel) ? " (ok. 6,7 GB)" : "";
                BannerText = $"W Ollamie nie ma modelu {wanted}. Pobierz go{size} albo wybierz inny model z listy.";
            }

            ollamaWorks = true;
        }
        catch (OllamaException e)
        {
            OllamaStatusBrush = BadBrush;
            // Modele w chmurze zostają na liście — działają bez Ollamy.
            _localModels = [];
            RebuildModels();
            CanPull = false;
            StatusText = "";
            if (remote)
            {
                OllamaStatus = $"● {Network.ComputerName} nie odpowiada";
                CanStartOllama = false;
                ShowInstallHint = false;
                BannerText = e.Message + " Zmień komputer w Ustawieniach → Gdzie liczyć.";
            }
            else
            {
                OllamaStatus = "● Ollama nie odpowiada";
                var exe = OllamaLauncher.Find();
                CanStartOllama = exe is not null && IsLoopback(_local.BaseUrl);
                ShowInstallHint = exe is null;
                BannerText = exe is null
                    ? $"Na tym komputerze nie ma Ollamy. Zainstaluj ją w PowerShellu poleceniem: {OllamaLauncher.InstallCommand} — potem kliknij „Sprawdź ponownie”."
                    : e.Message;
            }
        }
        finally
        {
            _refreshing = false;
            CommandManager.InvalidateRequerySuggested();
        }

        if (ollamaWorks)
        {
            await UpdateRunningStatusAsync();
        }
    }

    /// <summary>Skończyła się odpowiedź w czacie (także z błędem albo przerwana) — panel rozmów zapisuje rozmowę.</summary>
    public event EventHandler? ChatTurnEnded;

    /// <summary>Okno czatu wyczyszczone („Wyczyść rozmowę”, „+ Nowa rozmowa”) — następne pytanie zaczyna nową rozmowę.</summary>
    public event EventHandler? ChatCleared;

    /// <summary>Wysyła pytanie do modelu i pokazuje odpowiedź na bieżąco, słowo po słowie.</summary>
    public async Task SendAsync()
    {
        var before = Exchanges.Count;
        try
        {
            await SendCoreAsync();
        }
        finally
        {
            if (Exchanges.Count > before)
            {
                ChatTurnEnded?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Nowa, pusta rozmowa w czacie (zapisana zostaje w panelu rozmów).</summary>
    public void NewChat() => Clear();

    /// <summary>Wczytuje zapisaną rozmowę do okna czatu (z „Pamiętaj rozmowę” model dostanie jej wcześniejsze pytania).</summary>
    public void LoadChat(IEnumerable<ExchangeViewModel> exchanges, string title)
    {
        Exchanges.Clear();
        foreach (var exchange in exchanges)
        {
            Exchanges.Add(exchange);
        }

        StatusText = "Rozmowa „" + title + "”" + (RememberConversation ? " — model pamięta jej wcześniejsze pytania." : " — włącz „Pamiętaj rozmowę”, żeby model znał wcześniejsze pytania.");
    }

    private async Task SendCoreAsync()
    {
        var question = Question.Trim();
        if (question.Length == 0 || IsBusy)
        {
            return;
        }

        if (SelectedModel is not { } selected)
        {
            StatusText = "Nie wybrano modelu — sprawdź, czy Ollama działa i ma pobrany model.";
            return;
        }

        if (!TryParseTemperature(TemperatureText, out var temperature))
        {
            StatusText = "Temperatura musi być liczbą od 0 do 2, np. 0,1.";
            return;
        }

        // „Popraw / jeszcze raz”: wyższa temperatura (przy 0,1 to samo pytanie daje prawie to samo).
        if (TurnIntent.AsksForCorrection(question) && Exchanges.Any(e => e.Completed && !e.IsError))
        {
            temperature = Math.Max(temperature, 0.5);
        }

        if (InternetAccess)
        {
            await SendWithInternetAsync(selected, question, temperature);
            return;
        }

        if (TurnIntent.AsksForWeb(question))
        {
            StatusText = "Prosisz o sprawdzenie w internecie, a dostęp jest zablokowany — włącz „Odblokuj dostęp do internetu” na górze okna.";
        }

        if (selected.Cloud is { } provider)
        {
            await SendCloudAsync(selected, provider, question, temperature);
            return;
        }

        var model = selected.Name;
        var messages = BuildMessages(question);
        var exchange = new ExchangeViewModel(question, model, Network.IsRemote ? Network.ComputerName : null);
        Exchanges.Add(exchange);
        Question = "";
        IsBusy = true;
        StatusText = "Model liczy… (Esc albo Stop przerywa)";

        // Komunikat o ładowaniu tylko wtedy, gdy modelu naprawdę nie ma w pamięci (albo ma inny kontekst — Ollama go przeładuje).
        var loaded = await TryGetRunningAsync(model);
        exchange.Stats = loaded != null && (loaded.ContextLength is null || loaded.ContextLength == ContextLength)
            ? "Model liczy…"
            : "Ładuję model do pamięci karty… (po przerwie albo zmianie kontekstu trwa to kilka–kilkanaście sekund)";

        var cts = new CancellationTokenSource();
        _cts = cts;
        _waitingExchange = exchange;
        var request = new ChatRequest
        {
            Model = model,
            Messages = messages,
            Options = new ChatOptions { Temperature = temperature, NumCtx = ContextLength, NumPredict = MaxAnswerTokens },
            // Bez „myślenia”, jak w zakładce Code: qwen3.5 myślał po kilkadziesiąt sekund, a raz wpadł w pętlę bez końca.
            Think = false,
        };
        var clock = Stopwatch.StartNew();
        TimeSpan? firstToken = null;
        ChatChunk? last = null;
        var text = new StringBuilder();
        var outcome = "ok";
        string? error = null;
        try
        {
            await foreach (var chunk in _client.ChatStreamAsync(request, cts.Token))
            {
                var piece = chunk.Message?.Content;
                if (!string.IsNullOrEmpty(piece))
                {
                    if (firstToken is null)
                    {
                        firstToken = clock.Elapsed;
                        _waitingExchange = null;
                        exchange.Stats = "Odpowiada…";
                    }

                    text.Append(piece);
                    exchange.Answer = text.ToString();
                }

                if (chunk.Done)
                {
                    last = chunk;
                }
            }

            clock.Stop();
            if (text.Length == 0)
            {
                exchange.Answer = "(model nie zwrócił tekstu)";
            }
            else if (last?.DoneReason == "length")
            {
                exchange.Answer = text + "\n\n*(Odpowiedź ucięta: limit " + MaxAnswerTokens + " tokenów — poproś o dalszą część.)*";
            }

            var running = await TryGetRunningAsync(model);
            if (last is not null)
            {
                exchange.StatsData = ChatStats.From(last, firstToken, clock.Elapsed, running?.GpuShare, Network.IsRemote ? Network.RemoteGpu : _gpuName);
                exchange.Stats = exchange.StatsData.Describe();
            }
            else
            {
                exchange.Stats = "";
            }

            exchange.Completed = true;
            DescribeRunning(running);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            outcome = "przerwano";
            exchange.Stats = string.Format(Pl, "Przerwano po {0:0.0} s.", clock.Elapsed.TotalSeconds);
            StatusText = "Przerwano.";
        }
        catch (OllamaException e)
        {
            outcome = "blad";
            error = e.Message;
            exchange.IsError = true;
            exchange.Answer = text.Length > 0 ? text + Environment.NewLine + Environment.NewLine + e.Message : e.Message;
            exchange.Stats = "";
            StatusText = "Błąd — opis w odpowiedzi.";
        }
        finally
        {
            _cts = null;
            _waitingExchange = null;
            cts.Dispose();
            IsBusy = false;
        }

        if (SaveHistory && !_options.IsAutoTest)
        {
            AppendHistory(exchange, model, temperature, messages, outcome, error);
        }

        SaveSettings();
        if (outcome == "blad")
        {
            // Pokaże baner z rozwiązaniem: Ollama nie działa albo nie ma modelu.
            await RefreshAsync();
        }
    }

    public void OnClosing()
    {
        _cts?.Cancel();
        SaveSettings();
        // Zamyka port bramy (inne komputery dostaną „nie ma połączenia”), najwyżej 3 s.
        Task.Run(Network.StopAsync).Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>Ustawienia dla okna pół-harnessu: ten sam serwer, model i kontekst co w oknie głównym.</summary>
    public HarnessContext CreateHarnessContext()
    {
        TryParseTemperature(TemperatureText, out var temperature);
        var selected = SelectedModel;
        return new HarnessContext
        {
            OllamaUrl = _client.BaseUrl,
            ComputeWhere = selected?.Cloud is { } cloud ? "liczy: chmura " + cloud.Name
                : Network.IsRemote ? "liczy: " + Network.ComputerName : "liczy: ten komputer",
            Model = selected?.Name ?? _settings.Model,
            ModelLabel = selected?.Label ?? ModelItem.ShortName(_settings.Model),
            Cloud = selected?.Cloud,
            ModelBrush = selected?.NameBrush,
            GetApiKey = provider => CloudViewModel.ResolveKey(_keys, provider),
            SelectPreferredModel = SelectPreferredModel,
            CurrentModelKey = SelectedModel?.Key,
            ModelChoices = Models.Select(m => new ModelChoice(m.Key, m.IsCloud ? m.Display + " (" + m.Cloud!.Name + ")" : m.Name)).ToList(),
            GetPostgres = Credentials.GetPostgres,
            Web = InternetAccess ? _web : null,
            ContextLength = HarnessContextLength(),
            Temperature = temperature,
            LocalOllamaUrl = _local.BaseUrl,
            LocalGpuGb = _gpuGb,
            LocalGpuText = _gpuDescription,
            ProfilesRoot = ProfilesFolder.Find(),
            Settings = _settings,
            Save = SaveSettings,
        };
    }

    /// <summary>
    /// Kontekst dla Code i czatu z internetem. Prompt umiejętności (karta, narzędzia, przykłady, wiedza) zajmuje 2–8 tys. tokenów,
    /// więc co najmniej 16 384 — ale nie więcej, niż przyjmuje komputer, który liczy (jego Ustawienia → Udostępnianie,
    /// „kontekst do”); większe zapytanie brama tamtego komputera odrzuca.
    /// </summary>
    internal int HarnessContextLength()
    {
        var wanted = Math.Max(16384, ContextLength);
        return Network.IsRemote && Network.RemoteMaxContext is int max && wanted > max ? max : wanted;
    }

    /// <summary>Dzisiejsza data dla modelu z dniem tygodnia („środa, 2026-11-18”) — sama data kończyła się zgadywaniem dnia.</summary>
    internal static string TodayText()
    {
        var today = DateTime.Today;
        return CultureInfo.GetCultureInfo("pl-PL").DateTimeFormat.GetDayName(today.DayOfWeek) + ", "
            + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal static bool TryParseTemperature(string? text, out double value)
    {
        var normalized = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 2;
    }

    /// <summary>Czat z modelem w chmurze: cała odpowiedź naraz (bez strumienia), statystyki z liczby tokenów.</summary>
    private async Task SendCloudAsync(ModelItem selected, CloudProvider provider, string question, double temperature)
    {
        var key = CloudViewModel.ResolveKey(_keys, provider);
        if (string.IsNullOrEmpty(key))
        {
            StatusText = $"Brak klucza API {provider.Name} — wpisz go w Ustawieniach → Modele w chmurze.";
            return;
        }

        var messages = BuildMessages(question);
        var exchange = new ExchangeViewModel(question, selected.Name, null, selected)
        {
            Stats = $"Czekam na odpowiedź z chmury {provider.Name}…",
        };
        Exchanges.Add(exchange);
        Question = "";
        IsBusy = true;
        StatusText = $"Liczy {provider.Family} w chmurze {provider.Name}… (Esc albo Stop przerywa)";
        var cts = new CancellationTokenSource();
        _cts = cts;
        var clock = Stopwatch.StartNew();
        var outcome = "ok";
        string? error = null;
        ILlmClient? llm = null;
        try
        {
            llm = CloudLlm.Create(provider, key, selected.Name, temperature);
            var response = await llm.CompleteAsync(
                new LlmRequest { Messages = messages.Select(m => new LlmMessage(m.Role, m.Content)).ToList() }, cts.Token);
            clock.Stop();
            exchange.Answer = string.IsNullOrWhiteSpace(response.Content) ? "(model nie zwrócił tekstu)" : response.Content;
            exchange.StatsData = new ChatStats(
                response.PromptTokens, response.OutputTokens, TimeSpan.Zero, TimeSpan.Zero, clock.Elapsed, clock.Elapsed, clock.Elapsed, null, "chmura " + provider.Name);
            exchange.Stats = string.Format(
                Pl, "chmura {0} · {1} tok. wejścia · {2} tok. odpowiedzi · {3:0.0} s", provider.Name, response.PromptTokens, response.OutputTokens, clock.Elapsed.TotalSeconds);
            exchange.Completed = true;
            StatusText = $"Odpowiedź z chmury {provider.Name}.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            outcome = "przerwano";
            exchange.Stats = string.Format(Pl, "Przerwano po {0:0.0} s.", clock.Elapsed.TotalSeconds);
            StatusText = "Przerwano.";
        }
        catch (LlmException e)
        {
            outcome = "blad";
            error = e.Message;
            exchange.IsError = true;
            exchange.Answer = e.Message;
            exchange.Stats = "";
            StatusText = "Błąd — opis w odpowiedzi.";
        }
        finally
        {
            (llm as IDisposable)?.Dispose();
            _cts = null;
            cts.Dispose();
            IsBusy = false;
        }

        if (SaveHistory && !_options.IsAutoTest)
        {
            AppendHistory(exchange, AppSettings.ModelKey(provider.Id, selected.Name), temperature, messages, outcome, error);
        }

        SaveSettings();
    }

    /// <summary>
    /// Czat z dostępem do internetu: pytanie idzie przez sesję harnessu z narzędziami szukaj_w_internecie, czytaj_strone
    /// i kalkulator (działa z każdym modelem — lokalnym i w chmurze). Odpowiedź przychodzi w całości, ze źródłami.
    /// </summary>
    private async Task SendWithInternetAsync(ModelItem selected, string question, double temperature)
    {
        string? key = null;
        if (selected.Cloud is { } cloud)
        {
            key = CloudViewModel.ResolveKey(_keys, cloud);
            if (string.IsNullOrEmpty(key))
            {
                StatusText = $"Brak klucza API {cloud.Name} — wpisz go w Ustawieniach → Modele w chmurze.";
                return;
            }
        }

        var messages = BuildMessages(question);
        var exchange = selected.Cloud is null
            ? new ExchangeViewModel(question, selected.Name, Network.IsRemote ? Network.ComputerName : null)
            : new ExchangeViewModel(question, selected.Name, null, selected);
        exchange.Stats = "Model myśli (z dostępem do internetu)…";
        Exchanges.Add(exchange);
        Question = "";
        IsBusy = true;
        StatusText = "Model liczy, może szukać w internecie… (Esc albo Stop przerywa)";
        var cts = new CancellationTokenSource();
        _cts = cts;
        var clock = Stopwatch.StartNew();
        var outcome = "ok";
        string? error = null;
        ILlmClient? llm = null;
        try
        {
            llm = selected.Cloud is { } provider
                ? CloudLlm.Create(provider, key!, selected.Name, temperature)
                : new OllamaLlmClient(new OllamaOptions
                {
                    Url = _client.BaseUrl,
                    Model = selected.Name,
                    ContextLength = HarnessContextLength(),
                    Temperature = temperature,
                    KeepAlive = "10m",
                    ClientName = "Czat (internet)",
                    RequestTimeout = TimeSpan.FromMinutes(10),
                });
            using var session = new HarnessSession(llm, BuildInternetChatProfile(), null, new SessionOptions
            {
                Web = _web,
                ContextNote = InternetChatNote(question),
            });
            session.Progress += (_, e) =>
            {
                if (e.Kind == HarnessEventKind.ToolStarted && e.Call is { } call)
                {
                    var text = DescribeWebCall(call);
                    Application.Current?.Dispatcher.InvokeAsync(() => exchange.Stats = text + "…");
                }
            };
            var result = await session.SendAsync(question, cts.Token);
            clock.Stop();
            var web = result.Calls.Where(c => BuiltInTools.IsWeb(c.Tool)).ToList();
            exchange.Answer = string.IsNullOrWhiteSpace(result.Text) ? "(model nie zwrócił tekstu)" : result.Text.Trim();
            exchange.Stats = string.Format(Pl, "{0} · internet: {1} · wywołań modelu: {2} · {3:0.0} s",
                selected.Cloud is { } c2 ? "chmura " + c2.Name : Network.IsRemote ? Network.ComputerName : "ten komputer",
                web.Count == 0 ? "nie był potrzebny" : string.Join("; ", web.Select(DescribeWebCall)),
                result.ModelCalls, clock.Elapsed.TotalSeconds);
            exchange.Completed = true;
            StatusText = web.Count == 0 ? "Odpowiedź bez sięgania do internetu." : "Odpowiedź z użyciem internetu.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            outcome = "przerwano";
            exchange.Stats = string.Format(Pl, "Przerwano po {0:0.0} s.", clock.Elapsed.TotalSeconds);
            StatusText = "Przerwano.";
        }
        catch (Exception e) when (e is LlmException or InvalidOperationException or HttpRequestException)
        {
            outcome = "blad";
            error = e.Message;
            exchange.IsError = true;
            exchange.Answer = e.Message;
            exchange.Stats = "";
            StatusText = "Błąd — opis w odpowiedzi.";
        }
        finally
        {
            (llm as IDisposable)?.Dispose();
            _cts = null;
            cts.Dispose();
            IsBusy = false;
        }

        if (SaveHistory && !_options.IsAutoTest)
        {
            AppendHistory(exchange, selected.Key, temperature, messages, outcome, error);
        }

        SaveSettings();
    }

    /// <summary>Profil czatu z internetem: polecenie systemowe z Ustawień + zasady korzystania z sieci.</summary>
    private HarnessProfile BuildInternetChatProfile()
    {
        var system = (string.IsNullOrWhiteSpace(SystemPrompt) ? AppSettings.DefaultSystemPrompt : SystemPrompt.Trim()) + """


            INTERNET
            - Masz narzędzia szukaj_w_internecie i czytaj_strone. Użyj ich, gdy pytanie dotyczy aktualnych informacji (ceny, wersje, wydarzenia, normy, producenci), gdy nie jesteś pewien faktu albo gdy użytkownik poda adres strony. Na zwykłe pytania i rozmowę odpowiadaj od razu, bez internetu.
            - Najpierw szukaj, potem przeczytaj 1–2 najlepsze strony. Nie wymyślaj adresów ani treści stron.
            - Gdy korzystałeś z internetu, na końcu odpowiedzi dopisz „Źródła:” z adresami stron, z których wziąłeś informacje.
            - Rachunki licz kalkulatorem.
            """;
        var settings = new ProfileSettings { Internet = true, MaxSteps = 8, MaxCallsPerStep = 3 };
        settings.BuiltInTools.Add(BuiltInTools.CalculatorName);
        foreach (var provider in CloudProvider.All)
        {
            settings.CloudProviders.Add(provider.Id);
        }

        return new HarnessProfile("czat", system + (FormattingRules() is { } rules ? "\n\n" + rules : ""), Array.Empty<ToolDefinition>(), settings).WithInternet();
    }

    /// <summary>Data i (przy „Pamiętaj rozmowę”) wcześniejsze pytania i odpowiedzi czatu, krótko.</summary>
    private string InternetChatNote(string question)
    {
        var note = new StringBuilder("Dziś jest ").Append(TodayText()).Append('.');
        // Bez „Pamiętaj rozmowę” prośba „popraw / jeszcze raz” i tak dotyczy ostatniej odpowiedzi — ją model musi widzieć.
        var take = RememberConversation ? 6 : TurnIntent.AsksForCorrection(question) ? 1 : 0;
        if (take > 0)
        {
            foreach (var earlier in Exchanges.Where(e => e.Completed && !e.IsError).TakeLast(take))
            {
                note.Append("\nWcześniej użytkownik: ").Append(Shorten(earlier.Question, 300))
                    .Append("\nWcześniej Ty: ").Append(Shorten(earlier.Answer, 600));
            }
        }

        return note.ToString();
    }

    private static string Shorten(string? text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text[..max] + "…";

    /// <summary>„szukam: norma PN-EN 1993” albo „czytam: pkn.pl”.</summary>
    private static string DescribeWebCall(ExecutedCall call)
    {
        try
        {
            using var args = JsonDocument.Parse(string.IsNullOrEmpty(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            var root = args.RootElement;
            if (call.Tool == BuiltInTools.WebSearchName && root.TryGetProperty("fraza", out var f))
            {
                return "szukam: " + f.GetString();
            }

            if (call.Tool == BuiltInTools.WebFetchName && root.TryGetProperty("adres", out var a))
            {
                var address = a.GetString() ?? "";
                return "czytam: " + (Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Host : address);
            }
        }
        catch (JsonException)
        {
        }

        return call.Tool;
    }

    /// <summary>
    /// Składa listę modeli: z Ollamy i dodane w chmurze; ulubione na górze. Zostawia wybór, a gdy go nie ma — zapamiętany model,
    /// potem domyślny lokalny. Modelu w chmurze nigdy nie wybiera sam (dane idą do dostawcy tylko z wyboru użytkownika).
    /// </summary>
    private void RebuildModels()
    {
        var previous = SelectedModel?.Key;
        var favorites = new HashSet<string>(_settings.FavoriteModels, StringComparer.OrdinalIgnoreCase);
        var items = _localModels.Select(m => new ModelItem(m)).ToList();
        foreach (var chosen in _settings.CloudModels)
        {
            if (CloudProvider.Find(chosen.Provider) is { } provider)
            {
                items.Add(new ModelItem(provider, chosen.Id, chosen.Name));
            }
        }

        foreach (var item in items)
        {
            item.IsFavorite = favorites.Contains(item.Key);
            item.FavoriteCommand = ToggleFavoriteCommand;
        }

        var wasRefreshing = _refreshing;
        _refreshing = true;
        try
        {
            Models.Clear();
            foreach (var item in SortModels(items))
            {
                Models.Add(item);
            }

            var wanted = AppSettings.ModelKey(_settings.ModelProvider, _settings.Model);
            SelectedModel = Models.FirstOrDefault(m => previous is not null && SameModel(m.Key, previous))
                ?? Models.FirstOrDefault(m => SameModel(m.Key, wanted))
                ?? Models.FirstOrDefault(m => !m.IsCloud && SameModel(m.Name, AppSettings.DefaultModel))
                ?? Models.FirstOrDefault(m => !m.IsCloud);
        }
        finally
        {
            _refreshing = wasRefreshing;
        }

        if (SelectedModel?.IsCloud == true && !IsBusy)
        {
            DescribeCloud(SelectedModel);
        }
    }

    /// <summary>Ulubione na górze; dalej modele lokalne alfabetycznie, potem chmura: Anthropic, OpenAI, Google.</summary>
    private static IEnumerable<ModelItem> SortModels(IEnumerable<ModelItem> items)
        => items
            .OrderBy(m => m.IsFavorite ? 0 : 1)
            .ThenBy(m => m.Cloud is null ? -1 : CloudProvider.All.ToList().IndexOf(m.Cloud))
            .ThenBy(m => m.IsCloud ? "" : m.Name, StringComparer.OrdinalIgnoreCase);

    private void ToggleFavorite(ModelItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsFavorite = !item.IsFavorite;
        _settings.FavoriteModels.RemoveAll(f => SameModel(f, item.Key));
        if (item.IsFavorite)
        {
            _settings.FavoriteModels.Add(item.Key);
        }

        var wasRefreshing = _refreshing;
        _refreshing = true;
        try
        {
            var sorted = SortModels(Models.ToList()).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                var current = Models.IndexOf(sorted[i]);
                if (current != i)
                {
                    Models.Move(current, i);
                }
            }
        }
        finally
        {
            _refreshing = wasRefreshing;
        }

        SaveSettings();
    }

    private void DescribeCloud(ModelItem model)
    {
        if (model.Cloud is { } provider)
        {
            StatusText = $"Model w chmurze {provider.Name}: pytania i rozmowa idą przez internet do {provider.Name}. W zakładce Code tylko w umiejętnościach, które na to pozwalają.";
        }
    }

    /// <summary>
    /// Zasady umiejętności „formatowanie” (tabele, listy, wykresy w bloku ```wykres) — także dla czatu, który nie ma umiejętności.
    /// Null, gdy folderu umiejętności nie ma.
    /// </summary>
    private static string? FormattingRules()
    {
        try
        {
            var path = Path.Combine(ProfilesFolder.Find(), "formatowanie", "system.md");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private List<ChatMessage> BuildMessages(string question)
    {
        var messages = new List<ChatMessage>();
        var system = string.Join("\n\n", new[] { SystemPrompt?.Trim(), FormattingRules() }.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (system.Length > 0)
        {
            messages.Add(new ChatMessage("system", system));
        }

        var completed = Exchanges.Where(e => e.Completed && !e.IsError).ToList();
        var correction = completed.Count > 0 && TurnIntent.AsksForCorrection(question);
        // Bez „Pamiętaj rozmowę” prośba o poprawkę i tak dotyczy ostatniej odpowiedzi — dołączamy przynajmniej ją.
        foreach (var earlier in RememberConversation ? completed : correction ? completed.TakeLast(1).ToList() : [])
        {
            messages.Add(new ChatMessage("user", earlier.Question));
            messages.Add(new ChatMessage("assistant", earlier.Answer));
        }

        var hints = new List<string>();
        if (correction)
        {
            hints.Add(TurnIntent.CorrectionHint);
        }

        if (TurnIntent.AsksForWeb(question))
        {
            hints.Add(TurnIntent.NoWebHint); // ta droga to czat bez internetu (z internetem idzie przez sesję harnessu)
        }

        messages.Add(new ChatMessage("user", hints.Count == 0 ? question : question + "\n\n" + string.Join("\n", hints)));
        return messages;
    }

    private async Task PullAsync()
    {
        var model = _modelToPull ?? AppSettings.DefaultModel;
        var cts = new CancellationTokenSource();
        _cts = cts;
        IsBusy = true;
        IsPulling = true;
        PullPercent = 0;
        BannerText = $"Pobieram {model}… (Stop przerywa)";
        var tracker = new PullTracker();
        var sinceUpdate = Stopwatch.StartNew();
        try
        {
            await foreach (var progress in _local.PullAsync(model, cts.Token))
            {
                tracker.Add(progress);
                if (sinceUpdate.ElapsedMilliseconds >= 250 || tracker.Succeeded)
                {
                    sinceUpdate.Restart();
                    PullPercent = tracker.Percent ?? 0;
                    BannerText = $"Pobieram {model}: {tracker.Describe()} (Stop przerywa)";
                }
            }

            BannerText = "";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            BannerText = "Pobieranie przerwane. „Pobierz model” wznowi je od miejsca przerwania.";
        }
        catch (OllamaException e)
        {
            BannerText = e.Message;
        }
        finally
        {
            _cts = null;
            cts.Dispose();
            IsPulling = false;
            IsBusy = false;
        }

        if (tracker.Succeeded)
        {
            _settings.Model = model;
            _settings.ModelProvider = null;
            await RefreshAsync();
        }
    }

    private async Task StartOllamaAsync()
    {
        var exe = OllamaLauncher.Find();
        if (exe is null)
        {
            BannerText = "Nie znalazłem Ollamy na tym komputerze.";
            return;
        }

        try
        {
            OllamaLauncher.Start(exe);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            BannerText = "Nie udało się uruchomić Ollamy: " + e.Message;
            return;
        }

        IsBusy = true;
        CanStartOllama = false;
        BannerText = "Uruchamiam Ollamę…";
        try
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(1000);
                try
                {
                    await _local.GetVersionAsync();
                    break;
                }
                catch (OllamaException)
                {
                    // Jeszcze startuje — próbujemy dalej.
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    private async Task UnloadAsync()
    {
        try
        {
            var running = await _local.ListRunningAsync();
            if (running.Count == 0)
            {
                StatusText = "Żaden model nie zajmuje teraz pamięci tego komputera.";
                return;
            }

            foreach (var model in running)
            {
                await _local.UnloadAsync(model.Name);
            }

            StatusText = "Zwolniono pamięć: " + string.Join(", ", running.Select(m => ModelItem.ShortName(m.Name))) + ".";
        }
        catch (OllamaException e)
        {
            StatusText = e.Message;
        }
    }

    private void Stop() => _cts?.Cancel();

    private void Clear()
    {
        Exchanges.Clear();
        StatusText = "Nowa rozmowa (poprzednia jest w panelu rozmów).";
        ChatCleared?.Invoke(this, EventArgs.Empty);
    }

    private void CopyInstallCommandToClipboard()
    {
        try
        {
            Clipboard.SetText(OllamaLauncher.InstallCommand);
            StatusText = "Skopiowano polecenie instalacji Ollamy — wklej je w PowerShellu.";
        }
        catch (ExternalException)
        {
            StatusText = "Schowek jest zajęty — spróbuj jeszcze raz.";
        }
    }

    private async Task UpdateRunningStatusAsync()
    {
        if (SelectedModel is not { } selected || IsBusy)
        {
            return;
        }

        if (selected.IsCloud)
        {
            DescribeCloud(selected);
            return;
        }

        DescribeRunning(await TryGetRunningAsync(selected.Name));
    }

    private async Task<RunningModel?> TryGetRunningAsync(string model)
    {
        try
        {
            return (await _client.ListRunningAsync()).FirstOrDefault(m => SameModel(m.Name, model));
        }
        catch (OllamaException)
        {
            return null;
        }
    }

    private void DescribeRunning(RunningModel? running)
    {
        if (running is null)
        {
            StatusText = "Model nie jest teraz w pamięci — pierwsze pytanie go załaduje (kilka–kilkanaście sekund).";
            return;
        }

        var place = ChatStats.DescribePlacement(running.GpuShare, Network.IsRemote ? Network.RemoteGpu : _gpuName);
        var until = running.ExpiresAt is { } expires && expires.Year < 2100
            ? $"; zwolni pamięć sam o {expires.ToLocalTime():HH:mm}"
            : "";
        StatusText = $"Model w pamięci{(Network.IsRemote ? " komputera " + Network.ComputerName : "")} — {place}; kontekst {running.ContextLength}{until}.";
    }

    private void AppendHistory(
        ExchangeViewModel exchange, string model, double temperature, IReadOnlyList<ChatMessage> messages, string outcome, string? error)
    {
        var stats = exchange.StatsData;
        try
        {
            _historyLog.Append(new HistoryEntry
            {
                Time = DateTimeOffset.Now,
                Model = model,
                Temperature = temperature,
                ContextLength = ContextLength,
                SystemPrompt = messages.FirstOrDefault(m => m.Role == "system")?.Content,
                RememberedConversation = RememberConversation,
                Question = exchange.Question,
                Answer = outcome == "blad" ? "" : exchange.Answer,
                Outcome = outcome,
                Error = error,
                PromptTokens = stats?.PromptTokens,
                OutputTokens = stats?.OutputTokens,
                TokensPerSecond = stats?.TokensPerSecond is double speed ? Math.Round(speed, 2) : null,
                Seconds = stats is null ? null : Math.Round(stats.Total.TotalSeconds, 2),
                GpuShare = stats?.GpuShare is double share ? Math.Round(share, 3) : null,
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            StatusText = "Nie zapisałem historii: " + e.Message;
        }
    }

    private void SaveSettings()
    {
        if (_options.IsAutoTest)
        {
            return;
        }

        _settings.SystemPrompt = SystemPrompt;
        if (TryParseTemperature(TemperatureText, out var temperature))
        {
            _settings.Temperature = temperature;
        }

        _settings.ContextLength = ContextLength;
        _settings.RememberConversation = RememberConversation;
        _settings.SaveHistory = SaveHistory;
        _settings.OllamaUrl = _local.BaseUrl;
        if (SelectedModel is not null)
        {
            _settings.Model = SelectedModel.Name;
            _settings.ModelProvider = SelectedModel.Provider;
        }

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            StatusText = "Nie zapisałem ustawień: " + e.Message;
        }
    }

    /// <summary>Tworzy nowego klienta, gdy zmienił się adres Ollamy. Zły adres → baner, false.</summary>
    private bool EnsureClient()
    {
        string url;
        try
        {
            url = OllamaClient.NormalizeUrl(OllamaUrl);
        }
        catch (ArgumentException e)
        {
            OllamaStatus = "● zły adres Ollamy";
            OllamaStatusBrush = BadBrush;
            BannerText = e.Message;
            CanStartOllama = false;
            CanPull = false;
            return false;
        }

        if (!string.Equals(url, _local.BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            var old = _local;
            _local = new OllamaClient(url);
            if (ReferenceEquals(_client, old))
            {
                _client = _local;
            }

            old.Dispose();
            _ = Network.SetOllamaUrlAsync(url);
        }

        if (_ollamaUrl != url)
        {
            _ollamaUrl = url;
            OnPropertyChanged(nameof(OllamaUrl));
        }

        return true;
    }

    /// <summary>Czat łączy się przez bramę (kolejka, wybrany komputer), gdy ta działa.</summary>
    private void UseGateway()
    {
        if (Network.GatewayUrl is { } url && !string.Equals(url, _client.BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            if (!ReferenceEquals(_client, _local))
            {
                _client.Dispose();
            }

            _client = new OllamaClient(url) { ClientName = "Czat" };
        }

        OnPropertyChanged(nameof(GatewayHint));
        OnPropertyChanged(nameof(EmptyChatHint));
    }

    private async Task OnComputeTargetChangedAsync()
    {
        UseGateway();
        ComputeTargetChanged?.Invoke(this, EventArgs.Empty);
        if (!IsBusy)
        {
            await RefreshAsync();
        }
    }

    /// <summary>Czat czeka w kolejce bramy (liczy inny program albo komputer): pokaż to pod pytaniem.</summary>
    private void ShowChatQueuePosition()
    {
        if (_waitingExchange is not { } exchange)
        {
            return;
        }

        var rows = Network.Queue.ToList();
        var mine = rows.FindIndex(r => r.Program == "Czat" && r.State == LokalneAI.Gateway.QueueState.Waiting);
        if (mine >= 0)
        {
            var ahead = rows.Take(mine).Select(r => r.Program + (r.Who == "ten komputer" ? "" : " (" + r.Who + ")")).ToList();
            exchange.Stats = "Czeka w kolejce — przed nim: " + string.Join(", ", ahead) + "…";
        }
        else if (exchange.Stats.StartsWith("Czeka w kolejce", StringComparison.Ordinal))
        {
            exchange.Stats = "Model liczy…";
        }
    }

    private static bool SameModel(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Limit długości jednej odpowiedzi czatu (tokeny) — długa odpowiedź z tabelą to ok. 1–2 tys.</summary>
    private const int MaxAnswerTokens = 4096;

    /// <summary>Ten sam model z Ollamy niezależnie od dopisku „:latest” (gemma4:12b = gemma4:12b:latest).</summary>
    private static bool SameLocalModel(string a, string b)
    {
        static string Strip(string name) => name.EndsWith(":latest", StringComparison.OrdinalIgnoreCase) ? name[..^7] : name;
        return SameModel(Strip(a), Strip(b));
    }

    /// <summary>
    /// Zakładka Code, wybór profilu: przełącza na pierwszy dostępny z preferowanych modeli profilu (od najlepszego) na maszynie,
    /// która liczy. Model w chmurze tylko, gdy pozwalają na niego wszystkie umiejętności (<paramref name="allowedCloud"/>)
    /// i jest klucz API. Zwraca wybrany model; null = żadnego z listy tu nie ma (zostaje bieżący).
    /// </summary>
    public ModelItem? SelectPreferredModel(IEnumerable<string> keys, IReadOnlyCollection<string> allowedCloud)
    {
        foreach (var key in keys)
        {
            var item = Models.FirstOrDefault(m => SameModel(m.Key, key) || (!m.IsCloud && !key.Contains('|') && SameLocalModel(m.Name, key)));
            if (item is null)
            {
                continue;
            }

            if (item.Cloud is { } cloud
                && (!allowedCloud.Contains(cloud.Id, StringComparer.OrdinalIgnoreCase) || string.IsNullOrEmpty(CloudViewModel.ResolveKey(_keys, cloud))))
            {
                continue;
            }

            if (!ReferenceEquals(SelectedModel, item))
            {
                SelectedModel = item;
            }

            return item;
        }

        return null;
    }

    private static bool IsLoopback(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
