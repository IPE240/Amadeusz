using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LokalneAI.Harness
{
    /// <summary>Wspólne drobiazgi: JSON, kultura polska, szacowanie tokenów.</summary>
    internal static class Text
    {
        public static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

        public static readonly JsonWriterOptions CompactWriter = new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static readonly JsonSerializerOptions Relaxed = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Zgrubna liczba tokenów dla polskiego tekstu (ok. 3,2 znaku na token).</summary>
        public static int EstimateTokens(string text) => string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 3.2) + 4;

        /// <summary>Zwarta postać JSON (jedna linia, czytelne polskie znaki).</summary>
        public static string Compact(JsonElement element)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, CompactWriter))
                {
                    element.WriteTo(writer);
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static JsonElement ParseElement(string json)
        {
            using (var document = JsonDocument.Parse(json))
            {
                return document.RootElement.Clone();
            }
        }

        public static string Shorten(string text, int max)
        {
            if (text == null)
            {
                return "";
            }

            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        /// <summary>Usuwa polskie znaki i zamienia na małe litery (do wyszukiwania).</summary>
        public static string Fold(string text)
        {
            var normalized = (text ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                builder.Append(c == 'ł' ? 'l' : c);
            }

            return builder.ToString();
        }
    }
}
