namespace LokalneAI.Gateway;

/// <summary>
/// Ustawienia bramy. Brama czyta je przy każdym zapytaniu, więc zmiana celu albo limitów działa od razu.
/// Port i udostępnianie w sieci zmienia <see cref="GatewayServer.ApplyAsync"/> (ponowne otwarcie portu).
/// </summary>
public sealed record GatewayOptions
{
    /// <summary>Port bramy, ten sam na każdym komputerze biura.</summary>
    public const int DefaultPort = 11435;

    /// <summary>Port UDP, na którym brama odpowiada na „kto udostępnia model?”.</summary>
    public const int DefaultDiscoveryPort = 11436;

    /// <summary>Port bramy. 0 = dowolny wolny (kontrola bez klikania).</summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>Port UDP do wyszukiwania komputerów. 0 = bez odpowiadania.</summary>
    public int DiscoveryPort { get; init; } = DefaultDiscoveryPort;

    /// <summary>Ollama na tym komputerze.</summary>
    public string OllamaUrl { get; init; } = "http://127.0.0.1:11434";

    /// <summary>
    /// Brama innego komputera, na którym mają liczyć programy z tego PC, np. <c>http://PC-ODS12:11435</c>.
    /// Null albo pusty = liczy ten komputer.
    /// </summary>
    public string? RemoteUrl { get; init; }

    /// <summary>Nazwa komputera z <see cref="RemoteUrl"/> do komunikatów (z jego <c>/lokalneai/info</c>).</summary>
    public string? RemoteName { get; init; }

    /// <summary>Przyjmuj zapytania z innych komputerów z tej samej podsieci. Wyłączone: port słucha tylko na 127.0.0.1.</summary>
    public bool ShareOnLan { get; init; }

    /// <summary>Ile zapytań naraz idzie do Ollamy tego PC. Powinno odpowiadać <c>OLLAMA_NUM_PARALLEL</c> (domyślnie 1).</summary>
    public int Parallel { get; init; } = 1;

    /// <summary>Największy kontekst (<c>num_ctx</c>), jaki mogą zamówić inne komputery. Większy dostaje błąd, nie obcięcie.</summary>
    public int MaxContextForOthers { get; init; } = 16384;

    /// <summary>Jak długo model zostaje w pamięci karty po zapytaniu z innego komputera (zamiast wartości od klienta).</summary>
    public string KeepAliveForOthers { get; init; } = "5m";

    /// <summary>Ile zapytań z innych komputerów może czekać naraz. Następne dostają „kolejka pełna”.</summary>
    public int MaxWaitingFromOthers { get; init; } = 8;

    /// <summary>Po ilu sekundach czekania zapytanie z innego komputera przestaje ustępować zapytaniom z tego PC.</summary>
    public int OthersYieldSeconds { get; init; } = 60;

    /// <summary>Największe zapytanie (bajty). Zwykły prompt to kilkadziesiąt kB.</summary>
    public int MaxRequestBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Nazwa tego komputera, którą widzą inni.</summary>
    public string MachineName { get; init; } = Environment.MachineName;

    /// <summary>Opis karty graficznej do <c>/lokalneai/info</c>, np. „NVIDIA GeForce RTX 4060, 8 GB”.</summary>
    public string? GpuDescription { get; init; }

    /// <summary>Wersja programu do <c>/lokalneai/info</c>.</summary>
    public string Version { get; init; } = typeof(GatewayOptions).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>Tylko do prób na jednym komputerze: połączenia z 127.0.0.1 traktuj jak z innego PC w podsieci.</summary>
    public bool LoopbackCountsAsLan { get; init; }

    /// <summary>Podmiana HTTP do Ollamy i innych bram (testy). Null = zwykłe połączenie.</summary>
    public HttpMessageHandler? UpstreamHandler { get; init; }

    /// <summary>Liczy inny komputer (ustawiony <see cref="RemoteUrl"/>).</summary>
    public bool IsRemote => !string.IsNullOrWhiteSpace(RemoteUrl);
}
