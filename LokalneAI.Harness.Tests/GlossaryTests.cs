using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

public class GlossaryTests
{
    private const string Sample = """
        duct – kanał
        flue gas duct – kanał spalin
        expansion joint – kompensator
        tee – trójnik
        damper – klapa
        hanger rod – cięgno (niepotwierdzone, alternatywa: tie rod)
        turnbuckle – śruba rzymska, nakrętka rzymska

        Dokumentacja i organizacja

        installation sequence – sekwencja montażu
        """;

    private static Glossary Make()
    {
        var glossary = new Glossary();
        glossary.Add(Sample, "glosariusz.txt");
        return glossary;
    }

    [Fact]
    public void Wczytuje_PaySeparatorem_PomijaNaglowkiGrup()
    {
        var glossary = Make();
        Assert.Equal(8, glossary.Count);
        Assert.Contains(glossary.Entries, e => e.Left == "expansion joint" && e.Right == "kompensator");
        Assert.DoesNotContain(glossary.Entries, e => e.Left.StartsWith("Dokumentacja", StringComparison.Ordinal));
    }

    [Fact]
    public void RozpoznajeGlosariusz_AleNieNotatkeMarkdown()
    {
        Assert.True(Glossary.LooksLikeGlossary(Sample));
        Assert.True(Glossary.LooksLikeGlossary("a\tb\nc\td\ne\tf"));
        Assert.False(Glossary.LooksLikeGlossary("# Słownik\n\nKompensator to element, który przejmuje wydłużenia.\nOpis — dłuższy akapit.\n\nInny akapit."));
        Assert.False(Glossary.LooksLikeGlossary("| PL | EN |\n|---|---|\n| kompensator | expansion joint |\n| klapa | damper |\n| kanał | duct |"));
    }

    [Fact]
    public void Znajduje_TerminyOdmienione_PoObuStronach()
    {
        var glossary = Make();
        var found = glossary.Find("Kompensatora nie montujemy na kanale spalin; klapy zostają.").Select(e => e.Left).ToList();
        Assert.Contains("expansion joint", found);
        Assert.Contains("flue gas duct", found);
        Assert.Contains("duct", found);
        Assert.Contains("damper", found);
        Assert.Equal("flue gas duct", found[0]); // dłuższe terminy pierwsze

        var english = glossary.Find("Check the expansion joints and the hanger rods.").Select(e => e.Left).ToList();
        Assert.Contains("expansion joint", english);
        Assert.Contains("hanger rod", english);
    }

    [Fact]
    public void KrotkieSlowa_TylkoDokladnie_IWszystkieSlowaTerminu()
    {
        var glossary = Make();
        Assert.Empty(glossary.Find("The teeth of the gear."));               // „tee” nie pasuje do „teeth”
        Assert.DoesNotContain(glossary.Find("Kanał wentylacyjny."), e => e.Left == "flue gas duct"); // brak „spalin”
        Assert.Contains(glossary.Find("Dokręcić śrubę rzymską."), e => e.Left == "turnbuckle");
    }

    [Fact]
    public void Podpowiedz_PustaGdyNicNiePasuje_ZWpisamiGdyPasuje()
    {
        var glossary = Make();
        Assert.Equal("", glossary.BuildHint("Dzień dobry, proszę o uwagi do piątku."));
        var hint = glossary.BuildHint("Wymienić cięgno.");
        Assert.Contains("Glosariusz biura", hint);
        Assert.Contains("- hanger rod – cięgno (niepotwierdzone, alternatywa: tie rod)", hint);
    }

    [Fact]
    public async Task Sesja_DoklejaTerminyDoPolecenia()
    {
        var profile = new HarnessProfile("tlumaczenie", "Tłumacz.", [], glossary: Make());
        var llm = new ScriptedLlm("{\"krok\":\"odpowiedz\",\"tekst\":\"The expansion joint shall be replaced.\"}");
        using var session = new HarnessSession(llm, profile, null);

        await session.SendAsync("Kompensator należy wymienić.");

        var sent = llm.Requests[0].Messages.Last().Content;
        Assert.StartsWith("Kompensator należy wymienić.", sent);
        Assert.Contains("expansion joint – kompensator", sent);
    }

    [Fact]
    public void Profil_WczytujeGlosariuszZWiedzy_IZPlikuZDysku_ACombineLaczy()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lokalneai_glosariusz_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "a", "wiedza"));
            Directory.CreateDirectory(Path.Combine(folder, "b", "wiedza"));
            foreach (var name in new[] { "a", "b" })
            {
                File.WriteAllText(Path.Combine(folder, name, "system.md"), "Rola " + name + ".");
                File.WriteAllText(Path.Combine(folder, name, "narzedzia.json"), "{\"narzedzia\":[]}");
            }

            File.WriteAllText(Path.Combine(folder, "a", "wiedza", "glosariusz.txt"), Sample);
            File.WriteAllText(Path.Combine(folder, "a", "wiedza", "notatka.md"), "# Notatka\n\nZwykły tekst, nie glosariusz.");
            var shared = Path.Combine(folder, "wspolny.txt");
            File.WriteAllText(shared, "bracket – konsola\nbeam – belka\ncolumn – słup");

            var a = HarnessProfile.Load(Path.Combine(folder, "a"));
            var b = HarnessProfile.Load(Path.Combine(folder, "b"));
            Assert.Equal(8, a.Glossary.Count);
            Assert.Equal(0, b.Glossary.Count);
            Assert.True(b.AddKnowledgeFile(shared, "dysk/wspolny.txt"));
            Assert.NotEmpty(b.Knowledge.Search("konsola"));

            var both = HarnessProfile.Combine([a, b]);
            Assert.Equal(11, both.Glossary.Count);
            Assert.Equal(11, both.WithInternet().Glossary.Count);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
