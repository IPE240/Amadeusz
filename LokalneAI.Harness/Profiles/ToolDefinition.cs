using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LokalneAI.Harness.Profiles
{
    /// <summary>
    /// Narzędzie (polecenie) dostępne dla modelu: nazwa, opis, parametry jako JSON Schema.
    /// Wykonuje je gospodarz — biblioteka tylko sprawdza argumenty.
    /// </summary>
    public sealed class ToolDefinition
    {
        /// <summary>
        /// Tworzy narzędzie: nazwa dla modelu, jednozdaniowy opis, JSON Schema obiektu argumentów (null = bez argumentów),
        /// czy zmienia model/dane (wtedy sesja pyta o zgodę) i opcjonalna ścieżka szablonu.
        /// </summary>
        public ToolDefinition(string name, string description, string parametersSchemaJson, bool mutates = false, string template = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Narzędzie musi mieć nazwę.", nameof(name));
            }

            Name = name;
            Description = description ?? "";
            Mutates = mutates;
            Template = template;
            Parameters = Text.ParseElement(string.IsNullOrWhiteSpace(parametersSchemaJson)
                ? "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
                : parametersSchemaJson);
        }

        /// <summary>Nazwa, której używa model, np. <c>wstaw_slup</c>.</summary>
        public string Name { get; }

        /// <summary>Opis dla modelu: jedno zdanie, co robi polecenie.</summary>
        public string Description { get; }

        /// <summary>Czy polecenie zmienia model/dane (gospodarz może wtedy pytać o potwierdzenie).</summary>
        public bool Mutates { get; }

        /// <summary>Ścieżka szablonu (np. skryptu C# dla mostu Tekli), względna do folderu profilu; null = brak.</summary>
        public string Template { get; }

        /// <summary>JSON Schema obiektu argumentów.</summary>
        public JsonElement Parameters { get; }

        /// <summary>Wbudowane narzędzie biblioteki (kalkulator, wyszukiwanie wiedzy), a nie gospodarza.</summary>
        public bool IsBuiltIn { get; internal set; }

        /// <summary>
        /// W trybie zakończenia „mieszane” udane wywołanie kończy turę jak polecenie zmieniające (np. <c>cd</c>):
        /// wynik mówi wszystko i model nie musi go opisywać. Klucz <c>konczy_ture</c> w <c>narzedzia.json</c>.
        /// </summary>
        public bool FinishesTurn { get; set; }

        /// <summary>
        /// Zwięzły opis w jednej linii: <c>nazwa(par1*, par2, typ∈{a|b}) — opis</c>; gwiazdka = wymagany.
        /// Jednostki są w nazwach parametrów (<c>_mm</c>, <c>_m</c>), więc długie opisy parametrów zwykle nie są potrzebne.
        /// </summary>
        public string DescribeCompact()
        {
            var builder = new StringBuilder("- ").Append(Name).Append('(');
            var required = new HashSet<string>(StringComparer.Ordinal);
            if (Parameters.TryGetProperty("required", out var requiredArray) && requiredArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredArray.EnumerateArray())
                {
                    required.Add(item.GetString());
                }
            }

            var first = true;
            if (Parameters.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    builder.Append(first ? "" : ", ").Append(property.Name);
                    first = false;
                    if (required.Contains(property.Name))
                    {
                        builder.Append('*');
                    }

                    if (property.Value.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
                    {
                        builder.Append("∈{").Append(string.Join("|", values.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()))).Append('}');
                    }
                }
            }

            builder.Append(") — ").Append(Description.Trim());
            if (Mutates)
            {
                builder.Append(" [zmienia model]");
            }

            return builder.ToString();
        }

        /// <summary>Opis dla modelu w poleceniu systemowym: nazwa, opis i parametry z typami.</summary>
        public string DescribeForPrompt()
        {
            var builder = new StringBuilder();
            builder.Append("- ").Append(Name).Append(": ").Append(Description.Trim());
            if (Mutates)
            {
                builder.Append(" [zmienia model]");
            }

            var required = new HashSet<string>(StringComparer.Ordinal);
            if (Parameters.TryGetProperty("required", out var requiredArray) && requiredArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in requiredArray.EnumerateArray())
                {
                    required.Add(item.GetString());
                }
            }

            if (Parameters.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    builder.Append("\n    ").Append(property.Name).Append(" (").Append(TypeName(property.Value));
                    builder.Append(required.Contains(property.Name) ? ", wymagany" : ", opcjonalny").Append(')');
                    if (property.Value.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(": ").Append(description.GetString());
                    }

                    if (property.Value.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
                    {
                        builder.Append(" — jedno z: ");
                        var first = true;
                        foreach (var value in values.EnumerateArray())
                        {
                            builder.Append(first ? "" : ", ").Append(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText());
                            first = false;
                        }
                    }
                }
            }

            return builder.ToString();
        }

        private static string TypeName(JsonElement schema)
        {
            if (!schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            {
                return "dowolny";
            }

            switch (type.GetString())
            {
                case "number": return "liczba";
                case "integer": return "liczba całkowita";
                case "string": return "tekst";
                case "boolean": return "tak/nie";
                case "array": return "lista";
                case "object": return "obiekt";
                default: return type.GetString();
            }
        }
    }
}
