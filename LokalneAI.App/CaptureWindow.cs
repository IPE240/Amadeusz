using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Shapes = System.Windows.Shapes;

namespace LokalneAI.App;

/// <summary>
/// Ramka trybu Capture zakładki Whiteboard: przezroczyste okno bez ramek (widać, co jest pod nim), w miejscu i rozmiarze
/// tablicy. Przeciąganie przesuwa, krawędzie zmieniają rozmiar; „Zatwierdź” (Enter) chowa ramkę i robi zrzut ekranu z jej
/// wnętrza, „Zaniechaj” (Esc) zamyka bez zrzutu. Okno główne chowa i przywraca <see cref="WhiteboardView"/>.
/// </summary>
public sealed class CaptureWindow : Window
{
    private readonly TaskCompletionSource<byte[]?> _result = new();
    private readonly TextBlock _size;
    private bool _done;

    public CaptureWindow(Rect area)
    {
        Title = "Amadeusz — Capture";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Prawie przezroczyste tło: widać, co jest pod spodem, a okno wciąż łapie mysz (całkiem przezroczyste piksele przepuszczają kliknięcia).
        Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
        ResizeMode = ResizeMode.CanResize;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = area.Left;
        Top = area.Top;
        Width = Math.Max(160, area.Width);
        Height = Math.Max(120, area.Height);
        MinWidth = 160;
        MinHeight = 120;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(8),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        var accent = (Brush)Application.Current.FindResource("AccentBrush");
        var frame = new Shapes.Rectangle
        {
            Stroke = accent,
            StrokeThickness = 2,
            StrokeDashArray = [4, 3],
            IsHitTestVisible = false,
        };
        var outer = new Shapes.Rectangle { Stroke = Brushes.White, StrokeThickness = 1, Margin = new Thickness(2), IsHitTestVisible = false, Opacity = 0.8 };

        var confirm = new Button { Content = "Zatwierdź", Width = 100, Height = 30, Margin = new Thickness(0, 0, 6, 0), IsDefault = true, ToolTip = "Zrzut ekranu z wnętrza ramki na tablicę (Enter)" };
        confirm.Style = (Style)Application.Current.FindResource("PrimaryButton");
        confirm.Click += async (_, _) => await ConfirmAsync();
        var cancel = new Button { Content = "Zaniechaj", Width = 100, Height = 30, IsCancel = true, ToolTip = "Wraca bez zrzutu (Esc)" };
        cancel.Click += (_, _) => Finish(null);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 14),
            Children = { confirm, cancel },
        };

        _size = new TextBlock { Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x45, 0x71)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(12, 12, 12, 56), // pod spodem miejsce na przyciski
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Capture: przeciągnij, żeby przesunąć; krawędzie zmieniają rozmiar.", Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                    _size,
                },
            },
        };

        Content = new Grid { Children = { frame, outer, hint, buttons } };
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            {
                DragMove();
            }
        };
        SizeChanged += (_, _) => UpdateSize();
        LocationChanged += (_, _) => UpdateSize();
        Closed += (_, _) => _result.TrySetResult(null);
    }

    /// <summary>
    /// Pokazuje ramkę i czeka na Zatwierdź (PNG zrzutu) albo Zaniechaj (null). <paramref name="drive"/> — kontrola bez
    /// klikania: dostaje okno i sama wywołuje <see cref="ConfirmAsync"/> albo <see cref="Cancel"/>.
    /// </summary>
    public Task<byte[]?> RunAsync(Func<CaptureWindow, Task>? drive = null)
    {
        if (drive is not null)
        {
            ShowActivated = false; // kontrola bez klikania: bez zabierania fokusu
            Show();
            _ = drive(this);
        }
        else
        {
            Show();
            Activate();
        }

        return _result.Task;
    }

    /// <summary>Zaniechaj: zamyka ramkę bez zrzutu.</summary>
    public void Cancel() => Finish(null);

    /// <summary>Zatwierdź: chowa ramkę (żeby nie trafiła na zrzut), robi zrzut wnętrza i zamyka.</summary>
    public async Task ConfirmAsync()
    {
        if (_done)
        {
            return;
        }

        var topLeft = PointToScreen(new Point(0, 0));
        var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
        var device = new Int32Rect((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
            (int)Math.Round(bottomRight.X - topLeft.X), (int)Math.Round(bottomRight.Y - topLeft.Y));
        Opacity = 0;
        Hide();
        await Task.Delay(250); // system musi zdjąć ramkę z ekranu (animacja okien), zanim zrobimy zrzut
        byte[]? png = null;
        try
        {
            png = ScreenCapture.Png(device);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            png = null;
        }

        Finish(png);
    }

    private void Finish(byte[]? png)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _result.TrySetResult(png);
        Close();
    }

    private void UpdateSize()
    {
        if (PresentationSource.FromVisual(this) is not { CompositionTarget: { } target })
        {
            return;
        }

        var scale = target.TransformToDevice.M11;
        _size.Text = Math.Round(ActualWidth * scale) + " × " + Math.Round(ActualHeight * scale) + " px · Enter = Zatwierdź, Esc = Zaniechaj";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Finish(null);
        }
    }
}

/// <summary>Zrzut prostokąta ekranu (piksele urządzenia) do PNG przez GDI+ (System.Drawing z WinForms).</summary>
internal static class ScreenCapture
{
    public static byte[]? Png(Int32Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        using var bitmap = new System.Drawing.Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(area.X, area.Y, 0, 0, new System.Drawing.Size(area.Width, area.Height), System.Drawing.CopyPixelOperation.SourceCopy);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
}
