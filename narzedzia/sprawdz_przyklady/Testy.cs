// Sekcja 13 z docs\biblioteki-dla-agentow.md, skopiowana 1:1 (zamiast xUnit — mały Assert niżej).
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace MojProgram
{
internal static class Testy
{
    public static async Task TestWykonawcy()
    {
        var definition = new ToolDefinition("wstaw_slup", "Wstawia słup.",
            "{\"type\":\"object\",\"properties\":{\"x_mm\":{\"type\":\"number\"},\"profil\":{\"type\":\"string\"}},\"required\":[\"x_mm\",\"profil\"],\"additionalProperties\":false}",
            mutates: true);
        var call = new ToolCall(definition, JsonDocument.Parse("{\"x_mm\":6000,\"profil\":\"HEB300\"}").RootElement, step: 1, index: 1);
        var result = await new MojWykonawca().ExecuteAsync(call, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Matches(@"id=\d+", result.Text);
    }

    public static async Task TestSesji(string profileFolder)
    {
        // Profil z szablony\profil: wstaw_slup wymaga x_mm, y_mm, poziom_dol_m, poziom_gora_m i profil.
        var llm = new UdawanyModel(
            "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"wstaw_slup\",\"argumenty\":{\"x_mm\":6000,\"y_mm\":0,\"poziom_dol_m\":0,\"poziom_gora_m\":3.53,\"profil\":\"HEB300\"}}]}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Wstawiłem słup HEB300.\"}");
        using (var session = new HarnessSession(llm, HarnessProfile.Load(profileFolder), new MojWykonawca()))
        {
            var turn = await session.SendAsync("wstaw słup HEB300 w 6000, 0 od zera do 3,53");
            Assert.Equal(TurnKind.Answer, turn.Kind);
            Assert.Equal("wstaw_slup", turn.Calls[0].Tool);
            Assert.True(turn.Calls[0].Success);              // argument niezgodny ze schematem = Success false i błąd dla modelu
        }
    }
}

sealed class UdawanyModel : ILlmClient
{
    private readonly Queue<string> _kroki;
    public UdawanyModel(params string[] kroki) => _kroki = new Queue<string>(kroki);
    public string ModelName => "udawany";
    public int ContextLength => 8192;
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new LlmResponse { Content = _kroki.Dequeue(), PromptTokens = 1000, OutputTokens = 50 });
}

internal static class Assert
{
    public static void True(bool value, string message = null)
    {
        if (!value)
        {
            throw new InvalidOperationException("Assert.True: " + (message ?? "false"));
        }
    }

    public static void Matches(string pattern, string text)
    {
        if (!Regex.IsMatch(text ?? "", pattern))
        {
            throw new InvalidOperationException("Assert.Matches: „" + text + "” nie pasuje do " + pattern);
        }
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException("Assert.Equal: oczekiwano „" + expected + "”, jest „" + actual + "”");
        }
    }
}
}
