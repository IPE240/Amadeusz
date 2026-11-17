using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LokalneAI.Core.Hardware;

namespace LokalneAI.App;

/// <summary>
/// Wykres temperatury na granatowym pasku tytułu: cienka, jasnoniebieska linia z ostatnich 2 minut (wygładzona krzywą)
/// i bieżąca temperatura na końcu, w tym samym kolorze. Nie przyjmuje kliknięć, więc pasek dalej przesuwa okno.
/// </summary>
public sealed class TemperatureGraph : FrameworkElement
{
    private static readonly Brush LineBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0xC4, 0xD4, 0xEC)));
    private static readonly Pen LinePen = Frozen(new Pen(LineBrush, 1.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    private static readonly Typeface Font = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private TemperatureHistory? _history;

    public TemperatureGraph()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>Rysuje historię od nowa (po każdej próbce).</summary>
    public void Show(TemperatureHistory history)
    {
        _history = history;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (_history is null || _history.Samples.Count == 0 || ActualWidth < 60 || ActualHeight < 8)
        {
            return;
        }

        var latest = _history.Samples[^1];
        var fresh = DateTime.Now - latest.Time < TimeSpan.FromSeconds(20);
        var text = new FormattedText(fresh ? latest.Celsius.ToString("0", CultureInfo.InvariantCulture) + "°C" : "—",
            CultureInfo.GetCultureInfo("pl-PL"), FlowDirection.LeftToRight, Font, 14, LineBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var plotWidth = ActualWidth - text.Width - 10;
        drawing.DrawText(text, new Point(plotWidth + 8, (ActualHeight - text.Height) / 2));
        if (plotWidth < 30)
        {
            return;
        }

        // Prawa krawędź = ostatnia próbka (linia kończy się przy liczbie); 1 px zapasu na grubość linii.
        var raw = _history.ToPoints(plotWidth, ActualHeight - 2, latest.Time).Select(p => new Point(p.X, p.Y + 1)).ToList();
        if (raw.Count == 1)
        {
            drawing.DrawEllipse(LineBrush, null, raw[0], 1.5, 1.5);
            return;
        }

        drawing.DrawGeometry(null, LinePen, Smooth(raw, ActualHeight));
    }

    /// <summary>
    /// Wygładzanie: najpierw średnia z sąsiadów (temperatura z karty jest w pełnych stopniach — schodki), potem krzywa
    /// Catmulla-Roma zamieniona na odcinki Béziera. Punkty kontrolne nie wychodzą poza pasek.
    /// </summary>
    private static Geometry Smooth(IReadOnlyList<Point> raw, double height)
    {
        var points = raw.Select((p, i) => i == 0 || i == raw.Count - 1
            ? p
            : new Point(p.X, (raw[i - 1].Y + (2 * p.Y) + raw[i + 1].Y) / 4)).ToList();
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            for (var i = 0; i < points.Count - 1; i++)
            {
                var p0 = points[Math.Max(0, i - 1)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Math.Min(points.Count - 1, i + 2)];
                var c1 = new Point(p1.X + ((p2.X - p0.X) / 6), Clamp(p1.Y + ((p2.Y - p0.Y) / 6), height));
                var c2 = new Point(p2.X - ((p3.X - p1.X) / 6), Clamp(p2.Y - ((p3.Y - p1.Y) / 6), height));
                context.BezierTo(c1, c2, p2, isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static double Clamp(double y, double height) => Math.Clamp(y, 1, height - 1);

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
