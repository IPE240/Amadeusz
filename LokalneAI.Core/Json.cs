using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LokalneAI.Core;

/// <summary>Wspólne ustawienia JSON: osobno dla API Ollamy i dla plików na dysku.</summary>
internal static class Json
{
    /// <summary>API Ollamy: nazwy pól snake_case (np. <c>num_ctx</c>), puste pola pomijane.</summary>
    public static readonly JsonSerializerOptions Api = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Pliki ustawień: wcięcia i czytelne polskie znaki (plik bywa otwierany w Notatniku).</summary>
    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Jeden obiekt w linii (historia w formacie JSONL), z czytelnymi polskimi znakami.</summary>
    public static readonly JsonSerializerOptions Line = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Polski zapis liczb (przecinek dziesiętny) w tekstach dla człowieka.</summary>
internal static class Formats
{
    public static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
}
