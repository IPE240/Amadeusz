using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Profiles
{
    /// <summary>Jak kończy się tura po udanym wykonaniu poleceń.</summary>
    public enum FinishMode
    {
        /// <summary>Model pisze odpowiedź po wynikach (np. pytania o dane — trzeba ubrać liczby w zdanie).</summary>
        Model,

        /// <summary>Biblioteka sama podsumowuje wykonane polecenia (szybciej — jedno wywołanie modelu mniej).</summary>
        Auto,

        /// <summary>
        /// Jak <see cref="Auto"/>, gdy w kroku były same polecenia zmieniające (zatwierdzone przez użytkownika);
        /// po poleceniach, które tylko czytają, model dostaje wyniki i decyduje dalej (np. „znajdź plik i otwórz go”).
        /// </summary>
        Mixed,
    }

    /// <summary>Ustawienia profilu z <c>profil.json</c>.</summary>
    public sealed class ProfileSettings
    {
        /// <summary>Jak kończy się tura (<c>zakonczenie</c>: <c>model</c>, <c>auto</c> albo <c>mieszane</c>).</summary>
        public FinishMode Finish { get; set; } = FinishMode.Model;

        /// <summary>Wbudowane narzędzia: <c>kalkulator</c>, <c>szukaj_wiedzy</c>.</summary>
        public IList<string> BuiltInTools { get; set; } = new List<string>();

        /// <summary>Sprawdzać, czy liczby w odpowiedzi pochodzą z wyników narzędzi.</summary>
        public bool CheckNumbers { get; set; }

        /// <summary>Narzędzia, których argument <c>sql</c> przechodzi przez strażnika SQL (tylko SELECT).</summary>
        public IList<string> SqlTools { get; set; } = new List<string>();

        /// <summary>Ile kroków modelu najwyżej na jedno polecenie użytkownika.</summary>
        public int MaxSteps { get; set; } = 6;

        /// <summary>Ile wywołań najwyżej w jednym kroku.</summary>
        public int MaxCallsPerStep { get; set; } = 4;

        /// <summary>Krótki plan przed wywołaniami (pomaga przy złożonych zdaniach, kosztuje czas).</summary>
        public bool Plan { get; set; }

        /// <summary>
        /// Argument <c>id</c> (albo kończący się na <c>_id</c>) równy 0 oznacza element zwrócony
        /// przez poprzednie wywołanie w tym samym kroku (tekst wyniku z <c>id=123</c>).
        /// </summary>
        public bool ReferencePreviousId { get; set; }

        /// <summary>Temperatura dla tego profilu; null = ustawienie klienta modelu.</summary>
        public double? Temperature { get; set; }

        /// <summary>Pełne opisy parametrów narzędzi w poleceniu systemowym (domyślnie zwięzła sygnatura — oszczędza kontekst).</summary>
        public bool FullToolDescriptions { get; set; }

        /// <summary>Reguły dla SQL: „jeśli zapytanie dotyczy X, musi zawierać Y” (np. v_my_hours → {{ja}}).</summary>
        public IList<SqlRule> SqlRules { get; set; } = new List<SqlRule>();

        /// <summary>Relacje, których SQL od modelu nie może czytać (np. tabele z danymi osobowymi).</summary>
        public IList<string> ForbiddenRelations { get; set; } = new List<string>();

        /// <summary>
        /// Dostawcy w chmurze dozwoleni w tym profilu (<c>chmura</c>), np. <c>["anthropic"]</c>. Pusta lista = tylko modele lokalne
        /// (ten komputer albo inny w sieci biura). Spoza listy <see cref="Llm.CloudProvider.All"/> nic nie przejdzie.
        /// </summary>
        public IList<string> CloudProviders { get; set; } = new List<string>();

        /// <summary>
        /// Czy profil pozwala na internet (<c>internet</c> w <c>profil.json</c>), gdy użytkownik go włączy. Brak pola = nie:
        /// np. w profilu z bazą danych model mógłby wysłać dane w zapytaniu do wyszukiwarki.
        /// </summary>
        public bool Internet { get; set; }

        /// <summary>
        /// Nazwa wpisu w Ustawieniach → Poświadczenia (<c>poswiadczenie</c> w <c>profil.json</c>), z którego profil bierze
        /// połączenie z bazą. Null = bez poświadczenia. Gospodarz może nie pokazywać profilu, dopóki takiego wpisu nie ma.
        /// </summary>
        public string Credential { get; set; }

        /// <summary>Czy model liczony przez <paramref name="cloudProviderId"/> wolno użyć (null = model lokalny, zawsze wolno).</summary>
        public bool AllowsCloud(string cloudProviderId)
        {
            if (cloudProviderId == null)
            {
                return true;
            }

            return Llm.CloudProvider.Find(cloudProviderId) != null
                && CloudProviders.Any(p => string.Equals(p?.Trim(), cloudProviderId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Reguła profilu dla SQL: gdy zapytanie zawiera <see cref="When"/>, musi zawierać <see cref="Requires"/>.</summary>
    public sealed class SqlRule
    {
        /// <summary>Fragment zapytania, który włącza regułę, np. <c>v_my_hours</c> (<c>jesli</c>).</summary>
        public string When { get; set; }

        /// <summary>Tekst, który wtedy musi być w zapytaniu, np. <c>{{ja}}</c> (<c>wymaga</c>).</summary>
        public string Requires { get; set; }

        /// <summary>Wyrażenie regularne, które musi pasować do zapytania (bez komentarzy), np. <c>person_id\s*=\s*'\{\{ja\}\}'</c>.</summary>
        public string RequiresPattern { get; set; }

        /// <summary>Komunikat dla modelu, gdy reguła nie jest spełniona (<c>komunikat</c>).</summary>
        public string Message { get; set; }
    }

    /// <summary>Jeden przykład few-shot: polecenie użytkownika i oczekiwany krok modelu.</summary>
    public sealed class FewShotExample
    {
        /// <summary>Stan przed poleceniem, np. „ostatnio wstawiony słup id=1001” (<c>stan</c>); null = brak.</summary>
        public string State { get; set; }

        /// <summary>Polecenie użytkownika (<c>uzytkownik</c>).</summary>
        public string User { get; set; }

        /// <summary>Zwarty JSON wywołań (tablica) albo null, gdy przykład to pytanie doprecyzowujące.</summary>
        public string CallsJson { get; set; }

        /// <summary>Pytanie doprecyzowujące, gdy model ma dopytać zamiast wykonywać (<c>pytanie</c>); inaczej null.</summary>
        public string Question { get; set; }
    }

    /// <summary>
    /// Profil = wiedza i narzędzia jednego zastosowania (np. Tekla albo baza CDE). Na dysku to folder:
    /// <c>system.md</c>, <c>karta.md</c>, <c>narzedzia.json</c>, <c>przyklady.jsonl</c>, <c>wiedza\*.md</c>, <c>profil.json</c>.
    /// </summary>
    public sealed class HarnessProfile
    {
        /// <summary>
        /// Profil złożony w kodzie, bez folderu. Zwykle wygodniej <see cref="Load"/>.
        /// Narzędzia wbudowane z <see cref="ProfileSettings.BuiltInTools"/> są dokładane do <paramref name="tools"/> same.
        /// </summary>
        public HarnessProfile(string name, string systemText, IEnumerable<ToolDefinition> tools, ProfileSettings settings = null,
            string cardText = null, IEnumerable<FewShotExample> examples = null, KnowledgeIndex knowledge = null, string folder = null,
            Glossary glossary = null)
        {
            Name = name ?? "profil";
            SystemText = systemText ?? "";
            CardText = cardText ?? "";
            Settings = settings ?? new ProfileSettings();
            Examples = (examples ?? Enumerable.Empty<FewShotExample>()).ToList();
            Knowledge = knowledge ?? new KnowledgeIndex();
            Glossary = glossary ?? new Glossary();
            Folder = folder;

            var all = new List<ToolDefinition>(tools ?? Enumerable.Empty<ToolDefinition>());
            foreach (var builtIn in Settings.BuiltInTools)
            {
                var definition = BuiltInTools.Get(builtIn);
                if (definition != null && all.All(t => t.Name != definition.Name))
                {
                    all.Add(definition);
                }
            }

            Tools = all;
        }

        /// <summary>
        /// Ten sam profil z narzędziami <c>szukaj_w_internecie</c> i <c>czytaj_strone</c> — gdy pozwala na internet
        /// (<see cref="ProfileSettings.Internet"/>); inaczej ten sam obiekt. Sesja potrzebuje jeszcze <see cref="Session.SessionOptions.Web"/>.
        /// </summary>
        public HarnessProfile WithInternet()
        {
            if (!Settings.Internet || Tools.Any(t => t.Name == BuiltInTools.WebSearchName))
            {
                return this;
            }

            var tools = Tools.Where(t => !t.IsBuiltIn || BuiltInTools.Get(t.Name) == null).ToList();
            tools.Add(BuiltInTools.Get(BuiltInTools.WebSearchName));
            tools.Add(BuiltInTools.Get(BuiltInTools.WebFetchName));
            return new HarnessProfile(Name, SystemText, tools, Settings, CardText, Examples, Knowledge, Folder, Glossary) { Description = Description, _parts = _parts };
        }

        private IReadOnlyList<HarnessProfile> _parts;

        /// <summary>Profile, z których złożono ten (<see cref="Combine"/>); dla zwykłego profilu — on sam.</summary>
        public IReadOnlyList<HarnessProfile> Parts => _parts ?? new[] { this };

        /// <summary>
        /// Łączy kilka profili w jeden do jednej sesji (np. <c>tekla</c> + <c>komputer</c>). Jeden profil = on sam.
        /// <list type="bullet">
        /// <item>Polecenie systemowe i karta: części kolejnych profili z nagłówkiem roli. Narzędzia: suma (przy tej samej nazwie
        /// wygrywa pierwszy profil). Przykłady: wszystkie. Wiedza: jeden indeks, źródła z przedrostkiem nazwy profilu.</item>
        /// <item>Zasady bezpieczeństwa — najostrzejsze: chmura tylko u dostawców dozwolonych we <b>wszystkich</b> profilach,
        /// internet tylko gdy pozwalają <b>wszystkie</b> (np. <c>cde</c> wyłącza go całej sesji), reguły i zakazy SQL — suma.</item>
        /// <item>Zakończenie tury: wspólne, gdy wszystkie mają takie samo; inaczej <see cref="FinishMode.Mixed"/>
        /// (koniec po zmianach, po samym czytaniu model decyduje dalej). Kroki i wywołania w kroku: największe.</item>
        /// </list>
        /// Polecenie systemowe rośnie o sumę części — więcej profili to dłuższy kontekst i trudniejszy wybór narzędzia dla małego modelu.
        /// </summary>
        public static HarnessProfile Combine(IEnumerable<HarnessProfile> profiles)
        {
            var list = (profiles ?? Enumerable.Empty<HarnessProfile>()).Where(p => p != null).SelectMany(p => p.Parts).Distinct().ToList();
            if (list.Count == 0)
            {
                throw new ArgumentException("Brak profili do połączenia.", nameof(profiles));
            }

            if (list.Count == 1)
            {
                return list[0];
            }

            var names = list.Select(p => p.Name).ToList();

            // Profil bez żadnych narzędzi (np. „formatowanie”) to nie rola, tylko zasady odpowiedzi doklejane na końcu;
            // nie zmienia zakończenia tury ani nie robi z jednej roli „kilku ról”.
            var roles = list.Where(p => p.Tools.Count > 0).ToList();
            var rules = list.Where(p => p.Tools.Count == 0).ToList();
            if (roles.Count == 0)
            {
                roles = list;
                rules = new List<HarnessProfile>();
            }

            var system = new StringBuilder();
            if (roles.Count == 1)
            {
                system.AppendLine(roles[0].SystemText.Trim());
            }
            else
            {
                system.AppendLine("Pracujesz naraz w kilku rolach (profile: " + string.Join(", ", roles.Select(p => p.Name)) + "). Każda sekcja „Rola” niżej opisuje jedną rolę;")
                    .AppendLine("jej zasady obowiązują przy jej narzędziach. Do polecenia wybierz rolę, której narzędzia do niego pasują; gdy nie wiesz którą — zapytaj.");
                foreach (var profile in roles)
                {
                    system.AppendLine().AppendLine("# Rola: " + profile.Name).AppendLine(profile.SystemText.Trim());
                }
            }

            foreach (var profile in rules)
            {
                system.AppendLine().AppendLine("# Zasady odpowiedzi (" + profile.Name + ")").AppendLine(profile.SystemText.Trim());
            }

            var card = new StringBuilder();
            foreach (var profile in list)
            {
                if (!string.IsNullOrWhiteSpace(profile.CardText))
                {
                    card.AppendLine("### " + profile.Name).AppendLine(profile.CardText.Trim()).AppendLine();
                }
            }

            var tools = new List<ToolDefinition>();
            foreach (var tool in list.SelectMany(p => p.Tools))
            {
                if (tools.All(t => t.Name != tool.Name))
                {
                    tools.Add(tool);
                }
            }

            var knowledge = new KnowledgeIndex();
            var glossary = new Glossary();
            foreach (var profile in list)
            {
                knowledge.AddFrom(profile.Knowledge, profile.Name);
                glossary.AddFrom(profile.Glossary);
            }

            var all = list.Select(p => p.Settings).ToList();
            var roleSettings = roles.Select(p => p.Settings).ToList();
            var finish = roleSettings.Select(s => s.Finish).Distinct().Count() == 1 ? roleSettings[0].Finish : FinishMode.Mixed;
            var temperatures = all.Where(s => s.Temperature.HasValue).Select(s => s.Temperature.Value).ToList();
            var settings = new ProfileSettings
            {
                Finish = finish,
                BuiltInTools = all.SelectMany(s => s.BuiltInTools).Distinct().ToList(),
                CheckNumbers = all.Any(s => s.CheckNumbers),
                SqlTools = all.SelectMany(s => s.SqlTools).Distinct().ToList(),
                MaxSteps = all.Max(s => s.MaxSteps),
                MaxCallsPerStep = all.Max(s => s.MaxCallsPerStep),
                Plan = all.Any(s => s.Plan),
                ReferencePreviousId = all.Any(s => s.ReferencePreviousId),
                Temperature = temperatures.Count > 0 ? temperatures.Min() : (double?)null,
                FullToolDescriptions = all.Any(s => s.FullToolDescriptions),
                SqlRules = all.SelectMany(s => s.SqlRules).ToList(),
                ForbiddenRelations = all.SelectMany(s => s.ForbiddenRelations).Distinct().ToList(),
                CloudProviders = all.Skip(1).Aggregate(
                    (IEnumerable<string>)all[0].CloudProviders.Select(c => c?.Trim().ToLowerInvariant()),
                    (allowed, s) => allowed.Intersect(s.CloudProviders.Select(c => c?.Trim().ToLowerInvariant()))).Where(c => !string.IsNullOrEmpty(c)).ToList(),
                Internet = all.All(s => s.Internet),
            };

            return new HarnessProfile(string.Join("+", names), system.ToString().TrimEnd(), tools, settings, card.ToString().TrimEnd(),
                list.SelectMany(p => p.Examples), knowledge, null, glossary)
            {
                Description = string.Join(" ", list.Select(p => p.Description).Where(d => !string.IsNullOrWhiteSpace(d))),
                _parts = list,
            };
        }

        /// <summary>Nazwa profilu: <c>nazwa</c> z <c>profil.json</c> albo nazwa folderu.</summary>
        public string Name { get; }

        /// <summary>Opis profilu (<c>opis</c> z <c>profil.json</c>), np. do listy profili w oknie.</summary>
        public string Description { get; set; } = "";

        /// <summary>Treść <c>system.md</c>: rola modelu i zasady.</summary>
        public string SystemText { get; }

        /// <summary>Treść <c>karta.md</c>, doklejana do każdego polecenia; pusta, gdy pliku nie ma.</summary>
        public string CardText { get; }

        /// <summary>Narzędzia z <c>narzedzia.json</c> i wbudowane z <c>narzedzia_wbudowane</c>.</summary>
        public IReadOnlyList<ToolDefinition> Tools { get; }

        /// <summary>Przykłady z <c>przyklady.jsonl</c>.</summary>
        public IReadOnlyList<FewShotExample> Examples { get; }

        /// <summary>
        /// Wyszukiwarka w <c>wiedza\</c> dla narzędzia <c>szukaj_wiedzy</c>. Indeks powstaje przy wczytaniu profilu,
        /// więc nowa wiedza działa w następnej sesji.
        /// </summary>
        public KnowledgeIndex Knowledge { get; }

        /// <summary>
        /// Glosariusz: pliki <c>wiedza\*.txt</c> (i <c>.tsv</c>, <c>.csv</c>) z parami terminów, jedna para w wierszu, oraz pliki dołożone
        /// przez <see cref="AddKnowledgeFile"/>. Sesja sama znajduje jego terminy w poleceniu i w wynikach narzędzi gospodarza
        /// i podaje modelowi odpowiedniki (<see cref="Glossary.BuildHint"/>). Pusty = profil bez glosariusza.
        /// </summary>
        public Glossary Glossary { get; }

        /// <summary>
        /// Dokłada plik spoza folderu profilu (np. wspólny glosariusz biura na dysku sieciowym): do wyszukiwarki
        /// <see cref="Knowledge"/>, a jeśli wygląda na glosariusz — także do <see cref="Glossary"/>. Wołać przed
        /// <see cref="Combine"/> i przed utworzeniem sesji. Zwraca, czy plik trafił do glosariusza.
        /// </summary>
        public bool AddKnowledgeFile(string path, string source = null)
        {
            var name = source ?? Path.GetFileName(path);
            var text = File.ReadAllText(path, Encoding.UTF8);
            Knowledge.Add(name, text);
            if (!Glossary.LooksLikeGlossary(text))
            {
                return false;
            }

            Glossary.Add(text, name);
            return true;
        }

        /// <summary>Ustawienia z <c>profil.json</c>.</summary>
        public ProfileSettings Settings { get; }

        /// <summary>Folder profilu na dysku (np. do szablonów); null dla profilu z kodu.</summary>
        public string Folder { get; }

        /// <summary>Narzędzie o tej nazwie albo null.</summary>
        public ToolDefinition FindTool(string name) => Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

        /// <summary>
        /// Czy profil w folderze ma własne polecenia gospodarza w <c>narzedzia.json</c>. <c>false</c> dla profilu samej wiedzy
        /// (pusta tablica <c>narzedzia</c>, np. <c>mkk</c>): model tylko szuka w dokumentacji i odpowiada, wykonawca nie jest potrzebny.
        /// Brak pliku albo błąd w nim = <c>false</c>; czyta sam plik narzędzi, bez indeksu wiedzy.
        /// </summary>
        public static bool HasHostTools(string folder)
        {
            try
            {
                return ReadTools(Path.Combine(folder ?? "", "narzedzia.json")).Count > 0;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is JsonException || e is KeyNotFoundException)
            {
                return false;
            }
        }

        /// <summary>Wczytuje profil z folderu. Brak <c>system.md</c> albo <c>narzedzia.json</c> to błąd.</summary>
        public static HarnessProfile Load(string folder)
        {
            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException("Nie ma folderu profilu: " + folder);
            }

            var settings = ReadSettings(Path.Combine(folder, "profil.json"), out var name, out var description);
            var system = ReadRequired(Path.Combine(folder, "system.md"));
            var card = ReadOptional(Path.Combine(folder, "karta.md"));
            var tools = ReadTools(Path.Combine(folder, "narzedzia.json"));
            var examples = ReadExamples(Path.Combine(folder, "przyklady.jsonl"));
            var knowledge = KnowledgeIndex.FromFolder(Path.Combine(folder, "wiedza"));
            var glossary = Glossary.FromFolder(Path.Combine(folder, "wiedza"));
            return new HarnessProfile(name ?? Path.GetFileName(folder.TrimEnd('\\', '/')), system, tools, settings, card, examples, knowledge, folder, glossary)
            {
                Description = description ?? "",
            };
        }

        internal static IList<ToolDefinition> ReadTools(string path)
        {
            var tools = new List<ToolDefinition>();
            using (var document = JsonDocument.Parse(ReadRequired(path)))
            {
                if (!document.RootElement.TryGetProperty("narzedzia", out var array) || array.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException(path + ": brak tablicy \"narzedzia\".");
                }

                foreach (var item in array.EnumerateArray())
                {
                    tools.Add(new ToolDefinition(
                        item.GetProperty("nazwa").GetString(),
                        item.TryGetProperty("opis", out var opis) ? opis.GetString() : "",
                        item.TryGetProperty("parametry", out var parametry) ? parametry.GetRawText() : null,
                        item.TryGetProperty("zmienia_model", out var zmienia) && zmienia.ValueKind == JsonValueKind.True,
                        item.TryGetProperty("szablon", out var szablon) && szablon.ValueKind == JsonValueKind.String ? szablon.GetString() : null)
                    {
                        FinishesTurn = item.TryGetProperty("konczy_ture", out var konczy) && konczy.ValueKind == JsonValueKind.True,
                    });
                }
            }

            return tools;
        }

        internal static IList<FewShotExample> ReadExamples(string path)
        {
            var examples = new List<FewShotExample>();
            if (!File.Exists(path))
            {
                return examples;
            }

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using (var document = JsonDocument.Parse(line))
                {
                    var root = document.RootElement;
                    var example = new FewShotExample
                    {
                        State = root.TryGetProperty("stan", out var stan) && stan.ValueKind == JsonValueKind.String ? stan.GetString() : null,
                        User = root.GetProperty("uzytkownik").GetString(),
                        Question = root.TryGetProperty("pytanie", out var pytanie) && pytanie.ValueKind == JsonValueKind.String ? pytanie.GetString() : null,
                    };
                    if (root.TryGetProperty("wywolania", out var calls) && calls.ValueKind == JsonValueKind.Array && calls.GetArrayLength() > 0)
                    {
                        example.CallsJson = Text.Compact(calls);
                    }

                    examples.Add(example);
                }
            }

            return examples;
        }

        /// <summary>
        /// Same ustawienia z <c>profil.json</c> (bez wiedzy i narzędzi) — szybko, np. żeby przed sesją sprawdzić pole <c>chmura</c>.
        /// Brak pliku = ustawienia domyślne.
        /// </summary>
        public static ProfileSettings LoadSettings(string folder)
            => ReadSettings(Path.Combine(folder ?? "", "profil.json"), out _, out _);

        private static ProfileSettings ReadSettings(string path, out string name, out string description)
        {
            var settings = new ProfileSettings();
            name = null;
            description = null;
            if (!File.Exists(path))
            {
                return settings;
            }

            using (var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8)))
            {
                var root = document.RootElement;
                name = GetString(root, "nazwa");
                description = GetString(root, "opis");
                var finish = GetString(root, "zakonczenie");
                if (finish == "auto")
                {
                    settings.Finish = FinishMode.Auto;
                }
                else if (finish == "mieszane")
                {
                    settings.Finish = FinishMode.Mixed;
                }

                settings.BuiltInTools = GetStrings(root, "narzedzia_wbudowane");
                settings.SqlTools = GetStrings(root, "narzedzia_sql");
                settings.CheckNumbers = GetBool(root, "sprawdzaj_liczby");
                settings.Plan = GetBool(root, "plan");
                settings.ReferencePreviousId = GetBool(root, "id_zero_to_poprzedni");
                settings.FullToolDescriptions = GetString(root, "opis_narzedzi") == "pelny";
                if (root.TryGetProperty("max_krokow", out var maxSteps) && maxSteps.TryGetInt32(out var steps))
                {
                    settings.MaxSteps = steps;
                }

                if (root.TryGetProperty("max_wywolan_w_kroku", out var maxCalls) && maxCalls.TryGetInt32(out var calls))
                {
                    settings.MaxCallsPerStep = calls;
                }

                if (root.TryGetProperty("temperatura", out var temperature) && temperature.TryGetDouble(out var t))
                {
                    settings.Temperature = t;
                }

                settings.ForbiddenRelations = GetStrings(root, "sql_zabronione");
                settings.CloudProviders = GetStrings(root, "chmura");
                settings.Internet = root.TryGetProperty("internet", out var internet) && internet.ValueKind == JsonValueKind.True;
                settings.Credential = GetString(root, "poswiadczenie");
                if (root.TryGetProperty("sql_reguly", out var rules) && rules.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rule in rules.EnumerateArray())
                    {
                        settings.SqlRules.Add(new SqlRule
                        {
                            When = GetString(rule, "jesli"),
                            Requires = GetString(rule, "wymaga"),
                            RequiresPattern = GetString(rule, "wymaga_wzorca"),
                            Message = GetString(rule, "komunikat"),
                        });
                    }
                }
            }

            return settings;
        }

        private static string GetString(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static bool GetBool(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        private static IList<string> GetStrings(JsonElement root, string name)
        {
            var list = new List<string>();
            if (root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    list.Add(item.GetString());
                }
            }

            return list;
        }

        private static string ReadRequired(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Brak pliku profilu: " + path, path);
            }

            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string ReadOptional(string path) => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
    }
}
