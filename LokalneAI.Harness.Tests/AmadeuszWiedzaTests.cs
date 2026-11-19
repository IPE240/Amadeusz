using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Tests;

/// <summary>
/// Dokumentacja sterowania (docs\sterowanie.md, docs\porty.md) i wiedza umiejętności „amadeusz” muszą opisywać to, co jest
/// w kodzie: każdy przełącznik LokalneAI.exe, polecenie kontroli (#…), trasę bramy i port. Nowy przełącznik albo trasa
/// bez opisu = czerwony test.
/// </summary>
public class AmadeuszWiedzaTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Paths.Root }.Concat(parts).ToArray()), Encoding.UTF8);

    private static string Knowledge()
        => string.Join("\n", Directory.GetFiles(Path.Combine(Paths.Profile("amadeusz"), "wiedza"), "*.md")
            .Where(f => !Path.GetFileName(f).Equals("CZYTAJ.md", StringComparison.OrdinalIgnoreCase))
            .Select(f => File.ReadAllText(f, Encoding.UTF8)));

    [Fact]
    public void PrzelacznikiIPoleceniaKontroli_SaOpisane()
    {
        var switches = Regex.Matches(Read("LokalneAI.App", "StartupOptions.cs"), "case \"(--[a-z]+)\":").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        var commands = Regex.Matches(Read("LokalneAI.App", "AutoTest.cs"), "\"(#[a-z]+:?)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(switches.Count >= 15, "Nie znalazłem przełączników w StartupOptions.cs");
        Assert.Contains("#tokeny", commands);

        var docs = Read("docs", "sterowanie.md");
        var knowledge = Knowledge();
        var missing = switches.Concat(commands)
            .SelectMany(s => new[] { docs.Contains(s) ? null : "docs\\sterowanie.md: " + s, knowledge.Contains(s) ? null : "profile\\amadeusz\\wiedza: " + s })
            .Where(s => s != null).ToList();
        Assert.True(missing.Count == 0, "Bez opisu: " + string.Join(", ", missing));
    }

    [Fact]
    public void TrasyBramy_SaOpisane()
    {
        var routes = Regex.Matches(Read("LokalneAI.Gateway", "GatewayServer.cs"), "\\[\"(?:GET|POST) (/[a-z0-9/_]+)\"\\]").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        Assert.True(routes.Count >= 10, "Nie znalazłem tras w GatewayServer.Routes");

        var docs = Read("docs", "sterowanie.md");
        var knowledge = Knowledge();
        var missing = routes.Append("/lokalneai/info")
            .SelectMany(r => new[] { docs.Contains(r) ? null : "docs\\sterowanie.md: " + r, knowledge.Contains(r) ? null : "profile\\amadeusz\\wiedza: " + r })
            .Where(s => s != null).ToList();
        Assert.True(missing.Count == 0, "Bez opisu: " + string.Join(", ", missing));
    }

    [Fact]
    public void Porty_SaOpisane()
    {
        var options = Read("LokalneAI.Gateway", "GatewayOptions.cs");
        var ports = Regex.Matches(options, "const int Default\\w*Port = (\\d+);").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(2, ports.Count);
        ports.Add(Regex.Match(Read("LokalneAI.Harness", "Llm", "LocalGateway.cs"), "OllamaUrl = \"http://127\\.0\\.0\\.1:(\\d+)\"").Groups[1].Value);

        foreach (var port in ports)
        {
            Assert.Contains(port, Read("docs", "porty.md"));
            Assert.Contains(port, Read("profile", "amadeusz", "wiedza", "porty.md"));
            Assert.Contains(port, Read("profile", "amadeusz", "karta.md"));
        }
    }

    [Fact]
    public void SekcjeWiedzy_MieszczaSieWeFragmencie()
    {
        // szukaj_wiedzy oddaje modelowi fragment ucięty do 900 znaków — dłuższa sekcja traci koniec.
        foreach (var file in Directory.GetFiles(Path.Combine(Paths.Profile("amadeusz"), "wiedza"), "*.md").Where(f => !f.EndsWith("CZYTAJ.md", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var section in Regex.Split(File.ReadAllText(file, Encoding.UTF8), "(?m)^(?=#{1,3} )").Where(s => s.Trim().Length > 0))
            {
                Assert.True(section.Length <= 900, Path.GetFileName(file) + ": sekcja „" + section.Split('\n')[0] + "” ma " + section.Length + " znaków");
            }
        }
    }
}
