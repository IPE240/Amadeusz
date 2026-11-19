using System.Text.Json;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Whiteboard;
using Xunit;

namespace LokalneAI.Harness.Tests;

/// <summary>Tablica (zakładka Whiteboard): kształty od modelu, opis tablicy, kolory, umiejętność whiteboard i obrazy w zapytaniach.</summary>
public class WhiteboardTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

    private sealed class FakeSurface : IWhiteboardSurface
    {
        public double Width { get; set; } = 1000;

        public double Height { get; set; } = 600;

        public List<BoardItem> Drawn { get; } = new List<BoardItem>();

        public int Clears { get; private set; }

        public Task DrawAsync(IReadOnlyList<BoardItem> items, CancellationToken cancellationToken)
        {
            Drawn.AddRange(items);
            return Task.CompletedTask;
        }

        public Task ClearModelLayerAsync(CancellationToken cancellationToken)
        {
            Clears++;
            Drawn.Clear();
            return Task.CompletedTask;
        }
    }

    private static HarnessProfile Profile() => HarnessProfile.Load(Paths.Profile("whiteboard"));

    private static ToolCall Call(string tool, string json)
        => new ToolCall(Profile().Tools.First(t => t.Name == tool), Paths.Json(json), 1, 1);

    [Fact]
    public void Parse_CzytaKsztaltyKoloryIPunkty()
    {
        var problems = new List<string>();
        var items = WhiteboardExecutor.Parse(Paths.Json("""
            {"elementy":[
              {"typ":"strzalka","x":400,"y":300,"x2":400,"y2":205,"kolor":"niebieski"},
              {"typ":"tekst","x":410,"y":305,"tekst":"środek"},
              {"typ":"lamana","punkty":[[200,300],[180,335],[220,335]]},
              {"typ":"prostokąt","x":"50","y":50,"x2":150,"y2":120,"kolor":"#00FF00"}
            ]}
            """), 1000, 600, problems);

        Assert.Empty(problems);
        Assert.Equal(4, items.Count);
        Assert.Equal(BoardItemKind.Arrow, items[0].Kind);
        Assert.Equal("#2563EB", items[0].Color);
        Assert.Equal(BoardColors.Default, items[1].Color); // bez koloru — fioletowy Amadeusza
        Assert.Equal("środek", items[1].Text);
        Assert.Equal(new double[] { 200, 300, 180, 335, 220, 335 }, items[2].Points);
        Assert.Equal(BoardItemKind.Rectangle, items[3].Kind); // „prostokąt” z ogonkiem i liczba w cudzysłowie też
        Assert.Equal(50, items[3].X);
        Assert.Equal("#00FF00", items[3].Color);
    }

    [Fact]
    public void Parse_ZleElementyIPoza_Tablica()
    {
        var problems = new List<string>();
        var notes = new List<string>();
        var items = WhiteboardExecutor.Parse(Paths.Json("""
            {"elementy":[
              {"typ":"strzalka","x":370,"y":260,"x2":370,"y2":260},
              {"typ":"gwiazdka","x":1,"y":1},
              {"typ":"linia","x":10,"y":10},
              {"typ":"prostokat","x":10,"y":10,"x2":200,"y2":10},
              {"typ":"linia","x":-50,"y":10,"x2":1500,"y2":10}
            ]}
            """), 1000, 600, problems, notes);

        Assert.Single(items);
        Assert.Equal(0, items[0].X);
        Assert.Equal(1000, items[0].X2);
        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.Contains("strzałka idzie z wolnego miejsca"));
        Assert.Contains(problems, p => p.Contains("nieznany typ „gwiazdka”"));
        Assert.Contains(problems, p => p.Contains("brak „x2”"));
        Assert.Contains(problems, p => p.Contains("szerokość i wysokość"));
        Assert.Contains(notes, n => n.Contains("przycięte"));
    }

    [Fact]
    public async Task Wykonawca_RysujeAlboNicPrzyBledzie()
    {
        var surface = new FakeSurface();
        var executor = new WhiteboardExecutor(surface);

        var ok = await executor.ExecuteAsync(Call("rysuj", """{"elementy":[{"typ":"linia","x":10,"y":10,"x2":300,"y2":10},{"typ":"tekst","x":20,"y":20,"tekst":"belka"}]}"""), CancellationToken.None);
        var bad = await executor.ExecuteAsync(Call("rysuj", """{"elementy":[{"typ":"linia","x":10,"y":10,"x2":300,"y2":10},{"typ":"strzalka","x":5,"y":5,"x2":5,"y2":5}]}"""), CancellationToken.None);

        Assert.True(ok.Success, ok.Text);
        Assert.Contains("linia, tekst", ok.Text);
        Assert.False(bad.Success);
        Assert.Contains("Nic nie narysowano", bad.Text);
        Assert.Equal(2, surface.Drawn.Count); // błędne wywołanie nie dołożyło połowy rysunku

        var cleared = await executor.ExecuteAsync(Call("wyczysc_rysunki", "{}"), CancellationToken.None);
        Assert.True(cleared.Success);
        Assert.Equal(1, surface.Clears);
        Assert.Empty(surface.Drawn);
    }

    [Fact]
    public void Opis_TekstyDoslownieRysunkiObrazyIOdreczne()
    {
        var board = new BoardSnapshot { Width = 1000.4, Height = 600, ImageAttached = true, HandwritingText = "q=5 kN/m", HandwritingConfidence = 0.62 };
        board.UserItems.Add(new BoardItem { Kind = BoardItemKind.Text, X = 120, Y = 80, Text = "belka HEB300\nL = 6 m", Color = "#DC2626" });
        board.UserItems.Add(new BoardItem { Kind = BoardItemKind.Pen, Points = new double[] { 400, 300, 450, 320 }, Color = "#111827" });
        board.UserItems.Add(new BoardItem { Kind = BoardItemKind.Pen, Points = new double[] { 500, 310, 600, 420 }, Color = "#111827" });
        board.UserItems.Add(new BoardItem { Kind = BoardItemKind.Arrow, X = 300, Y = 100, X2 = 300, Y2 = 190, Color = "#DC2626" });
        board.UserItems.Add(new BoardItem { Kind = BoardItemKind.Image, X = 600, Y = 50, X2 = 1000, Y2 = 350, Text = "zrzut 10:15", OcrText = "IPE 300", OcrConfidence = 0.91 });
        board.ModelItems.Add(new BoardItem { Kind = BoardItemKind.Text, X = 10, Y = 10, Text = "tu", Color = BoardColors.Default });

        var text = board.Describe();

        Assert.StartsWith("[Tablica 1000×600 px", text);
        Assert.Contains("Obraz całej tablicy jest dołączony.", text);
        Assert.Contains("- T1 w (120, 80), czerwony: „belka HEB300\nL = 6 m”", text);
        Assert.Contains("pióro (odręcznie): 2 pociągnięcia w obszarze (400, 300)–(600, 420), czarny", text);
        Assert.Contains("strzałka (300, 100) → (300, 190), czerwony", text);
        Assert.Contains("Odręczne napisy (OCR pociągnięć pióra, 62% pewności", text);
        Assert.Contains("- O1 w (600, 50), 400×300 px (zrzut 10:15) — OCR (91%): „IPE 300”", text);
        Assert.Contains("Twoje wcześniejsze rysunki (warstwa Amadeusza):\n- tekst w (10, 10): „tu”, fioletowy", text);
    }

    [Fact]
    public void Opis_PustaTablicaIBezObrazu()
    {
        var text = new BoardSnapshot { Width = 800, Height = 500 }.Describe();

        Assert.Contains("Obrazu tablicy nie widzisz", text);
        Assert.EndsWith("Tablica jest pusta.", text);
    }

    [Theory]
    [InlineData("pomarańczowy", "#EA580C")]
    [InlineData("Zolty", "#FACC15")]
    [InlineData("#aabbcc", "#AABBCC")]
    [InlineData("różowy", null)]
    public void Kolory_Nazwy(string name, string expected) => Assert.Equal(expected, BoardColors.Parse(name));

    [Fact]
    public void Kolory_NajblizszaNazwa()
    {
        Assert.Equal("czerwony", BoardColors.NameOf("#E02020"));
        Assert.Equal("zolty", BoardColors.NameOf("#80FACC15")); // z kanałem alfa
        Assert.Equal("czarny", BoardColors.NameOf("zły"));
    }

    [Fact]
    public void Umiejetnosc_WhiteboardMaNarzedziaISchemat()
    {
        var profile = Profile();

        Assert.Equal(new[] { "rysuj", "wyczysc_rysunki" }, profile.Tools.Where(t => !t.IsBuiltIn).Select(t => t.Name).ToArray());
        Assert.False(profile.Tools.First(t => t.Name == "rysuj").Mutates); // rysunek na własnej warstwie nie pyta o zgodę
        var schema = StepSchema.Build(profile.Tools, profile.Settings.MaxCallsPerStep, false);
        Assert.Contains("\"strzalka\"", schema);
        using (JsonDocument.Parse(schema))
        {
        }
    }

    [Fact]
    public async Task Sesja_ObrazyIdaZPoleceniemIModelRysuje()
    {
        var llm = new ScriptedLlm(
            """{"krok":"wywolania","wywolania":[{"narzedzie":"rysuj","argumenty":{"elementy":[{"typ":"strzalka","x":400,"y":350,"x2":400,"y2":265},{"typ":"tekst","x":410,"y":355,"tekst":"Mmax"}]}}]}""",
            """{"krok":"odpowiedz","tekst":"Zaznaczyłem środek belki."}""");
        var surface = new FakeSurface();
        using var session = new HarnessSession(llm, Profile(), new WhiteboardExecutor(surface));

        var turn = await session.SendAsync("[Tablica 1000×600 px]\n\nzaznacz środek", new[] { Png });

        Assert.Equal(TurnKind.Answer, turn.Kind);
        Assert.Equal(2, surface.Drawn.Count);
        var user = llm.Requests[1].Messages.First(m => m.Role == "user");
        Assert.Same(Png, Assert.Single(user.Images));
        Assert.All(llm.Requests[1].Messages.Where(m => m != user), m => Assert.Null(m.Images));
        Assert.Same(Png, session.History.First().Images.Single()); // obraz zostaje przy poleceniu w historii
    }

    [Fact]
    public void Ollama_ObrazyWBase64()
    {
        using var client = new OllamaLlmClient(new OllamaOptions { Model = "gemma4:12b" });

        var body = Paths.Json(client.BuildBody(new LlmRequest
        {
            Messages = new[] { new LlmMessage("system", "s"), new LlmMessage("user", "co to?", new[] { Png }) },
        }));

        var messages = body.GetProperty("messages");
        Assert.False(messages[0].TryGetProperty("images", out _));
        Assert.Equal(Convert.ToBase64String(Png), messages[1].GetProperty("images")[0].GetString());
    }

    [Fact]
    public void OpenAi_ObrazJakoImageUrl()
    {
        using var client = new OpenAiCompatibleLlmClient(new OpenAiCompatibleOptions { Url = "http://serwer:8080/v1", Model = "gpt" });
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

        var body = Paths.Json(client.BuildBody(new LlmRequest { Messages = new[] { new LlmMessage("user", "co to?", new[] { jpeg }) } }));

        var content = body.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("co to?", content[0].GetProperty("text").GetString());
        Assert.Equal("data:image/jpeg;base64," + Convert.ToBase64String(jpeg), content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public void Anthropic_ObrazPrzedTekstemTury()
    {
        using var client = new AnthropicLlmClient(new AnthropicOptions { ApiKey = "klucz", Model = "claude-test" });

        var body = Paths.Json(client.BuildBody(new LlmRequest
        {
            Messages = new[]
            {
                new LlmMessage("system", "s"),
                new LlmMessage("user", "pierwsze", new[] { Png }),
                new LlmMessage("assistant", "ok"),
                new LlmMessage("user", "drugie"),
            },
        }));

        var first = body.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("image", first[0].GetProperty("type").GetString());
        Assert.Equal("image/png", first[0].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String(Png), first[0].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("pierwsze", first[1].GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("messages")[2].GetProperty("content").ValueKind); // ostatnia tura: punkt pamięci podręcznej jak dotąd
    }
}
