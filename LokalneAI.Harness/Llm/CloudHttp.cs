using System;
using System.Text.Json;

namespace LokalneAI.Harness.Llm
{
    /// <summary>Wspólne kawałki klientów modeli w chmurze: polecenie formatu, wyjęcie JSON-a z odpowiedzi, opis błędu.</summary>
    internal static class CloudHttp
    {
        /// <summary>Dopisek do polecenia systemowego, gdy serwer nie wymusza schematu gramatyką.</summary>
        public static string SchemaInstruction(string schema)
            => "\n\nFORMAT ODPOWIEDZI: odpowiadaj wyłącznie jednym obiektem JSON zgodnym z poniższym JSON Schema. "
               + "Bez komentarza, bez bloków ``` i bez tekstu przed ani po obiekcie.\n" + schema;

        /// <summary>
        /// Wyjmuje obiekt JSON z odpowiedzi modelu, który mógł dodać ``` albo zdanie wstępu.
        /// Gdy nie ma poprawnego obiektu, zwraca tekst bez zmian (harness zgłosi niepoprawny krok i poprosi o poprawkę).
        /// </summary>
        public static string ExtractJson(string text)
        {
            var trimmed = (text ?? "").Trim();
            if (IsJsonObject(trimmed))
            {
                return trimmed;
            }

            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var candidate = trimmed.Substring(start, end - start + 1);
                if (IsJsonObject(candidate))
                {
                    return candidate;
                }
            }

            return trimmed;
        }

        /// <summary>Czytelny opis błędu HTTP dostawcy: kod, co z nim zrobić i komunikat serwera.</summary>
        public static string DescribeError(string service, int status, string body)
        {
            var message = ServerMessage(body);
            string hint;
            switch (status)
            {
                case 400:
                    hint = "zapytanie odrzucone";
                    break;
                case 401:
                case 403:
                    hint = "klucz API nie działa albo nie ma dostępu do tego modelu — sprawdź Ustawienia → Modele w chmurze";
                    break;
                case 404:
                    hint = "nie ma takiego modelu albo adresu";
                    break;
                case 429:
                    hint = "za dużo zapytań albo wyczerpany limit konta — spróbuj za chwilę albo sprawdź rozliczenia";
                    break;
                case 529:
                case 503:
                    hint = "serwery są przeciążone — spróbuj za chwilę";
                    break;
                default:
                    hint = status >= 500 ? "błąd po stronie serwera — spróbuj za chwilę" : "błąd zapytania";
                    break;
            }

            return service + ": " + hint + " (" + status + ")" + (message.Length > 0 ? ". " + Text.Shorten(message, 300) : ".");
        }

        /// <summary>Komunikat z treści błędu: <c>{"error":{"message":…}}</c> (OpenAI, Anthropic), tablica (Google) albo sam tekst.</summary>
        public static string ServerMessage(string body)
        {
            var text = (body ?? "").Trim();
            if (text.Length == 0)
            {
                return "";
            }

            try
            {
                using (var document = JsonDocument.Parse(text))
                {
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                    {
                        root = root[0];
                    }

                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
                    {
                        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                        {
                            return message.GetString() ?? "";
                        }

                        if (error.ValueKind == JsonValueKind.String)
                        {
                            return error.GetString() ?? "";
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Nie JSON — zwracamy tekst.
            }

            return text;
        }

        /// <summary>Czy serwer odrzucił parametr <c>temperature</c> (część nowszych modeli przyjmuje tylko domyślną).</summary>
        public static bool RejectsTemperature(int status, string body)
            => status == 400 && (body ?? "").IndexOf("temperature", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsJsonObject(string text)
        {
            if (text.Length < 2 || text[0] != '{')
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(text))
                {
                    return document.RootElement.ValueKind == JsonValueKind.Object;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
