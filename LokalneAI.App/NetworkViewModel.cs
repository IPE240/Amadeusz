using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LokalneAI.Core.Storage;
using LokalneAI.Gateway;

namespace LokalneAI.App;

/// <summary>
/// Ustawienia → „Gdzie liczyć”, „Udostępnianie tego komputera” i kolejka bramy. Brama (<see cref="GatewayServer"/>)
/// działa w tle; ten model tylko ją ustawia i pokazuje jej stan.
/// </summary>
public sealed class NetworkViewModel : ObservableObject
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    private readonly AppSettings _settings;
    private readonly StartupOptions _startup;
    private readonly Func<Task> _onTargetChanged;
    private readonly DispatcherTimer _clock;
    private GatewayServer? _gateway;
    private string? _gpuDescription;
    private bool _computeElsewhere;
    private string _remoteAddress;
    private string _remoteDescription = "";
    private bool _remoteOk;
    private bool _isSearching;
    private string _searchText = "";
    private bool _shareOnLan;
    private string _portText;
    private int _maxContextForOthers;
    private string _gatewayText = "Brama startuje…";
    private string _firewallText = "";
    private string _queueText = "";
    private bool _busy;

    public NetworkViewModel(AppSettings settings, StartupOptions startup, Func<Task> onTargetChanged)
    {
        _settings = settings;
        _startup = startup;
        _onTargetChanged = onTargetChanged;
        _computeElsewhere = settings.ComputeOn is not null;
        _remoteAddress = settings.RemoteAddress ?? settings.ComputeOn ?? "";
        _shareOnLan = settings.ShareOnLan;
        _portText = settings.GatewayPort.ToString(CultureInfo.InvariantCulture);
        _maxContextForOthers = settings.MaxContextForOthers;
        if (startup.ComputeOn is not null)
        {
            _computeElsewhere = true;
            _remoteAddress = startup.ComputeOn;
        }

        if (startup.Share)
        {
            _shareOnLan = true;
        }

        ConnectCommand = new RelayCommand(ConnectAsync, () => !_busy && RemoteAddress.Trim().Length > 0);
        FindCommand = new RelayCommand(FindAsync, () => !IsSearching);
        UsePeerCommand = new RelayCommand<PeerInfo>(UsePeerAsync);
        RejectCommand = new RelayCommand<QueueRow>(row => _gateway?.Queue.Reject(row.Id));
        FirewallCommand = new RelayCommand(SetupFirewallAsync, () => !_busy);
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => RefreshQueue();
    }

    public RelayCommand ConnectCommand { get; }

    public RelayCommand FindCommand { get; }

    public RelayCommand<PeerInfo> UsePeerCommand { get; }

    public RelayCommand<QueueRow> RejectCommand { get; }

    public RelayCommand FirewallCommand { get; }

    public ObservableCollection<PeerInfo> Peers { get; } = [];

    public ObservableCollection<QueueRow> Queue { get; } = [];

    /// <summary>Ostatnie zdarzenia bramy (kto policzył, kogo odrzucono), najnowsze na górze.</summary>
    public ObservableCollection<string> Activity { get; } = [];

    public IReadOnlyList<int> ContextChoices => AppSettings.ContextChoices;

    /// <summary>Adres bramy dla czatu i zakładki Code (null = brama nie działa, czat idzie prosto do Ollamy).</summary>
    public string? GatewayUrl { get; private set; }

    public bool ComputeHere
    {
        get => !_computeElsewhere;
        set
        {
            if (value && _computeElsewhere)
            {
                _computeElsewhere = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ComputeElsewhere));
                _ = ApplyTargetAsync(null, null);
            }
        }
    }

    public bool ComputeElsewhere
    {
        get => _computeElsewhere;
        set
        {
            if (value && !_computeElsewhere)
            {
                _computeElsewhere = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ComputeHere));
                if (RemoteAddress.Trim().Length > 0)
                {
                    _ = ConnectAsync();
                }
            }
        }
    }

    /// <summary>Liczy inny komputer (i jest ustawiony w bramie).</summary>
    public bool IsRemote => _gateway?.Options.IsRemote == true;

    /// <summary>Adres bramy komputera, który liczy (null = ten komputer).</summary>
    public string? RemoteUrl => IsRemote ? _gateway?.Options.RemoteUrl : null;

    /// <summary>Nazwa komputera, który liczy, do komunikatów.</summary>
    public string ComputerName => IsRemote ? _settings.ComputeOnName ?? RemoteAddress : Environment.MachineName;

    /// <summary>Karta komputera, który liczy (do statystyk „gdzie liczył”). Null = karta tego PC.</summary>
    public string? RemoteGpu { get; private set; }

    public string RemoteAddress
    {
        get => _remoteAddress;
        set
        {
            if (SetProperty(ref _remoteAddress, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string RemoteDescription
    {
        get => _remoteDescription;
        private set => SetProperty(ref _remoteDescription, value);
    }

    public bool RemoteOk
    {
        get => _remoteOk;
        private set => SetProperty(ref _remoteOk, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        private set => SetProperty(ref _searchText, value);
    }

    /// <summary>„Pozwalaj na przeliczanie zapytań z innych komputerów”.</summary>
    public bool ShareOnLan
    {
        get => _shareOnLan;
        set
        {
            if (SetProperty(ref _shareOnLan, value))
            {
                OnPropertyChanged(nameof(ShareSummary));
                _settings.ShareOnLan = value;
                _ = ApplySharingAsync();
            }
        }
    }

    public string PortText
    {
        get => _portText;
        set
        {
            if (SetProperty(ref _portText, value) && int.TryParse(value, out var port) && port is >= 1024 and <= 65535 && port != _settings.GatewayPort)
            {
                _settings.GatewayPort = port;
                _ = ApplySharingAsync();
            }
        }
    }

    public int MaxContextForOthers
    {
        get => _maxContextForOthers;
        set
        {
            if (SetProperty(ref _maxContextForOthers, value))
            {
                _settings.MaxContextForOthers = value;
                _ = ApplySharingAsync();
            }
        }
    }

    /// <summary>Stan bramy: port, podsieć, nazwa tego komputera.</summary>
    public string GatewayText
    {
        get => _gatewayText;
        private set => SetProperty(ref _gatewayText, value);
    }

    public string FirewallText
    {
        get => _firewallText;
        private set => SetProperty(ref _firewallText, value);
    }

    /// <summary>Kolejka pusta (napis „Nic nie czeka”).</summary>
    public bool IsQueueEmpty => Queue.Count == 0;

    /// <summary>Do nagłówka okna: „Liczy: ten komputer” albo „Liczy: PC-ODS12”.</summary>
    public string ComputeWhereText => IsRemote ? "Liczy: " + ComputerName : "Liczy: ten komputer";

    /// <summary>Do paska stanu: „Kolejka: liczy 1, czeka 2 · Udostępniam w sieci”.</summary>
    public string QueueText
    {
        get => _queueText;
        private set => SetProperty(ref _queueText, value);
    }

    /// <summary>Nagłówek zwiniętej sekcji „Kolejka” w Ustawieniach.</summary>
    public string QueueSummary => _gateway is null ? "brak bramy"
        : $"liczy {Queue.Count(r => r.State != QueueState.Waiting)}, czeka {Queue.Count(r => r.State == QueueState.Waiting)}";

    /// <summary>Nagłówek zwiniętej sekcji „Udostępnianie” w Ustawieniach.</summary>
    public string ShareSummary => _gateway?.IsListeningOnLan == true ? $"włączone, port {_gateway.Port}"
        : ShareOnLan ? "włączone, brama nie słucha w sieci (szczegóły po rozwinięciu)"
        : "wyłączone";

    /// <summary>Uruchamia bramę. Gdy port jest zajęty przez inne okno LokalneAI na tym PC, czat używa jego bramy.</summary>
    public async Task StartAsync(string ollamaUrl, string? gpuDescription)
    {
        _gpuDescription = gpuDescription;
        var options = BuildOptions(ollamaUrl);
        var gateway = new GatewayServer(options);
        try
        {
            await gateway.StartAsync();
        }
        catch (GatewayException e)
        {
            await gateway.DisposeAsync();
            var shared = "http://127.0.0.1:" + options.Port.ToString(CultureInfo.InvariantCulture);
            var probe = await GatewayServer.ProbeAsync(shared, TimeSpan.FromSeconds(2));
            if (probe.Info?.Computer == Environment.MachineName)
            {
                GatewayUrl = shared;
                GatewayText = "Brama działa w innym oknie LokalneAI na tym komputerze — kolejkę i udostępnianie ustawia tamto okno.";
            }
            else
            {
                GatewayUrl = null;
                GatewayText = e.Message + " Czat liczy bezpośrednio w Ollamie, bez kolejki.";
            }

            Notify();
            return;
        }

        _gateway = gateway;
        GatewayUrl = gateway.LocalUrl;
        gateway.Queue.Changed += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RefreshQueue);
        gateway.Activity += (_, text) => Application.Current?.Dispatcher.InvokeAsync(() => AddActivity(text));
        if (options.IsRemote && await CheckRemoteAsync(options.RemoteUrl!) is { } remote)
        {
            // Nazwa z odpowiedzi tamtego komputera (adres w ustawieniach bywa samym IP).
            _settings.ComputeOnName = remote.Computer;
            await gateway.ApplyAsync(gateway.Options with { RemoteName = remote.Computer });
        }

        DescribeGateway();
        Notify();
        _ = UpdateFirewallTextAsync();
    }

    /// <summary>Zmiana adresu Ollamy na tym PC (pole w Ustawieniach).</summary>
    public Task SetOllamaUrlAsync(string url)
        => _gateway is null ? Task.CompletedTask : _gateway.ApplyAsync(_gateway.Options with { OllamaUrl = url });

    public async Task StopAsync()
    {
        _clock.Stop();
        if (_gateway is not null)
        {
            await _gateway.DisposeAsync();
            _gateway = null;
        }
    }

    private GatewayOptions BuildOptions(string ollamaUrl)
    {
        var remote = _computeElsewhere ? TryNormalize(RemoteAddress) : null;
        return new GatewayOptions
        {
            // Kontrola bez klikania nie zajmuje portu użytkownika: dowolny wolny port, chyba że podano --port.
            Port = _startup.IsAutoTest ? _startup.Port ?? 0 : _settings.GatewayPort,
            DiscoveryPort = _startup.IsAutoTest ? 0 : GatewayOptions.DefaultDiscoveryPort,
            OllamaUrl = ollamaUrl,
            RemoteUrl = remote,
            RemoteName = remote is null ? null : _settings.ComputeOnName,
            ShareOnLan = _shareOnLan,
            MaxContextForOthers = _maxContextForOthers,
            GpuDescription = _gpuDescription,
            ReadTemperature = LokalneAI.Core.Hardware.GpuTemperature.Read,
            Version = typeof(NetworkViewModel).Assembly.GetName().Version?.ToString(3) ?? "?",
            LoopbackCountsAsLan = _startup.IsAutoTest && _startup.Share,
        };
    }

    private async Task ConnectAsync()
    {
        string url;
        try
        {
            url = GatewayServer.NormalizeUrl(RemoteAddress);
        }
        catch (ArgumentException e)
        {
            RemoteOk = false;
            RemoteDescription = e.Message;
            return;
        }

        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            RemoteDescription = "Sprawdzam " + new Uri(url).Authority + "…";
            var info = await CheckRemoteAsync(url);
            if (info is null)
            {
                return;
            }

            if (!_computeElsewhere)
            {
                _computeElsewhere = true;
                OnPropertyChanged(nameof(ComputeElsewhere));
                OnPropertyChanged(nameof(ComputeHere));
            }

            await ApplyTargetAsync(url, info.Computer);
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task<PeerInfo?> CheckRemoteAsync(string url)
    {
        var probe = await GatewayServer.ProbeAsync(url, TimeSpan.FromSeconds(4));
        if (probe.Info is null)
        {
            RemoteOk = false;
            RemoteDescription = "✗ " + probe.Error;
            return null;
        }

        if (probe.Info.Computer == Environment.MachineName && !_startup.IsAutoTest)
        {
            RemoteOk = false;
            RemoteDescription = "✗ To ten komputer. Wybierz „Na tym komputerze” albo podaj adres innego.";
            return null;
        }

        if (!probe.Info.Sharing)
        {
            RemoteOk = false;
            RemoteDescription = $"✗ {probe.Info.Computer} ma LokalneAI, ale nie udostępnia modelu innym (przełącznik w jego Ustawieniach).";
            return null;
        }

        RemoteOk = true;
        RemoteGpu = probe.Info.Gpu;
        RemoteDescription = "✓ " + probe.Info.Describe() + $" · kontekst do {probe.Info.MaxContext}";
        return probe.Info;
    }

    private async Task ApplyTargetAsync(string? url, string? name)
    {
        _settings.ComputeOn = url;
        _settings.ComputeOnName = name;
        if (url is not null)
        {
            _settings.RemoteAddress = RemoteAddress.Trim();
        }
        else
        {
            RemoteGpu = null;
        }

        if (_gateway is not null)
        {
            await _gateway.ApplyAsync(_gateway.Options with { RemoteUrl = url, RemoteName = name });
        }

        Notify();
        await _onTargetChanged();
    }

    private async Task UsePeerAsync(PeerInfo? peer)
    {
        if (peer is null)
        {
            return;
        }

        RemoteAddress = peer.Address + (peer.Port == GatewayOptions.DefaultPort ? "" : ":" + peer.Port.ToString(CultureInfo.InvariantCulture));
        await ConnectAsync();
    }

    private async Task FindAsync()
    {
        IsSearching = true;
        SearchText = "Szukam w sieci…";
        Peers.Clear();
        try
        {
            var found = await PeerDiscovery.FindAsync(TimeSpan.FromSeconds(1.5));
            foreach (var peer in found.Where(p => p.Computer != Environment.MachineName))
            {
                Peers.Add(peer);
            }

            SearchText = Peers.Count == 0
                ? "Nie znalazłem komputerów, które udostępniają model w tej podsieci. Adres możesz też wpisać ręcznie."
                : $"Znalezione: {Peers.Count}. Kliknij „Użyj”.";
        }
        catch (System.Net.Sockets.SocketException e)
        {
            SearchText = "Wyszukiwanie nie zadziałało: " + e.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task ApplySharingAsync()
    {
        if (_gateway is null)
        {
            return;
        }

        try
        {
            await _gateway.ApplyAsync(_gateway.Options with
            {
                ShareOnLan = _shareOnLan,
                Port = _startup.IsAutoTest ? _gateway.Options.Port : _settings.GatewayPort,
                MaxContextForOthers = _maxContextForOthers,
            });
        }
        catch (GatewayException e)
        {
            AddActivity(e.Message);
        }

        DescribeGateway();
        Notify();
        _ = UpdateFirewallTextAsync();
        if (_gateway.IsRunning && _gateway.LocalUrl != GatewayUrl)
        {
            // Nowy port: czat i zakładka Code muszą łączyć się pod nowy adres.
            GatewayUrl = _gateway.LocalUrl;
            await _onTargetChanged();
        }
    }

    private async Task UpdateFirewallTextAsync()
    {
        if (!_startup.IsAutoTest)
        {
            FirewallText = await FirewallSetup.DescribeAsync(_settings.GatewayPort, GatewayOptions.DefaultDiscoveryPort);
        }
    }

    private async Task SetupFirewallAsync()
    {
        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            FirewallText = "Czekam na zgodę administratora…";
            var error = await FirewallSetup.SetupAsync(_settings.GatewayPort, GatewayOptions.DefaultDiscoveryPort);
            FirewallText = (error is null ? "" : error + " ") + await FirewallSetup.DescribeAsync(_settings.GatewayPort, GatewayOptions.DefaultDiscoveryPort);
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void DescribeGateway()
    {
        if (_gateway is null)
        {
            return;
        }

        var subnet = LanAccess.Describe(_gateway.Networks);
        GatewayText = _gateway.IsListeningOnLan
            ? $"Udostępniam: inne komputery z podsieci {subnet} mogą liczyć na tym PC. Adres dla nich: {Environment.MachineName} (port {_gateway.Port})."
            : _gateway.LanError ?? $"Nie udostępniam. Brama działa tylko dla programów na tym komputerze (port {_gateway.Port}).";
        OnPropertyChanged(nameof(ShareSummary));
    }

    private void RefreshQueue()
    {
        if (_gateway is null)
        {
            return;
        }

        var items = _gateway.Queue.Snapshot();
        var now = DateTimeOffset.Now;
        Queue.Clear();
        foreach (var item in items)
        {
            Queue.Add(new QueueRow(item, now));
        }

        if (items.Count > 0)
        {
            _clock.Start();
        }
        else
        {
            _clock.Stop();
        }

        Notify();
    }

    private void AddActivity(string text)
    {
        Activity.Insert(0, DateTime.Now.ToString("HH:mm:ss", Pl) + "  " + text);
        while (Activity.Count > 30)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }
    }

    /// <summary>Ile zapytań w kolejce bramy właśnie liczy (do ikony w zasobniku).</summary>
    public int QueueRunning { get; private set; }

    /// <summary>Ile zapytań czeka w kolejce bramy (liczba na ikonie w zasobniku).</summary>
    public int QueueWaiting { get; private set; }

    private void Notify()
    {
        var running = Queue.Count(r => r.State != QueueState.Waiting);
        var waiting = Queue.Count(r => r.State == QueueState.Waiting);
        QueueRunning = running;
        QueueWaiting = waiting;
        var served = _gateway?.Queue.ServedForOthers ?? 0;
        var queue = _gateway is null ? "Kolejka: brak bramy" : $"Kolejka: liczy {running}, czeka {waiting}";
        var sharing = _gateway?.IsListeningOnLan == true ? $" · Udostępniam w sieci (policzone dla innych: {served})" : "";
        QueueText = queue + sharing;
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(ShareSummary));
        OnPropertyChanged(nameof(IsQueueEmpty));
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(ComputerName));
        OnPropertyChanged(nameof(ComputeWhereText));
    }

    private static string? TryNormalize(string text)
    {
        try
        {
            return GatewayServer.NormalizeUrl(text);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>Wiersz kolejki w oknie.</summary>
public sealed class QueueRow(QueueItem item, DateTimeOffset now)
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public long Id { get; } = item.Id;

    public QueueState State { get; } = item.State;

    public string Who { get; } = item.Origin == RequestOrigin.ThisComputer ? "ten komputer" : $"{item.Computer} ({item.Address})";

    public string Program { get; } = item.Program;

    public string Model { get; } = item.Model is null ? "" : ModelItem.ShortName(item.Model);

    public string StateText { get; } = item.State switch
    {
        QueueState.Waiting => string.Format(Pl, "czeka {0:0} s", (now - item.Arrived).TotalSeconds),
        QueueState.Running => string.Format(Pl, "liczy {0:0} s", (now - (item.Started ?? now)).TotalSeconds),
        _ => string.Format(Pl, "na {0}, {1:0} s", item.RemoteComputer, (now - (item.Started ?? now)).TotalSeconds),
    };

    /// <summary>Odrzucić można tylko zapytanie innego komputera.</summary>
    public bool CanReject { get; } = item.Origin == RequestOrigin.OtherComputer;
}
