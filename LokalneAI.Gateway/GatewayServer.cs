using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LokalneAI.Gateway;

/// <summary>Brama nie wystartowała (np. port zajęty).</summary>
public sealed class GatewayException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Wynik sprawdzenia bramy innego komputera.</summary>
/// <param name="Info">Opis bramy; null, gdy się nie udało.</param>
/// <param name="Error">Opis problemu po polsku; null, gdy się udało.</param>
public sealed record ProbeResult(PeerInfo? Info, string? Error);

/// <summary>
/// Brama LokalneAI: serwer HTTP zgodny z częścią API Ollamy. Programy z tego PC wysyłają do niej zapytania jak do Ollamy
/// (<c>http://127.0.0.1:11435</c>), a brama:
/// <list type="bullet">
/// <item>ustawia je w kolejce (<see cref="Queue"/>) przed Ollamą tego PC albo przekazuje do bramy innego komputera
/// (<see cref="GatewayOptions.RemoteUrl"/>);</item>
/// <item>gdy <see cref="GatewayOptions.ShareOnLan"/>, przyjmuje zapytania innych komputerów, ale tylko z tej samej podsieci;</item>
/// <item>przepuszcza tylko liczenie i odczyt (czat, generowanie, embeddingi, lista modeli); pobieranie, kasowanie
/// i tworzenie modeli są zablokowane;</item>
/// <item>odrzuca zapytania z przeglądarek (nagłówek <c>Origin</c>), żeby strona internetowa nie mogła użyć modelu.</item>
/// </list>
/// Treści zapytań brama nie zapisuje; w kolejce widać tylko komputer, program, model i czas.
/// </summary>
public sealed class GatewayServer : IAsyncDisposable
{
    private enum RouteKind
    {
        Meta,
        Compute,
    }

