using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using LokalneAI.Gateway;

namespace LokalneAI.Core.Tests;

/// <summary>Brama: kolejka, podsieć, adresy, limity dla innych komputerów i przekazywanie zapytań (na 127.0.0.1, bez Ollamy).</summary>
public sealed class GatewayTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Kolejka_ZapytaniaTegoPcIdaPrzedInnymiKomputerami()
    {
        var now = T0;
        var queue = new RequestQueue(() => now) { OthersYieldSeconds = 60 };
        var first = await queue.EnterAsync(Item(RequestOrigin.ThisComputer, "A", now), CancellationToken.None);

        now = T0.AddSeconds(1);
        var other = queue.EnterAsync(Item(RequestOrigin.OtherComputer, "B", now), CancellationToken.None);
        now = T0.AddSeconds(2);
        var mine = queue.EnterAsync(Item(RequestOrigin.ThisComputer, "C", now), CancellationToken.None);

        Assert.Equal(["A", "C", "B"], queue.Snapshot().Select(i => i.Computer));
        first.Dispose();
        (await mine).Dispose();
        (await other).Dispose();
        Assert.Empty(queue.Snapshot());
        Assert.Equal(1, queue.ServedForOthers);
    }

    [Fact]
    public async Task Kolejka_InnyKomputerPoMinucieCzekaniaJuzNieUstepuje()
    {
        var now = T0;
        var queue = new RequestQueue(() => now) { OthersYieldSeconds = 60 };
        var running = await queue.EnterAsync(Item(RequestOrigin.ThisComputer, "A", now), CancellationToken.None);
        var other = queue.EnterAsync(Item(RequestOrigin.OtherComputer, "B", now), CancellationToken.None);

        now = T0.AddSeconds(61);
        var mine = queue.EnterAsync(Item(RequestOrigin.ThisComputer, "C", now), CancellationToken.None);
        Assert.Equal(["A", "B", "C"], queue.Snapshot().Select(i => i.Computer));

        running.Dispose();
        var next = queue.Snapshot();
        Assert.Equal(("B", QueueState.Running), (next[0].Computer, next[0].State));
        Assert.Equal(("C", QueueState.Waiting), (next[1].Computer, next[1].State));
        (await other).Dispose();
        (await mine).Dispose();
    }

    [Fact]
    public async Task Kolejka_RozlaczonyKlientIOdrzucenieZnikajaZKolejki()
    {
        var queue = new RequestQueue();
        var running = await queue.EnterAsync(Item(RequestOrigin.ThisComputer, "A", T0), CancellationToken.None);
        using var gone = new CancellationTokenSource();
        var leaving = queue.EnterAsync(Item(RequestOrigin.ThisComputer, "B", T0), gone.Token);
        var rejectedItem = Item(RequestOrigin.OtherComputer, "C", T0);
        var rejected = queue.EnterAsync(rejectedItem, CancellationToken.None);

        gone.Cancel();
        Assert.True(queue.Reject(rejectedItem.Id));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rejected);
        Assert.Equal(["A"], queue.Snapshot().Select(i => i.Computer));
        Assert.False(queue.Reject(queue.Snapshot()[0].Id)); // zapytań z tego PC się nie odrzuca
        running.Dispose();
    }

    [Fact]
    public async Task Kolejka_DwaMiejscaNarazGdyOllamaLiczyRownolegle()
    {
        var queue = new RequestQueue { Parallel = 2 };
        var a = await queue.EnterAsync(Item(RequestOrigin.ThisComputer, "A", T0), CancellationToken.None);
        var b = queue.EnterAsync(Item(RequestOrigin.OtherComputer, "B", T0), CancellationToken.None);
        var c = queue.EnterAsync(Item(RequestOrigin.OtherComputer, "C", T0), CancellationToken.None);
        Assert.True(b.IsCompleted);
        Assert.False(c.IsCompleted);
        a.Dispose();
        (await b).Dispose();
        (await c).Dispose();
    }

    [Theory]
    [InlineData("10.0.0.17", true)]
    [InlineData("10.0.0.254", true)]
    [InlineData("10.0.1.17", false)]
    [InlineData("192.168.1.5", false)]
    [InlineData("::ffff:10.0.0.9", true)]
    [InlineData("fe80::1234:5678", true)]
    [InlineData("2001:db8::1", false)]
    public void Podsiec_PrzepuszczaTylkoAdresyZSieciTegoKomputera(string address, bool expected)
    {
        var networks = new[] { new LocalNetwork(IPAddress.Parse("10.0.0.20"), 24) };
        Assert.Equal(expected, LanAccess.IsInLocalSubnet(IPAddress.Parse(address), networks));
    }

    [Fact]
    public void Podsiec_OpisIAdresRozgloszeniowy()
    {
        var networks = new[] { new LocalNetwork(IPAddress.Parse("10.0.0.20"), 24), new LocalNetwork(IPAddress.Parse("172.16.5.9"), 20) };
        Assert.Equal("10.0.0.0/24, 172.16.0.0/20", LanAccess.Describe(networks));
        Assert.Equal(["10.0.0.255", "172.16.15.255"], LanAccess.BroadcastAddresses(networks).Select(a => a.ToString()));
        Assert.True(LanAccess.IsLoopback(IPAddress.Parse("::ffff:127.0.0.1")));
    }

    [Theory]
    [InlineData("PC-ODS12", "http://pc-ods12:11435")]
    [InlineData("10.0.0.50", "http://10.0.0.50:11435")]
    [InlineData(" 10.0.0.50:12000/ ", "http://10.0.0.50:12000")]
    [InlineData("http://10.0.0.50", "http://10.0.0.50:11435")]
    [InlineData("http://10.0.0.50:80", "http://10.0.0.50")]
    public void Adres_UzupelniaSchematIPort(string text, string expected)
        => Assert.Equal(expected, GatewayServer.NormalizeUrl(text));

    [Theory]
    [InlineData("")]
    [InlineData("ftp://10.0.0.5")]
    [InlineData("10.0.0.5/api/chat")]
    public void Adres_NiepoprawnyDajeCzytelnyBlad(string text)
        => Assert.Contains("komputer", Assert.Throws<ArgumentException>(() => GatewayServer.NormalizeUrl(text)).Message);

    [Fact]
    public void Limity_ZaDlugiKontekstToBladANieObciecie()
    {
        var options = new GatewayOptions { MachineName = "PC-A", MaxContextForOthers = 16384, KeepAliveForOthers = "5m" };
        var big = JsonNode.Parse("""{"model":"m","options":{"num_ctx":32768},"keep_alive":-1}""")!.AsObject();
        Assert.Contains("do 16384 tokenów", GatewayServer.ApplyLimitsForOthers(big, "/api/chat", options));

        var ok = JsonNode.Parse("""{"model":"m","options":{"num_ctx":16384},"keep_alive":-1}""")!.AsObject();
        Assert.Null(GatewayServer.ApplyLimitsForOthers(ok, "/api/chat", options));
        Assert.Equal("5m", (string?)ok["keep_alive"]);
    }

    [Fact]
    public async Task Brama_PrzepuszczaCzatIListeModeliAleNiePobieranieModeli()
    {
        var ollama = new RecordingOllama();
        await using var gateway = new GatewayServer(new GatewayOptions { Port = 0, DiscoveryPort = 0, MachineName = "PC-A", UpstreamHandler = ollama });
        await gateway.StartAsync();
        using var http = new HttpClient();

        var tags = await http.GetStringAsync(gateway.LocalUrl + "/api/tags");
        Assert.Contains("bielik", tags);

        using var chat = Post(gateway.LocalUrl + "/api/chat", """{"model":"bielik","messages":[{"role":"user","content":"Cześć"}],"stream":true}""", program: "Czat");
        using var answer = await http.SendAsync(chat);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Contains("\"done\":true", await answer.Content.ReadAsStringAsync());
        var sent = ollama.Requests.Single(r => r.Path == "/api/chat");
        Assert.Contains("Cześć", sent.Body);
        Assert.Equal("Czat", GatewayHeaders.Decode(sent.Program));

        using var pull = await http.PostAsync(gateway.LocalUrl + "/api/pull", new StringContent("""{"model":"x"}"""));
        Assert.Equal(HttpStatusCode.NotFound, pull.StatusCode);
        Assert.DoesNotContain(ollama.Requests, r => r.Path == "/api/pull");
    }

    [Fact]
    public async Task Brama_OdrzucaStronyInternetoweIKomputeryGdyNieUdostepnia()
    {
        var ollama = new RecordingOllama();
        await using var gateway = new GatewayServer(new GatewayOptions
        {
            Port = 0, DiscoveryPort = 0, MachineName = "PC-A", UpstreamHandler = ollama, LoopbackCountsAsLan = true, ShareOnLan = false,
        });
        await gateway.StartAsync();
        using var http = new HttpClient();

        using var fromNetwork = await http.GetAsync(gateway.LocalUrl + "/api/tags");
        Assert.Equal(HttpStatusCode.Forbidden, fromNetwork.StatusCode);
        Assert.Contains("nie udostępnia", await fromNetwork.Content.ReadAsStringAsync());

        await gateway.ApplyAsync(gateway.Options with { ShareOnLan = true });
        using var browser = new HttpRequestMessage(HttpMethod.Get, gateway.LocalUrl + "/api/tags");
        browser.Headers.Add("Origin", "https://strona.example");
        using var fromBrowser = await http.SendAsync(browser);
        Assert.Equal(HttpStatusCode.Forbidden, fromBrowser.StatusCode);
        Assert.Empty(ollama.Requests);
    }

    [Fact]
    public async Task Brama_PrzekazujeZapytanieDoInnegoKomputeraKtoryWidziNazweITrzymaLimity()
    {
        var ollamaOnA = new RecordingOllama();
        await using var a = new GatewayServer(new GatewayOptions
        {
            Port = 0, DiscoveryPort = 0, MachineName = "PC-A", GpuDescription = "RTX 4090, 24 GB",
            UpstreamHandler = ollamaOnA, LoopbackCountsAsLan = true, ShareOnLan = true, MaxContextForOthers = 16384,
        });
        await a.StartAsync();
        await using var b = new GatewayServer(new GatewayOptions { Port = 0, DiscoveryPort = 0, MachineName = "PC-B", RemoteUrl = a.LocalUrl, RemoteName = "PC-A" });
        await b.StartAsync();
        var seenOnA = new List<QueueItem>();
        a.Queue.Changed += (_, _) => seenOnA.AddRange(a.Queue.Snapshot());
        using var http = new HttpClient();

        using var chat = Post(b.LocalUrl + "/api/chat", """{"model":"bielik","messages":[],"options":{"num_ctx":8192},"keep_alive":-1}""", program: "Code: tekla");
        using var answer = await http.SendAsync(chat);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        var sent = ollamaOnA.Requests.Single(r => r.Path == "/api/chat");
        Assert.Contains("\"keep_alive\":\"5m\"", sent.Body);
        var item = Assert.Single(seenOnA.DistinctBy(i => i.Id));
        Assert.Equal(RequestOrigin.OtherComputer, item.Origin);
        Assert.Equal("PC-B", item.Computer);
        Assert.Equal("Code: tekla", item.Program);
        Assert.Equal(1, a.Queue.ServedForOthers);
        var tokens = Assert.Single(a.Tokens.Snapshot());
        Assert.Equal(("PC-B", false, 1L, 12L, 3L), (tokens.Computer, tokens.IsThisComputer, tokens.Requests, tokens.PromptTokens, tokens.OutputTokens));
        Assert.Empty(b.Tokens.Snapshot()); // B tylko przekazał — liczył A

        using var tooBig = Post(b.LocalUrl + "/api/chat", """{"model":"bielik","messages":[],"options":{"num_ctx":32768}}""", program: "Czat");
        using var refused = await http.SendAsync(tooBig);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("PC-A przyjmuje", await refused.Content.ReadAsStringAsync());

        var probe = await GatewayServer.ProbeAsync(a.LocalUrl, TimeSpan.FromSeconds(5));
        Assert.Null(probe.Error);
        Assert.Equal("PC-A", probe.Info!.Computer);
        Assert.Equal("RTX 4090, 24 GB", probe.Info.Gpu);
        Assert.True(probe.Info.Sharing);
    }

    [Fact]
    public async Task Brama_ZrozumialyBladGdyInnyKomputerNieOdpowiada()
    {
        await using var b = new GatewayServer(new GatewayOptions { Port = 0, DiscoveryPort = 0, MachineName = "PC-B", RemoteUrl = "http://127.0.0.1:1", RemoteName = "PC-X" });
        await b.StartAsync();
        using var http = new HttpClient();
        using var answer = await http.SendAsync(Post(b.LocalUrl + "/api/chat", """{"model":"m","messages":[]}""", program: "Czat"));
        Assert.Equal(HttpStatusCode.BadGateway, answer.StatusCode);
        Assert.Contains("Nie ma połączenia z PC-X", await answer.Content.ReadAsStringAsync());

        var probe = await GatewayServer.ProbeAsync("127.0.0.1:1", TimeSpan.FromSeconds(5));
        Assert.Null(probe.Info);
        Assert.Contains("Nie ma połączenia", probe.Error);
    }

    [Fact]
    public async Task Wyszukiwanie_ZnajdujeBrameKtoraUdostepnia()
    {
        const int port = 41436;
        await using var a = new GatewayServer(new GatewayOptions
        {
            Port = 0, DiscoveryPort = port, MachineName = "PC-A", UpstreamHandler = new RecordingOllama(), LoopbackCountsAsLan = true, ShareOnLan = true,
        });
        await a.StartAsync();
        var found = await PeerDiscovery.FindAsync(TimeSpan.FromSeconds(1), port);
        var peer = Assert.Single(found, p => p.Computer == "PC-A");
        Assert.Equal(a.Port, peer.Port);
        Assert.Equal("http://127.0.0.1:" + a.Port, peer.Url);
    }

    [Fact]
    public void KoniecOdpowiedzi_LiczbyTokenowZOllamyIOpenAI_NieZTresci()
    {
        var tail = new ResponseTail();
        tail.Append(Encoding.UTF8.GetBytes("{\"message\":{\"content\":\"pole \\\"eval_count\\\": 99\"},\"done\":false}\n"));
        tail.Append(Encoding.UTF8.GetBytes(new string('x', 20_000))); // długa odpowiedź: zostaje tylko koniec
        tail.Append(Encoding.UTF8.GetBytes("{\"done\":true,\"prompt_eval_count\":3462,\"eval_count\":33}\n"));
        Assert.Equal((3462, 33), tail.Read());

        var content = new ResponseTail();
        content.Append(Encoding.UTF8.GetBytes("{\"message\":{\"content\":\"\\\"eval_count\\\": 99\"}}"));
        Assert.Equal((0, 0), content.Read());

        var openAi = new ResponseTail();
        openAi.Append(Encoding.UTF8.GetBytes("{\"choices\":[],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":7,\"total_tokens\":127}}"));
        Assert.Equal((120, 7), openAi.Read());
    }

    [Fact]
    public void Liczniki_SumujaPoKomputerze_ResetZeruje()
    {
        var tally = new TokenTally();
        tally.Add("PC-B", "10.0.0.5", false, 100, 10, DateTimeOffset.Now);
        tally.Add("pc-b", "10.0.0.6", false, 50, 5, DateTimeOffset.Now);
        tally.Add("PC-A", null, true, 7, 1, DateTimeOffset.Now);
        var rows = tally.Snapshot();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsThisComputer); // ten komputer pierwszy
        Assert.Equal((2L, 150L, 15L, "10.0.0.6"), (rows[1].Requests, rows[1].PromptTokens, rows[1].OutputTokens, rows[1].Address));

        tally.Load(rows, new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(2, tally.Snapshot().Count);
        tally.Reset();
        Assert.Empty(tally.Snapshot());
        Assert.True(tally.Since > DateTimeOffset.Now.AddMinutes(-1));
    }

    private static QueueItem Item(RequestOrigin origin, string computer, DateTimeOffset arrived)
        => new(origin, computer, "127.0.0.1", "test", "m", arrived);

    private static HttpRequestMessage Post(string url, string json, string program)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8) };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.Add(GatewayHeaders.Program, GatewayHeaders.Encode(program));
        return message;
    }

    /// <summary>Udaje Ollamę za bramą: zapisuje ścieżkę, treść i nagłówek programu.</summary>
    private sealed class RecordingOllama : HttpMessageHandler
    {
        public List<(string Path, string? Body, string? Program)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var program = request.Headers.TryGetValues(GatewayHeaders.Program, out var values) ? values.FirstOrDefault() : null;
            lock (Requests)
            {
                Requests.Add((request.RequestUri!.AbsolutePath, body, program));
            }

            var json = request.RequestUri!.AbsolutePath switch
            {
                "/api/tags" => """{"models":[{"name":"bielik"}]}""",
                "/api/chat" => "{\"message\":{\"content\":\"Dzień\"},\"done\":false}\n{\"message\":{\"content\":\" dobry\"},\"done\":true,\"prompt_eval_count\":12,\"eval_count\":3}\n",
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/x-ndjson") };
        }
    }
}
