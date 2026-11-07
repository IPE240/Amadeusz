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
            window.SettingsExpander.IsExpanded = options.ExpandSettings;
            await viewModel.WhenInitialized;
            if (options.Harness is not null)
            {
                exitCode = await RunHarnessAsync(viewModel, options, outputDir);
                Application.Current.Shutdown(exitCode);
                return;
            }
            if (!string.IsNullOrWhiteSpace(options.AutoTestQuestion) && options.AutoTestQuestion != "-")
            {
                viewModel.Question = options.AutoTestQuestion;
                await viewModel.SendAsync();
            }

            // Niech okno dorysuje ostatnie zmiany przed zrzutem.
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            window.SavePng(Path.Combine(outputDir, "okno.png"));

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

    /// <summary>Kontrola okna pół-harnessu: profil, polecenia po kolei, zrzut i zapis rozmowy.</summary>
    private static async Task<int> RunHarnessAsync(MainViewModel viewModel, StartupOptions options, string outputDir)
    {
        var context = viewModel.CreateHarnessContext();
        // Kontrola nie zapisuje ustawień użytkownika — pracuje na kopii.
        var copy = JsonSerializer.Deserialize<LokalneAI.Core.Storage.AppSettings>(JsonSerializer.Serialize(context.Settings))!;
        copy.HarnessProfile = options.Harness;
        copy.CdeConnectionFile = options.HarnessConnection ?? "";
        copy.CdePersonId = options.HarnessPerson ?? "";
        // Kontrola sama zatwierdza zmiany, więc bez --foldery pracuje w pustym folderze tymczasowym, nie w C:\ODS.
        var roots = options.HarnessRoots;
        if (string.IsNullOrWhiteSpace(roots))
        {
            roots = Path.Combine(Path.GetTempPath(), "LokalneAI_kontrola");
            Directory.CreateDirectory(roots);
        }

        copy.FileRoots = roots;
        var window = new HarnessWindow(new HarnessContext
        {
            OllamaUrl = context.OllamaUrl,
            Model = context.Model,
            ContextLength = context.ContextLength,
            Temperature = context.Temperature,
            ProfilesRoot = context.ProfilesRoot,
            Settings = copy,
            Save = null,
        })
        {
            AutoConfirm = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -6000,
            Top = 0,
        };
        window.Show();
        var commands = (options.AutoTestQuestion ?? "").Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToList();
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

        await window.RunAsync(commands);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(300);
        window.SavePng(Path.Combine(outputDir, "harness.png"));
        var entries = window.Panel.Entries.Select(e => new { rodzaj = e.Kind.ToString(), tekst = e.Text, szczegoly = e.Detail, ok = e.Success }).ToList();
        File.WriteAllText(Path.Combine(outputDir, "harness.json"), JsonSerializer.Serialize(new
        {
            profil = window.ProfileName,
            kontekst = window.Panel.Session?.Context?.ToString(),
            wpisy = entries,
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        window.Close();
        return entries.Any(e => e.rodzaj == "Error") ? 1 : 0;
    }
}
