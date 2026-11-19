using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Session
{
    /// <summary>
    /// Umiejętność, którą gospodarz może włączyć w trakcie rozmowy: nazwa, opis, polecenia (nazwa i opis) i dodatkowe słowa
    /// do wyszukiwania (np. początek karty). Do <see cref="ISkillCatalog"/>.
    /// </summary>
    public sealed class SkillCatalogEntry
    {
        /// <summary>Tworzy wpis.</summary>
        public SkillCatalogEntry(string name, string description, IReadOnlyList<KeyValuePair<string, string>> tools = null, string keywords = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? "";
            Tools = tools ?? new KeyValuePair<string, string>[0];
            Keywords = keywords ?? "";
        }

        /// <summary>Nazwa umiejętności (folder profilu), np. <c>pliki</c>.</summary>
        public string Name { get; }

        /// <summary>Opis z <c>profil.json</c>.</summary>
        public string Description { get; }

        /// <summary>Polecenia umiejętności: nazwa i opis; pusta lista = umiejętność samej wiedzy.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Tools { get; }

        /// <summary>Dodatkowe słowa do wyszukiwania (np. początek karty albo README).</summary>
        public string Keywords { get; }
    }

    /// <summary>
    /// Katalog umiejętności gospodarza, których sesja nie ma włączonych. Z nim sesja dostaje narzędzia
    /// <c>znajdz_umiejetnosc</c> i <c>wlacz_umiejetnosci</c> i zasadę: zamiast odpowiadać „nie mam narzędzia”, model szuka
    /// umiejętności, prosi o zgodę na włączenie, a gospodarz po zgodzie włącza ją i ponawia polecenie.
    /// Podaj tylko umiejętności, które ta sesja może dostać (widoczne dla użytkownika, z poświadczeniami, dozwolone dla modelu).
    /// </summary>
    public interface ISkillCatalog
    {
        /// <summary>Umiejętności do włączenia (bez już włączonych).</summary>
        IReadOnlyList<SkillCatalogEntry> Skills { get; }

        /// <summary>
        /// Pyta użytkownika (przycisk w rozmowie) o włączenie <paramref name="skills"/>; <paramref name="reason"/> — zdanie modelu
        /// dla użytkownika (dane, nie polecenie). True = zgoda: gospodarz włącza umiejętności po turze i ponawia polecenie.
        /// Bez kliknięcia użytkownika (np. brak okna) — false. Wołane z wątku w tle.
        /// </summary>
        Task<bool> RequestAsync(IReadOnlyList<string> skills, string reason, CancellationToken cancellationToken);
    }

    /// <summary>Wyszukiwanie w katalogu umiejętności i rozpoznanie odpowiedzi „nie mam narzędzia” (bez modelu).</summary>
    public static class SkillCatalog
    {
        /// <summary>Najwięcej umiejętności w wyniku <c>znajdz_umiejetnosc</c>.</summary>
        public const int MaxHits = 3;

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "jest", "się", "sie", "nie", "dla", "albo", "oraz", "który", "która", "które", "jak", "czy", "jaki", "jaka", "jakie", "tego",
            "ten", "tej", "tym", "przez", "przy", "pod", "nad", "bez", "można", "mozna", "mam", "masz", "trzeba", "chcę", "chce", "proszę",
            "prosze", "polecenie", "polecenia", "narzędzie", "narzedzie", "narzędzia", "narzedzia", "the", "and", "for", "with",
        };

        /// <summary>
        /// Najlepiej pasujące umiejętności do opisu zadania: słowa (≥ 3 znaki, porównanie po 5 pierwszych literach, bez polskich
        /// znaków) w nazwie umiejętności liczą się ×3, w nazwach poleceń ×2, w opisach i słowach kluczowych ×1. Bez trafień — pusta lista.
        /// </summary>
        public static IReadOnlyList<SkillCatalogEntry> Search(IEnumerable<SkillCatalogEntry> skills, string task, int max = MaxHits)
        {
            var stems = Stems(task).Distinct().ToList();
            if (stems.Count == 0 || skills == null)
            {
                return new SkillCatalogEntry[0];
            }

            return skills
                .Select((skill, index) => new { skill, index, score = Score(skill, stems) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.index)
                .Take(Math.Max(1, max))
                .Select(x => x.skill)
                .ToList();
        }

        /// <summary>Wynik <c>znajdz_umiejetnosc</c> dla modelu: umiejętności z opisem i poleceniami albo informacja, że nic nie pasuje.</summary>
        public static string Describe(IReadOnlyList<SkillCatalogEntry> hits, IEnumerable<SkillCatalogEntry> all)
        {
            var text = new StringBuilder();
            if (hits.Count == 0)
            {
                text.Append("Żadna niewłączona umiejętność nie pasuje do tego zadania.");
                var names = all?.Select(s => s.Name).ToList() ?? new List<string>();
                if (names.Count > 0)
                {
                    text.Append(" Są: ").Append(string.Join(", ", names)).Append(". Gdy któraś jednak pasuje — spróbuj innymi słowami; inaczej powiedz użytkownikowi, czego brakuje.");
                }

                return text.ToString();
            }

            text.Append("Pasujące umiejętności (jeszcze niewłączone) — gdy któraś ma potrzebne polecenie, wywołaj wlacz_umiejetnosci:");
            foreach (var skill in hits)
            {
                text.Append("\n- ").Append(skill.Name);
                if (skill.Description.Length > 0)
                {
                    text.Append(" — ").Append(Text.Shorten(skill.Description.Trim(), 300));
                }

                if (skill.Tools.Count > 0)
                {
                    text.Append("\n  polecenia: ").Append(string.Join("; ", skill.Tools.Take(12).Select(t => t.Key + (string.IsNullOrWhiteSpace(t.Value) ? "" : " (" + Text.Shorten(t.Value.Trim(), 90) + ")"))));
                    if (skill.Tools.Count > 12)
                    {
                        text.Append("; … i ").Append(skill.Tools.Count - 12).Append(" innych");
                    }
                }
                else
                {
                    text.Append("\n  (sama wiedza — bez poleceń)");
                }
            }

            return text.ToString();
        }

        private static readonly Regex MissingTool = new Regex(
            @"\b(nie\s+(mam|posiadam|dysponuj[eę])\s+(odpowiedniego\s+|takiego\s+|potrzebnego\s+|żadnego\s+|zadnego\s+)?(narz[eę]dzi[ae]?|polece[nń]|dost[eę]pu|mo[zż]liwo[sś]ci|funkcj)"
            + @"|nie\s+(potrafi[eę]|umiem|jestem\s+w\s+stanie|mog[eę])\s+(tego\s+|teraz\s+|samodzielnie\s+|bezpo[sś]rednio\s+)?(zrobi[cć]|wykona[cć]|sprawdzi[cć]|odczyta[cć]|otworzy[cć]|przeczyta[cć]|wy[sś]wietli[cć]|pokaza[cć]|wys[lł]a[cć]|zapisa[cć]|uruchomi[cć])"
            + @"|brak(uje)?\s+(mi\s+)?(narz[eę]dzi|polece[nń]|dost[eę]pu)"
            + @"|(narz[eę]dzia|polecenia)\s+nie\s+(pozwalaj|obejmuj)"
            + @"|nie\s+mam\s+jak\s+(to\s+)?(zrobi|sprawdzi|odczyta)"
            + @"|nie\s+mam\s+wgl[aą]du"
            + @"|nie\s+(otwieram|przegl[aą]dam|czytam|odczytuj[eę]|widz[eę]|obs[lł]uguj[eę]|wysy[lł]am|uruchamiam)\s+(\w+\s+)?(plik|folder|katalog|dysk|tw[oó]j|twoich|lokaln|komputer|program|poczt|maili|wiadomo[sś]ci|dokument|arkusz|model|stron))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Czy odpowiedź albo pytanie modelu to „nie mam narzędzia / nie potrafię tego zrobić / nie otwieram plików”. Wtedy sesja z katalogiem raz cofa model:
        /// najpierw <c>znajdz_umiejetnosc</c>, dopiero potem odpowiedź, że tego nie ma.
        /// </summary>
        public static bool LooksLikeMissingTool(string answer) => !string.IsNullOrWhiteSpace(answer) && MissingTool.IsMatch(answer);

        /// <summary>
        /// Uwaga programu dla modelu, który odpowiedział „nie potrafię”: program sam przeszukał katalog i podaje pasujące
        /// umiejętności z poleceniami — model ma wywołać <c>wlacz_umiejetnosci</c>.
        /// </summary>
        public static string NudgeFor(IReadOnlyList<SkillCatalogEntry> hits)
            => "Nie odpowiadaj, że nie masz narzędzia. Program przeszukał inne umiejętności Amadeusza i do tego polecenia pasuje:\n"
               + Describe(hits, hits).Substring(Describe(hits, hits).IndexOf('\n') + 1)
               + "\nWywołaj teraz wlacz_umiejetnosci: umiejetnosci = nazwa z tej listy, powod = jedno zdanie dla użytkownika, czego brakuje. "
               + "Użytkownik zobaczy przycisk zgody, a po zgodzie program sam ponowi polecenie.";

        private static readonly Regex Paths = new Regex(@"([a-zA-Z]:\\|\\\\)[^\s""'„”]*", RegexOptions.CultureInvariant);

        /// <summary>Tekst bez ścieżek (C:\…, \\serwer\…): nazwa folderu „…\Rfem” nie może wybrać umiejętności rfem.</summary>
        public static string WithoutPaths(string text) => Paths.Replace(text ?? "", " ");

        /// <summary>Dzieli tekst na rdzenie słów do porównania (małe litery, bez polskich znaków, 5 pierwszych liter).</summary>
        internal static IEnumerable<string> Stems(string text)
        {
            foreach (Match match in Regex.Matches((text ?? "").ToLowerInvariant(), @"[\p{L}\p{Nd}]+"))
            {
                foreach (var part in match.Value.Split('_'))
                {
                    if (part.Length < 3 || StopWords.Contains(part))
                    {
                        continue;
                    }

                    var plain = Plain(part);
                    yield return plain.Length <= 5 ? plain : plain.Substring(0, 5);
                }
            }
        }

        private static int Score(SkillCatalogEntry skill, List<string> stems)
        {
            var name = new HashSet<string>(Stems(skill.Name.Replace('_', ' ')));
            var tools = new HashSet<string>(skill.Tools.SelectMany(t => Stems(t.Key.Replace('_', ' '))));
            var text = new HashSet<string>(Stems(skill.Description + " " + string.Join(" ", skill.Tools.Select(t => t.Value)) + " " + skill.Keywords));
            var score = 0;
            foreach (var stem in stems)
            {
                score += name.Contains(stem) ? 3 : tools.Contains(stem) ? 2 : text.Contains(stem) ? 1 : 0;
            }

            return score;
        }

        private static string Plain(string text)
        {
            var normalized = text.Replace('ł', 'l').Normalize(NormalizationForm.FormD);
            var result = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }
    }
}