    private static readonly Dictionary<string, RouteKind> Routes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GET /api/version"] = RouteKind.Meta,
        ["GET /api/tags"] = RouteKind.Meta,
        ["GET /api/ps"] = RouteKind.Meta,
        ["POST /api/show"] = RouteKind.Meta,
        ["GET /v1/models"] = RouteKind.Meta,
        ["POST /api/chat"] = RouteKind.Compute,
        ["POST /api/generate"] = RouteKind.Compute,
        ["POST /api/embed"] = RouteKind.Compute,
        ["POST /api/embeddings"] = RouteKind.Compute,
        ["POST /v1/chat/completions"] = RouteKind.Compute,
        ["POST /v1/completions"] = RouteKind.Compute,
        ["POST /v1/embeddings"] = RouteKind.Compute,
    };

    /// <summary>Komunikaty z polskimi literami czytelne także w surowym JSON-ie (curl, dziennik).</summary>
    private static readonly JsonSerializerOptions ErrorJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly HttpClient _http;
    private readonly object _networksLock = new();
    private GatewayOptions _options;
    private WebApplication? _app;
    private PeerDiscovery? _responder;
    private IReadOnlyList<LocalNetwork> _networks = [];
    private DateTimeOffset _networksRead = DateTimeOffset.MinValue;

    /// <summary>Tworzy bramę (jeszcze nie słucha — <see cref="StartAsync"/>).</summary>
    public GatewayServer(GatewayOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = options.UpstreamHandler is null
            ? new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5), PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            : new HttpClient(options.UpstreamHandler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        Queue = new RequestQueue { Parallel = options.Parallel, OthersYieldSeconds = options.OthersYieldSeconds };
    }

    /// <summary>Kolejka zapytań (do okna: kto czeka, kto liczy, „Odrzuć”).</summary>
    public RequestQueue Queue { get; }

    /// <summary>Bieżące ustawienia.</summary>
    public GatewayOptions Options => _options;

    /// <summary>Port, na którym brama słucha (po <see cref="StartAsync"/>).</summary>
    public int Port { get; private set; }

    /// <summary>Adres bramy dla programów z tego PC.</summary>
    public string LocalUrl => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);

    /// <summary>Brama słucha.</summary>
    public bool IsRunning => _app is not null;

    /// <summary>Brama przyjmuje połączenia z sieci (nie tylko z tego PC).</summary>
    public bool IsListeningOnLan { get; private set; }

    /// <summary>Dlaczego nie udało się otworzyć portu w sieci (null = bez problemu).</summary>
    public string? LanError { get; private set; }

    /// <summary>Krótkie zdarzenia do okna, np. „PC-B · Czat: policzone w 12,3 s” (wątek w tle).</summary>
    public event EventHandler<string>? Activity;

    /// <summary>Podsieci tego komputera, z których brama przyjmuje zapytania.</summary>
    public IReadOnlyList<LocalNetwork> Networks
    {
        get
        {
            lock (_networksLock)
            {
                if (DateTimeOffset.Now - _networksRead > TimeSpan.FromSeconds(30))
                {
                    _networks = LanAccess.LocalNetworks();
                    _networksRead = DateTimeOffset.Now;
                }

                return _networks;
            }
        }
    }

    /// <summary>Ujednolica adres bramy z pola w oknie: „PC-ODS12” → <c>http://PC-ODS12:11435</c>, „10.0.0.5:12000” → <c>http://10.0.0.5:12000</c>.</summary>
    /// <exception cref="ArgumentException">To nie jest adres komputera.</exception>
    public static string NormalizeUrl(string text)
    {
        var value = (text ?? "").Trim().TrimEnd('/');
        if (value.Length == 0)
        {
            throw new ArgumentException("Podaj nazwę albo adres IP komputera, np. PC-ODS12 albo 10.0.0.50.", nameof(text));
        }

        var hasScheme = value.Contains("://", StringComparison.Ordinal);
        if (!hasScheme)
        {
            value = "http://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || uri.Host.Length == 0
            || uri.AbsolutePath.Length > 1)
        {
            throw new ArgumentException($"Niepoprawny adres komputera: „{text}”. Przykład: PC-ODS12 albo 10.0.0.50 albo 10.0.0.50:{GatewayOptions.DefaultPort}.", nameof(text));
        }

        var explicitPort = !uri.IsDefaultPort || value.EndsWith(":80", StringComparison.Ordinal);
        var port = explicitPort ? uri.Port : GatewayOptions.DefaultPort;
        return new UriBuilder(Uri.UriSchemeHttp, uri.Host, port).Uri.ToString().TrimEnd('/');
    }

    /// <summary>Sprawdza bramę innego komputera (<c>GET /lokalneai/info</c>).</summary>
    public static async Task<ProbeResult> ProbeAsync(string url, TimeSpan timeout, HttpMessageHandler? handler = null, CancellationToken cancellationToken = default)
    {
        string baseUrl;
        try
        {
            baseUrl = NormalizeUrl(url);
        }
        catch (ArgumentException e)
        {
            return new ProbeResult(null, e.Message);
        }

        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var where = new Uri(baseUrl).Authority;
        try
        {
            using var response = await http.GetAsync(baseUrl + "/lokalneai/info", limit.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ProbeResult(null, $"{where}: {ReadError(body) ?? "HTTP " + (int)response.StatusCode}");
            }

            var info = PeerInfo.Parse(body);
            if (info is null)
            {
                return new ProbeResult(null, $"Pod adresem {where} działa inny program, nie LokalneAI.");
            }

            info.Address = new Uri(baseUrl).Host;
            return new ProbeResult(info, null);
        }
        catch (HttpRequestException e)
        {
            return new ProbeResult(null, $"Nie ma połączenia z {where} ({e.Message}). Czy LokalneAI tam działa, ma włączone udostępnianie i otwartą zaporę?");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(null, $"{where} nie odpowiedział w ciągu {timeout.TotalSeconds:0} s. Czy LokalneAI tam działa i ma otwartą zaporę?");
        }
    }

    /// <summary>Otwiera port. Gdy port w sieci się nie otworzy, brama działa dla tego PC, a powód jest w <see cref="LanError"/>.</summary>
    /// <exception cref="GatewayException">Nie da się otworzyć nawet portu dla tego PC (np. zajęty).</exception>
    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StartCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Zmienia ustawienia. Zmiana portu albo udostępniania ponownie otwiera port (przerywa trwające odpowiedzi).</summary>
    public async Task ApplyAsync(GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var previous = _options;
            _options = options with { UpstreamHandler = previous.UpstreamHandler };
            Queue.Parallel = options.Parallel;
            Queue.OthersYieldSeconds = options.OthersYieldSeconds;
            var portChanged = options.Port != previous.Port && !(options.Port == 0 && Port != 0);
            if (_app is not null && (portChanged || options.ShareOnLan != previous.ShareOnLan || options.DiscoveryPort != previous.DiscoveryPort))
            {
                await StopCoreAsync().ConfigureAwait(false);
                await StartCoreAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Zamyka port.</summary>
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Zamyka port i połączenia.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _http.Dispose();
    }

    /// <summary>Jak ta brama przedstawia się innym.</summary>
    public PeerInfo DescribeSelf()
    {
        var options = _options;
        var snapshot = Queue.Snapshot();
        return new PeerInfo
        {
            Computer = options.MachineName,
            Version = options.Version,
            Port = Port,
            Sharing = IsListeningOnLan,
            Gpu = options.GpuDescription,
            Running = snapshot.Count(i => i.State == QueueState.Running),
            Waiting = snapshot.Count(i => i.State == QueueState.Waiting),
            MaxContext = options.MaxContextForOthers,
        };
    }

    /// <summary>
    /// Sprawdza zapytanie innego komputera i dopasowuje je do zasad tego PC: kontekst nie większy niż
    /// <see cref="GatewayOptions.MaxContextForOthers"/> (większy = błąd, nie ciche obcięcie) i <c>keep_alive</c> tego PC.
    /// Zwraca opis błędu albo null.
    /// </summary>
    internal static string? ApplyLimitsForOthers(JsonObject body, string path, GatewayOptions options)
    {
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (body["options"] is JsonObject modelOptions && modelOptions["num_ctx"] is JsonValue value
            && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<double>(out var context)
            && context > options.MaxContextForOthers)
        {
            return $"{options.MachineName} przyjmuje od innych komputerów kontekst do {options.MaxContextForOthers} tokenów, "
                + $"a to zapytanie chce {context:0}. Zmniejsz kontekst albo licz na swoim komputerze.";
        }

        body["keep_alive"] = options.KeepAliveForOthers;
        return null;
    }

    private async Task StartCoreAsync()
    {
        if (_app is not null)
        {
            return;
        }

        var options = _options;
        LanError = null;
        if (options.LoopbackCountsAsLan)
        {
            // Próba na jednym komputerze: port tylko na 127.0.0.1 (bez okna zapory Windows), a 127.0.0.1 gra „inny komputer”.
            _app = await LaunchAsync(options, onLan: false).ConfigureAwait(false);
            IsListeningOnLan = options.ShareOnLan;
            if (IsListeningOnLan && options.DiscoveryPort > 0)
            {
                _responder = PeerDiscovery.TryStartResponder(options.DiscoveryPort, DescribeSelf, IsAllowedPeer, IPAddress.Loopback);
            }

            return;
        }

        if (options.ShareOnLan)
        {
            try
            {
                _app = await LaunchAsync(options, onLan: true).ConfigureAwait(false);
                IsListeningOnLan = true;
            }
            catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                LanError = $"Nie otworzyłem portu {options.Port} w sieci: {e.Message}";
            }
        }

        if (_app is null)
        {
            try
            {
                _app = await LaunchAsync(options, onLan: false).ConfigureAwait(false);
                IsListeningOnLan = false;
            }
            catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                throw new GatewayException($"Brama nie wystartowała na porcie {options.Port}: {e.Message}", e);
            }
        }

        if (IsListeningOnLan && options.DiscoveryPort > 0)
        {
            _responder = PeerDiscovery.TryStartResponder(options.DiscoveryPort, DescribeSelf, IsAllowedPeer);
        }
    }

    private async Task StopCoreAsync()
    {
        _responder?.Dispose();
        _responder = null;
        var app = _app;
        _app = null;
        IsListeningOnLan = false;
        if (app is not null)
        {
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<WebApplication> LaunchAsync(GatewayOptions options, bool onLan)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(2));
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = options.MaxRequestBytes;
            if (onLan)
            {
                kestrel.ListenAnyIP(options.Port);
            }
            else
            {
                kestrel.Listen(IPAddress.Loopback, options.Port);
            }
        });
        var app = builder.Build();
        app.Run(HandleAsync);
        try
        {
            await app.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        Port = address is not null && Uri.TryCreate(address.Replace("://+:", "://localhost:", StringComparison.Ordinal), UriKind.Absolute, out var uri)
            ? uri.Port
            : options.Port;
        return app;
    }

    private bool IsAllowedPeer(IPAddress remote)
        => (LanAccess.IsLoopback(remote) && _options.ShareOnLan) || LanAccess.IsInLocalSubnet(remote, Networks);

    private async Task HandleAsync(HttpContext context)
    {
        var options = _options;
        var request = context.Request;
        var remote = LanAccess.Normalize(context.Connection.RemoteIpAddress ?? IPAddress.None);
        var fromThisPc = LanAccess.IsLoopback(remote) && !options.LoopbackCountsAsLan;
        var aborted = context.RequestAborted;

        if (request.Headers.ContainsKey("Origin"))
        {
            await WriteErrorAsync(context, 403, "Brama LokalneAI nie przyjmuje zapytań ze stron internetowych.").ConfigureAwait(false);
            return;
        }

        if (!fromThisPc)
        {
            if (!options.ShareOnLan)
            {
                await WriteErrorAsync(context, 403, $"{options.MachineName} nie udostępnia teraz modelu innym komputerom.").ConfigureAwait(false);
                return;
            }

            if (!LanAccess.IsLoopback(remote) && !LanAccess.IsInLocalSubnet(remote, Networks))
            {
                OnActivity($"Odrzucono {remote}: adres spoza podsieci {LanAccess.Describe(Networks)}.");
                await WriteErrorAsync(context, 403, $"{options.MachineName} przyjmuje zapytania tylko z podsieci {LanAccess.Describe(Networks)}.").ConfigureAwait(false);
                return;
            }
        }

        var path = request.Path.Value ?? "/";
        if (path == "/" && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("LokalneAI: brama działa", aborted).ConfigureAwait(false);
            return;
        }

        if (path.Equals("/lokalneai/info", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(request.Method))
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(DescribeSelf().ToJson(), aborted).ConfigureAwait(false);
            return;
        }

        if (!Routes.TryGetValue(request.Method + " " + path, out var kind))
        {
            await WriteErrorAsync(context, 404, $"Brama LokalneAI nie przepuszcza {request.Method} {path}. Model pobiera i usuwa się w Ollamie na danym komputerze.").ConfigureAwait(false);
            return;
        }

        byte[]? body = null;
        JsonObject? json = null;
        if (HttpMethods.IsPost(request.Method))
        {
            try
            {
                body = await ReadBodyAsync(request, aborted).ConfigureAwait(false);
            }
            catch (BadHttpRequestException e)
            {
                await WriteErrorAsync(context, e.StatusCode, "Zapytanie jest za duże albo uszkodzone: " + e.Message).ConfigureAwait(false);
                return;
            }
            catch (Exception e) when (e is OperationCanceledException or IOException)
            {
                return;
            }

            if (kind == RouteKind.Compute)
            {
                try
                {
                    json = JsonNode.Parse(body) as JsonObject;
                }
                catch (JsonException)
                {
                    json = null;
                }

                if (json is null)
                {
                    await WriteErrorAsync(context, 400, "Zapytanie nie jest poprawnym obiektem JSON.").ConfigureAwait(false);
                    return;
                }

                if (!fromThisPc)
                {
                    var limitError = ApplyLimitsForOthers(json, path, options);
                    if (limitError is not null)
                    {
                        await WriteErrorAsync(context, 400, limitError).ConfigureAwait(false);
                        return;
                    }

                    body = Encoding.UTF8.GetBytes(json.ToJsonString());
                }
            }
        }

        var forwarded = request.Headers[GatewayHeaders.Forwarded].ToString() == "1";
        var toRemote = fromThisPc && !forwarded && options.IsRemote;
        var target = toRemote ? options.RemoteUrl!.TrimEnd('/') : options.OllamaUrl.TrimEnd('/');
        var targetName = toRemote ? options.RemoteName ?? new Uri(target).Host : "Ollama";
        var program = GatewayHeaders.Decode(request.Headers[GatewayHeaders.Program].ToString()) ?? ShortUserAgent(request) ?? "?";
        var computer = GatewayHeaders.Decode(request.Headers[GatewayHeaders.Computer].ToString())
            ?? (fromThisPc ? options.MachineName : remote.ToString());
        var headers = new Dictionary<string, string> { [GatewayHeaders.Program] = GatewayHeaders.Encode(program) };
        if (toRemote)
        {
            headers[GatewayHeaders.Computer] = GatewayHeaders.Encode(options.MachineName);
            headers[GatewayHeaders.Forwarded] = "1";
        }

        var pathAndQuery = path + request.QueryString.Value;
        if (kind == RouteKind.Meta)
        {
            await ForwardAsync(context, target, targetName, pathAndQuery, body, headers, aborted).ConfigureAwait(false);
            return;
        }

        var model = json?["model"] is JsonValue m && m.TryGetValue<string>(out var name) ? name : null;
        var origin = fromThisPc ? RequestOrigin.ThisComputer : RequestOrigin.OtherComputer;
        var item = new QueueItem(origin, computer, remote.ToString(), program, model, DateTimeOffset.Now);
        if (origin == RequestOrigin.OtherComputer && Queue.WaitingFromOthers >= options.MaxWaitingFromOthers)
        {
            await WriteErrorAsync(context, 503, $"Kolejka na {options.MachineName} jest pełna ({options.MaxWaitingFromOthers} zapytań z innych komputerów). Spróbuj za chwilę.").ConfigureAwait(false);
            return;
        }

        if (toRemote)
        {
            using (Queue.TrackRemote(item, targetName))
            {
                await ForwardAsync(context, target, targetName, pathAndQuery, body, headers, aborted).ConfigureAwait(false);
            }

            return;
        }

        IDisposable lease;
        try
        {
            lease = await Queue.EnterAsync(item, aborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (item.Rejected && !aborted.IsCancellationRequested)
            {
                OnActivity($"{computer} · {program}: odrzucone przez właściciela.");
                await WriteErrorAsync(context, 503, $"Właściciel komputera {options.MachineName} odrzucił to zapytanie.").ConfigureAwait(false);
            }

            return;
        }

        using (lease)
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(aborted, item.Cancellation.Token))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var ok = await ForwardAsync(context, target, targetName, pathAndQuery, body, headers, linked.Token).ConfigureAwait(false);
            if (item.Rejected)
            {
                OnActivity($"{computer} · {program}: przerwane przez właściciela.");
                if (!context.Response.HasStarted)
                {
                    await WriteErrorAsync(context, 503, $"Właściciel komputera {options.MachineName} przerwał to zapytanie.").ConfigureAwait(false);
                }
                else
                {
                    context.Abort();
                }
            }
            else if (origin == RequestOrigin.OtherComputer && ok)
            {
                OnActivity(string.Format(CultureInfo.GetCultureInfo("pl-PL"), "{0} · {1}: policzone w {2:0.0} s.", computer, program, clock.Elapsed.TotalSeconds));
            }
        }
    }

    /// <summary>Przekazuje zapytanie dalej i strumieniuje odpowiedź. Zwraca true, gdy odpowiedź przeszła w całości.</summary>
    private async Task<bool> ForwardAsync(
        HttpContext context, string target, string targetName, string pathAndQuery, byte[]? body, Dictionary<string, string> headers, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(new HttpMethod(context.Request.Method), target + pathAndQuery);
        if (body is not null)
        {
            message.Content = new ByteArrayContent(body);
            message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.Length > 0)
        {
            message.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        foreach (var header in headers)
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            var hint = targetName == "Ollama"
                ? "Czy Ollama działa na tym komputerze?"
                : "Czy LokalneAI tam działa, ma włączone udostępnianie i otwartą zaporę?";
            await WriteErrorAsync(context, 502, $"Nie ma połączenia z {targetName} ({new Uri(target).Authority}): {e.Message} {hint}").ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            var contentType = response.Content.Headers.ContentType?.ToString();
            if (contentType is not null)
            {
                context.Response.ContentType = contentType;
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or HttpRequestException)
            {
                // Klient się rozłączył albo serwer przerwał odpowiedź: kończymy bez dalszego pisania.
                return false;
            }
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static async Task WriteErrorAsync(HttpContext context, int status, string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        try
        {
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }, ErrorJson), context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            // Klient już się rozłączył.
        }
    }

    private static string? ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error)
                ? error.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ShortUserAgent(HttpRequest request)
    {
        var agent = request.Headers.UserAgent.ToString();
        if (agent.Length == 0)
        {
            return null;
        }

        var first = agent.Split(' ', 2)[0];
        return first.Length > 40 ? first[..40] : first;
    }

    private void OnActivity(string text) => Activity?.Invoke(this, text);
}
