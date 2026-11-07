using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Validation;

namespace LokalneAI.Harness.Tests;

/// <summary>
/// Pilnuje opisów dla AI: mapa API w docs\biblioteki-dla-agentow.md obejmuje wszystkie publiczne typy,
/// linki w dokumentacji prowadzą do istniejących plików, a szablon profilu jest poprawny.
/// </summary>
public sealed class DokumentacjaTests
{
    private static readonly Regex MarkdownLink = new Regex(@"\]\(([^)\s]+)\)", RegexOptions.Compiled);

    [Fact]
    public void MapaApi_ZawieraKazdyPublicznyTypBiblioteki()
    {
        var guide = File.ReadAllText(Path.Combine(Paths.Root, "docs", "biblioteki-dla-agentow.md"), Encoding.UTF8);
        var missing = typeof(HarnessSession).Assembly.GetExportedTypes()
            .Where(t => !t.IsNested)
            .Select(t => t.Name)
            .Where(name => guide.IndexOf("`" + name + "`", StringComparison.Ordinal) < 0)
            .OrderBy(name => name)
            .ToList();

        Assert.True(missing.Count == 0,
            "Typy publiczne bez opisu w docs\\biblioteki-dla-agentow.md (sekcja „Mapa API”): " + string.Join(", ", missing));
    }

    [Fact]
    public void LinkiWDokumentacji_ProwadzaDoIstniejacychPlikow()
    {
        var root = Paths.Root;
        var files = new[] { "AGENTS.md", "CLAUDE.md", "README.md" }
            .Select(f => Path.Combine(root, f))
            .Concat(Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(root, "szablony"), "*.md", SearchOption.AllDirectories))
            .Where(File.Exists)
            .ToList();
        var broken = new List<string>();
        foreach (var file in files)
        {
            foreach (Match match in MarkdownLink.Matches(File.ReadAllText(file, Encoding.UTF8)))
            {
                var target = match.Groups[1].Value;
                if (Regex.IsMatch(target, "^[a-z]+:", RegexOptions.IgnoreCase) || target.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;   // adres w sieci albo kotwica w tym samym pliku
                }

                var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), Uri.UnescapeDataString(target.Split('#')[0])));
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    broken.Add(file.Substring(root.Length + 1) + " → " + target);
                }
            }
        }

        Assert.True(broken.Count == 0, "Martwe linki: " + string.Join("; ", broken));
    }

    [Fact]
    public void ClaudeMd_WczytujeAgentsMd()
    {
        Assert.Equal("@AGENTS.md", File.ReadAllText(Path.Combine(Paths.Root, "CLAUDE.md")).Trim());
        Assert.True(File.Exists(Path.Combine(Paths.Root, "AGENTS.md")));
    }

    [Fact]
    public void SzablonProfilu_WczytujeSieAPrzykladyITestyPasujaDoNarzedzi()
    {
        var folder = Path.Combine(Paths.Root, "szablony", "profil");
        var profile = HarnessProfile.Load(folder);

        Assert.Equal("moj_program", profile.Name);
        Assert.Contains(profile.Tools, t => t.Name == BuiltInTools.KnowledgeName);
        Assert.NotEmpty(profile.Examples);
        Assert.Equal(0, profile.Knowledge.ChunkCount);   // sam CZYTAJ.md — pomijany

        foreach (var example in profile.Examples.Where(e => e.CallsJson != null))
        {
            foreach (var call in Paths.Json(example.CallsJson).EnumerateArray())
            {
                AssertValidCall(profile, call, "przyklady.jsonl: " + example.User);
            }
        }

        foreach (var line in File.ReadAllLines(Path.Combine(folder, "testy.jsonl"), Encoding.UTF8).Where(l => l.Trim().Length > 0))
        {
            var test = Paths.Json(line);
            foreach (var step in test.GetProperty("kroki").EnumerateArray())
            {
                foreach (var call in step.GetProperty("oczekiwane").EnumerateArray())
                {
                    AssertValidCall(profile, call, "testy.jsonl: " + test.GetProperty("id").GetString());
                }
            }
        }
    }

    [Fact]
    public void Wiedza_PomijaCzytajMd()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lokalneai_czytaj_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "zrodla"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "CZYTAJ.md"), "# Co tu wkładać\nInstrukcja dla ludzi o folderze.", Encoding.UTF8);
            File.WriteAllText(Path.Combine(folder, "zrodla", "czytaj.md"), "# Inny opis\nInstrukcja dla ludzi w podfolderze.", Encoding.UTF8);
            File.WriteAllText(Path.Combine(folder, "kotwy.md"), "# Kotwy\nKotwy M24 mają długość 400 mm.", Encoding.UTF8);

            var index = KnowledgeIndex.FromFolder(folder);

            Assert.Equal(1, index.ChunkCount);
            Assert.Empty(index.Search("instrukcja ludzi"));
            Assert.Contains("400 mm", index.Search("kotwy")[0]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void BibliotekiMajaPlikDokumentacjiXml()
    {
        // Komentarze /// trafiają do LokalneAI.Harness.xml obok dll — agent w innym repozytorium widzi je w podpowiedziach.
        var xml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LokalneAI.Harness.xml");
        Assert.True(File.Exists(xml), "Brak " + xml + " (GenerateDocumentationFile w LokalneAI.Harness.csproj).");
        Assert.Contains("M:LokalneAI.Harness.Tools.ToolResult.Ok(System.String)", File.ReadAllText(xml));
    }

    private static void AssertValidCall(HarnessProfile profile, JsonElement call, string where)
    {
        var name = call.GetProperty("narzedzie").GetString();
        var tool = profile.FindTool(name);
        Assert.True(tool != null, where + ": nie ma narzędzia " + name);
        var errors = SchemaValidator.Validate(call.GetProperty("argumenty"), tool.Parameters);
        Assert.True(errors.Count == 0, where + ": " + string.Join("; ", errors));
    }
}
