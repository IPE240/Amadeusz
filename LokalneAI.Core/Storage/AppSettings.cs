using LokalneAI.Core.Ollama;

namespace LokalneAI.Core.Storage;

/// <summary>Ustawienia okna testowego, zapisywane na tym PC.</summary>
public sealed class AppSettings
{
    public const string DefaultModel = "SpeakLeash/bielik-11b-v3.0-instruct:Q4_K_M";

    public const string DefaultSystemPrompt = "Jesteś pomocnym asystentem. Odpowiadasz po polsku, zwięźle i rzeczowo.";

    /// <summary>Zalecana przez SpeakLeash temperatura Bielika (z jego pliku Modelfile).</summary>
    public const double DefaultTemperature = 0.1;

    public const int DefaultContextLength = 8192;

    public static readonly IReadOnlyList<int> ContextChoices = [2048, 4096, 8192, 16384, 32768];

    public const int DefaultGatewayPort = 11435;

    public string OllamaUrl { get; set; } = OllamaClient.DefaultUrl;

    public string Model { get; set; } = DefaultModel;

    public string SystemPrompt { get; set; } = DefaultSystemPrompt;

    public double Temperature { get; set; } = DefaultTemperature;

    public int ContextLength { get; set; } = DefaultContextLength;

    /// <summary>
    /// Kontekst zapytań w zakładce Code (lista „Kontekst” nad rozmową); 0 = jak w Czacie, ale co najmniej 16 384. Przy liczeniu na innym
    /// komputerze i tak nie więcej, niż on przyjmuje („kontekst do” w jego Ustawieniach → Udostępnianie).
    /// </summary>
    public int CodeContextLength { get; set; }

    /// <summary>Wysyłać modelowi wcześniejsze pytania i odpowiedzi.</summary>
    public bool RememberConversation { get; set; }

    /// <summary>Zapisywać pytania i odpowiedzi w pliku historii na tym PC.</summary>
    public bool SaveHistory { get; set; } = true;

    /// <summary>Okno pół-harnessu: ostatnio wybrany profil (do wersji 0.4; teraz <see cref="HarnessProfiles"/>).</summary>
    public string? HarnessProfile { get; set; }

    /// <summary>Zakładka Code: zaznaczone profile (kilka naraz). Null = jeszcze nie zapisano (wtedy <see cref="HarnessProfile"/>).</summary>
    public List<string>? HarnessProfiles { get; set; }

    /// <summary>
    /// Zakładka Code: profile — nazwane zestawy umiejętności. Wybór profilu (jednokrotny) zaznacza jego umiejętności,
    /// które potem można zmienić ręcznie. Null = jeszcze nie zapisano (wtedy <see cref="WorkProfileSetting.Defaults"/>).
    /// </summary>
    public List<WorkProfileSetting>? WorkProfiles { get; set; }

    /// <summary>
    /// Wiedza umiejętności z dysku (nazwa umiejętności → pliki albo foldery), np. wspólny glosariusz biura na serwerze.
    /// Tylko w ustawieniach tego komputera — adresy serwerów nie trafiają do repozytorium. Zob. <see cref="DiskKnowledge"/>.
    /// </summary>
    public Dictionary<string, List<string>>? DiskKnowledge { get; set; }

    /// <summary>Umiejętności „zawsze włączone” (<see cref="WorkProfileSetting.AlwaysOn"/>) już raz dopisane do profili — drugi raz program ich nie dopisze.</summary>
    public List<string> AddedDefaultSkills { get; set; } = [];

