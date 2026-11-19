using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using Xunit;

namespace LokalneAI.Harness.Tests;

/// <summary>Brakujące polecenia: katalog umiejętności, szukanie, prośba o włączenie i cofnięcie odpowiedzi „nie mam narzędzia”.</summary>
public class SkillCatalogTests
{
    private static readonly SkillCatalogEntry Files = new SkillCatalogEntry("pliki", "Pliki i foldery na tym komputerze: szukanie, czytanie, kopiowanie.",
        new[] { Pair("pokaz_zawartosc", "Pokazuje foldery i pliki w folderze"), Pair("czytaj_plik", "Czyta plik tekstowy") });

    private static readonly SkillCatalogEntry Rfem = new SkillCatalogEntry("rfem", "Modele RFEM 5 z pliku .rf5: pręty, przekroje, siły, reakcje.",
        new[] { Pair("rfem_sily", "Siły w pręcie z wyników") }, "Kombinacje KO, przypadki PO");

    private static readonly SkillCatalogEntry Office = new SkillCatalogEntry("office", "Word, Excel, Outlook i PowerPoint.",
        new[] { Pair("outlook_wyslij", "Tworzy szkic wiadomości e-mail w Outlooku") });

    private static readonly SkillCatalogEntry Tables = new SkillCatalogEntry("tablice", "Tablice Żyburtowicza: wymiary i wskaźniki profili stalowych.");

    private static KeyValuePair<string, string> Pair(string name, string description) => new KeyValuePair<string, string>(name, description);

    private sealed class FakeCatalog(bool approve, params SkillCatalogEntry[] skills) : ISkillCatalog
    {
        public IReadOnlyList<SkillCatalogEntry> Skills { get; } = skills;

        public List<(IReadOnlyList<string> Skills, string Reason)> Requests { get; } = new();

        public Task<bool> RequestAsync(IReadOnlyList<string> names, string reason, CancellationToken cancellationToken)
        {
            Requests.Add((names, reason));
            return Task.FromResult(approve);
        }
    }

    [Theory]
    [InlineData("lista plików w folderze", "pliki")]
    [InlineData("siły w pręcie modelu RFEM", "rfem")]
    [InlineData("wyślij maila w Outlooku", "office")]
    [InlineData("wskaźnik Wy profilu IPE z tablic", "tablice")]
    public void Szukaj_NajlepszaUmiejetnosc(string task, string expected)
        => Assert.Equal(expected, SkillCatalog.Search(new[] { Files, Rfem, Office, Tables }, task).First().Name);

    [Fact]
    public void Szukaj_NicNiePasuje()
    {
        var hits = SkillCatalog.Search(new[] { Files, Rfem }, "przepis na pierogi");

        Assert.Empty(hits);
        Assert.Contains("Żadna niewłączona umiejętność nie pasuje", SkillCatalog.Describe(hits, new[] { Files, Rfem }));
        Assert.Contains("Są: pliki, rfem", SkillCatalog.Describe(hits, new[] { Files, Rfem }));
    }

    [Fact]
    public void Opis_ZPoleceniamiIWiedza()
    {
        var text = SkillCatalog.Describe(new[] { Files, Tables }, new[] { Files, Tables });

        Assert.Contains("- pliki — Pliki i foldery", text);
        Assert.Contains("polecenia: pokaz_zawartosc (Pokazuje foldery i pliki w folderze); czytaj_plik", text);
        Assert.Contains("(sama wiedza — bez poleceń)", text);
    }

    [Theory]
    [InlineData("Nie mam narzędzia do odczytu plików na dysku.", true)]
    [InlineData("Niestety nie potrafię tego zrobić — nie widzę Twojego komputera.", true)]
    [InlineData("Brakuje mi dostępu do Outlooka.", true)]
    [InlineData("Nie mam dostępu do systemu plików, więc nie sprawdzę folderu.", true)]
    [InlineData("Nie jestem w stanie odczytać zawartości folderu.", true)]
    [InlineData("Nie otwieram plików ani nie przeglądam lokalnych folderów na Twoim komputerze. Wklej tekst.", true)]
    [InlineData("Nie mam wglądu w Twoją pocztę.", true)]
    [InlineData("Belka waży 702 kg.", false)]
    [InlineData("Nie widzę błędu w tym zestawieniu.", false)]
    [InlineData("Nie mam pewności, czy to IPE 300 — podaj rysunek.", false)]
    [InlineData("Nie mogę znaleźć pliku hala.rf5 w folderze bieżącym.", false)]
    public void RozpoznajeOdpowiedzBezNarzedzia(string answer, bool expected) => Assert.Equal(expected, SkillCatalog.LooksLikeMissingTool(answer));

