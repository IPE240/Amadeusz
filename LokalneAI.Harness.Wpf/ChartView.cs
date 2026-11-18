using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LokalneAI.Harness.Charts;

namespace LokalneAI.Harness.Wpf
{
    /// <summary>
    /// Wykres z bloku <c>```wykres</c> w odpowiedzi (<see cref="MarkdownRenderer"/>): słupkowy (pionowy, poziomy, skumulowany, z liniami
    /// nad słupkami), liniowy (z wypełnieniem pod linią i między dwiema liniami, przerywany), kołowy i pierścieniowy. Dane, kolejność
    /// (sortowanie, odwrócenie) i kolory czyta <see cref="ChartSpec"/> z <c>LokalneAI.Harness.Charts</c>.
    /// Wygląd według zasad czytelnych wykresów: stała kolejność kolorów (sprawdzona pod kątem daltonizmu), cienkie słupki
    /// (≤ 24 px, zaokrąglony koniec, 2 px przerwy), linie 2 px, delikatna siatka, legenda przy dwóch i więcej seriach albo przy grupach,
    /// teksty zawsze w kolorach tekstu (nie serii), wartości po najechaniu myszą.
    /// </summary>
    public sealed class ChartView : FrameworkElement
    {
        private static readonly Brush Surface = Frozen(Color.FromRgb(0xff, 0xff, 0xff));
        private static readonly Brush SurfaceBorder = Frozen(Color.FromRgb(0xe5, 0xe7, 0xeb));
        private static readonly Brush TextPrimary = Frozen(Color.FromRgb(0x11, 0x18, 0x27));
        private static readonly Brush TextSecondary = Frozen(Color.FromRgb(0x52, 0x51, 0x4e));
        private static readonly Brush Grid = Frozen(Color.FromRgb(0xec, 0xee, 0xf1));
        private static readonly Brush Axis = Frozen(Color.FromRgb(0xc9, 0xcd, 0xd3));
        private static readonly Brush HoverBand = Frozen(Color.FromArgb(0x10, 0x11, 0x18, 0x27));
        private static readonly Typeface Font = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface FontBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
        private static readonly Dictionary<string, SolidColorBrush> Brushes = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

        private readonly ChartSpec _spec;
        private readonly List<int> _bars;
        private readonly List<int> _lines;
        private int _hover = -1;
        private Rect _plot;
        private Point _pieCenter;
        private double _pieRadius;
        private double _pieInner;
        private List<PieSlice> _slices = new List<PieSlice>();

        /// <summary>Kawałek kołowego: nazwa, wartość, numer pozycji w danych (-1 = „Pozostałe”) i miejsce w palecie.</summary>
        private sealed class PieSlice
        {
            public string Name;
            public double Value;
            public int Point;
            public int Slot;
        }

        /// <summary>Wykres z gotowej specyfikacji.</summary>
        public ChartView(ChartSpec spec)
        {
            _spec = spec ?? throw new ArgumentNullException(nameof(spec));
            var indexes = Enumerable.Range(0, spec.Series.Count).ToList();
            if (spec.Kind == ChartKind.Line)
            {
                _bars = new List<int>();
                _lines = indexes;
            }
            else
            {
                // Na poziomym wszystko jest słupkiem; na pionowym seria „liniowa” idzie nad słupkami (np. granica nośności).
                _bars = indexes.Where(i => spec.Horizontal || !spec.Series[i].AsLine).ToList();
                _lines = indexes.Where(i => !_bars.Contains(i)).ToList();
                if (_bars.Count == 0)
                {
                    _bars = indexes;
                    _lines = new List<int>();
                }
            }

            Height = spec.Height ?? (spec.Kind == ChartKind.Pie ? 250 : spec.Horizontal ? HorizontalHeight() : 280);
            MinWidth = 320;
            Margin = new Thickness(0, 2, 0, 10);
            Cursor = Cursors.Arrow;
            if (spec.Kind == ChartKind.Pie)
            {
                _slices = Slices(spec);
            }
        }

        /// <summary>Specyfikacja wykresu (dane do kopiowania).</summary>
        public ChartSpec Spec => _spec;

        /// <inheritdoc />
        protected override Size MeasureOverride(Size availableSize)
            => new Size(double.IsInfinity(availableSize.Width) ? 560 : Math.Max(MinWidth, availableSize.Width), Height);

        /// <inheritdoc />
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = HitTest(e.GetPosition(this));
            if (index != _hover)
            {
                _hover = index;
                InvalidateVisual();
            }
        }

