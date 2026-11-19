using System.Globalization;
using System.Text.RegularExpressions;
using LokalneAI.Core.Ollama;

namespace LokalneAI.Core;

/// <summary>
/// Komputer dla podagentów (Ustawienia → Gdzie liczyć → Komputery dla podagentów): adres bramy innego komputera
/// (nazwa albo IP, port tylko gdy inny niż 11435). „Ten komputer” nie jest wpisem — to osobny przełącznik
/// <see cref="Storage.AppSettings.AgentsUseThisComputer"/>.
/// </summary>
public sealed class AgentMachineSetting
{
    /// <summary>Adres, jak w „Gdzie liczyć”: <c>PC-ODS12</c>, <c>10.0.0.50</c> albo <c>10.0.0.50:12000</c>.</summary>
    public string Address { get; set; } = "";

    /// <summary>Nazwa komputera z jego odpowiedzi (<c>/lokalneai/info</c>); null, dopóki nie sprawdzono.</summary>
    public string? Name { get; set; }
}

/// <summary>
/// Dobór modelu podagenta do karty danego komputera: z listy modeli tego komputera (<c>GET /api/tags</c>), tego, co już
/// siedzi w pamięci (<c>GET /api/ps</c>), i pamięci karty. Czysta logika, bez sieci — do testów.
/// </summary>
public static class ModelChooser
{
    private static readonly Regex GpuMemory = new(@"(\d+(?:[.,]\d+)?)\s*GB", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Pamięć karty w GB z opisu „NVIDIA GeForce RTX 4060 (8 GB)” albo „RTX 4090, 24 GB”; null, gdy opis jej nie podaje.</summary>
    public static double? ParseGpuGb(string? gpuDescription)
    {
        if (string.IsNullOrWhiteSpace(gpuDescription))
        {
            return null;
        }

        var match = GpuMemory.Match(gpuDescription);
        return match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) && gb > 0
            ? gb
            : null;
    }

    /// <summary>
    /// Szacunek pamięci karty, jakiej model potrzebuje z kontekstem: plik modelu z 10% zapasu, pamięć podręczna kontekstu
    /// (ok. 0,08 GB na 1 000 tokenów) i stały narzut. Zgrubnie — do wyboru modelu, nie do rozliczeń.
    /// </summary>
    public static double EstimateGb(LocalModel model, int contextLength)
        => model.Size / (1024.0 * 1024 * 1024) * 1.1 + Math.Max(0, contextLength) / 1024.0 * 0.08 + 0.4;

    /// <summary>
    /// Model do rozmowy (nie embeddingi, nie chmura Ollamy): po możliwościach z <c>/api/tags</c>, gdy serwer je podaje,
    /// inaczej po nazwie.
    /// </summary>
    public static bool IsChatModel(LocalModel model)
    {
        var name = (model.Name ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0 || name.Contains("embed", StringComparison.Ordinal) || name.EndsWith(":cloud", StringComparison.Ordinal) || name.EndsWith("-cloud", StringComparison.Ordinal))
        {
            return false;
        }

        if (model.Capabilities is { Count: > 0 } capabilities)
        {
            return capabilities.Any(c => string.Equals(c, "completion", StringComparison.OrdinalIgnoreCase))
                && !capabilities.Any(c => string.Equals(c, "embedding", StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }

    /// <summary>
    /// Wybiera model podagenta. Kolejność: pierwszy z <paramref name="preferred"/> (klucze jak w ustawieniach: nazwa z Ollamy),
    /// który jest na tym komputerze i mieści się w karcie z zapasem 40% (model wybrany przez użytkownika może częściowo liczyć
    /// procesor — tak pracuje Bielik 11B na 8 GB); potem największy model już załadowany do pamięci (<paramref name="running"/>)
    /// z zapasem 10%; potem największy, który mieści się w całości; gdy nic się nie mieści — najmniejszy. Null = brak modeli do rozmowy.
    /// </summary>
    public static LocalModel? Choose(IReadOnlyList<LocalModel> models, IReadOnlyList<RunningModel>? running, double? vramGb, int contextLength, IEnumerable<string>? preferred)
    {
        var candidates = (models ?? []).Where(IsChatModel).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        bool Fits(LocalModel m, double tolerance = 1.0) => vramGb is null || EstimateGb(m, contextLength) <= vramGb.Value * tolerance;
        foreach (var key in preferred ?? [])
        {
            var wanted = candidates.FirstOrDefault(c => SameModel(c.Name, key));
            if (wanted is not null && Fits(wanted, 1.4))
            {
                return wanted;
            }
        }

        var loaded = (running ?? []).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inMemory = candidates.Where(c => loaded.Contains(c.Name) && Fits(c, 1.1)).OrderByDescending(c => c.Size).FirstOrDefault();
        if (inMemory is not null)
        {
            return inMemory;
        }

        return candidates.Where(c => Fits(c)).OrderByDescending(c => c.Size).FirstOrDefault()
            ?? candidates.OrderBy(c => c.Size).First();
    }

    /// <summary>Czy nazwa modelu z Ollamy odpowiada kluczowi: równa, albo klucz bez tagu równy nazwie bez tagu (<c>gemma4:12b</c> = <c>gemma4:12b</c>, <c>bielik</c> = <c>bielik:latest</c>).</summary>
    public static bool SameModel(string? name, string? key)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        name = name.Trim();
        key = key.Trim();
        if (key.Contains('|'))
        {
            return false; // klucz modelu w chmurze („dostawca|id”) — podagent liczy tylko lokalnie
        }

        if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(name, key + ":latest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !key.Contains(':') && string.Equals(name.Split(':')[0], key, StringComparison.OrdinalIgnoreCase);
    }
}
