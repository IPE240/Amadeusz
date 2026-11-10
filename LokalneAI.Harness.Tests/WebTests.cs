using System.Net;
using System.Text;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>Dostęp do internetu: wyszukiwanie, czytanie stron, blokada sieci biura, włączanie w profilu i sesji (bez prawdziwej sieci).</summary>
public sealed class WebTests
{
    private const string LiteHtml = """
        <table>
        <tr><td><a rel="nofollow" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fsklep.pkn.pl%2Fpn%2Den%2D1993.html&amp;rut=x" class='result-link'>PN-EN 1993 &ndash; <b>PKN</b></a></td></tr>
        <tr><td class='result-snippet'>Eurokod 3: <b>Projektowanie</b> konstrukcji stalowych.</td></tr>
        <tr><td><a rel="nofollow" href="https://duckduckgo.com/y.js?ad=1" class='result-link'>Reklama</a></td></tr>
        <tr><td class='result-snippet'>reklama</td></tr>
        </table>
        """;

    private static WebAccess Web(Func<HttpRequestMessage, HttpResponseMessage> respond, string resolvedIp = "93.184.216.34")
        => new(new WebAccessOptions
        {
            Handler = new FakeHttp(respond),
            Resolve = _ => Task.FromResult(new[] { IPAddress.Parse(resolvedIp) }),
        });

    private static HttpResponseMessage Html(string html)
        => new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    [Fact]
    public async Task Szukaj_WynikiZDuckDuckGo_BezReklam()
    {
        using var web = Web(_ => Html(LiteHtml));

        var result = await web.SearchAsync("PN-EN 1993");

        Assert.True(result.Success, result.Text);
        Assert.Contains("1. PN-EN 1993 – PKN", result.Text);
        Assert.Contains("https://sklep.pkn.pl/pn-en-1993.html", result.Text);
        Assert.Contains("Eurokod 3: Projektowanie konstrukcji stalowych.", result.Text);
        Assert.DoesNotContain("Reklama", result.Text);
    }

    [Fact]
    public async Task CzytajStrone_TekstBezSkryptowICzesciami()
    {
        var body = "<html><head><title>Kotwy M24</title><script>alert('x')</script></head><body><h1>Kotwy</h1><p>Długość 500&nbsp;mm.</p>"
            + "<p>" + new string('a', 7000) + "</p></body></html>";
        using var web = Web(_ => Html(body));

        var first = await web.FetchAsync("example.com/kotwy");
        var second = await web.FetchAsync("https://example.com/kotwy", 6000);

        Assert.True(first.Success, first.Text);
        Assert.Contains("Strona: Kotwy M24", first.Text);
        Assert.Contains("Długość 500 mm.", first.Text);
        Assert.DoesNotContain("alert", first.Text);
        Assert.Contains("od_znaku=6000", first.Text);
        Assert.Contains("Znaki 6000", second.Text);
    }

    [Theory]
    [InlineData("http://10.0.0.214:5441/")]
    [InlineData("http://10.0.0.30/budowa")]
    [InlineData("http://localhost:11435/api/tags")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://serwer-ods/")]
    [InlineData("file:///C:/Windows/win.ini")]
    public async Task SiecBiuraITenKomputer_Zablokowane(string address)
    {
        var asked = false;
        using var web = Web(_ => { asked = true; return Html("x"); });

        var result = await web.FetchAsync(address);

        Assert.False(result.Success);
        Assert.StartsWith("BŁĄD", result.Text);
        Assert.False(asked);
    }

    [Fact]
    public async Task NazwaWskazujacaAdresPrywatny_IPrzekierowanieDoBiura_Zablokowane()
    {
        using var rebind = Web(_ => Html("x"), resolvedIp: "10.0.0.214");
        var redirected = new List<string>();
        using var redirect = Web(request =>
        {
            redirected.Add(request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://10.0.0.30/budowa");
            return response;
        });

        var viaDns = await rebind.FetchAsync("https://zly.example.com/");
        var viaRedirect = await redirect.FetchAsync("https://example.com/");

        Assert.Contains("sieci lokalnej", viaDns.Text);
        Assert.False(viaRedirect.Success);
        Assert.Single(redirected); // do 10.0.0.30 nie poszło żadne zapytanie
    }

    [Fact]
    public void Profile_InternetTylkoGdziePozwala()
    {
        var cde = HarnessProfile.Load(Paths.Profile("cde"));
        var komputer = HarnessProfile.Load(Paths.Profile("komputer"));

        Assert.False(cde.Settings.Internet);
        Assert.Same(cde, cde.WithInternet());
        Assert.True(komputer.Settings.Internet);
        var withWeb = komputer.WithInternet();
        Assert.Contains(withWeb.Tools, t => t.Name == BuiltInTools.WebSearchName);
        Assert.Contains(withWeb.Tools, t => t.Name == BuiltInTools.WebFetchName);
        Assert.Equal(komputer.Tools.Count + 2, withWeb.Tools.Count);
        Assert.DoesNotContain(komputer.Tools, t => t.Name == BuiltInTools.WebSearchName);
    }

    [Fact]
    public async Task Sesja_ModelSzukaICzyta_PotemOdpowiada()
    {
        var settings = new ProfileSettings { Internet = true };
        var profile = new HarnessProfile("czat", "Odpowiadasz po polsku.", Array.Empty<ToolDefinition>(), settings).WithInternet();
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"szukaj_w_internecie\",\"argumenty\":{\"fraza\":\"PN-EN 1993\"}}]}",
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"czytaj_strone\",\"argumenty\":{\"adres\":\"https://sklep.pkn.pl/pn-en-1993.html\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"To Eurokod 3. Źródła: https://sklep.pkn.pl/pn-en-1993.html\"}");
        using var web = Web(request => Html(request.RequestUri!.Host.Contains("duckduckgo") ? LiteHtml : "<title>PN-EN 1993</title><p>Eurokod 3</p>"));
        using var session = new HarnessSession(llm, profile, null, new SessionOptions { Web = web });

        var result = await session.SendAsync("Co to jest PN-EN 1993?");

        Assert.Equal(TurnKind.Answer, result.Kind);
        Assert.Equal(2, result.Calls.Count);
        Assert.All(result.Calls, c => Assert.True(c.Success, c.Result));
        Assert.Contains("Eurokod 3", result.Calls[1].Result);
        Assert.Contains("Źródła", result.Text);
    }

    [Fact]
    public async Task Sesja_BezDostepu_ModelDostajeBlad()
    {
        var profile = new HarnessProfile("czat", "x", Array.Empty<ToolDefinition>(), new ProfileSettings { Internet = true }).WithInternet();
        var llm = new ScriptedLlm(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"szukaj_w_internecie\",\"argumenty\":{\"fraza\":\"x\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Nie mam dostępu do internetu.\"}");
        using var session = new HarnessSession(llm, profile, null, new SessionOptions());

        var result = await session.SendAsync("Szukaj x");

        Assert.False(result.Calls[0].Success);
        Assert.Contains("wyłączony", result.Calls[0].Result);
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2a00:1450:4001::64", false)]
    public void AdresyPrywatne(string ip, bool isPrivate) => Assert.Equal(isPrivate, WebAccess.IsPrivate(IPAddress.Parse(ip)));
}
