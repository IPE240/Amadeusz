using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Session
{
    /// <summary>
    /// Pilnuje, żeby model nie poddawał się za łatwo (decyzja użytkownika z 2026-11-19: „Amadeusz za łatwo się poddaje”).
    /// Gdy odpowiedź brzmi jak „nie znalazłem / nie mam dostępu / nie da się”, a model nie wyczerpał możliwości, sesja cofa go
    /// z uwagą programu:
    /// <list type="bullet">
    /// <item>internet — mniej niż <see cref="MinWebSearches"/> wyszukiwań w tej turze: zmień frazę i szukaj dalej;</item>
    /// <item>pliki — „nie mam dostępu” bez próby: wywołaj polecenie z tą ścieżką (program zapyta użytkownika o dostęp) albo zapytaj.</item>
    /// </list>
    /// Rozpoznawanie po słowach, nie przez model: mały model potrafi zignorować zasadę z polecenia systemowego.
    /// </summary>
    public static class GiveUpGuard
    {
        /// <summary>Ile różnych wyszukiwań w internecie w jednej turze, zanim wolno odpowiedzieć, że czegoś nie ma.</summary>
        public const int MinWebSearches = 3;

        /// <summary>Najwięcej cofnięć w jednej turze z powodu internetu (z dostępem do plików — jedno).</summary>
        public const int MaxWebNudges = 2;

        private static readonly Regex GivingUp = new Regex(
            @"\b(nie\s+(udal\w*\s+(mi\s+)?sie|moglem|moge|zdolal\w*|znalaz\w*|znaleziono|da\s+sie|jestem\s+w\s+stanie|mam\s+(dostepu|uprawnien\w*|mozliwosci|informacji|danych)|posiadam)"
            + @"|brak\s+(wynikow|informacji|danych|dostepu|uprawnien\w*)|nic\s+nie\s+(znalaz\w*|ma)|nie\s+ma\s+(informacji|danych|wynikow|dostepu)"
            + @"|nie\s+dotarl\w*|bez\s+dostepu|odmow\w*\s+dostepu)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AccessWords = new Regex(
            @"\b(dostep\w*|uprawnien\w*|dozwolon\w*|zabronion\w*|odmow\w*)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Odpowiedź brzmi jak poddanie się („nie znalazłem”, „nie mam dostępu”, „nie da się”).</summary>
        public static bool LooksLikeGivingUp(string answer) => GivingUp.IsMatch(Text.Fold(answer ?? ""));

        /// <summary>Poddanie się z powodu dostępu do plików albo uprawnień.</summary>
        public static bool LooksLikeNoAccess(string answer)
        {
            var folded = Text.Fold(answer ?? "");
            return GivingUp.IsMatch(folded) && AccessWords.IsMatch(folded);
        }

        /// <summary>Frazy wyszukiwań w internecie z wywołań tej tury (bez powtórzeń).</summary>
        public static IReadOnlyList<string> WebPhrases(IEnumerable<ExecutedCall> calls)
        {
            var phrases = new List<string>();
            foreach (var call in calls ?? Enumerable.Empty<ExecutedCall>())
            {
                if (call.Tool != BuiltInTools.WebSearchName || string.IsNullOrWhiteSpace(call.ArgumentsJson))
                {
                    continue;
                }

                try
                {
                    using (var json = JsonDocument.Parse(call.ArgumentsJson))
                    {
                        if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("fraza", out var f) && f.ValueKind == JsonValueKind.String)
                        {
                            var phrase = (f.GetString() ?? "").Trim();
                            if (phrase.Length > 0 && !phrases.Contains(phrase, StringComparer.OrdinalIgnoreCase))
                            {
                                phrases.Add(phrase);
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Argumenty sprawdziła już sesja; zepsute po prostu pomijamy.
                }
            }

            return phrases;
        }

        /// <summary>Uwaga dla modelu, który za szybko przestał szukać w internecie.</summary>
        public static string WebRetryText(IReadOnlyList<string> phrases)
        {
            var count = phrases?.Count ?? 0;
            var tried = count == 0
                ? "Jeszcze nie szukałeś w internecie."
                : "Szukałeś w internecie tylko " + count + (count == 1 ? " raz" : " razy") + " (" + string.Join(", ", phrases.Select(p => "„" + p + "”")) + ").";
            return "[Uwaga programu: nie poddawaj się jeszcze. " + tried + " Zmień kryteria i spróbuj co najmniej jeszcze "
                + Math.Max(1, MinWebSearches - count) + " razy: inne słowa i synonimy, po angielsku, krótsza fraza (2–4 słowa), "
                + "nazwa producenta, normy albo programu, bez roku i szczegółów. Przeczytaj (" + BuiltInTools.WebFetchName + ") 1–2 najlepsze wyniki, "
                + "także gdy opis pasuje tylko częściowo. Dopiero gdy i to nic nie da, napisz, czego szukałeś i co znalazłeś najbliżej.]";
        }

        /// <summary>Uwaga dla modelu, który mówi „nie mam dostępu”, choć program może o niego zapytać.</summary>
        public const string AccessRetryText =
            "[Uwaga programu: nie odpowiadaj, że nie masz dostępu, zanim spróbujesz. Wywołaj polecenie z tą ścieżką — gdy folder jest poza "
            + "dozwolonymi, program sam zapyta użytkownika o dostęp i ponowi polecenie. Gdy nie znasz ścieżki albo brakuje uprawnienia "
            + "(np. zgody na zmianę), zadaj pytanie użytkownikowi (krok pytanie): o ścieżkę, o dostęp albo o zgodę.]";

        /// <summary>Stan jednej tury: ile razy już cofnięto model.</summary>
        internal sealed class Turn
        {
            private int _webNudges;
            private bool _accessNudged;

            /// <summary>
            /// Uwaga dla modelu albo null, gdy odpowiedź może zostać. <paramref name="hasWeb"/> — sesja ma wyszukiwanie;
            /// <paramref name="canAskAccess"/> — program umie zapytać o dostęp do folderu.
            /// </summary>
            public string Check(string answer, IReadOnlyList<ExecutedCall> calls, bool hasWeb, bool wantsWeb, bool canAskAccess)
            {
                if (!LooksLikeGivingUp(answer))
                {
                    return null;
                }

                var phrases = WebPhrases(calls);
                var searched = phrases.Count > 0 || calls.Any(c => c.Tool == BuiltInTools.WebSearchName);
                if (hasWeb && (searched || wantsWeb) && phrases.Count < MinWebSearches && _webNudges < MaxWebNudges)
                {
                    _webNudges++;
                    return WebRetryText(phrases);
                }

                var refused = calls.Any(c => !c.Success && (c.Result ?? "").StartsWith("ODRZUCONE", StringComparison.Ordinal));
                if (canAskAccess && !_accessNudged && !refused && LooksLikeNoAccess(answer))
                {
                    _accessNudged = true;
                    return AccessRetryText;
                }

                return null;
            }
        }
    }
}
