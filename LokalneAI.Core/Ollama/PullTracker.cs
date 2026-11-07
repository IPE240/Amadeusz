namespace LokalneAI.Core.Ollama;

/// <summary>
/// Składa postęp pobierania modelu z wielu warstw (plików) w jeden procent.
/// Ollama raportuje każdą warstwę osobno: duży plik wag i kilka małych (szablon, parametry).
/// </summary>
public sealed class PullTracker
{
    private readonly Dictionary<string, (long Total, long Completed)> _layers = new(StringComparer.Ordinal);

    public string Status { get; private set; } = "";

    public bool Succeeded { get; private set; }

    public long TotalBytes => _layers.Values.Sum(l => l.Total);

    public long CompletedBytes => _layers.Values.Sum(l => Math.Min(l.Completed, l.Total));

    /// <summary>Procent 0–100; null, dopóki Ollama nie poda rozmiaru.</summary>
    public double? Percent => Succeeded ? 100 : TotalBytes > 0 ? 100.0 * CompletedBytes / TotalBytes : null;

    public void Add(PullProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!string.IsNullOrEmpty(progress.Status))
        {
            Status = progress.Status;
        }

        if (progress.Status == "success")
        {
            Succeeded = true;
        }

        if (!string.IsNullOrEmpty(progress.Digest) && progress.Total is > 0)
        {
            var before = _layers.TryGetValue(progress.Digest, out var old) ? old.Completed : 0;
            _layers[progress.Digest] = (progress.Total.Value, Math.Max(before, progress.Completed ?? 0));
        }
    }

    /// <summary>Opis dla człowieka, np. „1,25 z 6,72 GB (19%)”.</summary>
    public string Describe()
    {
        if (Succeeded)
        {
            return "gotowe";
        }

        var phase = Status switch
        {
            "pulling manifest" => "pobieram opis modelu…",
            "verifying sha256 digest" => "sprawdzam sumę kontrolną…",
            "writing manifest" => "zapisuję model…",
            "removing unused layers" or "removing any unused layers" => "porządkuję pliki…",
            _ => null,
        };

        if (phase is not null && (TotalBytes == 0 || !Status.StartsWith("pulling", StringComparison.Ordinal)))
        {
            return phase;
        }

        return TotalBytes > 0
            ? string.Format(Formats.Pl, "{0:0.00} z {1:0.00} GB ({2:0}%)", CompletedBytes / 1e9, TotalBytes / 1e9, Percent)
            : Status;
    }
}
