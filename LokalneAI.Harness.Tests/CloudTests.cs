using System.Net;
using System.Text.Json;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using Xunit;

namespace LokalneAI.Harness.Tests;

/// <summary>Modele w chmurze: zasada biura (kto liczy, co wolno w profilu), klienci Claude, GPT i Gemini, lista modeli. Bez sieci.</summary>
public class CloudTests
{
    private const string Schema = "{\"anyOf\":[{\"type\":\"object\",\"properties\":{\"krok\":{\"type\":\"string\",\"enum\":[\"odpowiedz\"]},\"tekst\":{\"type\":\"string\"}},\"required\":[\"krok\",\"tekst\"]}]}";

    [Theory]
    [InlineData("http://127.0.0.1:11434", null)]
    [InlineData("http://localhost:11435", null)]
    [InlineData("http://PC-ODS12:11435", null)]
    [InlineData("http://10.0.0.50:11435", null)]
    [InlineData("http://192.168.1.5:8080/v1", null)]
    [InlineData("http://serwer-ai.ods.local:8080", null)]
    [InlineData("http://[fe80::1]:11434", null)]
    [InlineData("https://api.anthropic.com", "anthropic")]
    [InlineData("https://api.openai.com/v1", "openai")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/openai", "google")]
    [InlineData("https://api.deepseek.com", CloudProvider.OutsideListId)]
    [InlineData("https://openrouter.ai/api/v1", CloudProvider.OutsideListId)]
    [InlineData("http://8.8.8.8:11434", CloudProvider.OutsideListId)]
    public void ClassifyUrl_SiecBiuraDozwoleniDostawcyIReszta(string url, string expected)
        => Assert.Equal(expected, CloudProvider.ClassifyUrl(url));

    [Theory]
    [InlineData("deepseek-v3.1:671b-cloud", true)]
    [InlineData("gpt-oss:120b-cloud", true)]
    [InlineData("glm-4.6:cloud", true)]
    [InlineData("SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M", false)]
    [InlineData("deepseek-r1:8b", false)]
    public void OllamaCloud_RozpoznajeModeleLiczoneNaOllamaCom(string model, bool cloud)
        => Assert.Equal(cloud, CloudProvider.IsOllamaCloudModel(model));

    [Fact]
    public void Find_TylkoTrzechDostawcow()
    {
        Assert.Equal(new[] { "anthropic", "openai", "google" }, CloudProvider.All.Select(p => p.Id));
        Assert.Null(CloudProvider.Find("deepseek"));
        Assert.Same(CloudProvider.Anthropic, CloudProvider.Find(" Anthropic "));
    }

    [Fact]
    public void ProfilCde_TylkoLokalneIClaude()
    {
        var settings = HarnessProfile.LoadSettings(Paths.Profile("cde"));

        Assert.True(settings.AllowsCloud(null));
        Assert.True(settings.AllowsCloud("anthropic"));
        Assert.False(settings.AllowsCloud("openai"));
        Assert.False(settings.AllowsCloud("google"));
        Assert.False(settings.AllowsCloud(CloudProvider.OutsideListId));
        Assert.False(settings.AllowsCloud(CloudProvider.OllamaCloudId));
    }

    [Fact]
    public void ProfilBezPolaChmura_TylkoLokalne()
    {
        var settings = new ProfileSettings();
        settings.CloudProviders.Add("deepseek");

        Assert.True(settings.AllowsCloud(null));
        Assert.False(settings.AllowsCloud("anthropic"));
        Assert.False(settings.AllowsCloud("deepseek"));
    }

    [Fact]
    public void Sesja_OdmawiaGptWProfiluCde_PrzyjmujeClaude()
    {
        var profile = HarnessProfile.Load(Paths.Profile("cde"));
        using var gpt = (OpenAiCompatibleLlmClient)CloudLlm.Create(CloudProvider.OpenAI, "klucz", "gpt-test");
        using var claude = (AnthropicLlmClient)CloudLlm.Create(CloudProvider.Anthropic, "klucz", "claude-test");

        var error = Assert.Throws<InvalidOperationException>(() => new HarnessSession(gpt, profile, null));
        Assert.Contains("modele lokalne i Claude (Anthropic)", error.Message);
        Assert.Contains("GPT (OpenAI)", error.Message);
        _ = new HarnessSession(claude, profile, null);
    }

    [Fact]
    public void Sesja_OdmawiaSerweraSpozaListyIChmuryOllamy()
    {
        var profile = HarnessProfile.Load(Paths.Profile("pliki"));
        using var deepseek = new OpenAiCompatibleLlmClient(new OpenAiCompatibleOptions { Url = "https://api.deepseek.com", Model = "deepseek-chat" });
        using var ollamaCloud = new OllamaLlmClient(new OllamaOptions { Model = "deepseek-v3.1:671b-cloud" });
        using var local = new OllamaLlmClient(new OllamaOptions { Model = "deepseek-r1:8b" });

        Assert.Contains("spoza listy", Assert.Throws<InvalidOperationException>(() => new HarnessSession(deepseek, profile, null)).Message);
        Assert.Contains("chmura Ollamy", Assert.Throws<InvalidOperationException>(() => new HarnessSession(ollamaCloud, profile, null)).Message);
        _ = new HarnessSession(local, profile, null);
    }

    [Fact]
    public async Task Claude_SystemOsobno_NarzedzieKrok_PamiecPodreczna()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(
            "{\"model\":\"claude-test\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"krok\",\"input\":{\"dane\":{\"krok\":\"odpowiedz\",\"tekst\":\"Gotowe\"}}}],"
            + "\"stop_reason\":\"tool_use\",\"usage\":{\"input_tokens\":10,\"cache_read_input_tokens\":900,\"cache_creation_input_tokens\":100,\"output_tokens\":20}}"));
        using var client = new AnthropicLlmClient(new AnthropicOptions { ApiKey = "klucz", Model = "claude-test" }, http);
        var messages = new[]
        {
            new LlmMessage("system", "Jesteś asystentem."),
            new LlmMessage("user", "pierwsze"),
            new LlmMessage("assistant", "{\"krok\":\"odpowiedz\",\"tekst\":\"a\"}"),
            new LlmMessage("tool", "wynik 1"),
            new LlmMessage("user", "drugie"),
        };

