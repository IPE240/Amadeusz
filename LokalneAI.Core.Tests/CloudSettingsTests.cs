using System.Runtime.Versioning;
using System.Text;
using LokalneAI.Core.Storage;
using LokalneAI.Gateway;
using Xunit;

namespace LokalneAI.Core.Tests;

/// <summary>Modele w chmurze po stronie okna: klucze zaszyfrowane kontem Windows, ulubione, blokada chmury Ollamy w bramie.</summary>
[SupportedOSPlatform("windows")]
public class CloudSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lokalneai_klucze_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact]
    public void Klucze_ZapisOdczytUsuniecie_PlikBezJawnegoKlucza()
    {
        var path = Path.Combine(_folder, "klucze.json");
        var store = new CloudKeyStore(path);

        store.Set("anthropic", "  sk-test-123  ");
        store.Set("openai", "sk-openai-456");

        Assert.Equal("sk-test-123", new CloudKeyStore(path).Get("anthropic"));
        Assert.True(store.Has("openai"));
        Assert.False(store.Has("google"));
        Assert.DoesNotContain("sk-test-123", File.ReadAllText(path, Encoding.UTF8));

        store.Set("anthropic", null);
        Assert.Null(store.Get("anthropic"));
        Assert.Equal("sk-openai-456", store.Get("openai"));
    }

    [Fact]
    public void Klucze_UszkodzonyPlikToBrakKluczy()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "klucze.json");
        File.WriteAllText(path, "{\"anthropic\":\"to-nie-jest-dpapi\"}");

        Assert.Null(new CloudKeyStore(path).Get("anthropic"));
    }

    [Fact]
    public void Ustawienia_PorzadkujaModeleWChmurzeIUlubione()
    {
        var settings = new AppSettings
        {
            ModelProvider = " Anthropic ",
            CloudModels =
            [
                new CloudModelChoice { Provider = "Anthropic", Id = "claude-test" },
                new CloudModelChoice { Provider = "anthropic", Id = "claude-test" },
                new CloudModelChoice { Provider = "", Id = "bez-dostawcy" },
            ],
            FavoriteModels = ["anthropic|claude-test", "ANTHROPIC|claude-test", " "],
        }.Normalize();

        Assert.Equal("anthropic", settings.ModelProvider);
        Assert.Single(settings.CloudModels);
        Assert.Equal("anthropic", settings.CloudModels[0].Provider);
        Assert.Single(settings.FavoriteModels);
        Assert.Equal("bielik", AppSettings.ModelKey(null, "bielik"));
        Assert.Equal("openai|gpt-test", AppSettings.ModelKey("openai", "gpt-test"));
    }

    [Theory]
    [InlineData("deepseek-v3.1:671b-cloud", true)]
    [InlineData("glm-4.6:cloud", true)]
    [InlineData("SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M", false)]
    [InlineData(null, false)]
    public void Brama_RozpoznajeModeleChmuryOllamy(string? model, bool cloud)
        => Assert.Equal(cloud, GatewayServer.IsOllamaCloudModel(model));
}
