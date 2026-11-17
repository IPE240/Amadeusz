namespace LokalneAI.Core.Hardware;

/// <summary>
/// Historia temperatury do wykresu na pasku tytułu: ostatnie <see cref="Window"/> (2 minuty), skala od <see cref="Floor"/>
/// (35°C — karta w spoczynku) do najwyższej wartości z historii, ale nie mniej niż <see cref="MinimumCeiling"/> (65°C).
/// </summary>
public sealed class TemperatureHistory
{
    /// <summary>Dół skali: typowa temperatura bez obciążenia.</summary>
    public const double Floor = 35;

    /// <summary>Najniższa góra skali, żeby spokojna karta nie wyglądała na „pełny wykres”.</summary>
    public const double MinimumCeiling = 65;

    private readonly List<(DateTime Time, double Celsius)> _samples = [];

    /// <summary>Jak długą historię pokazywać.</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromMinutes(2);

    /// <summary>Próbki z ostatnich 2 minut, od najstarszej.</summary>
    public IReadOnlyList<(DateTime Time, double Celsius)> Samples => _samples;

    /// <summary>Ostatnia temperatura albo null.</summary>
    public double? Current => _samples.Count == 0 ? null : _samples[^1].Celsius;

    /// <summary>Góra skali: max z historii, nie mniej niż 65°C.</summary>
    public double Ceiling => Math.Max(MinimumCeiling, _samples.Count == 0 ? 0 : _samples.Max(s => s.Celsius));

    /// <summary>Dokłada próbkę i usuwa starsze niż 2 minuty.</summary>
    public void Add(DateTime time, double celsius)
    {
        _samples.Add((time, celsius));
        _samples.RemoveAll(s => time - s.Time > Window);
    }

    /// <summary>Kasuje historię (np. po zmianie komputera, który liczy).</summary>
    public void Clear() => _samples.Clear();

    /// <summary>
    /// Punkty wykresu w polu <paramref name="width"/>×<paramref name="height"/> (y w dół): prawa krawędź = <paramref name="now"/>,
    /// lewa = 2 minuty wcześniej; temperatura poniżej 35°C leży na dole.
    /// </summary>
    public IReadOnlyList<(double X, double Y)> ToPoints(double width, double height, DateTime now)
    {
        var ceiling = Ceiling;
        var span = Window.TotalSeconds;
        return _samples.Select(s =>
        {
            var x = width - (Math.Clamp((now - s.Time).TotalSeconds, 0, span) / span * width);
            var fraction = Math.Clamp((s.Celsius - Floor) / (ceiling - Floor), 0, 1);
            return (x, height - (fraction * height));
        }).ToList();
    }
}
