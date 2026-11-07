using System;
using System.Text.Json;
using LokalneAI.Harness.Profiles;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Narzędzia wykonywane przez samą bibliotekę (nie przez gospodarza).</summary>
    public static class BuiltInTools
    {
        public const string CalculatorName = "kalkulator";
        public const string KnowledgeName = "szukaj_wiedzy";

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
                default:
                    return null;
            }
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
