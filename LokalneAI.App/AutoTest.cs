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

            if (options.Tab is "whiteboard" or "tablica")
            {
                exitCode = await RunWhiteboardAsync(window, options, outputDir);
                Application.Current.Shutdown(exitCode);
                return;
            }

            if (options.AutoTestQuestion == "#ikona")
            {
                TrayIcon.SavePreview(Path.Combine(outputDir, "ikona.png")); // podgląd ikony w zasobniku
                options = options with { AutoTestQuestion = "-" };
            }

            // Czat w kontroli: rozmowy zapisują się tylko z --rozmowy <folder> (nigdy w Dokumenty\Amadeusz\Czat).
            window.ChatSidebar.ConfigureForTest(options.HarnessConversations);
            if (!string.IsNullOrWhiteSpace(options.AutoTestQuestion) && options.AutoTestQuestion != "-")
            {
                foreach (var question in options.AutoTestQuestion.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries).Select(q => q.Trim()))
                {
                    viewModel.Question = question;
                    await viewModel.SendAsync();
                }
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
        var inlineConsent = commands.Remove("#zgoda"); // pytanie o zgodę w rozmowie: zrzut, potem klik „Tak”
        var skillsList = commands.Remove("#lista");
        if (commands.Remove("#tokeny"))
        {
            // „Pokaż tokeny i czasy” (Ustawienia → Czat) tylko w tej kontroli — w autoteście ustawienia się nie zapisują.
            viewModel.ShowTokens = true;
        }

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

        // „#profil:Nazwa” — wybór profilu z listy (zaznacza jego umiejętności).
        foreach (var pick in commands.Where(c => c.StartsWith("#profil:", StringComparison.Ordinal)).ToList())
        {
            commands.Remove(pick);
            if (!view.SelectWorkProfileForTest(pick["#profil:".Length..].Trim()))
            {
                Console.Error.WriteLine("Nie ma profilu: " + pick);
            }
        }

        if (skillsList)
        {
            await view.SaveSkillsListAsync(Path.Combine(outputDir, "umiejetnosci.png"));
        }

        if (inlineConsent)
        {
            view.AutoConfirm = false;
            _ = AnswerConsentAsync(window, view, Path.Combine(outputDir, "zgoda.png"));
        }

        await view.RunAsync(commands); // przy --rozmowy każde polecenie zapisuje rozmowę, jak w zwykłym oknie
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(300);
        window.SavePng(Path.Combine(outputDir, "harness.png"));
        var entries = view.Panel.Entries.Select(e => new { rodzaj = e.Kind.ToString(), tekst = e.Text, szczegoly = e.Detail, ok = e.Success, metryki = e.Metrics }).ToList();
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

    /// <summary>
    /// Kontrola zakładki Whiteboard (<c>--zakladka whiteboard</c>): <c>#demo</c> kładzie przykładowy szkic (belka, podpory,
    /// siła, teksty), <c>#obraz:plik.png</c> wkleja obraz z pliku, <c>#capture</c> sprawdza Capture (ramka 360×240 w lewym
    /// górnym rogu ekranu na chwilę, zrzut na tablicę); reszta poleceń idzie przez „Zatwierdź”. Wynik: whiteboard.png
    /// (okno), tablica.png (tablica jak dla modelu) i whiteboard.json (opis tablicy, rysunki Amadeusza, wpisy).
    /// </summary>
    private static async Task<int> RunWhiteboardAsync(MainWindow window, StartupOptions options, string outputDir)
    {
        var board = window.Whiteboard;
        window.SelectTab("whiteboard");
        window.CodeView.AutoConfirm = true; // zgody (gdy sesja ma umiejętności z Code) zatwierdza kontrola
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var commands = (options.AutoTestQuestion ?? "").Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).Where(c => c != "-").ToList();
        foreach (var command in commands)
        {
            if (command == "#demo")
            {
                board.LoadDemoForTest(null);
            }
            else if (command.StartsWith("#obraz:", StringComparison.Ordinal))
            {
                board.LoadDemoForTest(null);
                board.AddImage(File.ReadAllBytes(command["#obraz:".Length..].Trim()), new Point(700, 40), false, Path.GetFileName(command["#obraz:".Length..].Trim()));
            }
            else if (command == "#capture")
            {
                await board.CaptureAsync(async capture =>
                {
                    capture.Left = 0;
                    capture.Top = 0;
                    capture.Width = 360;
                    capture.Height = 240;
                    await Task.Delay(700);
                    // Zrzut ramki w trakcie (jak widzi ją użytkownik): ramka, podpowiedź i przyciski nad tym, co jest pod spodem.
                    var corner = capture.PointToScreen(new Point(0, 0));
                    var far = capture.PointToScreen(new Point(capture.ActualWidth, capture.ActualHeight));
                    var frame = ScreenCapture.Png(new Int32Rect((int)corner.X, (int)corner.Y, (int)(far.X - corner.X), (int)(far.Y - corner.Y)));
                    if (frame is not null)
                    {
                        File.WriteAllBytes(Path.Combine(outputDir, "capture.png"), frame);
                    }

                    await capture.ConfirmAsync();
                });
            }
            else
            {
                await board.SubmitAsync(command);
            }
        }

        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(300);
        window.SavePng(Path.Combine(outputDir, "whiteboard.png"));
        File.WriteAllBytes(Path.Combine(outputDir, "tablica.png"), board.RenderBoardPng());
        var entries = board.Answers.Entries.Select(e => new { rodzaj = e.Kind.ToString(), tekst = e.Text, szczegoly = e.Detail, ok = e.Success }).ToList();
        File.WriteAllText(Path.Combine(outputDir, "whiteboard.json"), JsonSerializer.Serialize(new
        {
            opis = board.DescribeForTest(),
            rysunkiAmadeusza = board.ModelItemsForTest.Select(i => LokalneAI.Harness.Whiteboard.WhiteboardExecutor.KindName(i.Kind) + " " + i.X + "," + i.Y + " " + i.X2 + "," + i.Y2 + (i.Text is null ? "" : " „" + i.Text + "”")).ToList(),
            wpisy = entries,
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return entries.Any(e => e.rodzaj == "Error") ? 1 : 0;
    }

    /// <summary>
    /// Kontrola pytania o zgodę w rozmowie (<c>#zgoda</c>): czeka na wpis z przyciskami, zapisuje zrzut okna i klika „Tak”
    /// (przez automatyzację interfejsu, jak użytkownik).
    /// </summary>
    private static async Task AnswerConsentAsync(MainWindow window, HarnessView view, string screenshot)
    {
        for (var i = 0; i < 600; i++)
        {
            await Task.Delay(200);
            if (view.Panel.Entries.FirstOrDefault(e => e.Kind == LokalneAI.Harness.Wpf.PanelEntryKind.Confirm && e.IsPending) is null)
            {
                continue;
            }

            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            window.SavePng(screenshot);
            var yes = FindButton(view.Panel, "Tak, wykonaj") ?? FindButton(view.Panel, "Tak, włącz i ponów"); // zgoda na polecenie albo na brakującą umiejętność
            if (yes is not null)
            {
                ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(yes)
                    .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
            }

            return;
        }
    }

    private static System.Windows.Controls.Button? FindButton(DependencyObject root, string content)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Button { IsVisible: true } button && Equals(button.Content, content))
            {
                return button;
            }

            if (FindButton(child, content) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
