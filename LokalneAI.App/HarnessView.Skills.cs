using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Wpf;

namespace LokalneAI.App;

/// <summary>
/// Brakujące polecenia w zakładce Code: sesja główna dostaje katalog umiejętności, których nie ma zaznaczonych
/// (<see cref="ISkillCatalog"/>). Model zamiast „nie mam narzędzia” szuka w nim (<c>znajdz_umiejetnosc</c>) i prosi
/// o włączenie (<c>wlacz_umiejetnosci</c>) — w rozmowie pojawia się przycisk „Tak, włącz i ponów”. Po zgodzie umiejętności
/// dochodzą do zaznaczonych (<see cref="RequestSkills"/> w trybie „dodaj”, stosowane po turze przez <see cref="ApplyPendingSkills"/>),
/// a polecenie użytkownika idzie jeszcze raz, już z nowymi poleceniami. Najwyżej <see cref="MaxSkillRetries"/> ponowienia z rzędu.
/// </summary>
public partial class HarnessView
{
    /// <summary>Najwięcej automatycznych ponowień z rzędu (gdy model po włączeniu znowu prosi o kolejną umiejętność).</summary>
    private const int MaxSkillRetries = 2;

    /// <summary>Polecenie do ponowienia po turze, w której użytkownik zgodził się włączyć umiejętności; null = nic.</summary>
    private (string Command, List<string> Skills)? _retryCommand;

    /// <summary>Ile ponowień z rzędu (zeruje je tura bez prośby o umiejętność).</summary>
    private int _skillRetries;

    /// <summary>Trwające ponowienie (kontrola bez klikania czeka na nie w <see cref="RunAsync"/>).</summary>
    private Task? _retryRun;

    /// <summary>
    /// Katalog dla sesji głównej Code: umiejętności widoczne na liście (z poświadczeniem), niezaznaczone, nie same zasady
    /// i dozwolone dla bieżącego modelu (chmura — pole „chmura” w profil.json). Null, gdy nie ma czego włączyć. W wątku okna.
    /// </summary>
    private ISkillCatalog? CreateSkillCatalog(IEnumerable<string> active)
    {
        var on = new HashSet<string>(active, StringComparer.OrdinalIgnoreCase);
        var available = AvailableSkills;
        var cloud = _context.Cloud?.Id;
        var entries = new List<SkillCatalogEntry>();
        foreach (var name in ProfilesPanel.Children.OfType<CheckBox>().Select(c => (string)c.Tag))
        {
            if (on.Contains(name) || !available.Contains(name) || ProfilesFolder.Hidden.Contains(name, StringComparer.OrdinalIgnoreCase) || IsRuleOnly(name))
            {
                continue;
            }

            var folder = Path.Combine(_context.ProfilesRoot, name);
            if (CloudViewModel.SafeSettings(folder) is not { } settings || !settings.AllowsCloud(cloud))
            {
                continue; // np. amadeusz i cde przy GPT/Gemini — nowa sesja i tak by odmówiła
            }

            entries.Add(new SkillCatalogEntry(name, CloudViewModel.SafeDescription(folder) ?? "", ReadTools(folder), ReadCard(folder)));
        }

        return entries.Count == 0 ? null : new CodeSkillCatalog(this, entries);
    }

