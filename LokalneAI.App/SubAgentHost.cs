using System.Net.Http;
using System.Globalization;
using LokalneAI.Core;
using LokalneAI.Core.Ollama;
using LokalneAI.Gateway;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Session;

namespace LokalneAI.App;

/// <summary>Co zakładka Code daje gospodarzowi podagentów: komputery, umiejętności, model główny i sposób budowy sesji.</summary>
internal sealed class SubAgentHostOptions
{
    /// <summary>Inne komputery z Ustawień → Gdzie liczyć → Komputery dla podagentów.</summary>
    public IReadOnlyList<AgentMachineSetting> Machines { get; init; } = [];

    /// <summary>Podagenci mogą liczyć na tym komputerze (jego karcie) — niezależnie od „Gdzie liczyć”.</summary>
    public bool UseThisComputer { get; init; } = true;

    /// <summary>Adres, którym zakładka Code łączy się z modelem (brama okna albo Ollama); dla „ten komputer” z nagłówkiem „przekazane”.</summary>
    public string LocalGatewayUrl { get; init; } = "";

    /// <summary>Ollama na tym komputerze (lista modeli i to, co w pamięci).</summary>
    public string LocalOllamaUrl { get; init; } = "";

    /// <summary>Pamięć karty tego komputera w GB; null = nieznana.</summary>
    public double? LocalGpuGb { get; init; }

    /// <summary>Opis karty tego komputera do polecenia systemowego, np. „NVIDIA GeForce RTX 4060 (8 GB)”.</summary>
    public string? LocalGpuText { get; init; }

    /// <summary>Umiejętności, które podagent może dostać (nazwa i opis).</summary>
    public IReadOnlyList<SubAgentSkill> Skills { get; init; } = [];

    /// <summary>Umiejętności, gdy model nie poda żadnych (zwykle te same, co sesja główna).</summary>
    public IReadOnlyList<string> DefaultSkills { get; init; } = [];

    /// <summary>Kontekst sesji głównej; podagent dostaje tyle albo mniej, gdy tamten komputer przyjmuje mniej.</summary>
    public int ContextLength { get; init; } = 16384;

    public double Temperature { get; init; } = 0.1;

    /// <summary>Modele od najlepszego (nazwy z Ollamy): model główny i preferowane profilu — podagent bierze pierwszy, który jest na komputerze i mieści się w karcie.</summary>
    public IReadOnlyList<string> PreferredModels { get; init; } = [];

    /// <summary>Buduje sesję podagenta (profil z umiejętności, wykonawcy, zgody) na podanym kliencie modelu; bez gospodarza podagentów.</summary>
    public Func<IReadOnlyList<string>, ILlmClient, Task<HarnessSession>> BuildSession { get; init; } = (_, _) => throw new InvalidOperationException("Brak BuildSession.");