        var response = await client.CompleteAsync(new LlmRequest { Messages = messages, JsonSchema = Schema }, CancellationToken.None);

        Assert.Equal("{\"krok\":\"odpowiedz\",\"tekst\":\"Gotowe\"}", response.Content);
        Assert.Equal(1010, response.PromptTokens);
        Assert.Equal(20, response.OutputTokens);
        Assert.Equal("/v1/messages", http.Paths[0]);
        var body = Paths.Json(http.Bodies[0]);
        Assert.Equal("Jesteś asystentem.", body.GetProperty("system")[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", body.GetProperty("system")[0].GetProperty("cache_control").GetProperty("type").GetString());
        var turns = body.GetProperty("messages");
        Assert.Equal(3, turns.GetArrayLength());
        Assert.Equal("user", turns[2].GetProperty("role").GetString());
        Assert.Equal("wynik 1\n\ndrugie", turns[2].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("object", body.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("type").GetString());
        Assert.True(body.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("properties").GetProperty("dane").TryGetProperty("anyOf", out _));
        Assert.Equal("krok", body.GetProperty("tool_choice").GetProperty("name").GetString());
        Assert.True(body.GetProperty("max_tokens").GetInt32() > 0);
    }

    [Fact]
    public async Task Claude_CalaSesjaTekli_KrokZNarzedziaWykonujeSieWSymulatorze()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(
            "{\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"krok\",\"input\":{\"dane\":"
            + "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":6000,\"y_mm\":0,\"poziom_dol_m\":0,\"poziom_gora_m\":3.53,\"profil\":\"HEB300\"}}]}}}],"
            + "\"usage\":{\"input_tokens\":5000,\"output_tokens\":60}}"));
        using var claude = CloudLlm.Create(CloudProvider.Anthropic, "klucz", "claude-test", handler: http) as AnthropicLlmClient;
        var model = new SimulatedModelExecutor();
        using var session = new HarnessSession(claude!, HarnessProfile.Load(Paths.Profile("tekla")), model);

        var turn = await session.SendAsync("wstaw słup HEB300 w punkcie 6000, 0 od zera do 3,53");

        Assert.Equal(TurnKind.Answer, turn.Kind);
        Assert.True(turn.Calls.Single().Success);
        Assert.Equal("HEB300", model.Objects.Values.Single()["profil"]);
        // Claude dostał tę samą dokumentację profilu co model lokalny (polecenie systemowe z karty i narzędzi).
        Assert.Contains("wstaw_slup", Paths.Json(http.Bodies[0]).GetProperty("system")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Claude_CzatBezSchematu_ZwykłyTekstBezNarzedzi()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("{\"content\":[{\"type\":\"text\",\"text\":\"Dzień dobry\"}],\"usage\":{\"input_tokens\":5,\"output_tokens\":3}}"));
        using var client = new AnthropicLlmClient(new AnthropicOptions { ApiKey = "klucz", Model = "claude-test" }, http);

        var response = await client.CompleteAsync(new LlmRequest { Messages = new[] { new LlmMessage("user", "cześć") } }, CancellationToken.None);

        Assert.Equal("Dzień dobry", response.Content);
        Assert.False(Paths.Json(http.Bodies[0]).TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task Claude_OdrzuconaTemperatura_PonawiaBezNiej()
    {
        var calls = 0;
        var http = new FakeHttp(_ => ++calls == 1
            ? FakeHttp.Json("{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"temperature is not supported for this model\"}}", HttpStatusCode.BadRequest)
            : FakeHttp.Json("{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}"));
        using var client = new AnthropicLlmClient(new AnthropicOptions { ApiKey = "klucz", Model = "claude-test" }, http);

        var response = await client.CompleteAsync(new LlmRequest { Messages = new[] { new LlmMessage("user", "x") } }, CancellationToken.None);

        Assert.Equal("ok", response.Content);
        Assert.True(Paths.Json(http.Bodies[0]).TryGetProperty("temperature", out _));
        Assert.False(Paths.Json(http.Bodies[1]).TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task Claude_ZlyKlucz_CzytelnyBladPoPolsku()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}", HttpStatusCode.Unauthorized));
        using var client = new AnthropicLlmClient(new AnthropicOptions { ApiKey = "zly", Model = "claude-test" }, http);

        var error = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync(new LlmRequest { Messages = new[] { new LlmMessage("user", "x") } }, CancellationToken.None));

        Assert.Contains("Anthropic: klucz API nie działa", error.Message);
        Assert.Contains("(401)", error.Message);
        Assert.Contains("invalid x-api-key", error.Message);
    }

    [Fact]
    public async Task Gpt_SchematWPoleceniu_JsonObject_WyjmujeJsonZOdpowiedzi()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(
            "{\"choices\":[{\"message\":{\"content\":\"```json\\n{\\\"krok\\\":\\\"odpowiedz\\\",\\\"tekst\\\":\\\"ok\\\"}\\n```\"}}],\"usage\":{\"prompt_tokens\":7,\"completion_tokens\":2}}"));
        using var client = (OpenAiCompatibleLlmClient)CloudLlm.Create(CloudProvider.OpenAI, "klucz", "gpt-test", handler: http);