    [Fact]
    public async Task Sesja_CofaNiePotrafieSzukaIPoZgodzieKonczyTure()
    {
        var llm = new ScriptedLlm(
            """{"krok":"odpowiedz","tekst":"Nie mam narzędzia do przeglądania folderów."}""",
            """{"krok":"wywolania","wywolania":[{"narzedzie":"znajdz_umiejetnosc","argumenty":{"zadanie":"lista plików w folderze"}}]}""",
            """{"krok":"wywolania","wywolania":[{"narzedzie":"wlacz_umiejetnosci","argumenty":{"umiejetnosci":"pliki","powod":"Potrzebuję pokaz_zawartosc, żeby zajrzeć do folderu."}}]}""");
        var catalog = new FakeCatalog(true, Files, Rfem);
        using var session = new HarnessSession(llm, HarnessProfile.Load(Paths.Profile("ocr")), null, new SessionOptions { SkillCatalog = catalog });

        var turn = await session.SendAsync("co jest w folderze LokalneAI.Harness.Rfem?");

        Assert.Equal(TurnKind.Answer, turn.Kind);
        Assert.Equal("Włączam umiejętność pliki i ponawiam polecenie.", turn.Text);
        Assert.Equal(3, llm.Requests.Count); // bez czwartego wywołania modelu po zgodzie
        Assert.Contains(turn.Warnings, w => w.Contains("program znalazł umiejętność pliki"));
        Assert.Contains("pokaz_zawartosc", llm.Requests[1].Messages.Last().Content); // uwaga programu z wynikiem wyszukiwania
        var request = Assert.Single(catalog.Requests);
        Assert.Equal(new[] { "pliki" }, request.Skills);
        Assert.Contains("pokaz_zawartosc", request.Reason);
        var found = turn.Calls.First(c => c.Tool == BuiltInTools.FindSkillName);
        Assert.StartsWith("Pasujące umiejętności", found.Result);
        Assert.Contains("- pliki", found.Result);
        Assert.Equal("assistant", session.History.Last().Role); // historia gotowa do ponowienia polecenia
    }

    [Fact]
    public async Task Sesja_CofaTezPytanieZamiastNarzedzia()
    {
        var llm = new ScriptedLlm(
            """{"krok":"pytanie","tekst":"Nie otwieram plików ani nie przeglądam folderów. Wklej tekst."}""",
            """{"krok":"wywolania","wywolania":[{"narzedzie":"znajdz_umiejetnosc","argumenty":{"zadanie":"zawartość folderu"}}]}""",
            """{"krok":"wywolania","wywolania":[{"narzedzie":"wlacz_umiejetnosci","argumenty":{"umiejetnosci":"pliki","powod":"foldery"}}]}""");
        var catalog = new FakeCatalog(true, Files);
        using var session = new HarnessSession(llm, HarnessProfile.Load(Paths.Profile("tlumaczenie")), null, new SessionOptions { SkillCatalog = catalog });

        var turn = await session.SendAsync("co jest w folderze C:\\Projekty?");

        Assert.Equal(TurnKind.Answer, turn.Kind);
        Assert.Single(catalog.Requests);
    }

    [Fact]
    public async Task Sesja_DrugiRazBezNarzedzia_ProgramSamPytaOUmiejetnosc()
    {
        var llm = new ScriptedLlm(
            """{"krok":"pytanie","tekst":"Nie otwieram plików ani nie przeglądam folderów. Wklej tekst."}""",
            """{"krok":"odpowiedz","tekst":"Zrozumiałem, ale nie mam narzędzia do przeglądania folderów."}""");
        var catalog = new FakeCatalog(true, Files, Rfem);
        using var session = new HarnessSession(llm, HarnessProfile.Load(Paths.Profile("tlumaczenie")), null, new SessionOptions { SkillCatalog = catalog });

        var turn = await session.SendAsync("Co jest w folderze C:\\Projekty\\Rfem?");

        Assert.Equal(TurnKind.Answer, turn.Kind);
        Assert.Equal("Włączam umiejętność pliki i ponawiam polecenie.", turn.Text);
        Assert.Equal(new[] { "pliki" }, Assert.Single(catalog.Requests).Skills); // nie rfem — ścieżka nie liczy się do wyszukiwania
        Assert.Equal(2, llm.Requests.Count);
        Assert.Contains(turn.Calls, c => c.Tool == BuiltInTools.EnableSkillsName && c.Success);
    }

