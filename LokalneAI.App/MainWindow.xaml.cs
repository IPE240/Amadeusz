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
        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            if (IsActive)
            {
                QuestionBox.Focus();
            }
        };
        Closing += (_, _) => _viewModel.OnClosing();
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

    private void OpenHarness_Click(object sender, RoutedEventArgs e)
        => new HarnessWindow(_viewModel.CreateHarnessContext()) { Owner = this }.Show();

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
