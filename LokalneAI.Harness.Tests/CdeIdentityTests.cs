using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>
/// Gdy model sam wpisze login zamiast '{{ja}}', reguła profilu cde odrzuca zapytanie, zanim pójdzie do bazy (także z „formatowaniem”).
/// (Zgłoszenie 2026-11-18 z błędem „invalid input syntax for type uuid” to był login wpisany jako person_id w poświadczeniu —
/// okno ostrzega o tym przy tworzeniu sesji.)
/// </summary>
public class CdeIdentityTests
{
    private const string LoginInsteadOfJa = """
        SELECT COALESCE(SUM(h.hours_counted),0) AS godziny_razem,
               COALESCE(SUM(h.hours_pending),0) AS godziny_czekajace
        FROM timesheet.v_my_hours h
        WHERE h.person_id = 'jan.kowalski'
        """;

    private static async Task<(int Executed, TurnResult Result)> Run(HarnessProfile profile, string sql)
    {
        var executed = 0;
        var executor = new DelegateExecutor((c, ct) => { executed++; return Task.FromResult(ToolResult.Ok("godziny_razem\n1")); });
        var step = "{\"krok\":\"wywolania\",\"wywolania\":[{\"narzedzie\":\"zapytanie_sql\",\"argumenty\":{\"sql\":"
            + System.Text.Json.JsonSerializer.Serialize(sql) + ",\"cel\":\"podsumowanie\"}}]}";
        using var session = new HarnessSession(new ScriptedLlm(step, "{\"krok\":\"odpowiedz\",\"tekst\":\"ok\"}"), profile, executor,
            new SessionOptions { Variables = new Dictionary<string, string> { ["ja"] = "11111111-2222-3333-4444-555555555555" } });
        var result = await session.SendAsync("Podsumuj moje godziny");
        return (executed, result);
    }

    [Fact]
    public async Task SamaUmiejetnoscCde_LoginZamiastJa_Odrzucone()
    {
        var (executed, result) = await Run(HarnessProfile.Load(Paths.Profile("cde")), LoginInsteadOfJa);
        Assert.Equal(0, executed);
        Assert.Contains(result.Calls, c => !c.Success && c.Result.Contains("{{ja}}"));
    }

    [Fact]
    public async Task CdeZFormatowaniem_LoginZamiastJa_Odrzucone()
    {
        var profile = HarnessProfile.Combine([HarnessProfile.Load(Paths.Profile("cde")), HarnessProfile.Load(Paths.Profile("formatowanie"))]);
        var (executed, result) = await Run(profile, LoginInsteadOfJa);
        Assert.Equal(0, executed);
        Assert.Contains(result.Calls, c => !c.Success && c.Result.Contains("{{ja}}"));
    }
}
