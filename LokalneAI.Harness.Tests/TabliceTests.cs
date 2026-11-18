using System.Text.Json;
using System.Text.RegularExpressions;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>Umiejętność „tablice”: zestawienia rodzin przekrojów dają porównanie całej rodziny w jednym wywołaniu szukaj_wiedzy.</summary>
public class TabliceTests
{
    [Fact]
    public void ZestawienieRodziny_JednoWywolanieDajeKilkanascieProfili()
    {
        var profile = HarnessProfile.Load(Paths.Profile("tablice"));
        var result = BuiltInTools.TryExecute(BuiltInTools.KnowledgeName, JsonDocument.Parse("""{"fraza": "IPE zestawienie rodziny masa", "ile": 4}""").RootElement, profile.Knowledge);

        Assert.True(result.Success, result.Text);
        var fragments = result.Text.Split(new[] { "\n\n" }, StringSplitOptions.None);
        Assert.Equal(4, fragments.Length);
        Assert.Contains(fragments, f => f.Contains("zestawienie rodziny IPE") && f.Contains("IPE 80:") && f.Contains("IPE 300:"));

        // Każda sekcja zestawienia mieści się w fragmencie (900 znaków) — inaczej koniec rodziny ginie.
        var text = File.ReadAllText(Path.Combine(Paths.Profile("tablice"), "wiedza", "zyburtowicz_zestawienia.md"), System.Text.Encoding.UTF8);
        var sections = Regex.Split(text, "(?m)^# ").Skip(1).ToList();
        Assert.True(sections.Count > 200);
        var tooLong = sections.Where(s => s.Substring(s.IndexOf('\n') + 1).Trim().Length > 900).Select(s => s.Split('\n')[0]).ToList();
        Assert.True(tooLong.Count == 0, "Za długie sekcje: " + string.Join("; ", tooLong));
    }

    [Fact]
    public void Grubosci_NieMajaZgubionychPrzecinkow()
    {
        // OCR gubił przecinek (tw = 39 zamiast 3,9); kontrola: tw ≤ 0,5·b, tf ≤ 0,25·h, t ≤ 0,35·a.
        var problems = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Paths.Profile("tablice"), "wiedza", "dane"), "zyburtowicz_t*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var row in document.RootElement.GetProperty("wiersze").EnumerateArray())
            {
                double? Get(string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                var h = Get("h");
                var b = Get("b");
                var a = Get("a");
                if (Get("tw") is double tw && b.HasValue && tw > 0.5 * b.Value) problems.Add(row.GetProperty("nazwa").GetString() + " tw");
                if (Get("tf") is double tf && h.HasValue && tf > 0.25 * h.Value) problems.Add(row.GetProperty("nazwa").GetString() + " tf");
                if (Get("t") is double t && (a ?? b).HasValue && t > 0.35 * (a ?? b).Value) problems.Add(row.GetProperty("nazwa").GetString() + " t");
            }
        }

        Assert.True(problems.Count == 0, string.Join(", ", problems));
    }
}
