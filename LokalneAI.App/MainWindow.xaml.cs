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
        _viewModel.Credentials.Changed += (_, _) =>
        {
            CodeView.RefreshCdeText();
            CodeView.RebuildSession();
        };
        CodeView.CredentialsRequested += (_, _) => OpenSettingsSection(SectionCredentials);
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
        Closing += (_, _) =>
        {
            CodeView.SaveChoices();
            CodeView.Shutdown();
            _viewModel.OnClosing();
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
