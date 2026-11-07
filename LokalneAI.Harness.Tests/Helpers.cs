using System.Net;
using System.Text;
using System.Text.Json;
using LokalneAI.Harness.Llm;

namespace LokalneAI.Harness.Tests;

/// <summary>Udawany model: zwraca przygotowane kroki po kolei i zapamiętuje zapytania.</summary>
internal sealed class ScriptedLlm : ILlmClient
{
    private readonly Queue<string> _responses;

    public ScriptedLlm(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }

    public List<LlmRequest> Requests { get; } = new List<LlmRequest>();

    public int PromptTokens { get; set; } = 1234;

    public string ModelName => "udawany";

    public int ContextLength { get; set; } = 8192;

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var content = _responses.Count > 0 ? _responses.Dequeue() : "{\"krok\":\"odpowiedz\",\"tekst\":\"koniec skryptu\"}";
        return Task.FromResult(new LlmResponse { Content = content, PromptTokens = PromptTokens, OutputTokens = 50 });
    }
}

internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public List<string> Bodies { get; } = new List<string>();

    public List<string> Paths { get; } = new List<string>();

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync());
        Paths.Add(request.RequestUri.AbsolutePath);
        return _respond(request);
    }
}

internal static class Paths
{
    /// <summary>Folder repozytorium LokalneAI (szukany w górę od folderu testów).</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LokalneAI.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new DirectoryNotFoundException("Nie znalazłem LokalneAI.sln");
        }
    }

    public static string Profile(string name) => Path.Combine(Root, "profile", name);

    public static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
