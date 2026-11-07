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
    private readonly SettingsStore _settingsStore = new();
    private readonly HistoryLog _historyLog = new();
    private readonly AppSettings _settings;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Czat: brama LokalneAI (kolejka, wybrany komputer), a gdy jej nie ma — Ollama na tym PC.</summary>
    private OllamaClient _client;

    /// <summary>Ollama na tym PC: pobieranie modeli, zwalnianie pamięci, uruchamianie.</summary>
    private OllamaClient _local;
    private ExchangeViewModel? _waitingExchange;
    private CancellationTokenSource? _cts;
    private string? _gpuName;
    private string? _gpuDescription;
    private string? _modelToPull;
    private bool _refreshing;

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
        Exchanges.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsTranscriptEmpty));
    }

    public ObservableCollection<ModelItem> Models { get; } = [];

    /// <summary>Ustawienia → „Gdzie liczyć”, udostępnianie i kolejka.</summary>
    public NetworkViewModel Network { get; }

    /// <summary>Zmienił się komputer, który liczy (zakładka Code odświeża opis).</summary>
    public event EventHandler? ComputeTargetChanged;

    /// <summary>Podpowiedź na pustej rozmowie.</summary>
    public string EmptyChatHint => Network.IsRemote
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
            if (!SetProperty(ref _selectedModel, value) || value is null || _refreshing)
            {
                return;
            }

            // Wybór użytkownika: zapamiętaj go i schowaj baner o brakującym modelu domyślnym.
            _settings.Model = value.Name;
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
        set => SetProperty(ref _temperatureText, value);
    }

    public int ContextLength
    {
        get => _contextLength;
        set => SetProperty(ref _contextLength, value);
    }

    public bool RememberConversation
    {
        get => _rememberConversation;
        set => SetProperty(ref _rememberConversation, value);
    }

    public bool SaveHistory
    {
        get => _saveHistory;
        set => SetProperty(ref _saveHistory, value);
    }

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

    public async Task InitializeAsync()
    {
        try
        {
            var cards = await Task.Run(() => GpuInfo.Detect());
            var card = cards.FirstOrDefault();
            _gpuName = card?.Name;
            _gpuDescription = card?.Describe();
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
            var models = (await _client.ListModelsAsync())
                .Where(m => m.Capabilities is null || m.Capabilities.Contains("completion"))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Models.Clear();
            foreach (var model in models)
            {
                Models.Add(new ModelItem(model));
            }

            SelectedModel = Models.FirstOrDefault(m => SameModel(m.Name, wanted))
                ?? Models.FirstOrDefault(m => SameModel(m.Name, AppSettings.DefaultModel))
                ?? Models.FirstOrDefault();

            if (Models.Any(m => SameModel(m.Name, wanted)))
            {
                CanPull = false;
                BannerText = "";
            }
            else if (remote)
            {
                CanPull = false;
                BannerText = Models.Count == 0
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
            Models.Clear();
            SelectedModel = null;
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

    /// <summary>Wysyła pytanie do modelu i pokazuje odpowiedź na bieżąco, słowo po słowie.</summary>
    public async Task SendAsync()
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
            Options = new ChatOptions { Temperature = temperature, NumCtx = ContextLength },
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
        return new HarnessContext
        {
            OllamaUrl = _client.BaseUrl,
            ComputeWhere = Network.IsRemote ? "liczy: " + Network.ComputerName : "liczy: ten komputer",
            Model = SelectedModel?.Name ?? _settings.Model,
            // Prompt profilu (karta, narzędzia, przykłady) zajmuje 4–8 tys. tokenów — mniej niż 16 384 nie ma sensu.
            ContextLength = Math.Max(16384, ContextLength),
            Temperature = temperature,
            ProfilesRoot = ProfilesFolder.Find(),
            Settings = _settings,
            Save = SaveSettings,
        };
    }

    internal static bool TryParseTemperature(string? text, out double value)
    {
        var normalized = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 2;
    }

    private List<ChatMessage> BuildMessages(string question)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            messages.Add(new ChatMessage("system", SystemPrompt.Trim()));
        }

        if (RememberConversation)
        {
            foreach (var earlier in Exchanges.Where(e => e.Completed && !e.IsError))
            {
                messages.Add(new ChatMessage("user", earlier.Question));
                messages.Add(new ChatMessage("assistant", earlier.Answer));
            }
        }

        messages.Add(new ChatMessage("user", question));
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
        StatusText = "Wyczyszczono rozmowę.";
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

    private static bool IsLoopback(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
