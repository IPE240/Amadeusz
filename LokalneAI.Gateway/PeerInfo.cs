using System.Text.Json;
using System.Text.Json.Serialization;

namespace LokalneAI.Gateway;

/// <summary>Jak brama przedstawia się innym (<c>GET /lokalneai/info</c> i odpowiedź na wyszukiwanie w sieci).</summary>
public sealed class PeerInfo
{
    /// <summary>Zawsze „LokalneAI” — po tym klient poznaje, że po drugiej stronie jest brama, a nie inny program.</summary>
    [JsonPropertyName("program")]
    public string Program { get; set; } = "LokalneAI";

    /// <summary>Nazwa komputera.</summary>
    [JsonPropertyName("komputer")]
    public string Computer { get; set; } = "";

    /// <summary>Wersja programu.</summary>
    [JsonPropertyName("wersja")]
    public string Version { get; set; } = "";

    /// <summary>Port bramy.</summary>
    [JsonPropertyName("port")]
    public int Port { get; set; }

    /// <summary>Czy przyjmuje zapytania z innych komputerów.</summary>
    [JsonPropertyName("udostepnia")]
    public bool Sharing { get; set; }

    /// <summary>Karta graficzna, np. „NVIDIA GeForce RTX 4090, 24 GB”.</summary>
    [JsonPropertyName("karta")]
    public string? Gpu { get; set; }

    /// <summary>Ile zapytań teraz liczy.</summary>
    [JsonPropertyName("liczy")]
    public int Running { get; set; }

    /// <summary>Ile zapytań czeka.</summary>
    [JsonPropertyName("czeka")]
    public int Waiting { get; set; }

    /// <summary>Największy kontekst dla innych komputerów.</summary>
    [JsonPropertyName("maks_kontekst")]
    public int MaxContext { get; set; }

    /// <summary>Adres, pod którym znaleziono bramę (uzupełnia wyszukiwanie; nie idzie przez sieć).</summary>
    [JsonIgnore]
    public string? Address { get; set; }

    /// <summary>Adres bramy do ustawień, np. <c>http://10.0.0.50:11435</c>.</summary>
    [JsonIgnore]
    public string Url => "http://" + (Address ?? Computer) + ":" + Port;

    /// <summary>Do listy znalezionych komputerów: opis i adres.</summary>
    [JsonIgnore]
    public string Summary => Describe() + " · " + (Address ?? Computer) + ":" + Port;

    /// <summary>Jedna linia do okna: „PC-ODS12 · RTX 4090, 24 GB · liczy 1, czeka 0”.</summary>
    public string Describe()
    {
        var gpu = string.IsNullOrWhiteSpace(Gpu) ? "karta nieznana" : Gpu;
        return $"{Computer} · {gpu} · liczy {Running}, czeka {Waiting}";
    }

    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Czyta odpowiedź bramy; null, gdy to nie jest brama LokalneAI.</summary>
    public static PeerInfo? Parse(string json)
    {
        try
        {
            var info = JsonSerializer.Deserialize<PeerInfo>(json, Json);
            return info is not null && info.Program == "LokalneAI" && info.Computer.Length > 0 ? info : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>JSON do wysłania.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>Nagłówki HTTP, którymi brama i biblioteka opisują zapytanie. Wartości są kodowane jak w adresie (%C5%82).</summary>
public static class GatewayHeaders
{
    /// <summary>Program, który pyta, np. „Czat”, „Code: tekla”, „Tekla”.</summary>
    public const string Program = "X-LokalneAI-Program";

    /// <summary>Komputer, z którego przyszło zapytanie (ustawia brama, która je przekazuje).</summary>
    public const string Computer = "X-LokalneAI-Komputer";

    /// <summary>„1” = zapytanie przekazała już inna brama; nie przekazywać dalej (bez pętli).</summary>
    public const string Forwarded = "X-LokalneAI-Przekazane";

    /// <summary>Koduje wartość nagłówka (polskie litery).</summary>
    public static string Encode(string value) => Uri.EscapeDataString(value);

    /// <summary>Odkodowuje wartość nagłówka; null albo pusta = null.</summary>
    public static string? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var text = Uri.UnescapeDataString(value.Trim());
            return text.Length > 80 ? text[..80] : text;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
