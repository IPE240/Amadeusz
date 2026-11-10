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

    /// <summary>Wysyłać modelowi wcześniejsze pytania i odpowiedzi.</summary>
    public bool RememberConversation { get; set; }

    /// <summary>Zapisywać pytania i odpowiedzi w pliku historii na tym PC.</summary>
    public bool SaveHistory { get; set; } = true;

    /// <summary>Okno pół-harnessu: ostatnio wybrany profil.</summary>
    public string? HarnessProfile { get; set; }

    /// <summary>Okno pół-harnessu: folder wymiany mostu TeklaBridge.</summary>
    public string? TeklaExchangeFolder { get; set; }

    /// <summary>Okno pół-harnessu: plik JSON z danymi połączenia z bazą CDE (host, port, dbname, user, password).</summary>
    public string? CdeConnectionFile { get; set; }

    /// <summary>Baza CDE (Ustawienia → Baza CDE): adres serwera. Hasło jest osobno, zaszyfrowane (<see cref="CloudKeyStore"/>, klucz <c>cde</c>).</summary>
    public string CdeHost { get; set; } = "10.0.0.214";

    /// <summary>Baza CDE: port serwera PostgreSQL.</summary>
    public int CdePort { get; set; } = 5441;

    /// <summary>Baza CDE: nazwa bazy.</summary>
    public string CdeDatabase { get; set; } = "cde";

    /// <summary>Baza CDE: użytkownik PostgreSQL; null = jeszcze nie podano.</summary>
    public string? CdeUser { get; set; }

    /// <summary>Okno pół-harnessu: person_id pytającego w CDE (dla „moich” godzin).</summary>
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

    /// <summary>Rozwinięte sekcje zakładki Ustawienia (nazwy z okna). Null = domyślne okna.</summary>
    public List<string>? OpenSections { get; set; }

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

        if (GatewayPort is < 1024 or > 65535)
        {
            GatewayPort = DefaultGatewayPort;
        }

        if (!ContextChoices.Contains(MaxContextForOthers))
        {
            MaxContextForOthers = 16384;
        }

        CodePermission = CodePermission == "czyta" ? "czyta" : "pytaj";
        CdeHost = string.IsNullOrWhiteSpace(CdeHost) ? "10.0.0.214" : CdeHost.Trim();
        CdePort = CdePort is < 1 or > 65535 ? 5441 : CdePort;
        CdeDatabase = string.IsNullOrWhiteSpace(CdeDatabase) ? "cde" : CdeDatabase.Trim();
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

        return this;
    }
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
