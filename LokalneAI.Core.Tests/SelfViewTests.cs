using LokalneAI.Core.Storage;

namespace LokalneAI.Core.Tests;

/// <summary>Amadeusz o sobie dla modelu (umiejętność „amadeusz”): ustawienia bez danych logowania, końcówka dziennika, wybór umiejętności.</summary>
public sealed class SelfViewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LokalneAI_selfview_" + Guid.NewGuid().ToString("N"));

    public SelfViewTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Ustawienia_BezPolaczeniaCdeIPersonId()
    {
        var settings = new AppSettings { GatewayPort = 11435, ShareOnLan = true, CdeHost = "serwer", CdeUser = "uzytkownik", CdePersonId = "3f2b-osoba" };
        var text = SelfView.SettingsForModel(settings, null);

        Assert.Contains("\"gatewayPort\": 11435", text);
        Assert.Contains("\"shareOnLan\": true", text);
        Assert.DoesNotContain("serwer", text);
        Assert.DoesNotContain("uzytkownik", text);
        Assert.DoesNotContain("3f2b-osoba", text);
        Assert.DoesNotMatch("\"cde[A-Za-z]*\"\\s*:", text);
    }

    [Fact]
    public void Ustawienia_WybranePola_IUkrytePolePoNazwie()
    {
        var settings = new AppSettings { GatewayPort = 12000, CdePersonId = "3f2b-osoba" };
        var text = SelfView.SettingsForModel(settings, "GatewayPort, cdePersonId, nieMaTakiego");

        Assert.Contains("\"gatewayPort\": 12000", text);
        Assert.DoesNotContain("3f2b-osoba", text);
        Assert.Contains("cdePersonId — pole ukryte", text);
        Assert.Contains("nieMaTakiego — nie ma takiego pola", text);
        Assert.DoesNotContain("shareOnLan", text);
    }

    [Theory]
    [InlineData("cdeHost", true)]
    [InlineData("CdePersonId", true)]
    [InlineData("apiKey", true)]
    [InlineData("gatewayPort", false)]
    [InlineData("codePermission", false)]
    [InlineData("codeGroups", false)]
    public void PolaUkryte(string name, bool hidden) => Assert.Equal(hidden, SelfView.IsHiddenField(name));

    [Fact]
    public void Dziennik_OstatnieLinieIBrakPliku()
    {
        var path = Path.Combine(_root, "bledy.log");
        Assert.Contains("nie ma pliku", SelfView.LogTail(path, 5));

        File.WriteAllLines(path, Enumerable.Range(1, 30).Select(i => "linia " + i).Concat(new[] { "", "  " }));
        var tail = SelfView.LogTail(path, 3);
        Assert.Equal("linia 28\nlinia 29\nlinia 30", tail);
    }

    [Fact]
    public void Dziennik_CzytaPlikOtwartyDoZapisu()
    {
        var path = Path.Combine(_root, "bezpieczenstwo.log");
        using var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
        writer.WriteLine("{\"kategoria\":\"siec_dostep\"}");
        writer.Flush();
        Assert.Contains("siec_dostep", SelfView.LogTail(path, 10));
    }

    [Theory]
    [InlineData("tekla", "ustaw", "amadeusz,pliki", "tekla")]
    [InlineData("Tekla, PLIKI", null, "amadeusz", "tekla,pliki")]
    [InlineData("pliki", "dodaj", "amadeusz", "pliki,amadeusz")]
    [InlineData("amadeusz", "usun", "amadeusz,pliki", "pliki")]
    [InlineData("tekla+cde", "ustaw", "", "tekla,cde")]
    public void WyborUmiejetnosci(string names, string? mode, string current, string expected)
    {
        var order = new[] { "tekla", "cde", "pliki", "amadeusz" };
        var chosen = SelfView.ApplySkillMode(
            current.Split(',', StringSplitOptions.RemoveEmptyEntries),
            SelfView.SplitNames(names).Select(n => order.First(o => string.Equals(o, n, StringComparison.OrdinalIgnoreCase))).ToList(),
            mode,
            order);
        Assert.Equal(expected, string.Join(",", chosen));
    }
}
