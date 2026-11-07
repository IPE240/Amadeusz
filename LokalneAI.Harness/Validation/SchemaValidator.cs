using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace LokalneAI.Harness.Validation
{
    /// <summary>
    /// Sprawdza argumenty wywołania z podzbiorem JSON Schema: type, properties, required,
    /// additionalProperties:false, enum, minimum, maximum, minLength, maxLength, items, minItems, maxItems.
    /// Gramatyka serwera pilnuje kształtu, ale nie wszystkich ograniczeń — dlatego sprawdzamy jeszcze raz.
    /// </summary>
    public static class SchemaValidator
    {
        public static IList<string> Validate(JsonElement value, JsonElement schema)
        {
            var errors = new List<string>();
            Check(value, schema, "argumenty", errors);
            return errors;
        }

        private static void Check(JsonElement value, JsonElement schema, string path, List<string> errors)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && !HasType(value, type.GetString()))
            {
                errors.Add(path + ": oczekiwano typu " + type.GetString() + ", jest " + Describe(value));
                return;
            }

            if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array
                && !allowed.EnumerateArray().Any(a => JsonEquals(a, value)))
            {
                errors.Add(path + ": wartość " + value.GetRawText() + " spoza listy: " + string.Join(", ", allowed.EnumerateArray().Select(a => a.GetRawText())));
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    CheckObject(value, schema, path, errors);
                    break;
                case JsonValueKind.Array:
                    CheckArray(value, schema, path, errors);
                    break;
                case JsonValueKind.Number:
                    CheckNumber(value, schema, path, errors);
                    break;
                case JsonValueKind.String:
                    var length = value.GetString().Length;
                    if (schema.TryGetProperty("minLength", out var minLength) && minLength.TryGetInt32(out var min) && length < min)
                    {
                        errors.Add(path + ": tekst krótszy niż " + min + " znaków");
                    }

                    if (schema.TryGetProperty("maxLength", out var maxLength) && maxLength.TryGetInt32(out var max) && length > max)
                    {
                        errors.Add(path + ": tekst dłuższy niż " + max + " znaków");
                    }

                    break;
            }
        }

        private static void CheckObject(JsonElement value, JsonElement schema, string path, List<string> errors)
        {
            var properties = schema.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in required.EnumerateArray().Select(r => r.GetString()))
                {
                    if (!value.TryGetProperty(name, out _))
                    {
                        errors.Add(path + ": brak wymaganego „" + name + "”");
                    }
                }
            }

            var closed = schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False;
            foreach (var property in value.EnumerateObject())
            {
                if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
                {
                    Check(property.Value, propertySchema, path + "." + property.Name, errors);
                }
                else if (closed)
                {
                    errors.Add(path + ": nieznany parametr „" + property.Name + "”");
                }
            }
        }

        private static void CheckArray(JsonElement value, JsonElement schema, string path, List<string> errors)
        {
            var count = value.GetArrayLength();
            if (schema.TryGetProperty("minItems", out var minItems) && minItems.TryGetInt32(out var min) && count < min)
            {
                errors.Add(path + ": za mało pozycji (najmniej " + min + ")");
            }

            if (schema.TryGetProperty("maxItems", out var maxItems) && maxItems.TryGetInt32(out var max) && count > max)
            {
                errors.Add(path + ": za dużo pozycji (najwyżej " + max + ")");
            }

            if (schema.TryGetProperty("items", out var items))
            {
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    Check(item, items, path + "[" + index++ + "]", errors);
                }
            }
        }

        private static void CheckNumber(JsonElement value, JsonElement schema, string path, List<string> errors)
        {
            if (!value.TryGetDouble(out var number) || double.IsInfinity(number))
            {
                errors.Add(path + ": liczba poza zakresem");
                return;
            }

            if (schema.TryGetProperty("minimum", out var minimum) && minimum.TryGetDouble(out var min) && number < min)
            {
                errors.Add(path + ": " + number.ToString(CultureInfo.InvariantCulture) + " mniejsze niż " + min.ToString(CultureInfo.InvariantCulture));
            }

            if (schema.TryGetProperty("maximum", out var maximum) && maximum.TryGetDouble(out var max) && number > max)
            {
                errors.Add(path + ": " + number.ToString(CultureInfo.InvariantCulture) + " większe niż " + max.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static bool HasType(JsonElement value, string type)
        {
            switch (type)
            {
                case "object": return value.ValueKind == JsonValueKind.Object;
                case "array": return value.ValueKind == JsonValueKind.Array;
                case "string": return value.ValueKind == JsonValueKind.String;
                case "boolean": return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
                case "number": return value.ValueKind == JsonValueKind.Number;
                case "integer": return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && !double.IsInfinity(d) && Math.Abs(d % 1) < 1e-9;
                case "null": return value.ValueKind == JsonValueKind.Null;
                default: return true;
            }
        }

        private static bool JsonEquals(JsonElement a, JsonElement b)
        {
            if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
            {
                return a.TryGetDouble(out var x) && b.TryGetDouble(out var y) && Math.Abs(x - y) < 1e-9;
            }

            if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.String)
            {
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
            }

            return a.ValueKind == b.ValueKind && a.GetRawText() == b.GetRawText();
        }

        private static string Describe(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return "tekst";
                case JsonValueKind.Number: return "liczba";
                case JsonValueKind.Object: return "obiekt";
                case JsonValueKind.Array: return "lista";
                case JsonValueKind.True:
                case JsonValueKind.False: return "tak/nie";
                default: return "puste";
            }
        }
    }
}
