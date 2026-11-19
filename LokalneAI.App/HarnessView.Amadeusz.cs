using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LokalneAI.Core.Storage;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Tools;

namespace LokalneAI.App;

/// <summary>
/// Umiejętność „amadeusz” z narzędziami: Amadeusz czyta swój stan, ustawienia (bez danych logowania), listę umiejętności i dzienniki,
/// a w rozmowie zakładki Code przełącza umiejętności albo profil — za zgodą jak każda zmiana, od następnego polecenia. Niczego innego
/// w sobie nie zmienia (docs\bezpieczenstwo.md). Umiejętność liczy tylko model lokalny albo Claude (profil.json: "chmura": ["anthropic"]),
/// więc dane Amadeusza nie trafiają do Gemini ani GPT (decyzja użytkownika z 2026-11-19).
/// </summary>
public partial class HarnessView
{
    /// <summary>Przełączenie zlecone narzędziem wybierz_umiejetnosci — robione po turze, żeby sesja nie zmieniała się w jej trakcie.</summary>
    private (List<string> Skills, WorkProfileSetting? Profile)? _pendingSkills;

    /// <summary>Umiejętność o samym Amadeuszu — wykonawca <see cref="SelfExecutor"/>, nie most Tekli.</summary>
    private static bool IsAmadeuszProfile(string? name) => name == "amadeusz";

    /// <summary>Po turze: zaznacza umiejętności albo profil wybrane przez model (jak kliknięcie na liście nad rozmową).</summary>
    private void ApplyPendingSkills()
    {
        if (_pendingSkills is not { } pending)
        {
            return;
        }

        _pendingSkills = null;
        if (pending.Profile is { } profile)
        {
            SelectWorkProfile(profile);
            return;
        }

        if (pending.Skills.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(SelectedProfiles.OrderBy(n => n, StringComparer.Ordinal)))
        {
            return;
        }

        _loading = true;
        foreach (var box in ProfilesPanel.Children.OfType<CheckBox>())
        {
            box.IsChecked = pending.Skills.Contains((string)box.Tag);
        }

        _loading = false;
        SkillsChanged();
    }

    /// <summary>Nazwa profilu (zestawu umiejętności) zaznaczonego na liście; null = własny wybór.</summary>
    private string? CurrentWorkProfileName() => (WorkProfileBox.SelectedItem as ComboBoxItem)?.Tag is WorkProfileSetting profile ? profile.Name : null;

    /// <summary>Stan Amadeusza dla modelu (narzędzie stan_amadeusza). W wątku okna.</summary>
    private string SelfStatus()
    {
        _context = _contextProvider?.Invoke() ?? _context;
        var settings = _context.Settings;
        var text = new StringBuilder();
        text.Append("Amadeusz ").Append(typeof(HarnessView).Assembly.GetName().Version?.ToString(3) ?? "?")
            .Append(" na komputerze ").Append(Environment.MachineName).Append(", użytkownik ").Append(Environment.UserName).AppendLine(".");
        text.Append("Model: ").Append(string.IsNullOrWhiteSpace(_context.ModelLabel) ? _context.Model : _context.ModelLabel)
            .Append(_context.Cloud is { } cloud ? " (w chmurze: " + cloud.Name + ")" : " (lokalny, kontekst " + _context.ContextLength + " tokenów)")
            .Append("; ").Append(string.IsNullOrWhiteSpace(_context.ComputeWhere) ? "liczy: ten komputer" : _context.ComputeWhere).AppendLine(".");
        text.Append("Brama: http://127.0.0.1:").Append(settings.GatewayPort)
            .Append("; udostępnianie innym komputerom: ").Append(settings.ShareOnLan ? "włączone" : "wyłączone")
            .Append("; limit kontekstu dla innych: ").Append(settings.MaxContextForOthers).AppendLine(".");
        text.Append("Internet dla umiejętności: ").AppendLine(settings.InternetAccess ? "odblokowany." : "zablokowany.");
        text.Append("Zakładka Code: umiejętności ").Append(SelectedProfiles.Count == 0 ? "(żadna)" : string.Join(", ", SelectedProfiles))
            .Append("; profil: ").Append(CurrentWorkProfileName() ?? "własny wybór")
            .Append("; uprawnienia: ").Append(_permission switch { "czyta" => "Tylko czyta", "bez" => "Bez pytania", _ => "Pyta o każdą zmianę" }).AppendLine(".");
        var (roots, work) = FileRoots();
        text.Append("Dozwolone foldery: ").Append(string.Join("; ", roots.Select(Environment.ExpandEnvironmentVariables)))
            .Append("; folder bieżący: ").Append(work ?? "—").AppendLine(".");
        var machines = (settings.AgentsUseThisComputer ? new[] { "ten komputer" } : Array.Empty<string>())
            .Concat(settings.AgentMachines.Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Address : m.Name!)).ToList();
        text.Append("Komputery dla podagentów: ").AppendLine(machines.Count == 0 ? "brak." : string.Join(", ", machines) + ".");
        if (!string.IsNullOrWhiteSpace(_context.LocalGpuText))
        {
            text.Append("Karta graficzna tego komputera: ").Append(_context.LocalGpuText).AppendLine(".");
        }