    /// <summary>Jak długo wynik sprawdzenia komputera (karta, modele, kolejka) jest ważny.</summary>
    public TimeSpan ProbeLifetime { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Gospodarz podagentów zakładki Code (<see cref="ISubAgentHost"/>): na każde zlecenie sprawdza komputery z listy
/// (brama: karta, kolejka, limit kontekstu; Ollama: modele i co jest w pamięci), bierze najmniej obciążony, dobiera model
/// do jego karty (<see cref="ModelChooser"/>), buduje sesję podagenta i prowadzi ją przez <see cref="SubAgentRunner"/>.
/// „Ten komputer” liczy przez bramę okna z nagłówkiem „przekazane”, więc liczy jego własna karta, choćby „Gdzie liczyć”
/// wskazywało inny komputer.
/// </summary>
internal sealed class SubAgentHost : ISubAgentHost
{
    private readonly SubAgentHostOptions _options;
    private readonly List<Machine> _machines = [];
    private readonly SemaphoreSlim _pick = new(1, 1);

    public SubAgentHost(SubAgentHostOptions options)
    {
        _options = options;
        if (options.UseThisComputer)
        {
            _machines.Add(new Machine
            {
                Name = "ten komputer",
                Url = options.LocalGatewayUrl,
                OllamaUrl = options.LocalOllamaUrl,
                IsLocal = true,
                VramGb = options.LocalGpuGb,
                Gpu = options.LocalGpuText,
            });
        }

        foreach (var setting in options.Machines)
        {
            string url;
            try
            {
                url = GatewayServer.NormalizeUrl(setting.Address);
            }
            catch (ArgumentException)
            {
                continue;
            }

            _machines.Add(new Machine { Name = setting.Name ?? setting.Address, Url = url, OllamaUrl = url, IsLocal = false });
        }
    }

    /// <summary>Czy jest choć jeden komputer do liczenia.</summary>
    public bool HasMachines => _machines.Count > 0;

    public IReadOnlyList<SubAgentSkill> Skills => _options.Skills;

    public string Describe()
        => string.Join(", ", _machines.Select(m => m.Name + (string.IsNullOrWhiteSpace(m.Gpu) ? "" : " (" + m.Gpu + ")")));

    public async Task<SubAgentResult> RunAsync(SubAgentRequest request, Action<HarnessEvent> report, CancellationToken cancellationToken)
    {
        var skills = (request.Skills is { Count: > 0 } wanted ? wanted : _options.DefaultSkills)
            .Where(s => _options.Skills.Any(k => string.Equals(k.Name, s, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (skills.Count == 0)
        {
            return Failure("Podagent nie ma żadnej umiejętności do tego zadania. Dostępne: " + string.Join(", ", _options.Skills.Select(s => s.Name)) + ".");
        }

        Machine? machine;
        LocalModel? model;
        await _pick.WaitAsync(cancellationToken);
        try
        {
            machine = await PickMachineAsync(cancellationToken);
            if (machine is null)
            {
                return Failure("Żaden komputer dla podagentów nie odpowiada: " + string.Join("; ", _machines.Select(m => m.Name + " — " + (m.Error ?? "?"))) + ".");
            }

            model = ModelChooser.Choose(machine.Models, machine.Loaded, machine.VramGb, _options.ContextLength, _options.PreferredModels);
            if (model is null)
            {
                return Failure("Na komputerze " + machine.Name + " nie ma modelu do rozmowy (ollama pull …).");
            }

            Interlocked.Increment(ref machine.Active);
        }
        finally
        {
            _pick.Release();
        }

        var context = machine.MaxContext > 0 ? Math.Min(_options.ContextLength, machine.MaxContext) : _options.ContextLength;
        var llm = new OllamaLlmClient(new OllamaOptions
        {
            Url = machine.Url,
            Model = model.Name,
            ContextLength = context,
            Temperature = _options.Temperature,
            KeepAlive = "10m",
            ClientName = "Podagent " + request.Number.ToString(CultureInfo.InvariantCulture) + ": " + string.Join(", ", skills),
            RequestTimeout = TimeSpan.FromMinutes(10),
            // Inny komputer widzi w kolejce i licznikach tokenów nazwę tego PC, nie sam adres IP.
            Headers = machine.IsLocal
                ? new Dictionary<string, string> { [GatewayHeaders.Forwarded] = "1" }
                : new Dictionary<string, string> { [GatewayHeaders.Computer] = GatewayHeaders.Encode(Environment.MachineName) },
        });
        var info = new SubAgentInfo
        {
            Number = request.Number,
            Task = request.Task,
            Skills = string.Join(", ", skills),
            Machine = machine.Name,
            Model = ModelItem.ShortName(model.Name),
        };
        HarnessSession session;
        try
        {
            session = await _options.BuildSession(skills, llm);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Interlocked.Decrement(ref machine.Active);
            llm.Dispose();
            return Failure("Nie udało się przygotować podagenta (" + string.Join(", ", skills) + "): " + e.Message, machine.Name, info.Model);
        }

        try
        {
            return await SubAgentRunner.RunAsync(session, request, info, report, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref machine.Active);
            session.Dispose();
            llm.Dispose();
        }
    }

    private static SubAgentResult Failure(string text, string? machine = null, string? model = null)
        => new() { Success = false, Text = text, Machine = machine, Model = model };

    /// <summary>
    /// Najmniej obciążony komputer, który odpowiada: najpierw mniej podagentów od tego okna, potem krótsza kolejka bramy,
    /// potem większa karta. Sprawdzenia (brama, modele) są równoległe i ważne przez <see cref="SubAgentHostOptions.ProbeLifetime"/>.
    /// </summary>
    private async Task<Machine?> PickMachineAsync(CancellationToken cancellationToken)
    {
        var stale = _machines.Where(m => DateTime.UtcNow - m.Probed > _options.ProbeLifetime).ToList();
        await Task.WhenAll(stale.Select(m => ProbeAsync(m, cancellationToken)));
        return _machines
            .Where(m => m.Models.Count > 0)
            .OrderBy(m => m.Active)
            .ThenBy(m => m.QueueLength)
            .ThenByDescending(m => m.VramGb ?? 0)
            .FirstOrDefault();
    }

    private static async Task ProbeAsync(Machine machine, CancellationToken cancellationToken)
    {
        machine.Probed = DateTime.UtcNow;
        machine.Error = null;
        try
        {
            if (!machine.IsLocal)
            {
                var probe = await GatewayServer.ProbeAsync(machine.Url, TimeSpan.FromSeconds(4), null, cancellationToken);
                if (probe.Info is null)
                {
                    machine.Models = [];
                    machine.Error = probe.Error;
                    return;
                }

                if (!probe.Info.Sharing)
                {
                    machine.Models = [];
                    machine.Error = "nie udostępnia modelu innym komputerom";
                    return;
                }

                machine.Name = probe.Info.Computer;
                machine.Gpu = probe.Info.Gpu;
                machine.VramGb = ModelChooser.ParseGpuGb(probe.Info.Gpu);
                machine.MaxContext = probe.Info.MaxContext;
                machine.QueueLength = probe.Info.Running + probe.Info.Waiting;
            }

            using var ollama = new OllamaClient(machine.OllamaUrl) { ClientName = "Podagenci", MetadataTimeout = TimeSpan.FromSeconds(5) };
            machine.Models = (await ollama.ListModelsAsync(cancellationToken)).ToList();
            try
            {
                machine.Loaded = (await ollama.ListRunningAsync(cancellationToken)).ToList();
            }
            catch (Exception e) when (e is OllamaException or HttpRequestException or TaskCanceledException)
            {
                machine.Loaded = [];
            }

            if (machine.Models.Count == 0)
            {
                machine.Error = "brak modeli";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is OllamaException or HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException)
        {
            machine.Models = [];
            machine.Error = e.Message;
        }
    }

    /// <summary>Komputer z listy ze stanem z ostatniego sprawdzenia.</summary>
    private sealed class Machine
    {
        public string Name = "";

        /// <summary>Adres do zapytań modelu (brama tamtego komputera albo brama/Ollama tego).</summary>
        public string Url = "";

        /// <summary>Adres do listy modeli (dla tego komputera — jego Ollama, nie brama, która mogłaby wskazywać inny PC).</summary>
        public string OllamaUrl = "";

        public bool IsLocal;

        public string? Gpu;

        public double? VramGb;

        public int MaxContext;

        public int QueueLength;

        /// <summary>Ilu podagentów tego okna właśnie na nim liczy.</summary>
        public int Active;

        public List<LocalModel> Models = [];

        public List<RunningModel> Loaded = [];

        public DateTime Probed = DateTime.MinValue;

        public string? Error;
    }
}