    /// <summary>Polecenia umiejętności z <c>narzedzia.json</c>: nazwa i opis.</summary>
    private static List<KeyValuePair<string, string>> ReadTools(string folder)
    {
        var result = new List<KeyValuePair<string, string>>();
        try
        {
            var file = Path.Combine(folder, "narzedzia.json");
            if (!File.Exists(file))
            {
                return result;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
            if (document.RootElement.TryGetProperty("narzedzia", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in tools.EnumerateArray())
                {
                    if (tool.TryGetProperty("nazwa", out var name) && name.GetString() is { Length: > 0 } toolName)
                    {
                        result.Add(new KeyValuePair<string, string>(toolName, tool.TryGetProperty("opis", out var opis) ? opis.GetString() ?? "" : ""));
                    }
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Uszkodzony plik — umiejętność zostaje w katalogu z samym opisem.
        }

        return result;
    }

    /// <summary>Początek karty umiejętności (pojęcia, nazwy programów) — dodatkowe słowa do wyszukiwania.</summary>
    private static string ReadCard(string folder)
    {
        try
        {
            var file = Path.Combine(folder, "karta.md");
            if (!File.Exists(file))
            {
                return "";
            }

            var text = File.ReadAllText(file, Encoding.UTF8);
            return text.Length <= 1500 ? text : text[..1500];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// Prośba modelu o włączenie: przycisk w rozmowie (kontrola bez klikania zatwierdza sama). Po zgodzie zaznacza umiejętności
    /// po turze i zapamiętuje polecenie do ponowienia. W wątku okna.
    /// </summary>
    private async Task<bool> AskToEnableSkillsAsync(IReadOnlyList<string> skills, string reason, CancellationToken cancellationToken)
    {
        var list = string.Join(", ", skills);
        var question = "Do tego polecenia potrzebna jest umiejętność " + list + ", której nie masz zaznaczonej."
            + (string.IsNullOrWhiteSpace(reason) ? "" : "\nAmadeusz: " + Shorten(reason.Trim(), 300))
            + "\nWłączyć ją (dojdzie do zaznaczonych) i ponowić polecenie?";
        var approved = AutoConfirm || await Panel.AskAsync(question, "Tak, włącz i ponów", "Nie", cancellationToken);
        if (!approved)
        {
            return false;
        }

        var result = RequestSkills(list, "dodaj", null);
        if (!result.Success)
        {
            Panel.AddInfo("Nie włączyłem: " + result.Text);
            return false;
        }

        var command = Panel.Entries.LastOrDefault(e => e.Kind == PanelEntryKind.User)?.Text;
        _retryCommand = string.IsNullOrWhiteSpace(command) ? null : (command, skills.ToList());
        return true;
    }

    /// <summary>
    /// Po turze (po <see cref="ApplyPendingSkills"/>, które już zaznaczyło umiejętności i przebudowało sesję): ponawia polecenie
    /// użytkownika z nową sesją i całą rozmową. Kolejne ponowienia z rzędu — najwyżej <see cref="MaxSkillRetries"/>.
    /// </summary>
    private void RetryAfterSkillSwitch()
    {
        if (_retryCommand is not { } retry)
        {
            _skillRetries = 0;
            return;
        }

        _retryCommand = null;
        if (++_skillRetries > MaxSkillRetries)
        {
            _skillRetries = 0;
            Panel.AddInfo("Umiejętności włączone (" + string.Join(", ", SelectedProfiles) + "), ale to już trzecie ponowienie z rzędu — wyślij polecenie jeszcze raz sam.");
            return;
        }

        var now = string.Join(", ", SelectedProfiles);
        var message = "Włączono umiejętność " + string.Join(", ", retry.Skills) + " (zaznaczone teraz: " + now + ") — masz jej polecenia. "
            + "Wykonaj teraz moje polecenie: " + retry.Command;
        _retryRun = Dispatcher.InvokeAsync(
            () => Panel.SendAsync(message, null, retry.Command, "Ponowione automatycznie po włączeniu umiejętności " + string.Join(", ", retry.Skills) + "."),
            DispatcherPriority.Background).Task.Unwrap();
    }

    /// <summary>Katalog sesji: wpisy na chwilę tworzenia sesji, pytanie o zgodę przez okno.</summary>
    private sealed class CodeSkillCatalog(HarnessView view, IReadOnlyList<SkillCatalogEntry> skills) : ISkillCatalog
    {
        public IReadOnlyList<SkillCatalogEntry> Skills { get; } = skills;

        public Task<bool> RequestAsync(IReadOnlyList<string> names, string reason, CancellationToken cancellationToken)
            => view.Dispatcher.InvokeAsync(() => view.AskToEnableSkillsAsync(names, reason, cancellationToken)).Task.Unwrap();
    }
}
