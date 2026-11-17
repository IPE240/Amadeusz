using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace LokalneAI.App;

/// <summary>
/// Kontrola okna bez klikania (<c>--autotest</c>): zadaje pytanie, zapisuje zrzut okna i wynik
/// do folderu i zamyka program. Kody wyjścia: 0 = odpowiedź, 1 = błąd w odpowiedzi, 2 = wyjątek.
/// </summary>
internal static class AutoTest
{
    public static async Task RunAsync(MainWindow window, MainViewModel viewModel, StartupOptions options)
    {
        var outputDir = options.OutputDir ?? Path.Combine(Path.GetTempPath(), "LokalneAI-autotest");
        var exitCode = 0;
        try
        {
            Directory.CreateDirectory(outputDir);
            await viewModel.WhenInitialized;
            if (options.Harness is not null)
            {
                exitCode = await RunHarnessAsync(window, viewModel, options, outputDir);
                Application.Current.Shutdown(exitCode);
                return;
            }

            if (!string.IsNullOrWhiteSpace(options.AutoTestQuestion) && options.AutoTestQuestion != "-")
            {
                viewModel.Question = options.AutoTestQuestion;
                await viewModel.SendAsync();
            }

            window.SelectTab(options.Tab);

            // Niech okno dorysuje ostatnie zmiany przed zrzutem.
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            window.SavePng(Path.Combine(outputDir, "okno.png"));
            await window.SaveModelListPngAsync(Path.Combine(outputDir, "lista.png"));

            var last = viewModel.Exchanges.LastOrDefault();
            var report = new
            {
                pytanie = last?.Question,
                odpowiedz = last?.Answer,
                statystyki = last?.Stats,
                blad = last?.IsError ?? false,
                tokenyNaSekunde = last?.StatsData?.TokensPerSecond,
                udzialGpu = last?.StatsData?.GpuShare,
                ollama = viewModel.OllamaStatus,
                karta = viewModel.GpuText,
                liczy = viewModel.ComputeWhereText,
                model = viewModel.SelectedModel?.Key,
                modele = viewModel.Models.Select(m => (m.IsFavorite ? "★ " : "") + m.Key).ToList(),
                kolejka = viewModel.Network.QueueText,
                brama = viewModel.Network.GatewayText,
                gdzie = viewModel.Network.RemoteDescription,
                baner = viewModel.BannerText,
                status = viewModel.StatusText,
            };
            File.WriteAllText(
                Path.Combine(outputDir, "wynik.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }));
            exitCode = last?.IsError == true ? 1 : 0;
        }
        catch (Exception e)
        {
            // Kontrola ma zostawić ślad zamiast wisieć — każdy wyjątek trafia do pliku.
            exitCode = 2;
            try
            {
                File.WriteAllText(Path.Combine(outputDir, "wyjatek.txt"), e.ToString());
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            {
                // Nie da się zapisać nawet śladu — zostaje kod wyjścia 2.
            }
        }

        Application.Current.Shutdown(exitCode);
    }

    /// <summary>Kontrola zakładki Code: profil, polecenia po kolei, zrzut i zapis rozmowy.</summary>
    private static async Task<int> RunHarnessAsync(MainWindow window, MainViewModel viewModel, StartupOptions options, string outputDir)
    {
        var context = viewModel.CreateHarnessContext();
        // Kontrola sama zatwierdza zmiany, więc bez --foldery pracuje w pustym folderze tymczasowym, nie w C:\ODS.
        var roots = options.HarnessRoots;
        if (string.IsNullOrWhiteSpace(roots))
        {
            roots = Path.Combine(Path.GetTempPath(), "LokalneAI_kontrola");
            Directory.CreateDirectory(roots);
        }

        // Ustawienia użytkownika się nie zmienią: w kontroli okno ich nie zapisuje.
        var view = window.CodeView;
        window.SelectTab("code");
        view.AutoConfirm = true;
        var commands = (options.AutoTestQuestion ?? "").Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToList();
        var resume = commands.Remove("#wznow");
        view.ConfigureForTest(options.Harness!, roots, options.HarnessConnection, options.HarnessPerson, options.HarnessConversations, resume);
        if (commands.Remove("#zrodla"))
        {
            // Zrzut okna „Źródła wiedzy” wybranego profilu (bez zapisu).
            var sources = new SourcesWindow(options.Harness!, Path.Combine(context.ProfilesRoot, options.Harness!), Path.GetTempPath())
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -6000,
                Top = 0,
            };
            sources.Show();
            await sources.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Snapshot.Save((FrameworkElement)sources.Content, Path.Combine(outputDir, "zrodla.png"));
            sources.Close();
        }

        await view.RunAsync(commands); // przy --rozmowy każde polecenie zapisuje rozmowę, jak w zwykłym oknie
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(300);
        window.SavePng(Path.Combine(outputDir, "harness.png"));
        var entries = view.Panel.Entries.Select(e => new { rodzaj = e.Kind.ToString(), tekst = e.Text, szczegoly = e.Detail, ok = e.Success }).ToList();
        File.WriteAllText(Path.Combine(outputDir, "harness.json"), JsonSerializer.Serialize(new
        {
            profil = view.ProfileName,
            liczy = context.ComputeWhere,
            kontekst = view.Panel.Session?.Context?.ToString(),
            rozmowa = view.CurrentConversationPath,
            wpisy = entries,
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return entries.Any(e => e.rodzaj == "Error") ? 1 : 0;
    }
}
