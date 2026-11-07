using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LokalneAI.Core.Ollama;

namespace LokalneAI.Core.Tests;

public class OllamaClientTests
{
    private const string Bielik = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M";

    [Fact]
    public async Task ChatStream_SkladaTekstIZwracaStatystykiZOstatniegoFragmentu()
    {
        var handler = new FakeHandler(_ => FakeHandler.NdJson(
            """{"model":"m","message":{"role":"assistant","content":"Wynik "},"done":false}""",
            """{"model":"m","message":{"role":"assistant","content":"to 62,75."},"done":false}""",
            """{"model":"m","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","total_duration":6800000000,"load_duration":5600000000,"prompt_eval_count":42,"prompt_eval_duration":150000000,"eval_count":113,"eval_duration":5000000000}"""));
        using var client = new OllamaClient(handler: handler);

        var text = "";
        ChatChunk? last = null;
        await foreach (var chunk in client.ChatStreamAsync(new ChatRequest { Model = Bielik, Messages = [new("user", "Policz")] }))
        {
            text += chunk.Message?.Content;
            last = chunk;
        }

        Assert.Equal("Wynik to 62,75.", text);
        Assert.NotNull(last);
        Assert.True(last.Done);
        Assert.Equal(113, last.EvalCount);
        Assert.Equal(42, last.PromptEvalCount);
        Assert.Equal(5_000_000_000, last.EvalDuration);
    }

