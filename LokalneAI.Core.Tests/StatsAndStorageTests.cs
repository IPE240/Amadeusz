using LokalneAI.Core.Ollama;
using LokalneAI.Core.Storage;

namespace LokalneAI.Core.Tests;

public class ChatStatsTests
{
    [Fact]
    public void Describe_PodajeMiejsceLiczeniaISzybkoscPoPolsku()
    {
        var done = new ChatChunk
        {
            Done = true,
            EvalCount = 113,
            EvalDuration = 5_000_000_000,
            PromptEvalCount = 42,
            LoadDuration = 5_600_000_000,
            TotalDuration = 11_000_000_000,
        };

        var stats = ChatStats.From(done, TimeSpan.FromSeconds(6.1), TimeSpan.FromSeconds(11.2), 0.73, "NVIDIA GeForce RTX 4060");

        Assert.Equal(22.6, stats.TokensPerSecond!.Value, 1);
        Assert.Equal(
            "NVIDIA GeForce RTX 4060: 73% modelu w VRAM, reszta na CPU · 113 tok. · 22,6 tok/s · pierwsze słowo po 6,1 s · razem 11,0 s · w tym ładowanie modelu 5,6 s",
            stats.Describe());
    }

    [Theory]
    [InlineData(1.0, "RTX: cały model w VRAM")]
    [InlineData(0.0, "procesor (CPU): model poza kartą graficzną")]
    [InlineData(null, "nie wiem, gdzie liczył model")]
    public void DescribePlacement_RozrozniaGpuICpu(double? share, string expected)
        => Assert.Equal(expected, ChatStats.DescribePlacement(share, "RTX"));

    [Fact]
    public void Describe_PokazujeCzekanieWKolejce()
    {
        var done = new ChatChunk { Done = true, EvalCount = 10, EvalDuration = 500_000_000, TotalDuration = 800_000_000 };

        var stats = ChatStats.From(done, TimeSpan.FromSeconds(9.9), TimeSpan.FromSeconds(10.7), 1.0, "RTX");

        Assert.Equal(TimeSpan.FromSeconds(9.9), stats.Queued);
        Assert.Equal(
            "RTX: cały model w VRAM · 10 tok. · 20,0 tok/s · pierwsze słowo po 9,9 s · razem 10,7 s · w tym czekanie w kolejce 9,9 s",
            stats.Describe());
    }

    [Fact]
    public void From_BezCzasuOllamy_BierzeCzasZegara()
    {
        var stats = ChatStats.From(new ChatChunk { Done = true }, null, TimeSpan.FromSeconds(2), null, null);

        Assert.Equal(TimeSpan.FromSeconds(2), stats.Total);
        Assert.Null(stats.TokensPerSecond);
    }
}

public class PullTrackerTests
{
    [Fact]
    public void Add_SumujeWarstwyIPokazujeProcent()
    {
        var tracker = new PullTracker();
        tracker.Add(new PullProgress { Status = "pulling manifest" });
        Assert.Equal("pobieram opis modelu…", tracker.Describe());

        tracker.Add(new PullProgress { Status = "pulling c168", Digest = "a", Total = 6_000_000_000, Completed = 1_500_000_000 });
        tracker.Add(new PullProgress { Status = "pulling a72b", Digest = "b", Total = 1219 });
        Assert.Equal(25, tracker.Percent!.Value, 0);
        Assert.Equal("1,50 z 6,00 GB (25%)", tracker.Describe());

        // Starsza linia z mniejszym postępem nie cofa paska.
        tracker.Add(new PullProgress { Status = "pulling c168", Digest = "a", Total = 6_000_000_000, Completed = 1_000_000_000 });
        Assert.Equal(25, tracker.Percent!.Value, 0);

        tracker.Add(new PullProgress { Status = "verifying sha256 digest" });
        Assert.Equal("sprawdzam sumę kontrolną…", tracker.Describe());

        tracker.Add(new PullProgress { Status = "success" });
        Assert.True(tracker.Succeeded);
        Assert.Equal(100, tracker.Percent);
    }
}

public class GpuInfoTests
{
    [Fact]
    public void ParseNvidiaSmi_CzytaNazweIPamiec()
    {
        // Wynik nvidia-smi z tego PC (RTX 4060, 8 GB).
        var card = Assert.Single(GpuInfo.ParseNvidiaSmi("NVIDIA GeForce RTX 4060, 8188, 2049\r\n"));

        Assert.Equal("NVIDIA GeForce RTX 4060", card.Name);
        Assert.Equal(8188, card.TotalMiB);
        Assert.Equal(2049, card.UsedMiB);
        Assert.Equal("NVIDIA GeForce RTX 4060 (8 GB)", card.Describe());
    }

    [Fact]
    public void ParseNvidiaSmi_PustyWynik_BrakKart()
        => Assert.Empty(GpuInfo.ParseNvidiaSmi("\r\n"));
}

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LokalneAI-testy-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_ZapisIOdczyt_ZachowujaPolskieZnaki()
    {
        var store = new SettingsStore(Path.Combine(_dir, "ustawienia.json"));
        var settings = new AppSettings { SystemPrompt = "Odpowiadaj zwięźle, żółć gęślą jaźń", Temperature = 0.3, ContextLength = 4096 };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(settings.SystemPrompt, loaded.SystemPrompt);
        Assert.Equal(0.3, loaded.Temperature);
        Assert.Equal(4096, loaded.ContextLength);
        Assert.Contains("żółć gęślą jaźń", File.ReadAllText(store.Path));
        Assert.Null(store.LoadError);
    }

    [Fact]
    public void Settings_UszkodzonyPlik_DajeDomyslneIOpisBledu()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "ustawienia.json");
        File.WriteAllText(path, "{ to nie jest json");
        var store = new SettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(AppSettings.DefaultModel, loaded.Model);
        Assert.NotNull(store.LoadError);
    }

    [Fact]
    public void Settings_WartosciSpozaZakresu_SaPoprawiane()
    {
        var settings = new AppSettings { Temperature = 7, ContextLength = 1234, Model = " ", OllamaUrl = "" }.Normalize();

        Assert.Equal(AppSettings.DefaultTemperature, settings.Temperature);
        Assert.Equal(AppSettings.DefaultContextLength, settings.ContextLength);
        Assert.Equal(AppSettings.DefaultModel, settings.Model);
        Assert.Equal("http://127.0.0.1:11434", settings.OllamaUrl);
    }

    [Fact]
    public void History_DopisujeLinieJsonZCzytelnymiZnakami()
    {
        var log = new HistoryLog(Path.Combine(_dir, "historia.jsonl"));

        log.Append(new HistoryEntry { Question = "Ile przepracowałem godzin na WSP?", Answer = "845 h", Outcome = "ok" });
        log.Append(new HistoryEntry { Question = "Drugie", Outcome = "przerwano" });

        var lines = File.ReadAllLines(log.Path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("przepracowałem", lines[0]);
        Assert.DoesNotContain("\"error\"", lines[0]);
        Assert.Contains("\"outcome\":\"przerwano\"", lines[1]);
    }
}
