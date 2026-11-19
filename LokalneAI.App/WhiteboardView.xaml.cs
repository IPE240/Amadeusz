using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LokalneAI.Harness.Llm;
using LokalneAI.Harness.Ocr;
using LokalneAI.Harness.Session;
using LokalneAI.Harness.Whiteboard;
using LokalneAI.Harness.Wpf;
using Shapes = System.Windows.Shapes;

namespace LokalneAI.App;

/// <summary>
/// Zakładka Whiteboard: tablica z narzędziami jak w Lightshocie (pióro, linia, strzałka, prostokąt, marker, tekst, kolor),
/// wklejanie obrazków ze skalowaniem proporcjonalnym, Capture (zrzut fragmentu ekranu), pole polecenia pod tablicą
/// i odpowiedzi jak w zakładce Code. Model dostaje opis tablicy (<see cref="BoardSnapshot"/>: teksty dosłownie, rysunki
/// ze współrzędnymi, OCR obrazów i odręcznych napisów), a model z obsługą obrazów — także jej obraz; rysuje na osobnej,
/// wektorowej warstwie (<see cref="IWhiteboardSurface"/>).
/// </summary>
public partial class WhiteboardView : UserControl, IWhiteboardSurface
{
    /// <summary>Umiejętność tablicy (folder <c>profile\whiteboard</c>); poza listą umiejętności zakładki Code.</summary>
    public const string SkillName = "whiteboard";

    /// <summary>Rodzaj kształtu zapisany przy pociągnięciu: pen, marker, line, arrow, rect.</summary>
    private static readonly Guid KindKey = new("6d1f4b0e-5d0b-4c1e-9a55-0f2f7a3c9b11");

    private static readonly FontFamily BoardFont = new("Segoe UI");

    /// <summary>Jedna czcionka i jeden rozmiar tekstów użytkownika (bez formatowania).</summary>
    private const double TextSize = 18;

    /// <summary>Najwięcej kroków „Cofnij”.</summary>
    private const int MaxUndo = 100;

    private readonly WhiteboardExecutor _executor;
    private readonly List<BoardState> _undo = [];
    private readonly List<BoardState> _redo = [];
    private readonly List<BoardItem> _aiItems = [];
    private BoardState _last;
    private Func<HarnessContext>? _contextProvider;
    private HarnessView? _code;
    private string _tool = "pen";
    private Color _penColor = (Color)ColorConverter.ConvertFromString("#DC2626");
    private Color _markerColor = (Color)ColorConverter.ConvertFromString("#FACC15");
    private double _width = 4;
    private Stroke? _shape;
    private Point _start;
    private bool _erased;
    private bool _syncing;
    private bool _loading;
    private TextBox? _editor;
    private TextBlock? _editing;
    private int _imageCounter;
    private ILlmClient? _llm;
    private string _sessionKey = "";
    private List<LlmMessage>? _history;
    private string? _handwritingKey;
    private (string? Text, double Confidence) _handwriting;
    private double _boardWidth;
    private double _boardHeight;

    public WhiteboardView()
    {
        InitializeComponent();
        _executor = new WhiteboardExecutor(this);
        Ink.Focusable = true;
        BuildColors();
        Ink.StrokeCollected += Ink_StrokeCollected;
        Ink.StrokeErased += (_, _) => _erased = true;
        Ink.SelectionMoved += (_, _) => Commit();
        Ink.SelectionResized += (_, _) => Commit();
        Ink.SelectionResizing += Ink_SelectionResizing;
        Ink.PreviewMouseLeftButtonDown += Ink_PreviewMouseLeftButtonDown;
        Ink.PreviewMouseMove += Ink_PreviewMouseMove;
        Ink.PreviewMouseLeftButtonUp += Ink_PreviewMouseLeftButtonUp;
        Board.SizeChanged += (_, _) =>
        {
            _boardWidth = Board.ActualWidth;
            _boardHeight = Board.ActualHeight;
        };
        PreviewKeyDown += View_PreviewKeyDown;
        DataObject.AddPastingHandler(PromptBox, Prompt_Pasting);
        Answers.ShowInput = false;
        Answers.NewSessionClicked += (_, _) => _history = null;
        _last = Capture();
        ApplyEditingMode();
        UpdateHint();
    }

    /// <summary>Wiąże tablicę z oknem: model i ustawienia z nagłówka, sesja przez zakładkę Code (wykonawcy, uprawnienia).</summary>
    public void Attach(Func<HarnessContext> contextProvider, HarnessView code)
    {
        _contextProvider = contextProvider;
        _code = code;
        var settings = contextProvider().Settings;
        _loading = true;
        CodeSkillsBox.IsChecked = settings.WhiteboardCodeSkills;
        _loading = false;
        Answers.ShowTokens = settings.ShowTokens;
        Answers.ShowModelSteps = settings.ShowSteps;
        Answers.SessionFactory = CreateSession;
        RefreshStatus();
    }

    /// <summary>Opis modelu i umiejętności pod tablicą (po zmianie modelu, umiejętności w Code albo przy wejściu na zakładkę).</summary>
    public void RefreshStatus()
    {
        if (_contextProvider?.Invoke() is not { } context)
        {
            return;
        }

        var skills = _code?.CodeSkills ?? [];
        CodeSkillsBox.Content = "z umiejętnościami z Code" + (skills.Count == 0 ? "" : " (" + string.Join(", ", skills) + ")");
        BoardStatus.Text = "Model: " + context.ModelLabel + " · " + context.ComputeWhere + " · "
            + (context.ModelSeesImages ? "widzi obrazy — dostanie obraz tablicy i jej opis" : "nie widzi obrazów — dostanie opis tablicy i tekst z OCR");
        Answers.ShowTokens = context.Settings.ShowTokens;
        Answers.ShowModelSteps = context.Settings.ShowSteps;
    }