    [Fact]
    public async Task ChatStream_WysylaParametryWNazwachApiOllamy()
    {
        var handler = new FakeHandler(_ => FakeHandler.NdJson("""{"done":true}"""));
        using var client = new OllamaClient(handler: handler);
        var request = new ChatRequest
        {
            Model = Bielik,
            Messages = [new("system", "Odpowiadaj po polsku."), new("user", "Ile to 2+2?")],
            Options = new ChatOptions { Temperature = 0.1, NumCtx = 8192 },
        };

        await foreach (var _ in client.ChatStreamAsync(request))
        {
        }

        var (method, path, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/api/chat", path);
        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal(Bielik, root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal(8192, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal(0.1, root.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Ile to 2+2?", root.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.False(root.TryGetProperty("keep_alive", out _), "puste pola nie idą do Ollamy");
        Assert.False(root.GetProperty("messages")[0].TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task ChatStream_BrakModelu_DajeZrozumialyBlad()
    {
        // Prawdziwa odpowiedź Ollamy 0.40.14 na nieznany model.
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"error":"model 'nie-ma-takiego:1b' not found"}""", HttpStatusCode.NotFound));
        using var client = new OllamaClient(handler: handler);

        var error = await Assert.ThrowsAsync<ModelNotFoundException>(async () =>
        {
            await foreach (var _ in client.ChatStreamAsync(new ChatRequest { Model = "nie-ma-takiego:1b", Messages = [new("user", "x")] }))
            {
            }
        });

        Assert.Equal("nie-ma-takiego:1b", error.Model);
        Assert.Contains("ollama pull nie-ma-takiego:1b", error.Message);
    }

    [Fact]
    public async Task ChatStream_BladWTrakcieStrumienia_PrzerywaZKomunikatem()
    {
        var handler = new FakeHandler(_ => FakeHandler.NdJson(
            """{"message":{"role":"assistant","content":"Zaczynam"},"done":false}""",
            """{"error":"out of memory"}"""));
        using var client = new OllamaClient(handler: handler);

        var error = await Assert.ThrowsAsync<OllamaException>(async () =>
        {
            await foreach (var _ in client.ChatStreamAsync(new ChatRequest { Model = Bielik, Messages = [new("user", "x")] }))
            {
            }
        });

        Assert.Contains("out of memory", error.Message);
    }

    [Fact]
    public async Task OllamaWylaczona_DajeBladNiedostepnosciZAdresem()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException(
            "Nie można nawiązać połączenia", new SocketException((int)SocketError.ConnectionRefused)));
        using var client = new OllamaClient("127.0.0.1:11434", handler);

        var error = await Assert.ThrowsAsync<OllamaUnavailableException>(() => client.GetVersionAsync());

        Assert.Equal("http://127.0.0.1:11434", error.Url);
        Assert.Contains("nie odpowiada", error.Message);
    }

    [Fact]
    public async Task OllamaMilczy_PoLimicieCzasuDajeBladNiedostepnosci()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return FakeHandler.Json("{}");
        });
        using var client = new OllamaClient(handler: handler) { MetadataTimeout = TimeSpan.FromMilliseconds(100) };

        await Assert.ThrowsAsync<OllamaUnavailableException>(() => client.ListModelsAsync());
    }

    [Fact]
    public async Task Anulowanie_PrzezUzytkownika_NieJestBledemOllamy()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return FakeHandler.NdJson("""{"done":true}""");
        });
        using var client = new OllamaClient(handler: handler);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.ChatStreamAsync(new ChatRequest { Model = Bielik, Messages = [new("user", "x")] }, stop.Token))
            {
            }
        });
    }

    [Fact]
    public async Task ListModels_CzytaListeZApiTags()
    {
        // Fragment prawdziwej odpowiedzi Ollamy 0.40.14 z tego PC.
        var handler = new FakeHandler(_ => FakeHandler.Json("""
            {"models":[{"name":"SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M","model":"SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M",
            "modified_at":"2026-11-04T00:12:24.9737586+01:00","size":6724052531,"digest":"64d9b83a",
            "details":{"format":"gguf","family":"llama","parameter_size":"11.2B","quantization_level":"Q4_K_M","context_length":32768},
            "capabilities":["completion","tools"]}]}
            """));
        using var client = new OllamaClient(handler: handler);

        var model = Assert.Single(await client.ListModelsAsync());

        Assert.Equal(Bielik, model.Name);
        Assert.Equal(6_724_052_531, model.Size);
        Assert.Equal("Q4_K_M", model.Details?.QuantizationLevel);
        Assert.Equal(32768, model.Details?.ContextLength);
        Assert.Contains("completion", model.Capabilities!);
        Assert.Equal("/api/tags", handler.Requests[0].Path);
    }

    [Fact]
    public async Task ListRunning_LiczyUdzialKartyGraficznej()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""
            {"models":[{"name":"SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M","size":9234000000,"size_vram":6741000000,
            "context_length":8192,"expires_at":"2026-11-05T15:10:00+01:00"}]}
            """));
        using var client = new OllamaClient(handler: handler);

        var running = Assert.Single(await client.ListRunningAsync());

        Assert.Equal(0.73, running.GpuShare, 2);
        Assert.Equal(8192, running.ContextLength);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 15, 10, 0, TimeSpan.FromHours(1)), running.ExpiresAt);
    }

    [Fact]
    public async Task Pull_PrzekazujePostepIKonczyNaSuccess()
    {
        var handler = new FakeHandler(_ => FakeHandler.NdJson(
            """{"status":"pulling manifest"}""",
            """{"status":"pulling c16841621efe","digest":"sha256:c168","total":6724051584,"completed":1000000000}""",
            """{"status":"pulling c16841621efe","digest":"sha256:c168","total":6724051584,"completed":6724051584}""",
            """{"status":"verifying sha256 digest"}""",
            """{"status":"success"}""",
            """{"status":"to-sie-nie-powinno-wczytac"}"""));
        using var client = new OllamaClient(handler: handler);

        var statuses = new List<string?>();
        await foreach (var progress in client.PullAsync(Bielik))
        {
            statuses.Add(progress.Status);
        }

        Assert.Equal("success", statuses[^1]);
        Assert.Equal(5, statuses.Count);
        Assert.Equal("/api/pull", handler.Requests[0].Path);
        Assert.Contains("\"model\":\"" + Bielik + "\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task Unload_WysylaKeepAliveZero()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"done":true,"done_reason":"unload"}"""));
        using var client = new OllamaClient(handler: handler);

        await client.UnloadAsync(Bielik);

        var (_, path, body) = Assert.Single(handler.Requests);
        Assert.Equal("/api/generate", path);
        Assert.Contains("\"keep_alive\":0", body);
    }

    [Theory]
    [InlineData(null, "http://127.0.0.1:11434")]
    [InlineData("", "http://127.0.0.1:11434")]
    [InlineData("127.0.0.1:11434/", "http://127.0.0.1:11434")]
    [InlineData(" http://10.0.0.5:11434 ", "http://10.0.0.5:11434")]
    public void NormalizeUrl_UzupelniaAdres(string? input, string expected)
        => Assert.Equal(expected, OllamaClient.NormalizeUrl(input));

    [Theory]
    [InlineData("ftp://serwer")]
    [InlineData("http://zły adres")]
    public void NormalizeUrl_OdrzucaZlyAdres(string input)
        => Assert.Throws<ArgumentException>(() => OllamaClient.NormalizeUrl(input));
}
