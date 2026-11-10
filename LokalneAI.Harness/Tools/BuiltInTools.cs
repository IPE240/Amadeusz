using System;
using System.Text.Json;
using LokalneAI.Harness.Profiles;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Narzędzia wykonywane przez samą bibliotekę (nie przez gospodarza).</summary>
    public static class BuiltInTools
    {
        /// <summary>Kalkulator: nazwa do <c>narzedzia_wbudowane</c> w <c>profil.json</c>.</summary>
        public const string CalculatorName = "kalkulator";
        /// <summary>Wyszukiwarka w <c>wiedza\</c> profilu: nazwa do <c>narzedzia_wbudowane</c>.</summary>
        public const string KnowledgeName = "szukaj_wiedzy";
        /// <summary>Wyszukiwanie w internecie (dokładane, gdy włączony jest dostęp do internetu i profil na niego pozwala).</summary>
        public const string WebSearchName = "szukaj_w_internecie";
        /// <summary>Czytanie strony internetowej jako tekstu.</summary>
        public const string WebFetchName = "czytaj_strone";

        /// <summary>Czy to narzędzie internetowe (wykonywane przez <see cref="WebAccess"/> z opcji sesji).</summary>
        public static bool IsWeb(string name) => name == WebSearchName || name == WebFetchName;

        /// <summary>Definicja narzędzia wbudowanego albo null dla nieznanej nazwy.</summary>
        public static ToolDefinition Get(string name)
        {
            switch (name)
            {
                case CalculatorName:
                    return new ToolDefinition(
                        CalculatorName,
                        "Dokładnie liczy wyrażenie. Używaj do KAŻDEGO rachunku, którego nie zrobiła baza (nigdy nie licz w pamięci).",
                        "{\"type\":\"object\",\"properties\":{\"wyrazenie\":{\"type\":\"string\",\"description\":\"np. (7,5+8+6,5)/3 albo suma(7,5; 8; 6,5) albo zaokraglij(845/8; 2)\"}},\"required\":[\"wyrazenie\"],\"additionalProperties\":false}")
                    {
                        IsBuiltIn = true,
                    };
                case KnowledgeName:
                    return new ToolDefinition(
                        KnowledgeName,
                        "Szuka w notatkach z wiedzą profilu, gdy nie znasz pojęcia, zasady albo nazwy.",
                        "{\"type\":\"object\",\"properties\":{\"fraza\":{\"type\":\"string\",\"description\":\"kilka słów kluczowych po polsku\"}},\"required\":[\"fraza\"],\"additionalProperties\":false}")
                    {
                        IsBuiltIn = true,
                    };
                case WebSearchName:
                    return new ToolDefinition(
                        WebSearchName,
                        "Szuka w internecie (aktualne informacje, normy, producenci, ceny, dokumentacja programów). Zwraca tytuły, adresy i krótkie opisy stron.",
                        "{\"type\":\"object\",\"properties\":{\"fraza\":{\"type\":\"string\",\"description\":\"kilka słów, jak w wyszukiwarce\"}},\"required\":[\"fraza\"],\"additionalProperties\":false}")
                    {
                        IsBuiltIn = true,
                    };
                case WebFetchName:
                    return new ToolDefinition(
                        WebFetchName,
                        "Czyta stronę internetową jako tekst (adres z wyników wyszukiwania albo od użytkownika). Długie strony w częściach: od_znaku.",
                        "{\"type\":\"object\",\"properties\":{\"adres\":{\"type\":\"string\",\"description\":\"pełny adres https://…\"},\"od_znaku\":{\"type\":\"integer\",\"description\":\"od którego znaku czytać (domyślnie 0)\"}},\"required\":[\"adres\"],\"additionalProperties\":false}")
                    {
                        IsBuiltIn = true,
                    };
                default:
                    return null;
            }
        }

        /// <summary>Wykonuje narzędzie internetowe; bez <paramref name="web"/> (dostęp wyłączony) — błąd dla modelu.</summary>
        internal static async System.Threading.Tasks.Task<ToolResult> ExecuteWebAsync(string name, JsonElement arguments, WebAccess web, System.Threading.CancellationToken cancellationToken)
        {
            if (web == null)
            {
                return ToolResult.Error("BŁĄD: dostęp do internetu jest wyłączony. Odpowiedz bez niego albo poproś użytkownika o włączenie.");
            }

            if (name == WebSearchName)
            {
                return await web.SearchAsync(arguments.TryGetProperty("fraza", out var f) ? f.GetString() : "", cancellationToken).ConfigureAwait(false);
            }

            var from = arguments.TryGetProperty("od_znaku", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0;
            return await web.FetchAsync(arguments.TryGetProperty("adres", out var a) ? a.GetString() : "", from, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Wykonuje narzędzie wbudowane; null, gdy to nie jest narzędzie wbudowane.</summary>
        internal static ToolResult TryExecute(string name, JsonElement arguments, KnowledgeIndex knowledge)
        {
            switch (name)
            {
                case CalculatorName:
                    var expression = arguments.TryGetProperty("wyrazenie", out var e) ? e.GetString() : "";
                    try
                    {
                        return ToolResult.Ok(expression + " = " + Calculator.Format(Calculator.Evaluate(expression)));
                    }
                    catch (Exception ex) when (ex is FormatException || ex is ArithmeticException || ex is ArgumentException)
                    {
                        return ToolResult.Error("BŁĄD kalkulatora: " + ex.Message);
                    }

                case KnowledgeName:
                    var phrase = arguments.TryGetProperty("fraza", out var f) ? f.GetString() : "";
                    var hits = knowledge.Search(phrase);
                    return hits.Count == 0
                        ? ToolResult.Ok("Nic nie znaleziono dla: " + phrase)
                        : ToolResult.Ok(string.Join("\n\n", hits));
                default:
                    return null;
            }
        }
    }
}