    /// <summary>
    /// Raz na umiejętność: dopisuje nowe umiejętności „zawsze włączone” (np. formatowanie) do wszystkich zapisanych profili
    /// i do bieżącego wyboru w zakładce Code. Potem użytkownik może je odznaczyć — nie wrócą. Zwraca, czy coś zmieniło.
    /// </summary>
    public bool AddDefaultSkills(IEnumerable<string> availableSkills)
    {
        var available = availableSkills.ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var skill in WorkProfileSetting.AlwaysOn.Where(available.Contains).Where(s => !AddedDefaultSkills.Contains(s)))
        {
            foreach (var profile in WorkProfiles ?? [])
            {
                if (!profile.Skills.Contains(skill))
                {
                    profile.Skills.Add(skill);
                }
            }

            if (HarnessProfiles is { Count: > 0 } && !HarnessProfiles.Contains(skill))
            {
                HarnessProfiles.Add(skill);
            }

            AddedDefaultSkills.Add(skill);
            changed = true;
        }

        return changed;
    }

    /// <summary>Zakładka Code: grupy rozmów (foldery). Same wskazania — rozmowy leżą w <c>&lt;folder&gt;\.amadeusz\</c>.</summary>
    public List<CodeGroupSetting> CodeGroups { get; set; } = [];

    /// <summary>Folder grupy „Niezgrupowane”; null = <see cref="CodeConversationStore.DefaultUngroupedFolder"/> (Dokumenty\Amadeusz).</summary>
    public string? CodeUngroupedFolder { get; set; }

    /// <summary>Grupa „Niezgrupowane” zwinięta w panelu rozmów.</summary>
    public bool CodeUngroupedCollapsed { get; set; }

    /// <summary>Folder grupy, w której ostatnio zaczynano rozmowę; null = „Niezgrupowane”.</summary>
    public string? CodeLastGroup { get; set; }

    /// <summary>Okno pół-harnessu: folder wymiany mostu TeklaBridge.</summary>
    public string? TeklaExchangeFolder { get; set; }

    // Dawne dane bazy (do wersji 0.4). Program przy starcie przenosi je do Menedżera poświadczeń Windows
    // (CredentialVault, wpis PostgreSQL) i czyści — nowe wersje niczego tu nie zapisują i nie mają wartości domyślnych.

    /// <summary>Dawne: plik JSON z danymi połączenia (przenoszony do Menedżera poświadczeń).</summary>
    public string? CdeConnectionFile { get; set; }

    /// <summary>Dawne: adres serwera bazy (przenoszony do Menedżera poświadczeń).</summary>
    public string? CdeHost { get; set; }

    /// <summary>Dawne: port serwera bazy.</summary>
    public int? CdePort { get; set; }

    /// <summary>Dawne: nazwa bazy.</summary>
    public string? CdeDatabase { get; set; }

    /// <summary>Dawne: użytkownik bazy.</summary>
    public string? CdeUser { get; set; }

    /// <summary>Dawne: identyfikator pytającego w bazie.</summary>
    public string? CdePersonId { get; set; }

    /// <summary>Okno pół-harnessu, profil „pliki”: dozwolone foldery rozdzielone średnikiem.</summary>
    public string? FileRoots { get; set; }

    /// <summary>Zakładka Code: „czyta” (zmiany odrzucane) albo „pytaj” (zgoda na każdą zmianę). „Bez pytania” nie jest zapisywane.</summary>
    public string CodePermission { get; set; } = "pytaj";

    /// <summary>Port bramy LokalneAI (kolejka i sieć), ten sam na wszystkich komputerach biura.</summary>
    public int GatewayPort { get; set; } = DefaultGatewayPort;

    /// <summary>Gdzie liczyć: adres bramy innego komputera, np. <c>http://PC-ODS12:11435</c>. Null = ten komputer.</summary>
    public string? ComputeOn { get; set; }

    /// <summary>Nazwa komputera z <see cref="ComputeOn"/> (z jego odpowiedzi), do okna.</summary>
    public string? ComputeOnName { get; set; }

    /// <summary>Ostatnio wpisany adres innego komputera (zostaje także po powrocie na „ten komputer”).</summary>
    public string? RemoteAddress { get; set; }

    /// <summary>Komputery dla podagentów zakładki Code (Ustawienia → Gdzie liczyć → Komputery dla podagentów), bez tego komputera.</summary>
    public List<AgentMachineSetting> AgentMachines { get; set; } = [];

    /// <summary>Podagenci mogą liczyć także na tym komputerze (jego karcie), niezależnie od „Gdzie liczyć”.</summary>
    public bool AgentsUseThisComputer { get; set; } = true;

    /// <summary>Zakładka Code: pokazuj wpisy „Krok N” (co model zwrócił w każdym kroku). Przełącznik nad rozmową i w Ustawieniach → Czat.</summary>
    public bool ShowSteps { get; set; }

    /// <summary>
    /// Zakładka Whiteboard: sesja tablicy dostaje też umiejętności zaznaczone w zakładce Code (domyślnie nie — sama tablica,
    /// krótszy prompt i mniej pomyłek małych modeli).
    /// </summary>
    public bool WhiteboardCodeSkills { get; set; }

    /// <summary>Pozwalaj na przeliczanie zapytań z innych komputerów tej samej podsieci.</summary>
    public bool ShareOnLan { get; set; }

    /// <summary>Największy kontekst dla zapytań z innych komputerów.</summary>
    public int MaxContextForOthers { get; set; } = 16384;

    /// <summary>Dostawca wybranego modelu (<c>anthropic</c>, <c>openai</c>, <c>google</c>); null = model z Ollamy.</summary>
    public string? ModelProvider { get; set; }

    /// <summary>Modele w chmurze dodane w Ustawieniach (pokazywane na liście modeli). Klucze API są osobno, zaszyfrowane.</summary>
    public List<CloudModelChoice> CloudModels { get; set; } = [];

    /// <summary>Ulubione modele (gwiazdka na liście): klucz <see cref="ModelKey"/>. Ulubione są na górze listy.</summary>
    public List<string> FavoriteModels { get; set; } = [];

    /// <summary>„Odblokuj dostęp do internetu”: czat i zakładka Code (w profilach, które pozwalają) szukają w sieci i czytają strony.</summary>
    public bool InternetAccess { get; set; }

    /// <summary>
    /// Zakładka Code: tokeny i czasy przy każdym wpisie rozmowy (dokładne liczby z Ollamy; przy włączonym — jedno dodatkowe
    /// zapytanie na sesję liczy tokeny samych zasad i narzędzi). Do nauki i diagnostyki, domyślnie wyłączone.
    /// </summary>
    public bool ShowTokens { get; set; }

    /// <summary>Rozwinięte sekcje zakładki Ustawienia (nazwy z okna). Null = domyślne okna.</summary>
    public List<string>? OpenSections { get; set; }

    // OCR (Ustawienia → OCR): Tesseract skryptem, bez modelu. Używane przez umiejętności „ocr” i „office” (PDF) oraz przy
    // wklejaniu obrazka do pola rozmowy.

    /// <summary>Języki Tesseracta, np. <c>pol+eng</c>.</summary>
    public string OcrLanguages { get; set; } = "pol+eng";

    /// <summary>Obrót strony przed OCR: <c>auto</c> (wykrywanie), <c>0</c>, <c>90</c>, <c>180</c>, <c>270</c>.</summary>
    public string OcrRotation { get; set; } = "auto";

    /// <summary>Układ strony: <c>auto</c>, <c>tabela</c> (zachowane odstępy kolumn), <c>kolumna</c>, <c>luzny</c>.</summary>
    public string OcrLayout { get; set; } = "auto";

    /// <summary>Próg pewności słowa 0–100 (słowa poniżej są pomijane); 0 = bez progu.</summary>
    public int OcrMinConfidence { get; set; }

    /// <summary>Reguły ignorowania linii: wyrażenia regularne, po jednym w linii.</summary>
    public string? OcrIgnorePatterns { get; set; }

    /// <summary>Rozdzielczość renderowania stron PDF do OCR (dpi).</summary>
    public int OcrDpi { get; set; } = 300;

    /// <summary>Gdy strona PDF nie ma tekstu, <c>pdf_czytaj</c> robi na niej OCR.</summary>
    public bool OcrWhenNoText { get; set; } = true;

    /// <summary>Folder z danymi języków (<c>*.traineddata</c>); null = <c>%LOCALAPPDATA%\LokalneAI\tessdata</c>.</summary>
    public string? OcrTessdataFolder { get; set; }

    /// <summary>Obrazek wklejony do pola rozmowy (Code, Czat) jest od razu rozpoznawany, a tekst trafia do pola.</summary>
    public bool OcrOnPaste { get; set; } = true;

    /// <summary>Klucz modelu do ulubionych: sama nazwa dla Ollamy, „dostawca|id” dla chmury.</summary>
    public static string ModelKey(string? provider, string model)
        => string.IsNullOrEmpty(provider) ? model : provider + "|" + model;

    /// <summary>Poprawia wartości spoza zakresu (np. po ręcznej edycji pliku).</summary>
    public AppSettings Normalize()
    {
        OllamaUrl = string.IsNullOrWhiteSpace(OllamaUrl) ? OllamaClient.DefaultUrl : OllamaUrl.Trim();
        Model = string.IsNullOrWhiteSpace(Model) ? DefaultModel : Model.Trim();
        SystemPrompt ??= "";
        if (double.IsNaN(Temperature) || Temperature < 0 || Temperature > 2)
        {
            Temperature = DefaultTemperature;
        }

        if (!ContextChoices.Contains(ContextLength))
        {
            ContextLength = DefaultContextLength;
        }

        if (CodeContextLength != 0 && !ContextChoices.Contains(CodeContextLength))
        {
            CodeContextLength = 0; // jak w Czacie
        }

        if (GatewayPort is < 1024 or > 65535)
        {
            GatewayPort = DefaultGatewayPort;
        }

        if (!ContextChoices.Contains(MaxContextForOthers))
        {
            MaxContextForOthers = 16384;
        }

        CodePermission = CodePermission == "czyta" ? "czyta" : "pytaj";
        CdeHost = string.IsNullOrWhiteSpace(CdeHost) ? null : CdeHost.Trim();
        CdePort = CdePort is < 1 or > 65535 ? null : CdePort;
        CdeDatabase = string.IsNullOrWhiteSpace(CdeDatabase) ? null : CdeDatabase.Trim();
        CdeUser = string.IsNullOrWhiteSpace(CdeUser) ? null : CdeUser.Trim();
        ModelProvider = string.IsNullOrWhiteSpace(ModelProvider) ? null : ModelProvider.Trim().ToLowerInvariant();
        CloudModels = (CloudModels ?? [])
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Provider) && !string.IsNullOrWhiteSpace(m.Id))
            .GroupBy(m => ModelKey(m.Provider.Trim().ToLowerInvariant(), m.Id.Trim()), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CloudModelChoice { Provider = g.First().Provider.Trim().ToLowerInvariant(), Id = g.First().Id.Trim(), Name = g.First().Name })
            .ToList();
        FavoriteModels = (FavoriteModels ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        OpenSections = OpenSections?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();
        ComputeOn = string.IsNullOrWhiteSpace(ComputeOn) ? null : ComputeOn.Trim();
        AgentMachines = (AgentMachines ?? [])
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Address))
            .GroupBy(m => m.Address.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new AgentMachineSetting { Address = g.Key, Name = string.IsNullOrWhiteSpace(g.First().Name) ? null : g.First().Name!.Trim() })
            .ToList();
        AddedDefaultSkills = (AddedDefaultSkills ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();
        HarnessProfiles = HarnessProfiles?.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToList();
        CodeGroups = (CodeGroups ?? [])
            .Where(g => g is not null && !string.IsNullOrWhiteSpace(g.Folder))
            .GroupBy(g => g.Folder.Trim().TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CodeGroupSetting
            {
                Folder = g.Key,
                Name = string.IsNullOrWhiteSpace(g.First().Name) ? Path.GetFileName(g.Key) : g.First().Name.Trim(),
                Collapsed = g.First().Collapsed,
            })
            .ToList();
        WorkProfiles = WorkProfiles?
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new WorkProfileSetting
            {
                Name = g.Key,
                Skills = (g.First().Skills ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToList(),
                Models = (g.First().Models ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            })
            .ToList();
        DiskKnowledge = DiskKnowledge?
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .Select(p => (Skill: p.Key.Trim(), Paths: (p.Value ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .Where(p => p.Paths.Count > 0)
            .GroupBy(p => p.Skill, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.SelectMany(p => p.Paths).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.Ordinal);
        CodeUngroupedFolder = string.IsNullOrWhiteSpace(CodeUngroupedFolder) ? null : CodeUngroupedFolder.Trim();
        CodeLastGroup = string.IsNullOrWhiteSpace(CodeLastGroup) ? null : CodeLastGroup.Trim();

        return this;
    }
}

/// <summary>Profil zakładki Code: nazwa i umiejętności (nazwy folderów z <c>profile\</c>), które zaznacza jego wybór.</summary>
public sealed class WorkProfileSetting
{
    public string Name { get; set; } = "";

    public List<string> Skills { get; set; } = [];

    /// <summary>
    /// Preferowane modele od najlepszego (klucze jak <see cref="AppSettings.ModelKey"/>: nazwa z Ollamy albo „dostawca|id”).
    /// Wybór profilu przełącza na pierwszy dostępny na maszynie, która liczy; gdy żadnego nie ma — zostaje bieżący model.
    /// </summary>
    public List<string> Models { get; set; } = [];

    /// <summary>
    /// Profile na pierwsze uruchomienie (potem lista żyje w ustawieniach: „Zapisz jako profil…”, „Usuń profil”).
    /// Umiejętności, których na danym komputerze nie ma (np. cde bez poświadczenia), są pomijane; profil bez żadnej jest ukryty.
    /// </summary>
    public static List<WorkProfileSetting> Defaults() =>
    [
        new() { Name = "Konstruktor", Skills = ["tekla", "mkk", "pliki", "formatowanie"] },
        // Zestaw Check Structures (repozytorium CS): pięć najczęściej pytanych umiejętności; cs_rysunki, cs_robot i rfem dokłada się ręcznie,
        // bo każda rola powiększa polecenie systemowe (patrz profile\cs_program\README.md).
        new() { Name = "Check Structures", Skills = ["cs_program", "cs_ifc", "cs_konstrukcja", "cs_polaczenia", "cs_statyka", "formatowanie"] },
        new() { Name = "Biuro", Skills = ["office", "tlumaczenie", "pliki", "formatowanie"] },
        new() { Name = "Baza CDE", Skills = ["cde", "formatowanie"] },
        new() { Name = "Komputer", Skills = ["komputer", "formatowanie"] },
    ];

    /// <summary>Umiejętności domyślnie we wszystkich profilach (dopisywane raz także do profili zapisanych wcześniej).</summary>
    public static IReadOnlyList<string> AlwaysOn { get; } = ["formatowanie"];
}

/// <summary>Model w chmurze dodany w Ustawieniach.</summary>
public sealed class CloudModelChoice
{
    /// <summary><c>anthropic</c>, <c>openai</c> albo <c>google</c>.</summary>
    public string Provider { get; set; } = "";

    /// <summary>Id modelu u dostawcy, np. <c>claude-sonnet-5-5</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Nazwa do okna (np. „Claude Sonnet 5.5”); null = id.</summary>
    public string? Name { get; set; }
}