    /// <summary>Zamyka sesję tablicy i klienta modelu (koniec programu).</summary>
    public void Shutdown()
    {
        Answers.NewSession();
        (_llm as IDisposable)?.Dispose();
        _llm = null;
    }

    // ───────────────────────────── Sesja i polecenie ─────────────────────────────

    private HarnessSession CreateSession()
    {
        if (_code is null)
        {
            throw new InvalidOperationException("Tablica nie jest podpięta do okna.");
        }

        (_llm as IDisposable)?.Dispose();
        _llm = null;
        var (session, llm) = _code.CreateBoardSession(_executor, Answers, _history, CodeSkillsBox.IsChecked == true);
        _llm = llm;
        _sessionKey = SessionKey();
        _history = null;
        return session;
    }

    private string SessionKey()
    {
        if (_contextProvider?.Invoke() is not { } c)
        {
            return "";
        }

        var skills = CodeSkillsBox.IsChecked == true ? string.Join(",", _code?.CodeSkills ?? []) : "";
        return (c.Cloud?.Id ?? "") + "|" + c.Model + "|" + c.OllamaUrl + "|" + c.ContextLength + "|" + c.Temperature + "|" + skills + "|" + (c.Web is null ? "" : "web");
    }

    /// <summary>Inny model albo umiejętności niż w bieżącej sesji: następne polecenie idzie do nowej sesji z dotychczasową rozmową.</summary>
    private void RebuildIfChanged()
    {
        if (Answers.Session is { } session && !Answers.IsBusy && SessionKey() != _sessionKey)
        {
            _history = session.History.ToList();
            Answers.ResetSession();
            Answers.AddInfo("Od teraz: " + (_contextProvider?.Invoke().ModelLabel ?? "inny model") + (CodeSkillsBox.IsChecked == true ? " z umiejętnościami z Code" : "") + " (rozmowa trwa dalej).");
        }
    }

    private async void Submit_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private void Stop_Click(object sender, RoutedEventArgs e) => Answers.Cancel();

