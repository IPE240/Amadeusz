using System.Text.Json;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>
/// Liczniki tokenów i czasów (<see cref="ModelCallMetrics"/>): przyrost promptu między wywołaniami, opis tego, co dopisano,
/// pomiar samych zasad tylko przy Ollamie, czasy z odpowiedzi Ollamy.
/// </summary>
public class TokenMetricsTests
{
    /// <summary>Model „liczący znaki”: prompt = suma znaków wiadomości, odpowiedź = znaki odpowiedzi — przyrost da się sprawdzić co do znaku.</summary>
    private sealed class CountingLlm : ILlmClient
    {
        private readonly Queue<string> _responses;

        public CountingLlm(params string[] responses) => _responses = new Queue<string>(responses);

        public List<LlmRequest> Requests { get; } = new List<LlmRequest>();

        public string ModelName => "liczacy";

        public int ContextLength => 8192;

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var content = _responses.Count > 0 ? _responses.Dequeue() : "{\"krok\":\"odpowiedz\",\"tekst\":\"koniec\"}";
            return Task.FromResult(new LlmResponse
            {
                Content = content,
                PromptTokens = request.Messages.Sum(m => m.Content.Length),
                OutputTokens = content.Length,
            });
        }
    }

    private static HarnessProfile Counter() => new HarnessProfile(
        "test",
        "Liczysz.",
        new[] { new ToolDefinition("policz", "Liczy.", "{\"type\":\"object\",\"properties\":{\"co\":{\"type\":\"string\"}},\"required\":[\"co\"],\"additionalProperties\":false}") },
        new ProfileSettings { Finish = FinishMode.Model });

    private const string CallStep = "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"policz\",\"argumenty\":{\"co\":\"x\"}}]}";
    private const string AnswerStep = "{\"krok\":\"odpowiedz\",\"tekst\":\"42\"}";

    [Fact]
    public async Task Tura_PrzyrostTokenow_ToDopisaneWiadomosci_IZdarzeniePrzedNarzedziem()
    {
        var llm = new CountingLlm(CallStep, AnswerStep, AnswerStep);
        var executor = new DelegateExecutor((c, ct) => Task.FromResult(ToolResult.Ok("wynik 42")));
        using var session = new HarnessSession(llm, Counter(), executor);
        var events = new List<HarnessEventKind>();
        session.Progress += (s, e) => events.Add(e.Kind);

        var turn = await session.SendAsync("ile?");

        Assert.Equal(2, turn.ModelCallDetails.Count);
        var first = turn.ModelCallDetails[0];
        var second = turn.ModelCallDetails[1];
        Assert.Equal("wywolania", first.Outcome);
        Assert.Equal("pytanie", first.AddedKind);
        Assert.Null(first.AddedTokens); // bez pomiaru samych zasad nie wiadomo, ile z pierwszego promptu to pytanie
        Assert.Equal(llm.Requests[0].Messages.Sum(m => m.Content.Length), first.PromptTokens);
        Assert.True(first.EstimatedPromptTokens > 0);
        Assert.Equal("odpowiedz", second.Outcome);
        Assert.Equal("wyniki narzędzi", second.AddedKind);
        Assert.Equal(llm.Requests[1].Messages.Last().Content.Length, second.AddedTokens);
        Assert.Equal(2, second.CallInTurn);
        // Liczby kroku przychodzą przed wywołaniem narzędzia, które ten krok zlecił.
        Assert.True(events.IndexOf(HarnessEventKind.ModelCall) < events.IndexOf(HarnessEventKind.ToolStarted));
        Assert.Equal(2, events.Count(k => k == HarnessEventKind.ModelCall));
        Assert.True(turn.Calls.Single().Duration >= TimeSpan.Zero);

        // Następna tura: przyrost to samo nowe pytanie.
        var next = await session.SendAsync("a teraz?");
        var call = Assert.Single(next.ModelCallDetails);
        Assert.Equal("pytanie", call.AddedKind);
        Assert.Equal("a teraz?".Length, call.AddedTokens);
    }

    [Fact]
    public async Task PomiarZasad_TylkoOllama_JedenRazNaSesje_ICzasyZSerwera()
    {
        var count = 0;
        var http = new FakeHttp(_ =>
        {
            count++;
            return count == 1
                ? FakeHttp.Json("{\"model\":\"m\",\"message\":{\"content\":\"x\"},\"prompt_eval_count\":3000,\"eval_count\":1}")
                : FakeHttp.Json("{\"model\":\"m\",\"message\":{\"content\":" + JsonSerializer.Serialize(AnswerStep) + "},"
                    + "\"prompt_eval_count\":" + (3000 + 20 * (count - 1) + 15 * (count - 2)) + ",\"eval_count\":15,"
                    + "\"load_duration\":0,\"prompt_eval_duration\":500000000,\"eval_duration\":250000000,\"total_duration\":800000000}");
        });
        using var llm = new OllamaLlmClient(new OllamaOptions { Url = "http://test", Model = "m" }, http);
        using var session = new HarnessSession(llm, Counter(), null, new SessionOptions { MeasureSystemPrompt = true });

        var turn = await session.SendAsync("ile?");

        Assert.Equal(2, http.Bodies.Count);
        using (var probe = JsonDocument.Parse(http.Bodies[0]))
        {
            Assert.Equal(1, probe.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
            Assert.Equal("system", probe.RootElement.GetProperty("messages").EnumerateArray().Single().GetProperty("role").GetString());
        }

        var m = turn.ModelCallDetails.Single();
        Assert.Equal(3000, m.SystemTokens);
        Assert.Equal(20, m.AddedTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(500), m.PromptTime);
        Assert.Equal(TimeSpan.FromMilliseconds(250), m.OutputTime);
        Assert.Equal(TimeSpan.FromMilliseconds(800), m.ServerTime);
        Assert.Equal(TimeSpan.Zero, m.QueueTime);

        await session.SendAsync("a teraz?");
        Assert.Equal(3, http.Bodies.Count); // bez drugiego pomiaru
    }

    [Fact]
    public async Task PomiarZasad_PozaOllama_NieWysylaDodatkowegoZapytania()
    {
        var llm = new ScriptedLlm(AnswerStep);
        using var session = new HarnessSession(llm, Counter(), null, new SessionOptions { MeasureSystemPrompt = true });

        var turn = await session.SendAsync("ile?");

        Assert.Single(llm.Requests);
        Assert.Null(turn.ModelCallDetails.Single().SystemTokens);
    }
}