    [Fact]
    public async Task Sesja_NicNiePasuje_OdpowiedzZostaje()
    {
        var llm = new ScriptedLlm("""{"krok":"odpowiedz","tekst":"Nie potrafię zamówić pizzy."}""");
        var catalog = new FakeCatalog(true, Files, Rfem);
        using var session = new HarnessSession(llm, HarnessProfile.Load(Paths.Profile("tlumaczenie")), null, new SessionOptions { SkillCatalog = catalog });

        var turn = await session.SendAsync("zamów pizzę");

        Assert.Equal("Nie potrafię zamówić pizzy.", turn.Text);
        Assert.Empty(catalog.Requests);
        Assert.Single(llm.Requests);
    }

    [Fact]
    public void BezSciezek() => Assert.Equal("Co jest w folderze   ?", SkillCatalog.WithoutPaths("Co jest w folderze C:\\ODS\\Amadeusz\\LokalneAI.Harness.Rfem ?"));

    [Fact]
    public async Task Sesja_OdmowaWracaDoModelu()
    {
        var llm = new ScriptedLlm(
            """{"krok":"wywolania","wywolania":[{"narzedzie":"wlacz_umiejetnosci","argumenty":{"umiejetnosci":"pliki","powod":"foldery"}}]}""",
            """{"krok":"odpowiedz","tekst":"Bez umiejętności pliki nie zajrzę do folderu."}""");
        var catalog = new FakeCatalog(false, Files);
        using var session = new HarnessSession(llm, HarnessProfile.Load(Paths.Profile("ocr")), null, new SessionOptions { SkillCatalog = catalog });

        var turn = await session.SendAsync("co jest w folderze?");

        Assert.Equal("Bez umiejętności pliki nie zajrzę do folderu.", turn.Text);
        var call = Assert.Single(turn.Calls);
        Assert.False(call.Success);
        Assert.Contains("nie zgodził się", call.Result);
    }

    [Fact]
    public async Task Sesja_NieznanaNazwaBezPytania()
    {
        var llm = new ScriptedLlm(
            """{"krok":"wywolania","wywolania":[{"narzedzie":"wlacz_umiejetnosci","argumenty":{"umiejetnosci":"komputer","powod":"x"}}]}""",
            """{"krok":"odpowiedz","tekst":"Nie ma takiej umiejętności."}""");
        var catalog = new FakeCatalog(true, Files);
        using var session = new HarnessSession(llm, HarnessProfile.Load(Paths.Profile("ocr")), null, new SessionOptions { SkillCatalog = catalog });

        var turn = await session.SendAsync("pokaż procesy");

        Assert.Empty(catalog.Requests); // nazwa spoza katalogu — użytkownik nawet nie jest pytany
        Assert.Contains("Do włączenia: pliki", turn.Calls.Single().Result);
    }

    [Fact]
    public void Prompt_SekcjaTylkoZKatalogiem()
    {
        var profile = HarnessProfile.Load(Paths.Profile("ocr"));
        using var with = new HarnessSession(new ScriptedLlm(), profile, null, new SessionOptions { SkillCatalog = new FakeCatalog(true, Files, Rfem) });
        using var without = new HarnessSession(new ScriptedLlm(), profile, null);
        using var empty = new HarnessSession(new ScriptedLlm(), profile, null, new SessionOptions { SkillCatalog = new FakeCatalog(true) });

        Assert.Contains("## Brakujące polecenia", with.SystemPrompt);
        Assert.Contains("(niewłączone): pliki, rfem.", with.SystemPrompt);
        Assert.Contains("\"znajdz_umiejetnosc\"", with.StepSchemaJson);
        Assert.DoesNotContain("Brakujące polecenia", without.SystemPrompt);
        Assert.DoesNotContain("znajdz_umiejetnosc", without.StepSchemaJson);
        Assert.DoesNotContain("znajdz_umiejetnosc", empty.StepSchemaJson); // pusty katalog — nic do szukania
    }
}
