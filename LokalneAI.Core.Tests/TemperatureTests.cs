using LokalneAI.Core.Hardware;

namespace LokalneAI.Core.Tests;

/// <summary>Historia temperatury na pasku tytułu: 2 minuty, skala 35°C … max(65°C, max historii).</summary>
public sealed class TemperatureTests
{
    private static readonly DateTime Start = new(2026, 11, 17, 12, 0, 0);

    [Fact]
    public void Historia_DwieMinuty()
    {
        var history = new TemperatureHistory();
        for (var i = 0; i <= 30; i++)
        {
            history.Add(Start.AddSeconds(i * 5), 40 + i);
        }

        Assert.Equal(25, history.Samples.Count); // 0…120 s co 5 s
        Assert.Equal(70, history.Current);
        Assert.Equal(Start.AddSeconds(30), history.Samples[0].Time);
    }

    [Fact]
    public void Skala_Od35_DoMaxAleNieMniejNiz65()
    {
        var history = new TemperatureHistory();
        history.Add(Start, 35);
        history.Add(Start.AddSeconds(5), 50);
        var now = Start.AddSeconds(5);

        var calm = history.ToPoints(120, 30, now);

        Assert.Equal(65, history.Ceiling);
        Assert.Equal((120 - 5.0, 30.0), calm[0]); // 35°C na dole, 5 s wcześniej
        Assert.Equal(120, calm[1].X);
        Assert.Equal(15, calm[1].Y, 6); // 50°C w połowie skali 35…65

        history.Add(Start.AddSeconds(10), 80);
        Assert.Equal(80, history.Ceiling);
        Assert.Equal(0, history.ToPoints(120, 30, Start.AddSeconds(10))[2].Y); // max na górze
        history.Add(Start.AddSeconds(15), 20);
        Assert.Equal(30, history.ToPoints(120, 30, Start.AddSeconds(15))[3].Y); // poniżej 35°C — na dole
    }

    [Fact]
    public void Brama_PodajeTemperature_StaraBramaBez()
    {
        var info = LokalneAI.Gateway.PeerInfo.Parse("{\"program\":\"LokalneAI\",\"komputer\":\"PC-1\",\"temperatura\":61}");
        var old = LokalneAI.Gateway.PeerInfo.Parse("{\"program\":\"LokalneAI\",\"komputer\":\"PC-2\"}");

        Assert.Equal(61, info!.Temperature);
        Assert.Null(old!.Temperature);
        Assert.Contains("\"temperatura\":61", info.ToJson());
    }

    [Fact]
    public void OdczytKarty_LiczbaAlboBrak()
    {
        var celsius = GpuTemperature.Read(); // bez karty NVIDIA: null, bez wyjątku

        Assert.True(celsius is null or > 0 and < 150);
    }
}
