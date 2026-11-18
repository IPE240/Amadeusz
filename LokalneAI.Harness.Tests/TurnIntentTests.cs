using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>Zgłoszenie 2026-11-18: „sprawdź w internecie” kończyło się odpowiedzią z pamięci, a „popraw” — tą samą odpowiedzią.</summary>
public class TurnIntentTests
{
    [Theory]
    [InlineData("Sprawdź w internecie, ile kosztuje tona stali S355", true)]
    [InlineData("poszukaj w necie aktualnej wersji Tekli", true)]
    [InlineData("zobacz online kurs euro", true)]
    [InlineData("wygoogluj normę PN-EN 1993-1-8", true)]
    [InlineData("sprawdź w sieci", true)]
    [InlineData("Odpowiedz bez internetu", false)]
    [InlineData("nie szukaj w internecie, tylko powiedz z głowy", false)]
    [InlineData("przeczytaj, co jest na stronie 5 raportu", false)]
    [InlineData("poszukaj plików PDF w folderze", false)]
    public void ProsbaOInternet(string text, bool expected) => Assert.Equal(expected, TurnIntent.AsksForWeb(text));

    [Theory]
    [InlineData("Popraw odpowiedź", true)]
    [InlineData("jeszcze raz, ale dokładniej", true)]
    [InlineData("to nie tak, źle policzyłeś", true)]
    [InlineData("sprawdź to ponownie", true)]
    [InlineData("Ile kosztuje tona stali?", false)]
    [InlineData("Jaki błąd zgłasza Tekla przy imporcie?", false)]
    public void ProsbaOPoprawke(string text, bool expected) => Assert.Equal(expected, TurnIntent.AsksForCorrection(text));

    private static HarnessProfile Chat()
    {
        var settings = new ProfileSettings { Internet = true, MaxSteps = 6 };
        return new HarnessProfile("czat", "Odpowiadasz.", [], settings).WithInternet();
    }

    [Fact]
    public async Task ProsbaOInternet_OdpowiedzZPamieci_ProgramKazeSprawdzic()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"odpowiedz\",\"tekst\":\"S355 kosztuje 4000 zł/t.\"}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Nadal 4000 zł/t.\"}");
        using var web = new WebAccess();
        using var session = new HarnessSession(llm, Chat(), null, new SessionOptions { Web = web });

        var result = await session.SendAsync("Sprawdź w internecie cenę stali S355");

        Assert.Contains(TurnIntent.WebHint, llm.Requests[0].Messages.Last().Content);
        Assert.Equal(2, llm.Requests.Count);                                  // jedno cofnięcie, potem koniec (bez pętli)
        Assert.Contains("Nie sprawdziłeś w internecie", llm.Requests[1].Messages.Last().Content);
        Assert.Contains(result.Warnings, w => w.Contains("bez sprawdzenia w internecie"));
    }

    [Fact]
    public async Task ProsbaOInternet_BezInternetu_ModelMaToPowiedziec()
    {
        var llm = new ScriptedLlm("{\"krok\":\"odpowiedz\",\"tekst\":\"Internet jest wyłączony.\"}");
        using var session = new HarnessSession(llm, new HarnessProfile("pliki", "Pliki.", []), null);

        await session.SendAsync("sprawdź w internecie wersję programu");

        Assert.Single(llm.Requests);
        Assert.Contains(TurnIntent.NoWebHint, llm.Requests[0].Messages.Last().Content);
    }

    [Fact]
    public async Task ProsbaOPoprawke_UwagaIWyzszaTemperatura_TylkoGdyBylaOdpowiedz()
    {
        var llm = new ScriptedLlm(
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Pierwsza wersja.\"}",
            "{\"krok\":\"odpowiedz\",\"tekst\":\"Poprawiona wersja.\"}");
        var profile = new HarnessProfile("czat", "Odpowiadasz.", [], new ProfileSettings { Temperature = 0.1 });
        using var session = new HarnessSession(llm, profile, null);

        await session.SendAsync("popraw tekst maila");        // pierwsze polecenie — nie ma czego poprawiać w rozmowie
        await session.SendAsync("popraw odpowiedź");

        Assert.DoesNotContain(TurnIntent.CorrectionHint, llm.Requests[0].Messages.Last().Content);
        Assert.Equal(0.1, llm.Requests[0].Temperature);
        Assert.Contains(TurnIntent.CorrectionHint, llm.Requests[1].Messages.Last().Content);
        Assert.Equal(0.5, llm.Requests[1].Temperature);
    }
}
