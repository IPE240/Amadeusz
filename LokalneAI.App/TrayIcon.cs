using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LokalneAI.App;

/// <summary>Stan pokazywany na ikonie: pracuje, temperatura karty, ile zapytań czeka.</summary>
public readonly record struct TrayState(bool Busy, double? Temperature, int Waiting);

/// <summary>
/// Ikona Amadeusza w zasobniku (obok zegara): głowa Mozarta — biała w spoczynku, niebieska, gdy model liczy; przy pracy dookoła
/// łuk temperatury karty od 30 do 70°C, a gdy zapytania czekają w kolejce — ich liczba w rogu. Zamknięcie okna chowa je tutaj
/// (program, brama i kolejka działają dalej); menu ikony: „Pokaż Amadeusza”, „Zakończ Amadeusza”.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const double MinTemperature = 30;
    private const double MaxTemperature = 70;

    private static readonly Color Idle = Colors.White;
    private static readonly Color Working = Color.FromRgb(0x3B, 0x9C, 0xFF);
    private static readonly Color Hot = Color.FromRgb(0xF5, 0x9E, 0x0B);

    private readonly Forms.NotifyIcon _icon;
    private readonly Func<TrayState> _state;
    private readonly DispatcherTimer _timer;
    private readonly ImageSource? _silhouette;
    private TrayState? _shown;
    private IntPtr _handle;
    private int _ticks;

    /// <param name="state">Odczyt stanu (wołany co sekundę na wątku okna).</param>
    /// <param name="show">„Pokaż Amadeusza” i kliknięcie ikony.</param>
    /// <param name="exit">„Zakończ Amadeusza”.</param>
    public TrayIcon(Func<TrayState> state, Action show, Action exit)
    {
        _state = state;
        _silhouette = LoadSilhouette();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Pokaż Amadeusza", null, (_, _) => show());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Zakończ Amadeusza", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon { Text = "Amadeusz", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                show();
            }
        };
        Refresh();
        PromoteToVisibleArea();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    /// <summary>Dymek nad ikoną (np. „Amadeusz działa dalej tutaj”).</summary>
    public void ShowBalloon(string title, string text) => _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Icon = null;
        _icon.Dispose();
        if (_handle != IntPtr.Zero)
        {
            DestroyIcon(_handle);
            _handle = IntPtr.Zero;
        }
    }

    /// <summary>Rysuje ikonę na nowo tylko wtedy, gdy zmienił się stan (temperatura z dokładnością do stopnia).</summary>
    private void Refresh()
    {
        if (++_ticks == 5)
        {
            PromoteToVisibleArea(); // Windows zakłada wpis ikony chwilę po jej pokazaniu
        }

        TrayState state;
        try
        {
            state = _state();
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException)
        {
            return;
        }

        var rounded = state with { Temperature = state.Busy && state.Temperature is { } t ? Math.Round(t) : null };
        if (_shown == rounded)
        {
            return;
        }

        _shown = rounded;
        var size = Math.Max(16, Forms.SystemInformation.SmallIconSize.Width);
        var bitmap = Render(rounded, size, _silhouette);
        var old = _handle;
        using (var gdi = ToGdi(bitmap))
        {
            _handle = gdi.GetHicon();
        }

        _icon.Icon = System.Drawing.Icon.FromHandle(_handle);
        if (old != IntPtr.Zero)
        {
            DestroyIcon(old);
        }

        var text = "Amadeusz — " + (rounded.Busy ? "pracuje" : "gotowy")
            + (rounded.Temperature is { } c ? " · karta " + c.ToString("0") + "°C" : "")
            + (rounded.Waiting > 0 ? " · czeka " + rounded.Waiting : "");
        _icon.Text = text.Length > 63 ? text[..63] : text; // limit Windows na podpowiedź ikony
    }

    /// <summary>Ikona w pikselach <paramref name="size"/>: sylwetka, przy pracy łuk temperatury, w rogu liczba czekających.</summary>
    internal static BitmapSource Render(TrayState state, int size, ImageSource? silhouette)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var color = state.Busy ? Working : Idle;
            var ring = state.Busy && state.Temperature.HasValue;
            var inset = ring ? size * 0.2 : size * 0.04;
            var head = new Rect(inset, inset, size - 2 * inset, size - 2 * inset);
            DrawSilhouette(dc, head, color, silhouette);

            if (ring)
            {
                var stroke = Math.Max(1.5, size / 10.0);
                var center = new Point(size / 2.0, size / 2.0);
                var radius = size / 2.0 - stroke / 2 - 0.5;
                var track = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), Math.Max(1, stroke / 2.5));
                dc.DrawEllipse(null, track, center, radius, radius);
                var fraction = Math.Max(0.02, Math.Min(1, (state.Temperature!.Value - MinTemperature) / (MaxTemperature - MinTemperature)));
                var arcColor = state.Temperature.Value >= 65 ? Hot : Working;
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(arcColor), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                    Arc(center, radius, fraction));
            }

            if (state.Waiting > 0)
            {
                var label = state.Waiting > 9 ? "9+" : state.Waiting.ToString();
                var badge = size * 0.5;
                var box = new Rect(size - badge, size - badge, badge, badge);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xD9, 0x30, 0x25)), new Pen(Brushes.White, Math.Max(1, size / 24.0)),
                    new Point(box.X + badge / 2, box.Y + badge / 2), badge / 2, badge / 2);
                var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    badge * (label.Length > 1 ? 0.62 : 0.8), Brushes.White, 1.0);
                dc.DrawText(text, new Point(box.X + (badge - text.Width) / 2, box.Y + (badge - text.Height) / 2));
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Sylwetka w kolorze; delikatny ciemny cień, żeby biała ikona była widoczna także na jasnym pasku zadań.</summary>
    private static void DrawSilhouette(DrawingContext dc, Rect area, Color color, ImageSource? silhouette)
    {
        if (silhouette is not null)
        {
            var shadow = area;
            shadow.Offset(0, Math.Max(0.5, area.Height / 32));
            dc.PushOpacityMask(new ImageBrush(silhouette) { Stretch = Stretch.Uniform });
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), null, shadow);
            dc.Pop();
            dc.PushOpacityMask(new ImageBrush(silhouette) { Stretch = Stretch.Uniform });
            dc.DrawRectangle(new SolidColorBrush(color), null, area);
            dc.Pop();
            return;
        }

        var geometry = FallbackHead.Clone();
        geometry.Transform = new MatrixTransform(area.Width / 100, 0, 0, area.Height / 100, area.X, area.Y);
        dc.DrawGeometry(new SolidColorBrush(color), new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), Math.Max(0.5, area.Width / 40)), geometry);
    }

    /// <summary>Zastępcza sylwetka (profil w peruce z warkoczem), dopóki nie ma pliku Assets\mozart.png.</summary>
    private static readonly Geometry FallbackHead = Geometry.Parse(
        "M 58,6 C 40,4 27,15 25,30 L 21,40 L 14,51 L 22,54 L 21,61 L 24,65 L 23,71 C 28,77 35,79 42,78 L 43,94 L 70,94 L 67,79 " +
        "C 75,76 81,70 83,62 C 87,54 86,38 81,27 C 77,15 70,7 58,6 Z M 79,60 C 88,68 92,82 86,97 L 80,95 C 84,84 81,73 74,66 Z");

    /// <summary>Kontrola bez klikania: podgląd ikon w kilku stanach i rozmiarach (bez ikony w zasobniku).</summary>
    internal static void SavePreview(string path)
    {
        var silhouette = LoadSilhouette();
        TrayState[] states = [new(false, null, 0), new(true, 38, 0), new(true, 55, 0), new(true, 68, 3), new(false, null, 12)];
        int[] sizes = [16, 24, 32, 64];
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var dark = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24));
            var light = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            for (var row = 0; row < 2; row++)
            {
                dc.DrawRectangle(row == 0 ? dark : light, null, new Rect(0, row * 80, states.Length * 150, 80));
                for (var i = 0; i < states.Length; i++)
                {
                    var x = i * 150 + 6.0;
                    foreach (var size in sizes)
                    {
                        dc.DrawImage(Render(states[i], size, silhouette), new Rect(x, row * 80 + (80 - size) / 2.0, size, size));
                        x += size + 6;
                        if (size == 64)
                        {
                            break;
                        }
                    }
                }
            }
        }

        var bitmap = new RenderTargetBitmap(states.Length * 150, 160, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static Geometry Arc(Point center, double radius, double fraction)
    {
        var angle = -90 + 359.9 * fraction;
        Point At(double degrees) => new(center.X + radius * Math.Cos(degrees * Math.PI / 180), center.Y + radius * Math.Sin(degrees * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(-90), false, false);
            g.ArcTo(At(angle), new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Sylwetka z zasobu <c>Assets\mozart.png</c>: obrazek z przezroczystym tłem używany wprost; zwykły obrazek (ciemna
    /// sylwetka na jasnym tle) zamieniany na maskę — ciemne piksele to głowa, jasne to tło. Brak pliku = sylwetka zastępcza.
    /// </summary>
    private static ImageSource? LoadSilhouette()
    {
        try
        {
            var source = new BitmapImage(new Uri("pack://application:,,,/Assets/mozart.png"));
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);
            var hasAlpha = false;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] < 250)
                {
                    hasAlpha = true;
                    break;
                }
            }

            if (!hasAlpha)
            {
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var luminance = 0.114 * pixels[i] + 0.587 * pixels[i + 1] + 0.299 * pixels[i + 2];
                    pixels[i + 3] = luminance < 90 ? (byte)255 : luminance < 130 ? (byte)((130 - luminance) * 255 / 40) : (byte)0;
                }
            }

            var mask = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            mask.Freeze();
            return mask;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static System.Drawing.Bitmap ToGdi(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new System.Drawing.Bitmap(stream);
    }

    /// <summary>
    /// Windows 11 chowa nowe ikony w „Pokaż ukryte ikony”. Raz przy pierwszym uruchomieniu ustawia ikonę Amadeusza jako widoczną
    /// (wpis <c>HKCU\Control Panel\NotifyIconSettings</c> dla tego programu, <c>IsPromoted</c> = 1). Gdy użytkownik potem sam ją
    /// schowa, Windows zapisze 0 — tego program już nie zmienia.
    /// </summary>
    private static void PromoteToVisibleArea()
    {
        try
        {
            var exe = Environment.ProcessPath;
            using var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (exe is null || root is null)
            {
                return;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name, writable: true);
                if (key?.GetValue("ExecutablePath") is string path && string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)
                    && key.GetValue("IsPromoted") is null)
                {
                    key.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Bez uprawnień — ikona zostaje tam, gdzie postawi ją Windows.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