        var response = await client.CompleteAsync(
            new LlmRequest { Messages = new[] { new LlmMessage("system", "Instrukcja."), new LlmMessage("user", "x") }, JsonSchema = Schema }, CancellationToken.None);

        Assert.Equal("{\"krok\":\"odpowiedz\",\"tekst\":\"ok\"}", response.Content);
        Assert.Equal("/v1/chat/completions", http.Paths[0]);
        var body = Paths.Json(http.Bodies[0]);
        Assert.Equal("json_object", body.GetProperty("response_format").GetProperty("type").GetString());
        var system = body.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.StartsWith("Instrukcja.", system);
        Assert.Contains("FORMAT ODPOWIEDZI", system);
        Assert.Contains("\"anyOf\"", system);
        Assert.Equal("openai", client.CloudProviderId);
    }

    [Fact]
    public async Task Gpt_OdrzuconaTemperatura_PonawiaBezNiejIDalejNieWysyla()
    {
        var calls = 0;
        var http = new FakeHttp(_ => ++calls == 1
            ? FakeHttp.Json("{\"error\":{\"message\":\"Unsupported value: 'temperature' does not support 0.1 with this model.\"}}", HttpStatusCode.BadRequest)
            : FakeHttp.Json("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
        using var client = CloudLlm.Create(CloudProvider.OpenAI, "klucz", "gpt-test", handler: http) as OpenAiCompatibleLlmClient;
        var request = new LlmRequest { Messages = new[] { new LlmMessage("user", "x") } };

        await client!.CompleteAsync(request, CancellationToken.None);
        await client.CompleteAsync(request, CancellationToken.None);

        Assert.Equal(3, http.Bodies.Count);
        Assert.True(Paths.Json(http.Bodies[0]).TryGetProperty("temperature", out _));
        Assert.False(Paths.Json(http.Bodies[1]).TryGetProperty("temperature", out _));
        Assert.False(Paths.Json(http.Bodies[2]).TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task Gemini_AdresGoogleBezResponseFormat()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("{\"choices\":[{\"message\":{\"content\":\"{\\\"krok\\\":\\\"odpowiedz\\\",\\\"tekst\\\":\\\"ok\\\"}\"}}]}"));
        using var client = (OpenAiCompatibleLlmClient)CloudLlm.Create(CloudProvider.Google, "klucz", "gemini-test", handler: http);

        await client.CompleteAsync(new LlmRequest { Messages = new[] { new LlmMessage("user", "x") }, JsonSchema = Schema }, CancellationToken.None);

        Assert.Equal("/v1beta/openai/chat/completions", http.Paths[0]);
        var body = Paths.Json(http.Bodies[0]);
        Assert.False(body.TryGetProperty("response_format", out _));
        Assert.Equal("system", body.GetProperty("messages")[1].GetProperty("role").GetString());
        Assert.Equal("google", client.CloudProviderId);
    }

    [Fact]
    public void SerwerLokalnyZgodnyZOpenAi_BezZmian()
    {
        using var client = new OpenAiCompatibleLlmClient(new OpenAiCompatibleOptions { Url = "http://serwer:8080/v1", Model = "qwen" });

        var body = Paths.Json(client.BuildBody(new LlmRequest { Messages = new[] { new LlmMessage("user", "x") }, JsonSchema = Schema }));

        Assert.Null(client.CloudProviderId);
        Assert.Equal("json_schema", body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.True(body.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());
    }

    [Fact]
    public void Ollama_ZapytanieBezMyslenia_ChybaZeDozwolone()
    {
        // qwen3.5 potrafił myśleć kilka minut przed krokiem w JSON; Ollama przyjmuje "think": false także dla modeli bez myślenia.
        using var client = new OllamaLlmClient(new OllamaOptions { Model = "qwen" });
        using var thinking = new OllamaLlmClient(new OllamaOptions { Model = "qwen", AllowThinking = true });
        var request = new LlmRequest { Messages = new[] { new LlmMessage("user", "x") }, JsonSchema = Schema };

        var body = Paths.Json(client.BuildBody(request));
        var bodyThinking = Paths.Json(thinking.BuildBody(request));

        Assert.False(body.GetProperty("think").GetBoolean());
        Assert.True(body.GetProperty("format").TryGetProperty("anyOf", out _));   // schemat kroku idzie dalej jako gramatyka
        Assert.False(bodyThinking.TryGetProperty("think", out _));
    }

    [Fact]
    public void ListaModeli_BezModeliDoObrazowMowyIWektorow()
    {
        var openAi = CloudModels.Parse(CloudProvider.OpenAI,
            "{\"data\":[{\"id\":\"gpt-test-mini\",\"created\":200},{\"id\":\"gpt-test\",\"created\":300},{\"id\":\"text-embedding-3-small\"},{\"id\":\"gpt-image-1\"},"
            + "{\"id\":\"whisper-1\"},{\"id\":\"o4-mini\",\"created\":100},{\"id\":\"gpt-test-pro\"},{\"id\":\"tts-1\"},{\"id\":\"gpt-4o-realtime-preview\"}]}");
        var google = CloudModels.Parse(CloudProvider.Google,
            "{\"data\":[{\"id\":\"models/gemini-test-flash\"},{\"id\":\"models/text-embedding-004\"},{\"id\":\"models/imagen-test\"},{\"id\":\"models/gemini-test-pro\"}]}");
        var anthropic = CloudModels.Parse(CloudProvider.Anthropic,
            "{\"data\":[{\"id\":\"claude-test-1\",\"display_name\":\"Claude Test 1\",\"created_at\":\"2026-01-01T00:00:00Z\"}],\"has_more\":false}");

        Assert.Equal(new[] { "gpt-test", "gpt-test-mini", "o4-mini" }, openAi.Select(m => m.Id));
        Assert.Equal(new[] { "gemini-test-pro", "gemini-test-flash" }, google.Select(m => m.Id));
        Assert.Equal("Claude Test 1", anthropic.Single().DisplayName);
    }

    [Fact]
    public async Task ListaModeli_ZlyKluczToLlmException()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("{\"error\":{\"message\":\"Incorrect API key provided\"}}", HttpStatusCode.Unauthorized));

        var error = await Assert.ThrowsAsync<LlmException>(() => CloudModels.ListAsync(CloudProvider.OpenAI, "zly", http));

        Assert.Contains("OpenAI: klucz API nie działa", error.Message);
    }

    [Fact]
    public void BrakKlucza_CzytelnyBlad()
        => Assert.Contains("brak klucza API", Assert.Throws<LlmException>(() => CloudLlm.Create(CloudProvider.Anthropic, " ", "claude-test")).Message);

    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("Oto krok: {\"a\":{\"b\":2}} — gotowe.", "{\"a\":{\"b\":2}}")]
    [InlineData("bez json", "bez json")]
    public void ExtractJson_ZdejmujeOpakowanie(string text, string expected)
        => Assert.Equal(expected, CloudHttp.ExtractJson(text));
}
