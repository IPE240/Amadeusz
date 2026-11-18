using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LokalneAI.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>Przewijaj za odpowiedzią, dopóki użytkownik sam nie przewinie wyżej.</summary>
    private bool _followAnswer = true;

    /// <summary>Ikona w zasobniku; null w kontroli bez klikania (wtedy zamknięcie okna kończy program).</summary>
    private TrayIcon? _tray;

    /// <summary>Program naprawdę się kończy („Zakończ Amadeusza”, wylogowanie) — zamknięcie okna nie chowa go do zasobnika.</summary>
    private bool _exiting;

    private bool _trayHintShown;

    /// <summary>
    /// Włącza ikonę w zasobniku: zamknięcie okna chowa Amadeusza (program, brama i kolejka działają dalej), ikona pokazuje
    /// stan pracy, temperaturę karty i kolejkę. Wołane przy zwykłym starcie (nie w kontroli bez klikania).
    /// </summary>
    public void EnableTray()
    {
        _tray ??= new TrayIcon(
            () => new TrayState(
                _viewModel.IsBusy || CodeView.Panel.IsBusy || _viewModel.Network.QueueRunning > 0,
                _viewModel.Temperature.Current,
                _viewModel.Network.QueueWaiting),
            ShowFromTray,
            ExitApplication);
        if (Application.Current is { } app)
        {
            app.SessionEnding += (_, _) => _exiting = true;
        }
    }

    /// <summary>Pokazuje okno z zasobnika (kliknięcie ikony, drugie uruchomienie skrótu).</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;  // wyciągnięcie na wierzch spod innych okien
        Topmost = false;
        Focus();
    }

    /// <summary>„Zakończ Amadeusza” z menu ikony: zapis, zamknięcie sesji i bramy, koniec programu.</summary>
    public void ExitApplication()
    {
        _exiting = true;
        Show(); // zamknięcie ukrytego okna też przechodzi przez Closing
        Close();
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        AttachSettingsSections();
        Loaded += async (_, _) =>
        {
            // Najpierw zakładka Code (kontrola bez klikania czeka tylko na InitializeAsync), potem Ollama i brama.
            CodeView.Attach(_viewModel.CreateHarnessContext);
            _viewModel.TemperatureUpdated += (_, _) => TemperatureGraph.Show(_viewModel.Temperature);
            _viewModel.StartTemperature();
            await _viewModel.InitializeAsync();
            CodeView.RefreshModelText();
            if (IsActive)
            {
                QuestionBox.Focus();
            }
        };
        _viewModel.ComputeTargetChanged += (_, _) => CodeView.RefreshModelText();
        ChatSidebar.Attach(_viewModel);
        _viewModel.Credentials.Changed += (_, _) =>
        {
            CodeView.RefreshCdeText();
            CodeView.RebuildSession();
        };
        CodeView.CredentialsRequested += (_, _) => OpenSettingsSection(SectionCredentials);
        _viewModel.WorkProfiles.Changed += (_, _) => CodeView.ReloadWorkProfiles();
        _viewModel.Skills.Changed += (_, _) => CodeView.RebuildSession(); // nowa wiedza albo źródła — następne polecenie z nowym indeksem
        CodeView.WorkProfilesChanged += (_, _) => _viewModel.WorkProfiles.Reload();
        _viewModel.WorkProfiles.EditModelsRequested += (_, row) =>
        {
            var choices = _viewModel.Models
                .Select(m => new ModelChoice(m.Key, m.IsCloud ? m.Display + " (" + m.Cloud!.Name + ")" : m.Name))
                .ToList();
            var window = new PreferredModelsWindow(row.Name, row.Models, choices) { Owner = this };
            if (window.ShowDialog() == true)
            {
                row.SetModels(window.Keys);
            }
        };
        _viewModel.InternetChanged += (_, _) =>
        {
            CodeView.RefreshModelText();
            CodeView.RebuildSession();
        };
        Tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == Tabs && Tabs.SelectedItem == CodeTab && IsLoaded)
            {
                CodeView.RefreshModelText();
            }
        };
        Closing += (_, e) =>
        {
            if (_tray is not null && !_exiting)
            {
                // × chowa okno do zasobnika — Amadeusz działa dalej (brama, kolejka, liczenie dla innych).
                e.Cancel = true;
                CodeView.SaveChoices();
                Hide();
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    _tray.ShowBalloon("Amadeusz działa dalej", "Kliknij ikonę, żeby wrócić. Zamknięcie programu: prawy przycisk na ikonie → Zakończ Amadeusza.");
                }

                return;
            }

            CodeView.SaveChoices();
            CodeView.Shutdown();
            _viewModel.OnClosing();
            _tray?.Dispose();
            _tray = null;
        };
    }

    /// <summary>Menedżer poświadczeń: wiersz zapisuje się, gdy kursor go opuszcza.</summary>
    private void CredentialRow_FocusChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && sender is FrameworkElement { DataContext: CredentialRow row })
        {
            _viewModel.Credentials.Commit(row);
        }
    }

    private void CredentialPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: CredentialRow row } box)
        {
            row.NewPassword = box.Password;
        }
    }

    private void CredentialPassword_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: CredentialRow row } box)
        {
            row.NewPasswordCleared += (_, _) => box.Clear();
        }
    }

    /// <summary>Sekcje zakładki Ustawienia: rozwinięte tak jak ostatnio; każde zwinięcie i rozwinięcie jest zapamiętywane.</summary>
    private void AttachSettingsSections()
    {
        foreach (var section in SettingsPanel.Children.OfType<Expander>())
        {
            section.IsExpanded = _viewModel.IsSectionOpen(section.Name);
            section.Expanded += (_, e) =>
            {
                if (e.OriginalSource == section)
                {
                    _viewModel.SetSectionOpen(section.Name, true);
                }
            };
            section.Collapsed += (_, e) =>
            {
                if (e.OriginalSource == section)
                {
                    _viewModel.SetSectionOpen(section.Name, false);
                }
            };
        }
    }

    /// <summary>Przechodzi do Ustawień, rozwija sekcję i przewija do niej.</summary>
    private void OpenSettingsSection(Expander section)
    {
        Tabs.SelectedItem = SettingsTab;
        section.IsExpanded = true;
        Dispatcher.InvokeAsync(() => section.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Okno bez systemowej ramki po maksymalizacji wychodzi poza ekran o jej grubość — zostawiamy margines.</summary>
    private const double MaximizedEdge = 7;

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = new Thickness(maximized ? MaximizedEdge : 0);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Przywróć" : "Maksymalizuj";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Wybiera zakładkę: „czat”, „code” albo „ustawienia” (kontrola bez klikania).</summary>
    internal void SelectTab(string? name)
    {
        Tabs.SelectedItem = (name ?? "").ToLowerInvariant() switch
        {
            "code" => CodeTab,
            "ustawienia" => SettingsTab,
            _ => ChatTab,
        };
        Tabs.UpdateLayout();
    }

    /// <summary>Kontrola bez klikania: rozwija listę modeli i zapisuje ją do PNG (lista jest w osobnym okienku, poza zrzutem okna).</summary>
    internal async Task SaveModelListPngAsync(string path)
    {
        ModelBox.IsDropDownOpen = true;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(300);
        if (ModelBox.Template.FindName("PART_Popup", ModelBox) is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement list })
        {
            Snapshot.Save(list, path);
        }

        ModelBox.IsDropDownOpen = false;
    }

    /// <summary>Zapisuje wygląd okna do PNG (kontrola bez klikania, <c>--autotest</c>).</summary>
    internal void SavePng(string path)
    {
        Root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(Root);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(Root.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(Root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(Root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>
    /// Obrazek wklejony do pytania (Ctrl+V): zapis do <c>Dokumenty\Amadeusz\Czat\obrazy\</c> i OCR z Ustawień → OCR;
    /// rozpoznany tekst z nagłówkiem trafia w miejsce kursora. Tekst ze schowka wkleja się jak zwykle.
    /// </summary>
    private async void QuestionBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!_viewModel.Ocr.OnPaste || !e.DataObject.GetDataPresent(DataFormats.Bitmap) || e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            return;
        }

        e.CancelCommand();
        byte[] png;
        try
        {
            var image = Clipboard.GetImage();
            if (image == null)
            {
                return;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            png = stream.ToArray();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is NotSupportedException || ex is InvalidOperationException)
        {
            _viewModel.ShowStatus("Nie udało się odczytać obrazka ze schowka: " + ex.Message);
            return;
        }

        var caret = QuestionBox.CaretIndex;
        QuestionBox.IsEnabled = false;
        _viewModel.ShowStatus("Rozpoznaję tekst z wklejonego obrazka (OCR)…");
        try
        {
            var text = await _viewModel.Ocr.RecognizePastedAsync(png, Path.Combine(LokalneAI.Core.Storage.ChatConversationStore.DefaultRoot, "obrazy"));
            var current = _viewModel.Question ?? "";
            caret = Math.Min(caret, current.Length);
            _viewModel.Question = current.Substring(0, caret) + text + current.Substring(caret);
            QuestionBox.CaretIndex = caret + text.Length;
            var firstLine = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
            _viewModel.ShowStatus(firstLine.Trim('[', ']') + " — tekst jest w polu pytania, dopisz, o co chodzi.");
        }
        finally
        {
            QuestionBox.IsEnabled = true;
            QuestionBox.Focus();
        }
    }

    private void QuestionBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter wysyła, Shift+Enter wstawia nową linię.
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (_viewModel.SendCommand.CanExecute(null))
            {
                _followAnswer = true;
                _viewModel.SendCommand.Execute(null);
            }
        }
    }

    private void Transcript_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
        {
            // Przewinął użytkownik: śledź odpowiedź tylko wtedy, gdy jest na samym dole.
            _followAnswer = TranscriptScroll.VerticalOffset >= TranscriptScroll.ScrollableHeight - 4;
        }
        else if (_followAnswer)
        {
            TranscriptScroll.ScrollToEnd();
        }
    }

    private void Transcript_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Pola tekstowe odpowiedzi połykają kółko myszy; przewijamy całą rozmowę sami.
        TranscriptScroll.ScrollToVerticalOffset(TranscriptScroll.VerticalOffset - e.Delta * 0.4);
        e.Handled = true;
    }
}
