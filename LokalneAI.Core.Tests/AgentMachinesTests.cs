using System.Text.Json;
using LokalneAI.Core.Ollama;
using LokalneAI.Core.Storage;

namespace LokalneAI.Core.Tests;

/// <summary>Komputery dla podagentów: ustawienia, dobór modelu do karty (ModelChooser), zapis przebiegu podagenta w rozmowie.</summary>
public sealed class AgentMachinesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LokalneAI_podagenci_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static LocalModel Model(string name, double gib, params string[] capabilities)
        => new() { Name = name, Size = (long)(gib * 1024 * 1024 * 1024), Capabilities = capabilities.Length == 0 ? null : capabilities.ToList() };

    private static readonly IReadOnlyList<LocalModel> Models =
    [
        Model("gemma4:12b", 7.5),
        Model("qwen3.5:9b-q4_K_M", 6.1),
        Model("SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M", 6.3),
        Model("qwen3.5:4b-q4_K_M", 3.1),
        Model("nomic-embed-text:latest", 0.3, "embedding"),
        Model("deepseek-v3.1:671b-cloud", 0.0),
    ];

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 (8 GB)", 8.0)]
    [InlineData("NVIDIA GeForce RTX 4090, 24 GB", 24.0)]
    [InlineData("RTX 3090 (23,9 GB)", 23.9)]
    [InlineData("karta nieznana", null)]
    [InlineData(null, null)]
    public void ParseGpuGb_ZOpisuKarty(string? text, double? expected)
        => Assert.Equal(expected, ModelChooser.ParseGpuGb(text));

    [Fact]
    public void IsChatModel_PomijaEmbeddingiIChmureOllamy()
    {
        Assert.True(ModelChooser.IsChatModel(Model("gemma4:12b", 7.5)));
        Assert.True(ModelChooser.IsChatModel(Model("gemma4:12b", 7.5, "completion", "tools")));
        Assert.False(ModelChooser.IsChatModel(Model("nomic-embed-text", 0.3)));
        Assert.False(ModelChooser.IsChatModel(Model("x:latest", 1, "embedding")));
        Assert.False(ModelChooser.IsChatModel(Model("deepseek-v3.1:671b-cloud", 0)));
    }

    [Fact]
    public void EstimateGb_PlikZZapasemPlusKontekst()
    {
        var gb = ModelChooser.EstimateGb(Model("x", 6.0), 16384);
        Assert.InRange(gb, 8.2, 8.4);   // 6,6 + 1,28 + 0,4
    }

    [Fact]
    public void Choose_NajpierwPreferowany_GdyMiesciSieZZapasem()
    {
        // 8 GB: gemma4:12b potrzebuje ok. 9,9 GB — mieści się w 140% (11,2 GB), więc wybrany przez użytkownika model wygrywa.
        var chosen = ModelChooser.Choose(Models, [], 8.0, 16384, ["gemma4:12b"]);
        Assert.Equal("gemma4:12b", chosen!.Name);
        // Bielik Q8 (11 GB → ok. 13,9) nie zmieści się nawet z zapasem — bierze następny z listy preferowanych.
        var big = Models.Concat([Model("SpeakLeash/bielik-11b-v3.0-instruct:Q8_0", 11.1)]).ToList();
        Assert.Equal("qwen3.5:9b-q4_K_M", ModelChooser.Choose(big, [], 8.0, 16384, ["SpeakLeash/bielik-11b-v3.0-instruct:Q8_0", "qwen3.5:9b-q4_K_M"])!.Name);
        // Preferowany, którego na tym komputerze nie ma — pomijany.
        Assert.Equal("qwen3.5:4b-q4_K_M", ModelChooser.Choose(Models, [], 8.0, 16384, ["llama9:70b"])!.Name);
    }

    [Fact]
    public void Choose_BezPreferowanych_ZaladowanyPotemNajwiekszyMieszczacySie()
    {
        // 8 GB, kontekst 16k: w całości mieści się tylko qwen 4b (ok. 5,1 GB); qwen 9b (8,4) i bielik 11B Q4 (8,6) — nie.
        Assert.Equal("qwen3.5:4b-q4_K_M", ModelChooser.Choose(Models, [], 8.0, 16384, null)!.Name);
        // Już w pamięci karty — wygrywa z zapasem 10% (8,4 ≤ 8,8).
        var running = new[] { new RunningModel { Name = "qwen3.5:9b-q4_K_M", Size = 1, SizeVram = 1 } };
        Assert.Equal("qwen3.5:9b-q4_K_M", ModelChooser.Choose(Models, running, 8.0, 16384, null)!.Name);
        // Większa karta: największy, który się mieści (gemma4:12b ok. 9,9 GB).
        Assert.Equal("gemma4:12b", ModelChooser.Choose(Models, [], 12.0, 16384, null)!.Name);
        // Karta nieznana: wszystko się „mieści” — największy.
        Assert.Equal("gemma4:12b", ModelChooser.Choose(Models, [], null, 16384, null)!.Name);
        // Nic się nie mieści: najmniejszy model do rozmowy.
        Assert.Equal("qwen3.5:4b-q4_K_M", ModelChooser.Choose(Models, [], 2.0, 16384, null)!.Name);
        // Same embeddingi: null.
        Assert.Null(ModelChooser.Choose([Model("nomic-embed-text", 0.3)], [], 8.0, 16384, null));
    }

    [Theory]
    [InlineData("gemma4:12b", "gemma4:12b", true)]
    [InlineData("gemma4:12b", "GEMMA4:12B", true)]
    [InlineData("bielik:latest", "bielik", true)]
    [InlineData("gemma4:12b", "gemma4", true)]
    [InlineData("gemma4:12b", "gemma4:26b", false)]
    [InlineData("gemma4:12b", "anthropic|claude-sonnet-5-5", false)]
    [InlineData("", "x", false)]
    public void SameModel_NazwaIKlucz(string name, string key, bool expected)
        => Assert.Equal(expected, ModelChooser.SameModel(name, key));

    [Fact]
    public void Ustawienia_KomputeryPodagentow_NormalizacjaIDomyslne()
    {
        var settings = new AppSettings
        {
            AgentMachines =
            [
                new AgentMachineSetting { Address = " PC-ODS12 ", Name = "PC-ODS12" },
                new AgentMachineSetting { Address = "pc-ods12" },
                new AgentMachineSetting { Address = "  " },
                new AgentMachineSetting { Address = "10.0.0.50:12000", Name = " " },
            ],
        }.Normalize();

        Assert.Equal(["PC-ODS12", "10.0.0.50:12000"], settings.AgentMachines.Select(m => m.Address));
        Assert.Equal("PC-ODS12", settings.AgentMachines[0].Name);
        Assert.Null(settings.AgentMachines[1].Name);
        Assert.True(new AppSettings().AgentsUseThisComputer);
        Assert.False(new AppSettings().ShowSteps);
        Assert.Empty(new AppSettings().Normalize().AgentMachines);
    }

    [Fact]
    public void Rozmowa_PrzebiegPodagentaJakoWpisyPodrzedne_ZapisIOdczyt()
    {
        var group = Path.Combine(_root, "Projekt");
        Directory.CreateDirectory(group);
        var conversation = new CodeConversation
        {
            Title = "Podagenci",
            Created = DateTime.Now,
            Updated = DateTime.Now,
            Entries =
            [
                new CodeConversationEntry { Kind = "User", Text = "policz pliki" },
                new CodeConversationEntry { Kind = "Step", Text = "Krok 1: wywołania → zlec_podagentowi", Detail = "{\"krok\":\"wywolania\"}" },
                new CodeConversationEntry
                {
                    Kind = "Agent",
                    Text = "Podagent 1: policz pliki w A",
                    Detail = "ten komputer · gemma4:12b · pliki · 12 s\n3 pliki",
                    Success = true,
                    Children =
                    [
                        new CodeConversationEntry { Kind = "Call", Text = "→ policz_pliki", Success = true },
                        new CodeConversationEntry { Kind = "Answer", Text = "3 pliki" },
                    ],
                },
            ],
        };

        var path = CodeConversationStore.Save(group, conversation);
        var json = File.ReadAllText(path);
        var loaded = CodeConversationStore.Load(path)!;

        Assert.Equal(3, loaded.Entries.Count);
        Assert.Null(loaded.Entries[0].Children);
        Assert.Equal(2, loaded.Entries[2].Children!.Count);
        Assert.Equal("→ policz_pliki", loaded.Entries[2].Children![0].Text);
        // Zwykły wpis nie dostaje pustego pola „children”.
        using var document = JsonDocument.Parse(json);
        var first = document.RootElement.GetProperty("entries")[0];
        Assert.False(first.TryGetProperty("children", out _));
    }
}