    /// <summary>
    /// Zatwierdź: opis tablicy (teksty dosłownie, rysunki, OCR obrazów i odręcznych napisów) + polecenie; model z obsługą
    /// obrazów dostaje też obraz tablicy. W panelu odpowiedzi widać samo polecenie, a opis tablicy — skrócony pod nim.
    /// </summary>
    internal async Task<TurnResult?> SubmitAsync(string? prompt = null)
    {
        var text = (prompt ?? PromptBox.Text).Trim();
        if (text.Length == 0)
        {
            SetStatus("Wpisz polecenie pod tablicą, potem Zatwierdź.");
            PromptBox.Focus();
            return null;
        }

        if (Answers.IsBusy)
        {
            return null;
        }

        CommitEditor();
        SetBusy(true);
        try
        {
            RebuildIfChanged();
            var context = _contextProvider?.Invoke();
            foreach (var info in Images().Where(i => i.Ocr is { IsCompleted: false }))
            {
                SetStatus("Czekam na OCR obrazu " + info.Name + "…");
                await info.Ocr!;
            }

            var snapshot = BuildSnapshot();
            SetStatus("Odczytuję odręczne napisy…");
            (snapshot.HandwritingText, snapshot.HandwritingConfidence) = await HandwritingAsync();
            List<byte[]>? images = null;
            if (context?.ModelSeesImages == true && !snapshot.IsEmpty)
            {
                images = [RenderBoardPng()];
                snapshot.ImageAttached = true;
            }

            var description = snapshot.Describe();
            if (prompt is null)
            {
                PromptBox.Clear();
            }

            RefreshStatus();
            var shown = description.Length > 600 ? description[..600] + "…" : description;
            return await Answers.SendAsync(description + "\n\n" + text, images, text, shown);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Zatwierdź to async void — wyjątek stąd wywróciłby okno („Nieoczekiwany błąd”).
            Answers.AddInfo("Nie wysłano: " + e.Message);
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        SubmitButton.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
    }

    private void SetStatus(string text) => BoardStatus.Text = text;

    private void Prompt_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter zatwierdza (jak w Code i Czacie), Shift+Enter — nowa linia, Esc przerywa.
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (SubmitButton.IsEnabled)
            {
                Submit_Click(sender, e);
            }
        }
        else if (e.Key == Key.Escape)
        {
            Answers.Cancel();
        }
    }

    /// <summary>Obrazek wklejony do pola polecenia trafia na tablicę (tekst wkleja się jak zwykle).</summary>
    private void Prompt_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!ClipboardImages.IsImage(e.DataObject))
        {
            return;
        }

        e.CancelCommand();
        if (ClipboardImages.ReadPng() is { } png)
        {
            AddImage(png, null, false);
        }
    }

    private void CodeSkills_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _contextProvider?.Invoke() is not { } context)
        {
            return;
        }

        context.Settings.WhiteboardCodeSkills = CodeSkillsBox.IsChecked == true;
        context.Save?.Invoke();
    }

    // ───────────────────────────── Opis tablicy dla modelu ─────────────────────────────

    private BoardSnapshot BuildSnapshot()
    {
        var snapshot = new BoardSnapshot { Width = Board.ActualWidth, Height = Board.ActualHeight };
        foreach (var child in Ink.Children.OfType<FrameworkElement>().Where(c => c != _editor))
        {
            var left = Left(child);
            var top = Top(child);
            if (child is TextBlock block)
            {
                snapshot.UserItems.Add(new BoardItem
                {
                    Kind = BoardItemKind.Text,
                    X = left,
                    Y = top,
                    X2 = left + block.ActualWidth,
                    Y2 = top + block.ActualHeight,
                    Text = block.Text,
                    Color = Hex((block.Foreground as SolidColorBrush)?.Color ?? Colors.Black),
                });
            }
            else if (child is Image { Tag: BoardImage info } image)
            {
                snapshot.UserItems.Add(new BoardItem
                {
                    Kind = BoardItemKind.Image,
                    X = left,
                    Y = top,
                    X2 = left + image.ActualWidth,
                    Y2 = top + image.ActualHeight,
                    Text = info.Name,
                    OcrText = info.OcrText,
                    OcrConfidence = info.OcrConfidence,
                });
            }
        }

        foreach (var stroke in Ink.Strokes)
        {
            var points = stroke.StylusPoints;
            if (points.Count == 0)
            {
                continue;
            }

            var color = Hex(stroke.DrawingAttributes.Color);
            var first = points[0];
            var kind = Kind(stroke);
            var item = kind switch
            {
                "line" => new BoardItem { Kind = BoardItemKind.Line, X = first.X, Y = first.Y, X2 = points[^1].X, Y2 = points[^1].Y },
                "arrow" when points.Count >= 2 => new BoardItem { Kind = BoardItemKind.Arrow, X = first.X, Y = first.Y, X2 = points[1].X, Y2 = points[1].Y },
                "rect" => new BoardItem
                {
                    Kind = BoardItemKind.Rectangle,
                    X = points.Min(p => p.X),
                    Y = points.Min(p => p.Y),
                    X2 = points.Max(p => p.X),
                    Y2 = points.Max(p => p.Y),
                },
                _ => new BoardItem
                {
                    Kind = kind == "marker" ? BoardItemKind.Marker : BoardItemKind.Pen,
                    Points = points.SelectMany(p => new[] { p.X, p.Y }).ToList(),
                },
            };
            item.Color = color;
            snapshot.UserItems.Add(item);
        }

        foreach (var item in _aiItems)
        {
            snapshot.ModelItems.Add(item);
        }

        return snapshot;
    }

    /// <summary>
    /// OCR odręcznych napisów: same pociągnięcia pióra, czarne na białym, w powiększeniu. Wynik wraca tylko przy rozsądnej
    /// pewności i kilku literach/cyfrach (rysunek belki dałby śmieci) i jest pamiętany, dopóki pióro się nie zmieni.
    /// </summary>
    private async Task<(string? Text, double Confidence)> HandwritingAsync()
    {
        var settings = _contextProvider?.Invoke().Settings;
        var pen = Ink.Strokes.Where(s => Kind(s) == "pen").ToList();
        if (settings is null || !settings.OcrOnPaste || pen.Count == 0)
        {
            return (null, 0);
        }

        var key = string.Join(";", pen.Select(s => s.GetBounds().ToString(CultureInfo.InvariantCulture)));
        if (key == _handwritingKey)
        {
            return _handwriting;
        }

        var png = RenderStrokes(pen);
        try
        {
            var options = OcrViewModel.ToOptions(settings, [Path.GetTempPath()], Path.GetTempPath());
            var result = await Task.Run(() => new OcrEngine(options).RecognizePng(png));
            var text = result.Text.Trim();
            var useful = text.Count(char.IsLetterOrDigit) >= 2 && result.MeanConfidence >= 0.45;
            _handwriting = useful ? (text, result.MeanConfidence) : (null, result.MeanConfidence);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // OCR nie może zablokować polecenia: bez odręcznych napisów model ma i tak opis tablicy (i obraz, gdy widzi obrazy).
            _handwriting = (null, 0);
            SetStatus("OCR odręcznych napisów nie zadziałał: " + e.Message);
        }

        _handwritingKey = key;
        return _handwriting;
    }

    /// <summary>Pociągnięcia pióra jako PNG do OCR: czarne na białym, dwa razy większe, z marginesem.</summary>
    private static byte[] RenderStrokes(IReadOnlyList<Stroke> strokes)
    {
        const double scale = 2;
        const double margin = 16;
        var bounds = strokes.Select(s => s.GetBounds()).Aggregate(Rect.Union);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, (bounds.Width + 2 * margin) * scale, (bounds.Height + 2 * margin) * scale));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(margin - bounds.X, margin - bounds.Y));
            foreach (var stroke in strokes)
            {
                var copy = stroke.Clone();
                copy.DrawingAttributes.Color = Colors.Black;
                copy.DrawingAttributes.Width = Math.Max(3, copy.DrawingAttributes.Width);
                copy.DrawingAttributes.Height = Math.Max(3, copy.DrawingAttributes.Height);
                copy.Draw(dc);
            }
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling((bounds.Width + 2 * margin) * scale), (int)Math.Ceiling((bounds.Height + 2 * margin) * scale), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return ClipboardImages.ToPng(bitmap);
    }

    /// <summary>Obraz tablicy dla modelu (obie warstwy, bez zaznaczenia), najwyżej 1568 px na dłuższym boku.</summary>
    internal byte[] RenderBoardPng()
    {
        var hint = EmptyHint.Visibility;
        EmptyHint.Visibility = Visibility.Collapsed;
        Board.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(Board.ActualWidth)), Math.Max(1, (int)Math.Ceiling(Board.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(Board);
        EmptyHint.Visibility = hint;
        return ClipboardImages.Shrink(ClipboardImages.ToPng(bitmap));
    }

    // ───────────────────────────── Warstwa Amadeusza (IWhiteboardSurface) ─────────────────────────────

    double IWhiteboardSurface.Width => _boardWidth;

    double IWhiteboardSurface.Height => _boardHeight;

    Task IWhiteboardSurface.DrawAsync(IReadOnlyList<BoardItem> items, CancellationToken cancellationToken)
        => Dispatcher.InvokeAsync(() =>
        {
            foreach (var item in items)
            {
                _aiItems.Add(item);
                AiLayer.Children.Add(CreateShape(item));
            }

            ShowAiBox.IsChecked = true;
            UpdateHint();
        }).Task;

    Task IWhiteboardSurface.ClearModelLayerAsync(CancellationToken cancellationToken) => Dispatcher.InvokeAsync(ClearAi).Task;

    /// <summary>Kształt wektorowy WPF dla elementu narysowanego przez model.</summary>
    private static UIElement CreateShape(BoardItem item)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Color));
        brush.Freeze();
        const double thickness = 3;
        switch (item.Kind)
        {
            case BoardItemKind.Line:
                return new Shapes.Line
                {
                    X1 = item.X, Y1 = item.Y, X2 = item.X2, Y2 = item.Y2, Stroke = brush, StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                };
            case BoardItemKind.Arrow:
                return new Shapes.Path { Data = ArrowGeometry(new Point(item.X, item.Y), new Point(item.X2, item.Y2), 14), Stroke = brush, Fill = brush, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round };
            case BoardItemKind.Rectangle:
            case BoardItemKind.Ellipse:
                Shapes.Shape box = item.Kind == BoardItemKind.Rectangle ? new Shapes.Rectangle() : new Shapes.Ellipse();
                box.Width = Math.Max(1, item.Right - item.Left);
                box.Height = Math.Max(1, item.Bottom - item.Top);
                box.Stroke = brush;
                box.StrokeThickness = thickness;
                Canvas.SetLeft(box, item.Left);
                Canvas.SetTop(box, item.Top);
                return box;
            case BoardItemKind.Polyline:
                var points = new PointCollection();
                for (var i = 0; i + 1 < (item.Points?.Count ?? 0); i += 2)
                {
                    points.Add(new Point(item.Points![i], item.Points[i + 1]));
                }

                return new Shapes.Polyline { Points = points, Stroke = brush, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round };
            default:
                var text = new TextBlock
                {
                    Text = item.Text ?? "",
                    Foreground = brush,
                    FontFamily = BoardFont,
                    FontSize = item.FontSize ?? TextSize,
                    FontWeight = FontWeights.SemiBold,
                    Background = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
                    Padding = new Thickness(3, 0, 3, 1),
                };
                Canvas.SetLeft(text, item.X);
                Canvas.SetTop(text, item.Y);
                return text;
        }
    }

    /// <summary>Strzałka: odcinek i wypełniony grot w punkcie końcowym.</summary>
    private static Geometry ArrowGeometry(Point from, Point to, double head)
    {
        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        Point Side(double turn) => new(to.X - head * Math.Cos(angle + turn), to.Y - head * Math.Sin(angle + turn));
        var baseMid = new Point(to.X - head * 0.8 * Math.Cos(angle), to.Y - head * 0.8 * Math.Sin(angle));
        var geometry = new PathGeometry();
        geometry.Figures.Add(new PathFigure(from, [new LineSegment(baseMid, true)], false) { IsFilled = false });
        geometry.Figures.Add(new PathFigure(to, [new LineSegment(Side(0.42), true), new LineSegment(Side(-0.42), true)], true) { IsFilled = true });
        geometry.Freeze();
        return geometry;
    }

    private void ClearAi()
    {
        _aiItems.Clear();
        AiLayer.Children.Clear();
        UpdateHint();
    }

    private void ClearAi_Click(object sender, RoutedEventArgs e) => ClearAi();

    private void ShowAi_Changed(object sender, RoutedEventArgs e)
    {
        if (AiLayer is not null)
        {
            AiLayer.Visibility = ShowAiBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ───────────────────────────── Narzędzia ─────────────────────────────

    private void BuildColors()
    {
        foreach (var (name, hex) in BoardColors.Palette.Select(p => (p.Key, p.Value)))
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ColorSwatch"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Tag = hex,
                ToolTip = name.Replace("pomaranczowy", "pomarańczowy").Replace("zolty", "żółty"),
            };
            swatch.Checked += Color_Checked;
            ColorsPanel.Children.Add(swatch);
        }

        SyncColorSwatch();
    }

    private void Color_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string hex })
        {
            return;
        }

        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (_tool == "marker")
        {
            _markerColor = color;
        }
        else
        {
            _penColor = color;
        }

        // Zaznaczone pociągnięcia i teksty dostają nowy kolor (marker zostaje markerem).
        if (_tool == "select" && (Ink.GetSelectedStrokes().Count > 0 || Ink.GetSelectedElements().Count > 0))
        {
            foreach (var stroke in Ink.GetSelectedStrokes())
            {
                stroke.DrawingAttributes.Color = color;
            }

            foreach (var block in Ink.GetSelectedElements().OfType<TextBlock>())
            {
                block.Foreground = new SolidColorBrush(color);
            }

            Commit();
        }

        if (_editor is not null)
        {
            _editor.Foreground = new SolidColorBrush(color);
        }

        ApplyEditingMode();
    }

    private void SyncColorSwatch()
    {
        if (ColorsPanel is null)
        {
            return;
        }

        _syncing = true;
        var wanted = Hex(_tool == "marker" ? _markerColor : _penColor);
        foreach (var swatch in ColorsPanel.Children.OfType<RadioButton>())
        {
            swatch.IsChecked = string.Equals((string)swatch.Tag, wanted, StringComparison.OrdinalIgnoreCase);
        }

        _syncing = false;
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tool })
        {
            _tool = tool;
            if (Ink is not null)
            {
                CommitEditor();
                if (tool != "select")
                {
                    Ink.Select(new StrokeCollection());
                }

                ApplyEditingMode();
                SyncColorSwatch();
            }
        }
    }

    private void SelectTool(string tool)
    {
        var button = Toolbar.Children.OfType<RadioButton>().FirstOrDefault(b => (string)b.Tag == tool);
        if (button is not null)
        {
            button.IsChecked = true;
        }
    }

    private void Width_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (WidthBox.SelectedItem is ComboBoxItem { Tag: string tag } && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
        {
            _width = width;
            if (Ink is not null)
            {
                ApplyEditingMode();
            }
        }
    }

    private void ApplyEditingMode()
    {
        Ink.EditingMode = _editor is not null ? InkCanvasEditingMode.None : _tool switch
        {
            "select" => InkCanvasEditingMode.Select,
            "pen" or "marker" => InkCanvasEditingMode.Ink,
            "eraser" => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None,
        };
        Ink.DefaultDrawingAttributes = _tool == "marker" ? MarkerAttributes() : PenAttributes(fitToCurve: true);
        Ink.UseCustomCursor = _tool is "line" or "arrow" or "rect" or "text";
        Ink.Cursor = _tool == "text" ? Cursors.IBeam : _tool is "line" or "arrow" or "rect" ? Cursors.Cross : null;
    }

    private DrawingAttributes PenAttributes(bool fitToCurve) => new()
    {
        Color = _penColor,
        Width = _width,
        Height = _width,
        FitToCurve = fitToCurve,
        IgnorePressure = !fitToCurve,
        StylusTip = StylusTip.Ellipse,
    };

    private DrawingAttributes MarkerAttributes() => new()
    {
        Color = _markerColor,
        Width = Math.Max(14, _width * 4),
        Height = Math.Max(14, _width * 4),
        IsHighlighter = true,
        StylusTip = StylusTip.Rectangle,
        IgnorePressure = true,
    };

    private void Ink_StrokeCollected(object? sender, InkCanvasStrokeCollectedEventArgs e)
    {
        e.Stroke.AddPropertyData(KindKey, _tool == "marker" ? "marker" : "pen");
        Commit();
    }

    private static string Kind(Stroke stroke) => stroke.ContainsPropertyData(KindKey) ? stroke.GetPropertyData(KindKey) as string ?? "pen" : "pen";

    private void Ink_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(Ink);
        if (_editor is not null)
        {
            if (IsInside(e.OriginalSource as DependencyObject, _editor))
            {
                return; // klik w polu tekstu — kursor, zaznaczanie tekstu
            }

            CommitEditor();
            if (_tool == "text")
            {
                e.Handled = true; // klik obok kończy pisanie (następny klik — nowy tekst)
                return;
            }
        }

        if (_tool is not "text")
        {
            Ink.Focus();
        }

        switch (_tool)
        {
            case "line":
            case "arrow":
            case "rect":
                _start = point;
                _shape = new Stroke(ShapePoints(_tool, point, point, _width), PenAttributes(fitToCurve: false));
                _shape.AddPropertyData(KindKey, _tool);
                Ink.Strokes.Add(_shape);
                Ink.CaptureMouse();
                e.Handled = true;
                break;
            case "text":
                StartEditor(TextAt(point), point);
                e.Handled = true;
                break;
            case "select" when e.ClickCount == 2 && TextAt(point) is { } block:
                StartEditor(block, point);
                e.Handled = true;
                break;
            case "eraser":
                EraseTextsAt(point);
                break;
        }
    }

    private void Ink_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_shape is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var end = e.GetPosition(Ink);
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                end = Constrain(_start, end, _tool == "rect");
            }

            _shape.StylusPoints = ShapePoints(_tool, _start, end, _width);
        }
        else if (_tool == "eraser" && e.LeftButton == MouseButtonState.Pressed)
        {
            EraseTextsAt(e.GetPosition(Ink));
        }
    }

    private void Ink_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_shape is { } shape)
        {
            _shape = null;
            Ink.ReleaseMouseCapture();
            var bounds = shape.GetBounds();
            if (bounds.Width - shape.DrawingAttributes.Width < 3 && bounds.Height - shape.DrawingAttributes.Height < 3)
            {
                Ink.Strokes.Remove(shape); // klik bez przeciągnięcia
            }
            else
            {
                Commit();
            }

            e.Handled = true;
        }
        else if (_erased)
        {
            _erased = false;
            Commit();
        }
    }

    /// <summary>Punkty kształtu: linia (2), strzałka (odcinek i dwa ramiona grotu), prostokąt (5, zamknięty).</summary>
    internal static StylusPointCollection ShapePoints(string tool, Point a, Point b, double width)
    {
        Point[] points;
        switch (tool)
        {
            case "rect":
                points = [a, new Point(b.X, a.Y), b, new Point(a.X, b.Y), a];
                break;
            case "arrow":
                var head = Math.Max(12, width * 3.5);
                var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
                Point Side(double turn) => new(b.X - head * Math.Cos(angle + turn), b.Y - head * Math.Sin(angle + turn));
                points = [a, b, Side(0.45), b, Side(-0.45)];
                break;
            default:
                points = [a, b];
                break;
        }

        return new StylusPointCollection(points);
    }

    /// <summary>Shift: linia i strzałka co 45°, prostokąt — kwadrat.</summary>
    private static Point Constrain(Point a, Point b, bool square)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (square)
        {
            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new Point(a.X + Math.Sign(dx == 0 ? 1 : dx) * side, a.Y + Math.Sign(dy == 0 ? 1 : dy) * side);
        }

        var length = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point(a.X + length * Math.Cos(angle), a.Y + length * Math.Sin(angle));
    }

    /// <summary>Gumka kasuje też teksty, przez które przejdzie; obrazy nie (usuwa je Zaznacz → Delete), żeby nie znikały przy wycieraniu dopisków.</summary>
    private void EraseTextsAt(Point point)
    {
        foreach (var block in Ink.Children.OfType<TextBlock>().Where(b => Bounds(b).Contains(point)).ToList())
        {
            Ink.Children.Remove(block);
            _erased = true;
        }
    }

    /// <summary>
    /// Obraz wpisany w ramkę: zmiana rozmiaru zaznaczenia z obrazem zawsze proporcjonalna (jak w Paincie z Shiftem);
    /// ciągnięty lewy albo górny uchwyt — przeciwległa krawędź stoi.
    /// </summary>
    private void Ink_SelectionResizing(object? sender, InkCanvasSelectionEditingEventArgs e)
    {
        if (!Ink.GetSelectedElements().OfType<Image>().Any())
        {
            return;
        }

        e.NewRectangle = Proportional(e.OldRectangle, e.NewRectangle);
    }

    /// <summary>Nowy prostokąt o proporcjach starego: skala z boku, który zmienił się bardziej.</summary>
    internal static Rect Proportional(Rect old, Rect proposed)
    {
        if (old.Width <= 0 || old.Height <= 0)
        {
            return proposed;
        }

        var sx = proposed.Width / old.Width;
        var sy = proposed.Height / old.Height;
        var scale = Math.Max(0.02, Math.Abs(sx - 1) >= Math.Abs(sy - 1) ? sx : sy);
        var width = old.Width * scale;
        var height = old.Height * scale;
        var left = Math.Abs(proposed.Left - old.Left) > 0.5 ? proposed.Right - width : proposed.Left;
        var top = Math.Abs(proposed.Top - old.Top) > 0.5 ? proposed.Bottom - height : proposed.Top;
        return new Rect(left, top, width, height);
    }

    // ───────────────────────────── Teksty ─────────────────────────────

    private TextBlock? TextAt(Point point) => Ink.Children.OfType<TextBlock>().LastOrDefault(b => b.Visibility == Visibility.Visible && Bounds(b).Contains(point));

    /// <summary>Pole edycji tekstu w miejscu kliknięcia (albo na istniejącym tekście); po skończeniu zostaje zwykły tekst (TextBlock).</summary>
    private void StartEditor(TextBlock? existing, Point at)
    {
        CommitEditor();
        var brush = existing?.Foreground ?? new SolidColorBrush(_penColor);
        var editor = new TextBox
        {
            FontFamily = BoardFont,
            FontSize = TextSize,
            Foreground = brush,
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            MinWidth = 80,
            Text = existing?.Text ?? "",
        };
        var left = existing is null ? at.X : Left(existing);
        var top = existing is null ? at.Y - TextSize * 0.7 : Top(existing);
        InkCanvas.SetLeft(editor, left - EditorOffset.X);
        InkCanvas.SetTop(editor, top - EditorOffset.Y);
        if (existing is not null)
        {
            existing.Visibility = Visibility.Hidden;
        }

        _editor = editor;
        _editing = existing;
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CommitEditor();
            }
        };
        editor.LostKeyboardFocus += (_, e) =>
        {
            // Fokus poza tablicę (pasek narzędzi, pole polecenia, inna zakładka) kończy pisanie.
            if (e.NewFocus is not DependencyObject target || !IsInside(target, editor))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_editor == editor)
                    {
                        CommitEditor();
                    }
                });
            }
        };
        Ink.Children.Add(editor);
        ApplyEditingMode();
        editor.Loaded += (_, _) =>
        {
            editor.Focus();
            editor.CaretIndex = editor.Text.Length;
        };
    }

    /// <summary>Przesunięcie tekstu w polu edycji względem zwykłego tekstu (ramka i margines pola), żeby tekst nie skakał.</summary>
    private static readonly Vector EditorOffset = new(3, 1);

    private void CommitEditor()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        _editor = null;
        var text = editor.Text.TrimEnd();
        var left = InkCanvas.GetLeft(editor) + EditorOffset.X;
        var top = InkCanvas.GetTop(editor) + EditorOffset.Y;
        Ink.Children.Remove(editor);
        var changed = false;
        if (_editing is { } block)
        {
            block.Visibility = Visibility.Visible;
            if (text.Trim().Length == 0)
            {
                Ink.Children.Remove(block);
                changed = true;
            }
            else if (block.Text != text || !Equals((block.Foreground as SolidColorBrush)?.Color, (editor.Foreground as SolidColorBrush)?.Color))
            {
                block.Text = text;
                block.Foreground = editor.Foreground;
                changed = true;
            }
        }
        else if (text.Trim().Length > 0)
        {
            Ink.Children.Add(NewText(text, editor.Foreground, left, top));
            changed = true;
        }

        _editing = null;
        ApplyEditingMode();
        if (changed)
        {
            Commit();
        }
    }

    private static TextBlock NewText(string text, Brush brush, double left, double top)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = BoardFont,
            FontSize = TextSize,
            Foreground = brush,
            Background = Brushes.Transparent, // cały prostokąt tekstu da się kliknąć (zaznaczanie, gumka)
        };
        InkCanvas.SetLeft(block, left);
        InkCanvas.SetTop(block, top);
        return block;
    }

    // ───────────────────────────── Obrazy, schowek, Capture ─────────────────────────────

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteFromClipboard();

    /// <summary>Ctrl+V na tablicy: obrazek (w miejscu kursora, gdy jest nad tablicą) albo tekst jako napis.</summary>
    private void PasteFromClipboard()
    {
        var mouse = Mouse.GetPosition(Ink);
        Point? at = new Rect(0, 0, Ink.ActualWidth, Ink.ActualHeight).Contains(mouse) ? mouse : null;
        var data = Clipboard.GetDataObject();
        if (ClipboardImages.IsImage(data))
        {
            if (ClipboardImages.ReadPng() is { } png)
            {
                AddImage(png, at, false);
            }
            else
            {
                SetStatus("Nie udało się odczytać obrazka ze schowka.");
            }
        }
        else if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText().Trim();
            if (text.Length > 0)
            {
                var point = at ?? new Point(20, 20);
                Ink.Children.Add(NewText(text.Length > 2000 ? text[..2000] : text, new SolidColorBrush(_penColor), point.X, point.Y));
                Commit();
            }
        }
    }

    /// <summary>
    /// Kładzie obraz na tablicy (pod rysunkami): w skali ekranu, zmniejszony, gdy nie mieści się na tablicy. OCR rusza w tle
    /// i jest gotowy przed wysłaniem polecenia. <paramref name="fill"/> = zrzut z Capture: lewy górny róg tablicy.
    /// </summary>
    internal void AddImage(byte[] png, Point? at, bool fill, string? name = null)
    {
        BitmapSource bitmap;
        try
        {
            bitmap = ClipboardImages.Load(png);
        }
        catch (Exception e) when (e is NotSupportedException or IOException or InvalidOperationException or ArgumentException)
        {
            SetStatus("Nie da się wczytać obrazu: " + e.Message);
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = bitmap.PixelWidth / dpi.DpiScaleX;
        var height = bitmap.PixelHeight / dpi.DpiScaleY;
        var maxWidth = Math.Max(120, Ink.ActualWidth * (fill ? 1 : 0.9));
        var maxHeight = Math.Max(90, Ink.ActualHeight * (fill ? 1 : 0.9));
        var scale = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
        width *= scale;
        height *= scale;
        var position = fill ? new Point(0, 0) : at ?? new Point(20 + 24 * (_imageCounter % 5), 20 + 24 * (_imageCounter % 5));
        position.X = Math.Max(0, Math.Min(position.X, Ink.ActualWidth - width));
        position.Y = Math.Max(0, Math.Min(position.Y, Ink.ActualHeight - height));
        var info = new BoardImage(png, name ?? "obraz " + (++_imageCounter));
        var image = new Image { Source = bitmap, Stretch = Stretch.Fill, Width = width, Height = height, Tag = info };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        InkCanvas.SetLeft(image, position.X);
        InkCanvas.SetTop(image, position.Y);
        Ink.Children.Insert(0, image); // pod tekstami; pociągnięcia i tak są nad obrazami
        Commit();
        SetStatus(info.Name + " na tablicy (" + bitmap.PixelWidth + "×" + bitmap.PixelHeight + " px). Rozmiar: Zaznacz (V) i uchwyty — proporcjonalnie.");
        info.Ocr = OcrImageAsync(info);
    }

    private async Task OcrImageAsync(BoardImage info)
    {
        var settings = _contextProvider?.Invoke().Settings;
        if (settings is null || !settings.OcrOnPaste)
        {
            return; // Ustawienia → OCR: „OCR wklejonego obrazka” wyłączony
        }

        try
        {
            var options = OcrViewModel.ToOptions(settings, [Path.GetTempPath()], Path.GetTempPath());
            var result = await Task.Run(() => new OcrEngine(options).RecognizePng(info.Png));
            info.OcrText = result.Text;
            info.OcrConfidence = result.MeanConfidence;
            SetStatus(info.Name + ": OCR " + Math.Round(result.MeanConfidence * 100) + "%, " + result.Words + " słów — tekst pójdzie do modelu z opisem tablicy.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Błąd OCR (także nieudane ładowanie Tesseracta) zostaje przy obrazie — polecenie pójdzie bez tekstu z OCR.
            SetStatus(info.Name + ": OCR nie powiódł się (" + e.Message + ").");
        }
    }

    private IEnumerable<BoardImage> Images() => Ink.Children.OfType<Image>().Select(i => i.Tag).OfType<BoardImage>();

    /// <summary>
    /// Capture: okno Amadeusza chowa się, a w miejscu tablicy zostaje przezroczysta ramka (ten sam rozmiar) z przyciskami
    /// Zatwierdź i Zaniechaj; ramkę można przesuwać i zmieniać. Zatwierdź robi zrzut ekranu z wnętrza ramki na tablicę.
    /// Po obu przyciskach okno wraca w tym samym miejscu i rozmiarze.
    /// </summary>
    private async void Capture_Click(object sender, RoutedEventArgs e) => await CaptureAsync(null);

    internal async Task<byte[]?> CaptureAsync(Func<CaptureWindow, Task>? drive)
    {
        if (Window.GetWindow(this) is not { } window || PresentationSource.FromVisual(BoardFrame) is not { CompositionTarget: { } target })
        {
            return null;
        }

        CommitEditor();
        var topLeft = target.TransformFromDevice.Transform(BoardFrame.PointToScreen(new Point(0, 0)));
        var area = new Rect(topLeft, new Size(BoardFrame.ActualWidth, BoardFrame.ActualHeight));
        var capture = new CaptureWindow(area) { Icon = window.Icon };
        var wasVisible = window.IsVisible;
        window.Hide();
        byte[]? png;
        try
        {
            png = await capture.RunAsync(drive);
        }
        finally
        {
            if (wasVisible)
            {
                window.Show();
                if (window.ShowActivated)
                {
                    window.Activate(); // kontrola bez klikania nie zabiera fokusu użytkownikowi
                }
            }
        }

        if (png is not null)
        {
            AddImage(png, null, true, "zrzut " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }
        else
        {
            SetStatus("Capture zaniechany.");
        }

        return png;
    }

    // ───────────────────────────── Cofnij, ponów, usuń, wyczyść ─────────────────────────────

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase || e.OriginalSource is TextBoxBase)
        {
            return; // pisanie tekstu na tablicy, polecenia albo w panelu odpowiedzi
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.Z)
        {
            Undo();
        }
        else if (ctrl && e.Key == Key.Y)
        {
            Redo();
        }
        else if (ctrl && e.Key == Key.V)
        {
            PasteFromClipboard();
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelection();
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && ToolKey(e.Key) is { } tool)
        {
            SelectTool(tool);
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    private static string? ToolKey(Key key) => key switch
    {
        Key.V => "select",
        Key.P => "pen",
        Key.L => "line",
        Key.A => "arrow",
        Key.R => "rect",
        Key.M => "marker",
        Key.T => "text",
        Key.E => "eraser",
        _ => null,
    };

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void ClearBoard_Click(object sender, RoutedEventArgs e)
    {
        CommitEditor();
        if (Ink.Strokes.Count == 0 && Ink.Children.Count == 0)
        {
            return;
        }

        Ink.Select(new StrokeCollection());
        Ink.Strokes.Clear();
        Ink.Children.Clear();
        Commit();
        SetStatus("Tablica wyczyszczona (Ctrl+Z cofa). Rysunki Amadeusza czyści osobny przycisk.");
    }

    private void DeleteSelection()
    {
        var strokes = Ink.GetSelectedStrokes();
        var elements = Ink.GetSelectedElements();
        if (strokes.Count == 0 && elements.Count == 0)
        {
            return;
        }

        Ink.Select(new StrokeCollection());
        Ink.Strokes.Remove(strokes);
        foreach (var element in elements.ToList())
        {
            Ink.Children.Remove(element);
        }

        Commit();
    }

    /// <summary>Po każdej zmianie tablicy: stan sprzed zmiany idzie na stos „Cofnij”.</summary>
    private void Commit()
    {
        _undo.Add(_last);
        if (_undo.Count > MaxUndo)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _last = Capture();
        UpdateHint();
    }

    private void Undo()
    {
        CommitEditor();
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(_last);
        _last = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(_last);
    }

    private void Redo()
    {
        CommitEditor();
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(_last);
        _last = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Restore(_last);
    }

    private BoardState Capture() => new(
        Ink.Strokes.Clone(),
        Ink.Children.OfType<FrameworkElement>().Where(c => c != _editor)
            .Select(c => new ChildState(c, Left(c), Top(c), c.Width, c.Height, (c as TextBlock)?.Text, (c as TextBlock)?.Foreground))
            .ToList());

    private void Restore(BoardState state)
    {
        Ink.Select(new StrokeCollection());
        Ink.Strokes = state.Strokes.Clone();
        Ink.Children.Clear();
        foreach (var child in state.Children)
        {
            InkCanvas.SetLeft(child.Element, child.Left);
            InkCanvas.SetTop(child.Element, child.Top);
            child.Element.Width = child.Width;
            child.Element.Height = child.Height;
            if (child.Element is TextBlock block)
            {
                block.Text = child.Text ?? "";
                block.Foreground = child.Foreground ?? block.Foreground;
                block.Visibility = Visibility.Visible;
            }

            Ink.Children.Add(child.Element);
        }

        UpdateHint();
    }

    private void UpdateHint()
        => EmptyHint.Visibility = Ink.Strokes.Count == 0 && Ink.Children.Count == 0 && _aiItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ───────────────────────────── Pomocnicze ─────────────────────────────

    private static double Left(UIElement element) => InkCanvas.GetLeft(element) is var x && double.IsNaN(x) ? 0 : x;

    private static double Top(UIElement element) => InkCanvas.GetTop(element) is var y && double.IsNaN(y) ? 0 : y;

    private static Rect Bounds(FrameworkElement element) => new(Left(element), Top(element), element.ActualWidth, element.ActualHeight);

    private static string Hex(Color color) => "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");

    private static bool IsInside(DependencyObject? element, DependencyObject? container)
    {
        for (var current = element; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(current)
                 : LogicalTreeHelper.GetParent(current))
        {
            if (current == container)
            {
                return true;
            }
        }

        return false;
    }

    // ───────────────────────────── Kontrola bez klikania ─────────────────────────────

    /// <summary>
    /// Kontrola (<c>--zakladka whiteboard</c>): kładzie na tablicy przykładowy szkic — belka z podporami (linie), strzałka
    /// z obciążeniem, odręczna pętla piórem, teksty użytkownika i opcjonalnie obraz z pliku — jakby narysował go użytkownik.
    /// </summary>
    internal void LoadDemoForTest(string? imagePath)
    {
        UpdateLayout();
        void AddShape(string tool, Point a, Point b)
        {
            var stroke = new Stroke(ShapePoints(tool, a, b, _width), PenAttributes(fitToCurve: false));
            stroke.AddPropertyData(KindKey, tool);
            Ink.Strokes.Add(stroke);
        }

        AddShape("line", new Point(120, 260), new Point(620, 260));
        AddShape("line", new Point(120, 260), new Point(100, 300));
        AddShape("line", new Point(120, 260), new Point(140, 300));
        AddShape("line", new Point(100, 300), new Point(140, 300));
        AddShape("line", new Point(620, 260), new Point(600, 300));
        AddShape("line", new Point(620, 260), new Point(640, 300));
        AddShape("line", new Point(600, 300), new Point(640, 300));
        AddShape("arrow", new Point(370, 150), new Point(370, 252));
        // Odręczna pętla piórem wokół siły (ścieżka OCR odręcznych napisów przy Zatwierdź).
        var loop = new StylusPointCollection(Enumerable.Range(0, 37).Select(i => new Point(375 + 70 * Math.Cos(i * Math.PI / 18), 160 + 35 * Math.Sin(i * Math.PI / 18))));
        var pen = new Stroke(loop, PenAttributes(fitToCurve: true));
        pen.AddPropertyData(KindKey, "pen");
        Ink.Strokes.Add(pen);
        Ink.Children.Add(NewText("P = 20 kN", new SolidColorBrush(_penColor), 380, 140));
        Ink.Children.Add(NewText("L = 6 m, belka IPE 300", new SolidColorBrush(_penColor), 260, 320));
        if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            AddImage(File.ReadAllBytes(imagePath), new Point(700, 40), false, Path.GetFileName(imagePath));
        }

        Commit();
    }

    /// <summary>Kontrola: opis tablicy, jaki dostałby model (bez OCR odręcznych napisów).</summary>
    internal string DescribeForTest() => BuildSnapshot().Describe();

    /// <summary>Kontrola: rysunki Amadeusza na tablicy.</summary>
    internal IReadOnlyList<BoardItem> ModelItemsForTest => _aiItems;

    /// <summary>Stan tablicy do „Cofnij”: pociągnięcia i elementy (teksty, obrazy) z położeniem i rozmiarem.</summary>
    private sealed record BoardState(StrokeCollection Strokes, List<ChildState> Children);

    private sealed record ChildState(FrameworkElement Element, double Left, double Top, double Width, double Height, string? Text, Brush? Foreground);

    /// <summary>Obraz na tablicy: PNG, nazwa do opisu dla modelu i wynik OCR.</summary>
    private sealed class BoardImage(byte[] png, string name)
    {
        public byte[] Png { get; } = png;

        public string Name { get; } = name;

        public string? OcrText { get; set; }

        public double OcrConfidence { get; set; }

        public Task? Ocr { get; set; }
    }
}