        var models = _context.ModelChoices;
        text.Append("Modele na liście (").Append(models.Count).Append("): ")
            .Append(string.Join(", ", models.Take(25).Select(m => m.Display))).AppendLine(models.Count > 25 ? ", …" : ".");
        return text.ToString().TrimEnd();
    }

    /// <summary>Lista umiejętności i profili albo szczegóły jednej umiejętności (narzędzie umiejetnosci_amadeusza). W wątku okna.</summary>
    private ToolResult SelfSkills(string? name)
    {
        _context = _contextProvider?.Invoke() ?? _context;
        var boxes = ProfilesPanel.Children.OfType<CheckBox>().ToList();
        if (string.IsNullOrWhiteSpace(name))
        {
            var text = new StringBuilder("Umiejętności (✓ = zaznaczona nad rozmową):\n");
            foreach (var box in boxes)
            {
                var skill = (string)box.Tag;
                var folder = Path.Combine(_context.ProfilesRoot, skill);
                var settings = CloudViewModel.SafeSettings(folder);
                text.Append(box.IsChecked == true ? "✓ " : "- ").Append(skill)
                    .Append(box.Visibility == Visibility.Visible ? "" : " (niedostępna tutaj: brak poświadczenia)")
                    .Append(" — ").Append(Shorten(CloudViewModel.SafeDescription(folder) ?? "", 170))
                    .Append(" [").Append(HarnessProfile.HasHostTools(folder) ? "polecenia" : "sama wiedza")
                    .Append(settings?.Internet == true ? ", internet" : "")
                    .Append(", ").Append(DescribeCloud(settings)).AppendLine("]");
            }

            var available = AvailableSkills;
            var current = CurrentWorkProfileName();
            text.AppendLine().AppendLine("Profile (zestawy umiejętności, ✓ = wybrany):");
            foreach (var profile in _workProfiles)
            {
                var skills = SkillsOf(profile, available);
                text.Append(profile.Name == current ? "✓ " : "- ").Append(profile.Name).Append(": ")
                    .AppendLine(skills.Count == 0 ? "(żadnej z jego umiejętności nie ma tutaj)" : string.Join(", ", skills));
            }

            return ToolResult.Ok(text.ToString().TrimEnd());
        }

        var match = boxes.Select(b => (string)b.Tag).FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return ToolResult.Error("Nie ma umiejętności „" + name.Trim() + "”. Są: " + string.Join(", ", boxes.Select(b => (string)b.Tag)) + ".");
        }

        var skillFolder = Path.Combine(_context.ProfilesRoot, match);
        var skillSettings = CloudViewModel.SafeSettings(skillFolder);
        var details = new StringBuilder();
        details.Append("Umiejętność ").Append(match).Append(SelectedProfiles.Contains(match) ? " (zaznaczona)" : " (niezaznaczona)").AppendLine(":");
        details.AppendLine(CloudViewModel.SafeDescription(skillFolder) ?? "(bez opisu)");
        details.Append("Internet: ").Append(skillSettings?.Internet == true ? "tak, gdy odblokowany" : "nie")
            .Append("; ").Append(DescribeCloud(skillSettings))
            .Append("; najwięcej kroków: ").Append(skillSettings?.MaxSteps ?? 0).AppendLine(".");
        var tools = ToolSummaries(skillFolder);
        details.Append("Polecenia: ").AppendLine(tools.Count == 0 ? "brak (sama wiedza)." : "");
        foreach (var tool in tools)
        {
            details.Append("- ").AppendLine(tool);
        }

        if (skillSettings is { BuiltInTools.Count: > 0 })
        {
            details.Append("Narzędzia wbudowane: ").Append(string.Join(", ", skillSettings.BuiltInTools)).AppendLine(".");
        }

        var knowledge = Path.Combine(skillFolder, "wiedza");
        if (Directory.Exists(knowledge))
        {
            details.Append("Pliki wiedzy: ").Append(Directory.GetFiles(knowledge, "*.md", SearchOption.AllDirectories).Length).AppendLine(".");
        }

        var system = Path.Combine(skillFolder, "system.md");
        if (File.Exists(system))
        {
            details.AppendLine("Zasady (system.md):").AppendLine(Shorten(File.ReadAllText(system, Encoding.UTF8).Trim(), 2500));
        }

        return ToolResult.Ok(details.ToString().TrimEnd());
    }

    /// <summary>Polecenia umiejętności z <c>narzedzia.json</c>: nazwa, czy zmienia, krótki opis.</summary>
    private static List<string> ToolSummaries(string folder)
    {
        var result = new List<string>();
        var file = Path.Combine(folder, "narzedzia.json");
        if (!File.Exists(file))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
            if (document.RootElement.TryGetProperty("narzedzia", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray())
                {
                    var toolName = tool.TryGetProperty("nazwa", out var n) ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(toolName))
                    {
                        continue;
                    }

                    var changes = tool.TryGetProperty("zmienia_model", out var z) && z.ValueKind == JsonValueKind.True;
                    var description = tool.TryGetProperty("opis", out var o) ? o.GetString() ?? "" : "";
                    result.Add(toolName + (changes ? " (zmienia)" : "") + " — " + Shorten(description, 140));
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            result.Add("(nie udało się odczytać narzedzia.json: " + e.Message + ")");
        }

        return result;
    }

    private static string DescribeCloud(ProfileSettings? settings)
        => settings is null || settings.CloudProviders.Count == 0
            ? "tylko modele lokalne"
            : "chmura: " + string.Join(", ", settings.CloudProviders.Select(c => LokalneAI.Harness.Llm.CloudProvider.Find(c)?.Family ?? c));

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length].TrimEnd() + "…";

    /// <summary>
    /// Narzędzie wybierz_umiejetnosci: sprawdza nazwy (tylko umiejętności z listy) albo profil i zapamiętuje wybór do wykonania po turze.
    /// W wątku okna.
    /// </summary>
    private ToolResult RequestSkills(string? skillsText, string? mode, string? profileName)
    {
        var available = AvailableSkills;
        var order = ProfilesPanel.Children.OfType<CheckBox>().Select(c => (string)c.Tag).Where(available.Contains).ToList();
        var before = SelectedProfiles;
        var was = before.Count == 0 ? "żadna" : string.Join(", ", before);
        const string Finish = " Zakończ teraz krótką odpowiedzią — nowe polecenia i wiedza będą od następnej wiadomości, rozmowa trwa dalej.";
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var profile = _workProfiles.FirstOrDefault(p => string.Equals(p.Name, profileName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                return ToolResult.Error("Nie ma profilu „" + profileName.Trim() + "”. Są: " + string.Join(", ", _workProfiles.Select(p => p.Name)) + ".");
            }

            var skills = SkillsOf(profile, available);
            if (skills.Count == 0)
            {
                return ToolResult.Error("Żadnej umiejętności profilu „" + profile.Name + "” nie ma na tym komputerze (" + string.Join(", ", profile.Skills) + ").");
            }

            _pendingSkills = (skills, profile);
            return ToolResult.Ok("Od następnego polecenia profil „" + profile.Name + "”: " + string.Join(", ", skills) + " (było: " + was + ")." + Finish);
        }

        var names = SelfView.SplitNames(skillsText);
        if (names.Count == 0)
        {
            return ToolResult.Error("Podaj nazwy umiejętności w „umiejetnosci” (np. tekla, pliki) albo nazwę profilu w „profil”. Dostępne: " + string.Join(", ", order) + ".");
        }

        var resolved = new List<string>();
        var unknown = new List<string>();
        foreach (var name in names)
        {
            var match = order.FirstOrDefault(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                unknown.Add(name);
            }
            else
            {
                resolved.Add(match);
            }
        }

        if (unknown.Count > 0)
        {
            return ToolResult.Error("Nie ma umiejętności: " + string.Join(", ", unknown) + ". Dostępne: " + string.Join(", ", order) + ".");
        }

        var chosen = SelfView.ApplySkillMode(before, resolved, mode, order);
        if (chosen.Count == 0)
        {
            return ToolResult.Error("Nie zostałaby żadna umiejętność — zostaw co najmniej jedną.");
        }

        if (chosen.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(before.OrderBy(n => n, StringComparer.Ordinal)))
        {
            _pendingSkills = null;
            return ToolResult.Ok("Te umiejętności już są zaznaczone: " + string.Join(", ", chosen) + ".");
        }

        _pendingSkills = (chosen, null);
        return ToolResult.Ok("Od następnego polecenia zaznaczone będą: " + string.Join(", ", chosen) + " (było: " + was + ")." + Finish);
    }

    /// <summary>
    /// Wykonawca narzędzi umiejętności „amadeusz”. Czyta stan, ustawienia, umiejętności i dzienniki; przełącza umiejętności tylko
    /// w rozmowie zakładki Code (<paramref name="canSwitchSkills"/>; podagent i tablica — nie). Pola okna czyta w wątku okna.
    /// </summary>
    private sealed class SelfExecutor(HarnessView view, bool canSwitchSkills) : IToolExecutor
    {
        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            switch (call.Name)
            {
                case "stan_amadeusza":
                    return ToolResult.Ok(await view.Dispatcher.InvokeAsync(view.SelfStatus));

                case "ustawienia_amadeusza":
                    var fields = call.GetString("pola");
                    return ToolResult.Ok(await view.Dispatcher.InvokeAsync(() =>
                        SelfView.SettingsForModel((view._contextProvider?.Invoke() ?? view._context).Settings, fields)));

                case "umiejetnosci_amadeusza":
                    var name = call.GetString("nazwa");
                    return await view.Dispatcher.InvokeAsync(() => view.SelfSkills(name));

                case "dziennik_amadeusza":
                    var log = call.GetString("dziennik");
                    var lines = (int)Math.Clamp(call.GetNumber("linie") ?? 20, 1, 100);
                    var path = log == "bezpieczenstwo"
                        ? SafetyLog.Path
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokalneAI", "bledy.log");
                    return ToolResult.Ok(await Task.Run(() => SelfView.LogTail(path, lines), cancellationToken));

                case "wybierz_umiejetnosci":
                    if (!canSwitchSkills)
                    {
                        return ToolResult.Error("Umiejętności przełącza tylko rozmowa w zakładce Code (nie podagent ani tablica) — powiedz użytkownikowi, które zaznaczyć.");
                    }

                    var skills = call.GetString("umiejetnosci");
                    var mode = call.GetString("tryb");
                    var profile = call.GetString("profil");
                    return await view.Dispatcher.InvokeAsync(() => view.RequestSkills(skills, mode, profile));

                default:
                    return ToolResult.Error("Nieznane polecenie umiejętności amadeusz: " + call.Name);
            }
        }
    }
}
