using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using LokalneAI.Harness.Profiles;

namespace LokalneAI.Harness.Session
{
    /// <summary>
    /// Schemat JSON jednego kroku modelu, wymuszany gramatyką po stronie serwera (Ollama <c>format</c>).
    /// Trzy warianty: <c>wywolania</c> (lista poleceń z argumentami zgodnymi z katalogiem),
    /// <c>odpowiedz</c> (koniec tury) i <c>pytanie</c> (brakuje danych — pytamy użytkownika).
    /// Odpowiedź i wywołania są rozdzielone celowo: model nie może ogłosić wyniku przed wykonaniem.
    /// </summary>
    public static class StepSchema
    {
        /// <summary>Wartość pola <c>krok</c>, gdy model wywołuje narzędzia.</summary>
        public const string Calls = "wywolania";
        /// <summary>Wartość pola <c>krok</c>, gdy model odpowiada (pole <c>tekst</c>).</summary>
        public const string Answer = "odpowiedz";
        /// <summary>Wartość pola <c>krok</c>, gdy model pyta użytkownika (pole <c>tekst</c>).</summary>
        public const string Question = "pytanie";

        /// <summary>Buduje JSON Schema kroku: <c>anyOf</c> z wywołaniami (najwyżej <paramref name="maxCalls"/>), odpowiedzią i pytaniem.</summary>
        public static string Build(IReadOnlyList<ToolDefinition> tools, int maxCalls, bool withPlan)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, Text.CompactWriter))
                {
                    writer.WriteStartObject();
                    writer.WriteStartArray("anyOf");
                    if (tools.Count > 0)
                    {
                        WriteCallsVariant(writer, tools, maxCalls, withPlan);
                    }

                    WriteTextVariant(writer, Answer);
                    WriteTextVariant(writer, Question);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void WriteCallsVariant(Utf8JsonWriter writer, IReadOnlyList<ToolDefinition> tools, int maxCalls, bool withPlan)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");
            WriteKind(writer, Calls);
            if (withPlan)
            {
                writer.WriteStartObject("plan");
                writer.WriteString("type", "string");
                writer.WriteEndObject();
            }

            writer.WriteStartObject(Calls);
            writer.WriteString("type", "array");
            writer.WriteNumber("minItems", 1);
            writer.WriteNumber("maxItems", maxCalls < 1 ? 1 : maxCalls);
            writer.WriteStartObject("items");
            writer.WriteStartArray("anyOf");
            foreach (var tool in tools)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "object");
                writer.WriteStartObject("properties");
                writer.WriteStartObject("narzedzie");
                writer.WriteString("type", "string");
                writer.WriteStartArray("enum");
                writer.WriteStringValue(tool.Name);
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WritePropertyName("argumenty");
                tool.Parameters.WriteTo(writer);
                writer.WriteEndObject();
                WriteRequired(writer, "narzedzie", "argumenty");
                writer.WriteBoolean("additionalProperties", false);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            if (withPlan)
            {
                WriteRequired(writer, "krok", "plan", Calls);
            }
            else
            {
                WriteRequired(writer, "krok", Calls);
            }

            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }

        private static void WriteTextVariant(Utf8JsonWriter writer, string kind)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");
            WriteKind(writer, kind);
            writer.WriteStartObject("tekst");
            writer.WriteString("type", "string");
            writer.WriteEndObject();
            writer.WriteEndObject();
            WriteRequired(writer, "krok", "tekst");
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }

        private static void WriteKind(Utf8JsonWriter writer, string kind)
        {
            writer.WriteStartObject("krok");
            writer.WriteString("type", "string");
            writer.WriteStartArray("enum");
            writer.WriteStringValue(kind);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteRequired(Utf8JsonWriter writer, params string[] names)
        {
            writer.WriteStartArray("required");
            foreach (var name in names)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
        }
    }
}