        /// <inheritdoc />
        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                InvalidateVisual();
            }
        }

        /// <inheritdoc />
        protected override void OnRender(DrawingContext dc)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            dc.DrawRoundedRectangle(Surface, new Pen(SurfaceBorder, 1), new Rect(0.5, 0.5, width - 1, height - 1), 6, 6);
            var top = 12.0;
            if (!string.IsNullOrWhiteSpace(_spec.Title))
            {
                var title = Text(_spec.Title, 13, TextPrimary, FontBold, width - 28);
                dc.DrawText(title, new Point(14, top));
                top += title.Height + 4;
            }

            if (_spec.Kind == ChartKind.Pie)
            {
                RenderPie(dc, new Rect(14, top + 4, width - 28, height - top - 18));
                return;
            }

            var legend = LegendItems();
            if (legend.Count > 0)
            {
                top = RenderLegend(dc, legend, 14, top, width - 28) + 4;
            }

            var area = new Rect(14, top + 6, width - 28, Math.Max(40, height - top - 18));
            if (_spec.Horizontal)
            {
                RenderHorizontal(dc, area);
            }
            else
            {
                RenderVertical(dc, area);
            }
        }

        /// <summary>Wysokość poziomego: tyle, żeby każda kategoria miała swój wiersz (200–700 px).</summary>
        private double HorizontalHeight()
        {
            var perRow = _spec.Stacked || _bars.Count <= 1 ? 26 : Math.Min(48, (_bars.Count * 14) + 12);
            var legend = _spec.Groups != null || _spec.Series.Count >= 2 ? 24 : 0;
            return Math.Max(200, Math.Min(700, 70 + legend + (_spec.Labels.Count * perRow)));
        }

        /// <summary>Legenda: grupy (gdy są i jest jedna seria słupków), potem serie (gdy są co najmniej dwie).</summary>
        private List<Tuple<string, Brush>> LegendItems()
        {
            var items = new List<Tuple<string, Brush>>();
            var byGroup = _spec.Groups != null && _bars.Count <= 1;
            if (byGroup)
            {
                items.AddRange(_spec.GroupNames().Where(g => g.Length > 0).Select(g => Tuple.Create<string, Brush>(g, Brush(_spec.GroupColor(g)))));
            }

            if (_spec.Series.Count >= 2)
            {
                for (var s = 0; s < _spec.Series.Count; s++)
                {
                    if (byGroup && _bars.Contains(s))
                    {
                        continue; // słupki są w kolorach grup, nie serii
                    }

                    items.Add(Tuple.Create<string, Brush>(SeriesName(s), Brush(_spec.SeriesColor(s))));
                }
            }

            return items;
        }

        private string SeriesName(int s)
            => string.IsNullOrEmpty(_spec.Series[s].Name) ? (_spec.Series.Count == 1 && _spec.YTitle.Length > 0 ? _spec.YTitle : "seria " + (s + 1)) : _spec.Series[s].Name;

        private double RenderLegend(DrawingContext dc, List<Tuple<string, Brush>> items, double x0, double y, double width)
        {
            var x = x0;
            var line = 0.0;
            foreach (var item in items)
            {
                var label = Text(item.Item1, 12, TextSecondary, Font, 220);
                var w = 14 + label.Width + 16;
                if (x + w > x0 + width && x > x0)
                {
                    x = x0;
                    y += line + 4;
                    line = 0;
                }

                dc.DrawRoundedRectangle(item.Item2, null, new Rect(x, y + ((label.Height - 10) / 2), 10, 10), 2, 2);
                dc.DrawText(label, new Point(x + 14, y));
                x += w;
                line = Math.Max(line, label.Height);
            }

            return y + line;
        }

        /// <summary>Zakres osi wartości i podziałka: od zera (albo od ujemnych) do największej wartości, chyba że podano <c>zakres_y</c>.</summary>
        private void Range(out double min, out double max, out List<double> ticks)
        {
            var values = new List<double>();
            if (_spec.Stacked && _bars.Count > 0)
            {
                for (var i = 0; i < _spec.Labels.Count; i++)
                {
                    double positive = 0, negative = 0;
                    foreach (var s in _bars)
                    {
                        var v = Value(s, i);
                        if (v.HasValue)
                        {
                            if (v.Value >= 0)
                            {
                                positive += v.Value;
                            }
                            else
                            {
                                negative += v.Value;
                            }
                        }
                    }

                    values.Add(positive);
                    values.Add(negative);
                }
            }
            else
            {
                values.AddRange(_bars.SelectMany(s => _spec.Series[s].Values).Where(v => v.HasValue).Select(v => v.Value));
            }

            values.AddRange(_lines.SelectMany(s => _spec.Series[s].Values).Where(v => v.HasValue).Select(v => v.Value));
            if (values.Count == 0)
            {
                values.Add(0);
            }

            min = Math.Min(0, values.Min());
            max = Math.Max(0, values.Max());
            var explicitRange = _spec.YMin.HasValue || _spec.YMax.HasValue;
            if (_spec.YMin.HasValue)
            {
                min = _spec.YMin.Value;
            }

            if (_spec.YMax.HasValue)
            {
                max = _spec.YMax.Value;
            }

            if (max <= min)
            {
                max = min + 1;
            }

            ticks = NiceTicks(min, max, 5);
            if (!explicitRange)
            {
                min = ticks.First();
                max = ticks.Last();
            }
            else
            {
                var lo = min;
                var hi = max;
                ticks = ticks.Where(t => t >= lo - 1e-9 && t <= hi + 1e-9).ToList();
                if (ticks.Count < 2)
                {
                    ticks = new List<double> { min, max };
                }
            }
        }

        private double? Value(int series, int point)
        {
            var values = _spec.Series[series].Values;
            return point >= 0 && point < values.Count ? values[point] : null;
        }

        private void RenderVertical(DrawingContext dc, Rect area)
        {
            Range(out var min, out var max, out var ticks);
            var yLabelWidth = ticks.Select(t => Text(Format(t), 11, TextSecondary, Font, 200).Width).Max();
            var yTitleHeight = string.IsNullOrWhiteSpace(_spec.YTitle) ? 0 : 22;
            var xTitleHeight = string.IsNullOrWhiteSpace(_spec.XTitle) ? 0 : 16;
            var plot = new Rect(area.Left + yLabelWidth + 8, area.Top + yTitleHeight, Math.Max(40, area.Width - yLabelWidth - 8), Math.Max(40, area.Height - yTitleHeight - 22 - xTitleHeight));
            _plot = plot;
            var span = max - min;
            Func<double, double> y = v => _spec.ReverseY ? plot.Top + ((v - min) / span * plot.Height) : plot.Bottom - ((v - min) / span * plot.Height);

            if (yTitleHeight > 0)
            {
                dc.DrawText(Text(_spec.YTitle, 11, TextSecondary, Font, area.Width), new Point(area.Left, area.Top - 2));
            }

            foreach (var tick in ticks)
            {
                var ty = Math.Round(y(tick)) + 0.5;
                dc.DrawLine(new Pen(tick == 0 ? Axis : Grid, 1), new Point(plot.Left, ty), new Point(plot.Right, ty));
                var label = Text(Format(tick), 11, TextSecondary, Font, 200);
                dc.DrawText(label, new Point(plot.Left - 6 - label.Width, ty - (label.Height / 2)));
            }

            var count = _spec.Labels.Count;
            var band = plot.Width / Math.Max(1, count);
            if (_hover >= 0 && _hover < count)
            {
                dc.DrawRectangle(HoverBand, null, new Rect(plot.Left + (_hover * band), plot.Top, band, plot.Height));
            }

            // Etykiety osi X: co k-ta, żeby się nie nakładały.
            var widest = _spec.Labels.Select(l => Text(l, 11, TextSecondary, Font, 160).Width).DefaultIfEmpty(0).Max() + 8;
            var every = Math.Max(1, (int)Math.Ceiling(widest / Math.Max(1, band)));
            for (var i = 0; i < count; i += every)
            {
                var label = Text(_spec.Labels[i], 11, TextSecondary, Font, Math.Max(30, (band * every) - 4));
                dc.DrawText(label, new Point(plot.Left + (band * (i + 0.5)) - (label.Width / 2), plot.Bottom + 5));
            }

            if (xTitleHeight > 0)
            {
                var title = Text(_spec.XTitle, 11, TextSecondary, Font, plot.Width);
                dc.DrawText(title, new Point(plot.Left + ((plot.Width - title.Width) / 2), plot.Bottom + 22));
            }

            Func<int, double> cx = i => plot.Left + (band * (i + 0.5));
            dc.PushClip(new RectangleGeometry(new Rect(plot.Left - 1, plot.Top - 8, plot.Width + 2, plot.Height + 16)));
            RenderFillBetween(dc, cx, y);
            if (_bars.Count > 0)
            {
                if (_spec.Stacked)
                {
                    RenderStackedBars(dc, band, cx, y, min, max);
                }
                else
                {
                    RenderBars(dc, plot, band, y, min, max);
                }
            }

            RenderLines(dc, cx, y, min, max);
            dc.Pop();

            if (_hover >= 0 && _hover < count)
            {
                RenderTooltip(dc, _spec.Labels[_hover], TooltipRows(_hover), cx(_hover), plot.Top + 4);
            }
        }

        private List<Tuple<string, double?, Brush>> TooltipRows(int point)
        {
            var rows = new List<Tuple<string, double?, Brush>>();
            for (var s = 0; s < _spec.Series.Count; s++)
            {
                Brush brush = Brush(_bars.Contains(s) ? _spec.PointColor(s, point) : _spec.SeriesColor(s));
                rows.Add(Tuple.Create(SeriesName(s), Value(s, point), brush));
            }

            if (_spec.Stacked && _bars.Count >= 2)
            {
                var total = _bars.Select(s => Value(s, point)).Where(v => v.HasValue).Sum(v => v.Value);
                rows.Add(Tuple.Create("Razem", (double?)total, TextSecondary));
            }

            return rows;
        }

        private void RenderBars(DrawingContext dc, Rect plot, double band, Func<double, double> y, double min, double max)
        {
            var n = _bars.Count;
            const double gap = 2;
            var thickness = Math.Min(24, Math.Max(3, ((band * 0.7) - (gap * (n - 1))) / n));
            var group = (thickness * n) + (gap * (n - 1));
            var zero = y(Clamp(0, min, max));
            var count = _spec.Labels.Count;
            var showValues = _spec.ShowValues ?? (n == 1 && count <= 12 && band >= 34);
            for (var i = 0; i < count; i++)
            {
                var x = plot.Left + (band * i) + ((band - group) / 2);
                foreach (var s in _bars)
                {
                    if (Value(s, i) is double v)
                    {
                        var end = y(v);
                        dc.DrawGeometry(Brush(_spec.PointColor(s, i)), null, Bar(x, zero, end, thickness));
                        if (showValues && (n == 1 || thickness >= 10))
                        {
                            var label = Text(Format(v), n == 1 ? 11 : 10, TextSecondary, Font, Math.Max(thickness + 8, band / n));
                            var ly = end < zero ? end - label.Height - 2 : end + 2;
                            dc.DrawText(label, new Point(x + (thickness / 2) - (label.Width / 2), ly));
                        }
                    }

                    x += thickness + gap;
                }
            }
        }

        private void RenderStackedBars(DrawingContext dc, double band, Func<int, double> cx, Func<double, double> y, double min, double max)
        {
            var thickness = Math.Min(36, Math.Max(4, band * 0.6));
            for (var i = 0; i < _spec.Labels.Count; i++)
            {
                double positive = 0, negative = 0;
                var x = cx(i) - (thickness / 2);
                foreach (var s in _bars)
                {
                    if (!(Value(s, i) is double v) || v == 0)
                    {
                        continue;
                    }

                    double from, to;
                    if (v >= 0)
                    {
                        from = y(Clamp(positive, min, max));
                        positive += v;
                        to = y(Clamp(positive, min, max));
                    }
                    else
                    {
                        from = y(Clamp(negative, min, max));
                        negative += v;
                        to = y(Clamp(negative, min, max));
                    }

                    var top = Math.Min(from, to);
                    var h = Math.Abs(from - to);
                    if (h > 0)
                    {
                        dc.DrawRectangle(Brush(_spec.SeriesColor(s)), new Pen(Surface, 1), new Rect(x, top, thickness, h));
                    }
                }

                if (_spec.ShowValues == true && positive > 0)
                {
                    var label = Text(Format(positive), 11, TextSecondary, Font, band);
                    var edge = y(Clamp(positive, min, max));
                    dc.DrawText(label, new Point(cx(i) - (label.Width / 2), _spec.ReverseY ? edge + 2 : edge - label.Height - 2));
                }
            }
        }

        /// <summary>Słupek od linii zera: zaokrąglony koniec (4 px), prosty przy zerze.</summary>
        private static Geometry Bar(double x, double zero, double end, double width)
        {
            var up = end < zero;
            var length = Math.Abs(zero - end);
            var r = Math.Min(4, Math.Min(width / 2, length));
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                if (up)
                {
                    g.BeginFigure(new Point(x, zero), true, true);
                    g.LineTo(new Point(x, end + r), true, false);
                    g.ArcTo(new Point(x + r, end), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    g.LineTo(new Point(x + width - r, end), true, false);
                    g.ArcTo(new Point(x + width, end + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    g.LineTo(new Point(x + width, zero), true, false);
                }
                else
                {
                    g.BeginFigure(new Point(x, zero), true, true);
                    g.LineTo(new Point(x, end - r), true, false);
                    g.ArcTo(new Point(x + r, end), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                    g.LineTo(new Point(x + width - r, end), true, false);
                    g.ArcTo(new Point(x + width, end - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                    g.LineTo(new Point(x + width, zero), true, false);
                }
            }

            geometry.Freeze();
            return geometry;
        }

        /// <summary>Słupek poziomy od linii zera (w prawo albo w lewo), zaokrąglony koniec.</summary>
        private static Geometry HorizontalBar(double y, double zero, double end, double height)
        {
            var right = end > zero;
            var length = Math.Abs(end - zero);
            var r = Math.Min(4, Math.Min(height / 2, length));
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(zero, y), true, true);
                if (right)
                {
                    g.LineTo(new Point(end - r, y), true, false);
                    g.ArcTo(new Point(end, y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    g.LineTo(new Point(end, y + height - r), true, false);
                    g.ArcTo(new Point(end - r, y + height), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
                else
                {
                    g.LineTo(new Point(end + r, y), true, false);
                    g.ArcTo(new Point(end, y + r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                    g.LineTo(new Point(end, y + height - r), true, false);
                    g.ArcTo(new Point(end + r, y + height), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                }

                g.LineTo(new Point(zero, y + height), true, false);
            }

            geometry.Freeze();
            return geometry;
        }

        /// <summary>Wypełnienie między dwiema liniami (<c>wypelnij_miedzy</c>), rysowane pod liniami.</summary>
        private void RenderFillBetween(DrawingContext dc, Func<int, double> cx, Func<double, double> y)
        {
            var pair = _spec.FillBetween;
            if (pair == null || pair.Length != 2 || pair.Any(s => s < 0 || s >= _spec.Series.Count || !_lines.Contains(s)))
            {
                return;
            }

            var a = _spec.Series[pair[0]].Values;
            var b = _spec.Series[pair[1]].Values;
            var brush = Alpha(_spec.FillColor ?? _spec.SeriesColor(pair[0]), 0x30);
            var count = Math.Min(a.Count, b.Count);
            var i = 0;
            while (i < count)
            {
                var start = i;
                while (i < count && a[i].HasValue && b[i].HasValue)
                {
                    i++;
                }

                if (i - start >= 2)
                {
                    var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        g.BeginFigure(new Point(cx(start), y(a[start].Value)), true, true);
                        for (var k = start + 1; k < i; k++)
                        {
                            g.LineTo(new Point(cx(k), y(a[k].Value)), false, false);
                        }

                        for (var k = i - 1; k >= start; k--)
                        {
                            g.LineTo(new Point(cx(k), y(b[k].Value)), false, false);
                        }
                    }

                    geometry.Freeze();
                    dc.DrawGeometry(brush, null, geometry);
                }

                i = Math.Max(i, start + 1);
            }
        }

        private void RenderLines(DrawingContext dc, Func<int, double> cx, Func<double, double> y, double min, double max)
        {
            var baseline = y(Clamp(0, min, max));
            foreach (var s in _lines)
            {
                var series = _spec.Series[s];
                var color = _spec.SeriesColor(s);
                var pen = new Pen(Brush(color), 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (series.Dashed)
                {
                    pen.DashStyle = new DashStyle(new double[] { 3, 2.5 }, 0);
                }

                pen.Freeze();
                var values = series.Values;
                Point? last = null;
                var i = 0;
                while (i < values.Count)
                {
                    var start = i;
                    while (i < values.Count && values[i].HasValue)
                    {
                        i++;
                    }

                    if (i > start)
                    {
                        var points = Enumerable.Range(start, i - start).Select(k => new Point(cx(k), y(values[k].Value))).ToList();
                        if (series.Fill && points.Count >= 2)
                        {
                            var fill = new StreamGeometry();
                            using (var g = fill.Open())
                            {
                                g.BeginFigure(new Point(points[0].X, baseline), true, true);
                                foreach (var p in points)
                                {
                                    g.LineTo(p, false, false);
                                }

                                g.LineTo(new Point(points[points.Count - 1].X, baseline), false, false);
                            }

                            fill.Freeze();
                            dc.DrawGeometry(Alpha(color, 0x33), null, fill);
                        }

                        var line = new StreamGeometry();
                        using (var g = line.Open())
                        {
                            g.BeginFigure(points[0], false, false);
                            for (var k = 1; k < points.Count; k++)
                            {
                                g.LineTo(points[k], true, true);
                            }
                        }

                        line.Freeze();
                        dc.DrawGeometry(null, pen, line);
                        last = points[points.Count - 1];
                    }

                    i = Math.Max(i, start + 1); // przerwa w danych = przerwa w linii
                }

                var markers = new List<Point>();
                if (last is Point end)
                {
                    markers.Add(end);
                }

                if (Value(s, _hover) is double hv)
                {
                    markers.Add(new Point(cx(_hover), y(hv)));
                }

                foreach (var marker in markers)
                {
                    dc.DrawEllipse(Brush(color), new Pen(Surface, 2), marker, 4.5, 4.5); // pierścień w kolorze tła
                }
            }
        }

        private void RenderHorizontal(DrawingContext dc, Rect area)
        {
            Range(out var min, out var max, out var ticks);
            var labelWidth = Math.Min(area.Width * 0.4, _spec.Labels.Select(l => Text(l, 11, TextSecondary, Font, 400).Width).DefaultIfEmpty(0).Max()) + 8;
            var yTitleHeight = string.IsNullOrWhiteSpace(_spec.YTitle) ? 0 : 16;
            var count = _spec.Labels.Count;
            var n = _bars.Count;
            var plotHeight = Math.Max(40, area.Height - 4 - 18 - yTitleHeight);
            var band = plotHeight / Math.Max(1, count);
            // Podpisy wartości stoją za końcem słupka — zostaw na nie miejsce z prawej, żeby nie ucinał ich brzeg wykresu.
            var showValues = _spec.Stacked ? _spec.ShowValues == true : _spec.ShowValues ?? (n == 1 && count <= 40 && band >= 14);
            var valuePad = showValues
                ? _bars.SelectMany(s => _spec.Series[s].Values).Where(v => v.HasValue).Select(v => Text(Format(v.Value), 11, TextSecondary, Font, 120).Width).DefaultIfEmpty(0).Max() + 8
                : 0;
            var plot = new Rect(area.Left + labelWidth, area.Top + 4, Math.Max(40, area.Width - labelWidth - valuePad), plotHeight);
            _plot = plot;
            var span = max - min;
            Func<double, double> x = v => _spec.ReverseY ? plot.Right - ((v - min) / span * plot.Width) : plot.Left + ((v - min) / span * plot.Width);

            foreach (var tick in ticks)
            {
                var tx = Math.Round(x(tick)) + 0.5;
                dc.DrawLine(new Pen(tick == 0 ? Axis : Grid, 1), new Point(tx, plot.Top), new Point(tx, plot.Bottom));
                var label = Text(Format(tick), 11, TextSecondary, Font, 200);
                dc.DrawText(label, new Point(tx - (label.Width / 2), plot.Bottom + 4));
            }

            if (yTitleHeight > 0)
            {
                var title = Text(_spec.YTitle, 11, TextSecondary, Font, plot.Width);
                dc.DrawText(title, new Point(plot.Left + ((plot.Width - title.Width) / 2), plot.Bottom + 20));
            }

            Func<int, double> cy = i => plot.Top + (band * (i + 0.5));
            if (_hover >= 0 && _hover < count)
            {
                dc.DrawRectangle(HoverBand, null, new Rect(plot.Left, plot.Top + (_hover * band), plot.Width, band));
            }

            var every = Math.Max(1, (int)Math.Ceiling(14 / Math.Max(1, band)));
            for (var i = 0; i < count; i += every)
            {
                var label = Text(_spec.Labels[i], 11, TextSecondary, Font, labelWidth - 8);
                dc.DrawText(label, new Point(plot.Left - 6 - label.Width, cy(i) - (label.Height / 2)));
            }

            dc.PushClip(new RectangleGeometry(new Rect(plot.Left - 8, plot.Top - 1, plot.Width + 16 + valuePad, plot.Height + 2)));
            var zero = x(Clamp(0, min, max));
            if (_spec.Stacked)
            {
                var thickness = Math.Min(28, Math.Max(4, band * 0.6));
                for (var i = 0; i < count; i++)
                {
                    double positive = 0, negative = 0;
                    foreach (var s in _bars)
                    {
                        if (!(Value(s, i) is double v) || v == 0)
                        {
                            continue;
                        }

                        double from, to;
                        if (v >= 0)
                        {
                            from = x(Clamp(positive, min, max));
                            positive += v;
                            to = x(Clamp(positive, min, max));
                        }
                        else
                        {
                            from = x(Clamp(negative, min, max));
                            negative += v;
                            to = x(Clamp(negative, min, max));
                        }

                        var w = Math.Abs(from - to);
                        if (w > 0)
                        {
                            dc.DrawRectangle(Brush(_spec.SeriesColor(s)), new Pen(Surface, 1), new Rect(Math.Min(from, to), cy(i) - (thickness / 2), w, thickness));
                        }
                    }

                    if (_spec.ShowValues == true && positive > 0)
                    {
                        var label = Text(Format(positive), 11, TextSecondary, Font, 120);
                        var edge = x(Clamp(positive, min, max));
                        dc.DrawText(label, new Point(_spec.ReverseY ? edge - 4 - label.Width : edge + 4, cy(i) - (label.Height / 2)));
                    }
                }
            }
            else
            {
                const double gap = 2;
                var thickness = Math.Min(24, Math.Max(3, ((band * 0.7) - (gap * (n - 1))) / n));
                var group = (thickness * n) + (gap * (n - 1));
                for (var i = 0; i < count; i++)
                {
                    var y = cy(i) - (group / 2);
                    foreach (var s in _bars)
                    {
                        if (Value(s, i) is double v)
                        {
                            var end = x(v);
                            dc.DrawGeometry(Brush(_spec.PointColor(s, i)), null, HorizontalBar(y, zero, end, thickness));
                            if (showValues && (n == 1 || thickness >= 10))
                            {
                                var label = Text(Format(v), n == 1 ? 11 : 10, TextSecondary, Font, 120);
                                var lx = end >= zero ? end + 4 : end - 4 - label.Width;
                                dc.DrawText(label, new Point(lx, y + (thickness / 2) - (label.Height / 2)));
                            }
                        }

                        y += thickness + gap;
                    }
                }
            }

            dc.Pop();
            if (_hover >= 0 && _hover < count)
            {
                RenderTooltip(dc, _spec.Labels[_hover], TooltipRows(_hover), plot.Left + (plot.Width * 0.35), cy(_hover) + (band / 2) + 2);
            }
        }

        private void RenderPie(DrawingContext dc, Rect area)
        {
            var total = _slices.Sum(s => s.Value);
            var radius = Math.Max(30, Math.Min((area.Height / 2) - 4, area.Width * 0.22));
            var center = new Point(area.Left + radius + 6, area.Top + (area.Height / 2));
            _pieCenter = center;
            _pieRadius = radius;
            _pieInner = _spec.Donut ? radius * 0.55 : 0;
            var angle = -90.0;
            for (var i = 0; i < _slices.Count; i++)
            {
                var sweep = total <= 0 ? 0 : _slices[i].Value / total * 360;
                var r = i == _hover ? radius + 4 : radius;
                dc.DrawGeometry(SliceBrush(_slices[i]), new Pen(Surface, 2), Slice(center, r, _pieInner, angle, sweep));
                angle += sweep;
            }

            if (_spec.Donut)
            {
                var sum = Text(Format(total), 13, TextPrimary, FontBold, (_pieInner * 2) - 8);
                var unit = Text(_spec.YTitle.Length > 0 ? _spec.YTitle : "razem", 10, TextSecondary, Font, (_pieInner * 2) - 8);
                dc.DrawText(sum, new Point(center.X - (sum.Width / 2), center.Y - sum.Height + 2));
                dc.DrawText(unit, new Point(center.X - (unit.Width / 2), center.Y + 3));
            }

            // Legenda z wartościami i udziałem (tekst w kolorach tekstu, kolor tylko w kwadraciku).
            var x = center.X + radius + 24;
            var lineHeight = 20.0;
            var y = Math.Max(area.Top, center.Y - (_slices.Count * lineHeight / 2));
            for (var i = 0; i < _slices.Count; i++)
            {
                var slice = _slices[i];
                var share = total <= 0 ? 0 : slice.Value / total;
                var name = Text(slice.Name, 12, i == _hover ? TextPrimary : TextSecondary, i == _hover ? FontBold : Font, Math.Max(60, ActualWidth - x - 120));
                var value = Text(Format(slice.Value) + "  ·  " + share.ToString("0.#%", Pl), 12, TextPrimary, Font, 140);
                dc.DrawRoundedRectangle(SliceBrush(slice), null, new Rect(x, y + 4, 10, 10), 2, 2);
                dc.DrawText(name, new Point(x + 16, y));
                dc.DrawText(value, new Point(Math.Max(x + 16 + name.Width + 12, ActualWidth - 14 - value.Width), y));
                y += lineHeight;
            }
        }

        private Brush SliceBrush(PieSlice slice)
        {
            if (slice.Point < 0)
            {
                return Brush(ChartColors.Rest);
            }

            if (_spec.PointColors != null && slice.Point < _spec.PointColors.Count && _spec.PointColors[slice.Point] != null)
            {
                return Brush(_spec.PointColors[slice.Point]);
            }

            if (_spec.Groups != null && slice.Point < _spec.Groups.Count)
            {
                return Brush(_spec.GroupColor(_spec.Groups[slice.Point]));
            }

            return Brush(ChartColors.Of(slice.Slot));
        }

        private static Geometry Slice(Point center, double radius, double inner, double startDegrees, double sweepDegrees)
        {
            if (sweepDegrees >= 359.99)
            {
                var outer = new EllipseGeometry(center, radius, radius);
                if (inner <= 0)
                {
                    outer.Freeze();
                    return outer;
                }

                var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outer, new EllipseGeometry(center, inner, inner));
                ring.Freeze();
                return ring;
            }

            Point At(double degrees, double r) => new Point(center.X + (r * Math.Cos(degrees * Math.PI / 180)), center.Y + (r * Math.Sin(degrees * Math.PI / 180)));
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                if (inner <= 0)
                {
                    g.BeginFigure(center, true, true);
                    g.LineTo(At(startDegrees, radius), true, true);
                    g.ArcTo(At(startDegrees + sweepDegrees, radius), new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, true);
                }
                else
                {
                    g.BeginFigure(At(startDegrees, inner), true, true);
                    g.LineTo(At(startDegrees, radius), true, true);
                    g.ArcTo(At(startDegrees + sweepDegrees, radius), new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, true);
                    g.LineTo(At(startDegrees + sweepDegrees, inner), true, true);
                    g.ArcTo(At(startDegrees, inner), new Size(inner, inner), 0, sweepDegrees > 180, SweepDirection.Counterclockwise, true, true);
                }
            }

            geometry.Freeze();
            return geometry;
        }

        /// <summary>Kawałki kołowego: najwyżej <see cref="ChartSpec.MaxSlices"/>, reszta jako „Pozostałe” (szary); zera i ujemne pomijane.</summary>
        private static List<PieSlice> Slices(ChartSpec spec)
        {
            var values = spec.Series[0].Values;
            var items = spec.Labels.Select((l, i) => new PieSlice { Name = l, Value = i < values.Count && values[i] is double v && v > 0 ? v : 0, Point = i })
                .Where(s => s.Value > 0).ToList();
            if (items.Count <= spec.MaxSlices)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    items[i].Slot = i;
                }

                return items;
            }

            var kept = new HashSet<PieSlice>(items.OrderByDescending(s => s.Value).Take(spec.MaxSlices - 1));
            var top = items.Where(kept.Contains).ToList();
            for (var i = 0; i < top.Count; i++)
            {
                top[i].Slot = i;
            }

            top.Add(new PieSlice { Name = "Pozostałe", Value = items.Where(s => !kept.Contains(s)).Sum(s => s.Value), Point = -1, Slot = -1 });
            return top;
        }

        private void RenderTooltip(DrawingContext dc, string title, List<Tuple<string, double?, Brush>> rows, double anchorX, double anchorY)
        {
            var header = Text(title, 12, TextPrimary, FontBold, 260);
            var lines = rows.Select(r => Tuple.Create(Text(r.Item1, 12, TextSecondary, Font, 200), Text(r.Item2.HasValue ? Format(r.Item2.Value) : "—", 12, TextPrimary, FontBold, 120), r.Item3)).ToList();
            var width = Math.Max(header.Width, lines.Select(l => 16 + l.Item1.Width + 16 + l.Item2.Width).DefaultIfEmpty(0).Max()) + 20;
            var height = header.Height + lines.Sum(l => l.Item1.Height + 2) + 14;
            var x = anchorX + 12 + width > ActualWidth - 6 ? anchorX - 12 - width : anchorX + 12;
            var box = new Rect(Math.Max(4, x), Math.Max(4, Math.Min(anchorY, ActualHeight - height - 4)), width, height);
            dc.DrawRoundedRectangle(Surface, new Pen(SurfaceBorder, 1), box, 6, 6);
            dc.DrawText(header, new Point(box.Left + 10, box.Top + 6));
            var y = box.Top + 8 + header.Height;
            foreach (var line in lines)
            {
                dc.DrawRoundedRectangle(line.Item3, null, new Rect(box.Left + 10, y + 4, 10, 10), 2, 2);
                dc.DrawText(line.Item1, new Point(box.Left + 26, y));
                dc.DrawText(line.Item2, new Point(box.Right - 10 - line.Item2.Width, y));
                y += line.Item1.Height + 2;
            }
        }

        private int HitTest(Point p)
        {
            if (_spec.Kind == ChartKind.Pie)
            {
                var dx = p.X - _pieCenter.X;
                var dy = p.Y - _pieCenter.Y;
                var distance = (dx * dx) + (dy * dy);
                if (_pieRadius <= 0 || distance > (_pieRadius + 4) * (_pieRadius + 4) || distance < _pieInner * _pieInner)
                {
                    return -1;
                }

                var angle = ((Math.Atan2(dy, dx) * 180 / Math.PI) + 90 + 360) % 360;
                var total = _slices.Sum(s => s.Value);
                var start = 0.0;
                for (var i = 0; i < _slices.Count; i++)
                {
                    var sweep = total <= 0 ? 0 : _slices[i].Value / total * 360;
                    if (angle >= start && angle < start + sweep)
                    {
                        return i;
                    }

                    start += sweep;
                }

                return -1;
            }

            var count = Math.Max(1, _spec.Labels.Count);
            if (_spec.Horizontal)
            {
                if (_plot.Height <= 0 || p.Y < _plot.Top || p.Y > _plot.Bottom || p.X < _plot.Left - 120 || p.X > _plot.Right + 4)
                {
                    return -1;
                }

                var row = (int)((p.Y - _plot.Top) / (_plot.Height / count));
                return Math.Max(0, Math.Min(_spec.Labels.Count - 1, row));
            }

            if (_plot.Width <= 0 || p.X < _plot.Left || p.X > _plot.Right || p.Y < _plot.Top - 4 || p.Y > _plot.Bottom + 20)
            {
                return -1;
            }

            var index = (int)((p.X - _plot.Left) / (_plot.Width / count));
            return Math.Max(0, Math.Min(_spec.Labels.Count - 1, index));
        }

        /// <summary>Równe podziałki osi: 1, 2, 2,5 albo 5 × 10^n.</summary>
        internal static List<double> NiceTicks(double min, double max, int count)
        {
            var raw = (max - min) / Math.Max(1, count);
            var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            var step = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * magnitude).First(s => s >= raw);
            var start = Math.Floor(min / step) * step;
            var end = Math.Ceiling(max / step) * step;
            var ticks = new List<double>();
            for (var t = start; t <= end + (step / 2); t += step)
            {
                ticks.Add(Math.Abs(t) < step / 1e6 ? 0 : t);
            }

            return ticks;
        }

        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        /// <summary>„1 234,5”; od 10 000 na osi krótko: „12 tys.”, „1,5 mln”.</summary>
        private static string Format(double value)
        {
            var abs = Math.Abs(value);
            if (abs >= 1_000_000)
            {
                return (value / 1_000_000).ToString("0.#", Pl) + " mln";
            }

            if (abs >= 10_000)
            {
                return (value / 1000).ToString("0.#", Pl) + " tys.";
            }

            return value.ToString(abs >= 100 || value == Math.Round(value) ? "#,0" : abs < 1 ? "0.###" : "#,0.##", Pl);
        }

        private FormattedText Text(string text, double size, Brush brush, Typeface typeface, double maxWidth)
        {
            var formatted = new FormattedText(text ?? "", Pl, FlowDirection.LeftToRight, typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(10, maxWidth),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            return formatted;
        }

        /// <summary>Pędzel z „#rrggbb” (zamrożony, z pamięci podręcznej).</summary>
        private static SolidColorBrush Brush(string hex)
        {
            var key = hex ?? ChartColors.Rest;
            lock (Brushes)
            {
                if (!Brushes.TryGetValue(key, out var brush))
                {
                    ChartColors.Split(key, out var r, out var g, out var b);
                    brush = Frozen(Color.FromRgb(r, g, b));
                    Brushes[key] = brush;
                }

                return brush;
            }
        }

        /// <summary>Półprzezroczysty pędzel w kolorze serii (wypełnienia).</summary>
        private static SolidColorBrush Alpha(string hex, byte alpha)
        {
            ChartColors.Split(hex, out var r, out var g, out var b);
            return Frozen(Color.FromArgb(alpha, r, g, b));
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
