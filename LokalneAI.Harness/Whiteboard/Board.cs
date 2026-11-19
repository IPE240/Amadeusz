using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Whiteboard
{
    /// <summary>Rodzaj elementu tablicy: rysunek użytkownika, jego tekst, obraz albo kształt narysowany przez model.</summary>
    public enum BoardItemKind
    {
        /// <summary>Odręczne pociągnięcie piórem (lista punktów).</summary>
        Pen,

        /// <summary>Pociągnięcie markerem (półprzezroczyste podkreślenie).</summary>
        Marker,

        /// <summary>Odcinek od (X, Y) do (X2, Y2).</summary>
        Line,

        /// <summary>Strzałka od (X, Y) do grotu w (X2, Y2).</summary>
        Arrow,

        /// <summary>Prostokąt o rogach (X, Y) i (X2, Y2).</summary>
        Rectangle,

        /// <summary>Elipsa wpisana w prostokąt (X, Y)–(X2, Y2).</summary>
        Ellipse,

        /// <summary>Łamana przez <see cref="BoardItem.Points"/>.</summary>
        Polyline,

        /// <summary>Tekst zaczynający się w (X, Y) — pole tekstowe użytkownika albo napis modelu.</summary>
        Text,

        /// <summary>Wklejony obraz (zrzut, zdjęcie) w prostokącie (X, Y)–(X2, Y2).</summary>
        Image,
    }

    /// <summary>
    /// Element tablicy we współrzędnych tablicy (piksele, (0,0) = lewy górny róg, y w dół). Ten sam typ opisuje rysunki
    /// użytkownika (do <see cref="BoardSnapshot.Describe"/>) i kształty, które model rysuje narzędziem <c>rysuj</c>.
    /// </summary>
    public sealed class BoardItem
    {
        /// <summary>Rodzaj elementu.</summary>
        public BoardItemKind Kind { get; set; }

        /// <summary>Początek odcinka, lewy górny róg albo miejsce tekstu.</summary>
        public double X { get; set; }

        /// <summary>Początek odcinka, lewy górny róg albo miejsce tekstu.</summary>
        public double Y { get; set; }

        /// <summary>Koniec odcinka albo prawy dolny róg.</summary>
        public double X2 { get; set; }

        /// <summary>Koniec odcinka albo prawy dolny róg.</summary>
        public double Y2 { get; set; }

        /// <summary>Punkty łamanej i pociągnięcia piórem: x1, y1, x2, y2…; null przy innych rodzajach.</summary>
        public IReadOnlyList<double> Points { get; set; }

        /// <summary>Tekst (pole tekstowe, napis modelu) albo nazwa pliku obrazu.</summary>
        public string Text { get; set; }

        /// <summary>Kolor jako #RRGGBB.</summary>
        public string Color { get; set; } = BoardColors.Default;

        /// <summary>Tekst rozpoznany z obrazu (OCR); null = bez OCR.</summary>
        public string OcrText { get; set; }

        /// <summary>Pewność OCR 0–1.</summary>
        public double OcrConfidence { get; set; }

        /// <summary>Rozmiar tekstu w pikselach tablicy (napisy modelu); null = domyślny.</summary>
        public double? FontSize { get; set; }

        /// <summary>Lewa krawędź prostokąta otaczającego (dla odcinków i łamanych też).</summary>
        public double Left => Points != null && Points.Count >= 2 ? Xs().Min() : Math.Min(X, X2);

        /// <summary>Górna krawędź prostokąta otaczającego.</summary>
        public double Top => Points != null && Points.Count >= 2 ? Ys().Min() : Math.Min(Y, Y2);

        /// <summary>Prawa krawędź prostokąta otaczającego.</summary>
        public double Right => Points != null && Points.Count >= 2 ? Xs().Max() : Math.Max(X, X2);

        /// <summary>Dolna krawędź prostokąta otaczającego.</summary>
        public double Bottom => Points != null && Points.Count >= 2 ? Ys().Max() : Math.Max(Y, Y2);

        private IEnumerable<double> Xs() => Points.Where((v, i) => i % 2 == 0);

        private IEnumerable<double> Ys() => Points.Where((v, i) => i % 2 == 1);
    }

    /// <summary>Kolory tablicy: nazwy po polsku (do argumentów modelu i opisu tablicy) i wartości #RRGGBB.</summary>
    public static class BoardColors
    {
        /// <summary>Kolor rysunków modelu, gdy nie poda innego (fioletowy — odróżnia się od czerwonego pióra użytkownika).</summary>
        public const string Default = "#7C3AED";

        /// <summary>Nazwy i wartości w kolejności palety okna.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Palette { get; } = new[]
        {
            new KeyValuePair<string, string>("czerwony", "#DC2626"),
            new KeyValuePair<string, string>("niebieski", "#2563EB"),
            new KeyValuePair<string, string>("zielony", "#16A34A"),
            new KeyValuePair<string, string>("pomaranczowy", "#EA580C"),
            new KeyValuePair<string, string>("fioletowy", "#7C3AED"),
            new KeyValuePair<string, string>("czarny", "#111827"),
            new KeyValuePair<string, string>("zolty", "#FACC15"),
        };

        /// <summary>#RRGGBB dla nazwy z palety (bez polskich znaków albo z nimi) albo podanego #RRGGBB; null, gdy nieznany.</summary>
        public static string Parse(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var key = Plain(name.Trim().ToLowerInvariant());
            foreach (var pair in Palette)
            {
                if (pair.Key == key)
                {
                    return pair.Value;
                }
            }

            if (key.Length == 7 && key[0] == '#' && key.Skip(1).All(Uri.IsHexDigit))
            {
                return key.ToUpperInvariant();
            }

            return null;
        }

        /// <summary>Najbliższa nazwa z palety dla #RRGGBB (do opisu tablicy dla modelu).</summary>
        public static string NameOf(string color)
        {
            if (!TryRgb(color, out var r, out var g, out var b))
            {
                return "czarny";
            }

            var best = "czarny";
            var bestDistance = double.MaxValue;
            foreach (var pair in Palette)
            {
                TryRgb(pair.Value, out var pr, out var pg, out var pb);
                var distance = Math.Pow(r - pr, 2) + Math.Pow(g - pg, 2) + Math.Pow(b - pb, 2);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pair.Key;
                }
            }

            return best;
        }

        private static bool TryRgb(string color, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(color) || color.Length < 7 || color[0] != '#')
            {
                return false;
            }

            var hex = color.Substring(color.Length - 6); // #AARRGGBB też — bierzemy RGB
            return int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
                && int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
                && int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
        }

        private static string Plain(string text)
            => text.Replace('ą', 'a').Replace('ć', 'c').Replace('ę', 'e').Replace('ł', 'l').Replace('ń', 'n')
                .Replace('ó', 'o').Replace('ś', 's').Replace('ź', 'z').Replace('ż', 'z');
    }

    /// <summary>
    /// Stan tablicy w chwili wysłania polecenia: rozmiar, elementy użytkownika (teksty dosłownie — nie spłaszczone do obrazu),
    /// obrazy z OCR, odręczne napisy z OCR i rysunki modelu. <see cref="Describe"/> daje tekst, który idzie do modelu z poleceniem.
    /// </summary>
    public sealed class BoardSnapshot
    {
        /// <summary>Szerokość tablicy w pikselach.</summary>
        public double Width { get; set; }

        /// <summary>Wysokość tablicy w pikselach.</summary>
        public double Height { get; set; }

        /// <summary>Elementy użytkownika (pióro, marker, linie, strzałki, prostokąty, teksty, obrazy) w kolejności rysowania.</summary>
        public IList<BoardItem> UserItems { get; } = new List<BoardItem>();

        /// <summary>Kształty narysowane wcześniej przez model (warstwa Amadeusza).</summary>
        public IList<BoardItem> ModelItems { get; } = new List<BoardItem>();

        /// <summary>Tekst odczytany z pociągnięć pióra (OCR odręcznych napisów); null = nie było pióra albo nic nie rozpoznano.</summary>
        public string HandwritingText { get; set; }

        /// <summary>Pewność OCR odręcznych napisów 0–1.</summary>
        public double HandwritingConfidence { get; set; }

        /// <summary>Czy do polecenia dołączono obraz całej tablicy (model widzi obrazy).</summary>
        public bool ImageAttached { get; set; }

        /// <summary>Czy na tablicy nic nie ma (ani rysunków użytkownika, ani modelu).</summary>
        public bool IsEmpty => UserItems.Count == 0 && ModelItems.Count == 0;

        /// <summary>Najwięcej wierszy z rysunkami w opisie (reszta jako „i N innych”) — opis ma być krótki.</summary>
        public int MaxLines { get; set; } = 40;

        /// <summary>Opis tablicy dla modelu, po polsku, ze współrzędnymi w pikselach.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("[Tablica ").Append(N(Width)).Append('×').Append(N(Height))
                .Append(" px; x w prawo, y w dół, (0, 0) = lewy górny róg.")
                .Append(ImageAttached ? " Obraz całej tablicy jest dołączony." : " Obrazu tablicy nie widzisz — masz tylko ten opis.")
                .Append(']');
            if (IsEmpty)
            {
                text.Append("\nTablica jest pusta.");
                return text.ToString();
            }

            var texts = UserItems.Where(i => i.Kind == BoardItemKind.Text && !string.IsNullOrWhiteSpace(i.Text)).ToList();
            if (texts.Count > 0)
            {
                text.Append("\nTeksty wpisane przez użytkownika (dosłownie):");
                for (var i = 0; i < texts.Count; i++)
                {
                    text.Append("\n- T").Append(i + 1).Append(" w ").Append(P(texts[i].X, texts[i].Y)).Append(", ")
                        .Append(BoardColors.NameOf(texts[i].Color)).Append(": „").Append(texts[i].Text.Trim()).Append('”');
                }
            }

            var drawings = new List<string>();
            foreach (var group in UserItems.Where(i => i.Kind == BoardItemKind.Pen || i.Kind == BoardItemKind.Marker)
                .GroupBy(i => new { i.Kind, Color = BoardColors.NameOf(i.Color) }))
            {
                var items = group.ToList();
                drawings.Add((group.Key.Kind == BoardItemKind.Pen ? "pióro (odręcznie): " : "marker (zaznaczenie): ")
                    + items.Count + (items.Count == 1 ? " pociągnięcie" : items.Count < 5 ? " pociągnięcia" : " pociągnięć")
                    + " w obszarze " + P(items.Min(i => i.Left), items.Min(i => i.Top)) + "–" + P(items.Max(i => i.Right), items.Max(i => i.Bottom))
                    + ", " + group.Key.Color);
            }

            drawings.AddRange(UserItems.Where(i => i.Kind != BoardItemKind.Pen && i.Kind != BoardItemKind.Marker
                && i.Kind != BoardItemKind.Text && i.Kind != BoardItemKind.Image).Select(Shape));
            AppendLimited(text, "Rysunki użytkownika:", drawings);

            if (!string.IsNullOrWhiteSpace(HandwritingText))
            {
                text.Append("\nOdręczne napisy (OCR pociągnięć pióra, ").Append(Percent(HandwritingConfidence))
                    .Append(" pewności — może być niedokładny): „").Append(HandwritingText.Trim()).Append('”');
            }

            var images = UserItems.Where(i => i.Kind == BoardItemKind.Image).ToList();
            if (images.Count > 0)
            {
                text.Append("\nObrazy wklejone na tablicę:");
                for (var i = 0; i < images.Count; i++)
                {
                    var image = images[i];
                    text.Append("\n- O").Append(i + 1).Append(" w ").Append(P(image.Left, image.Top)).Append(", ")
                        .Append(N(image.Right - image.Left)).Append('×').Append(N(image.Bottom - image.Top)).Append(" px");
                    if (!string.IsNullOrWhiteSpace(image.Text))
                    {
                        text.Append(" (").Append(image.Text).Append(')');
                    }

                    if (image.OcrText != null)
                    {
                        text.Append(image.OcrText.Trim().Length == 0
                            ? " — OCR: brak tekstu"
                            : " — OCR (" + Percent(image.OcrConfidence) + "): „" + image.OcrText.Trim() + "”");
                    }
                }
            }

            AppendLimited(text, "Twoje wcześniejsze rysunki (warstwa Amadeusza):", ModelItems.Select(Shape).ToList());
            return text.ToString();
        }

        private void AppendLimited(StringBuilder text, string header, List<string> lines)
        {
            if (lines.Count == 0)
            {
                return;
            }

            text.Append('\n').Append(header);
            foreach (var line in lines.Take(MaxLines))
            {
                text.Append("\n- ").Append(line);
            }

            if (lines.Count > MaxLines)
            {
                text.Append("\n- … i ").Append(lines.Count - MaxLines).Append(" innych");
            }
        }

        /// <summary>Jeden kształt w opisie, np. „strzałka (300, 100) → (300, 190), czerwony”.</summary>
        internal static string Shape(BoardItem item)
        {
            var color = BoardColors.NameOf(item.Color);
            switch (item.Kind)
            {
                case BoardItemKind.Line:
                    return "linia " + P(item.X, item.Y) + " – " + P(item.X2, item.Y2) + ", " + color;
                case BoardItemKind.Arrow:
                    return "strzałka " + P(item.X, item.Y) + " → " + P(item.X2, item.Y2) + ", " + color;
                case BoardItemKind.Rectangle:
                    return "prostokąt " + P(item.Left, item.Top) + "–" + P(item.Right, item.Bottom) + ", " + color;
                case BoardItemKind.Ellipse:
                    return "elipsa w " + P(item.Left, item.Top) + "–" + P(item.Right, item.Bottom) + ", " + color;
                case BoardItemKind.Polyline:
                    return "łamana " + (item.Points == null ? "" : string.Join(" ", Pairs(item.Points))) + ", " + color;
                case BoardItemKind.Text:
                    return "tekst w " + P(item.X, item.Y) + ": „" + (item.Text ?? "").Trim() + "”, " + color;
                case BoardItemKind.Pen:
                    return "pióro w obszarze " + P(item.Left, item.Top) + "–" + P(item.Right, item.Bottom) + ", " + color;
                case BoardItemKind.Marker:
                    return "marker w obszarze " + P(item.Left, item.Top) + "–" + P(item.Right, item.Bottom) + ", " + color;
                default:
                    return "obraz w " + P(item.Left, item.Top) + "–" + P(item.Right, item.Bottom);
            }
        }

        private static IEnumerable<string> Pairs(IReadOnlyList<double> points)
        {
            for (var i = 0; i + 1 < points.Count; i += 2)
            {
                yield return P(points[i], points[i + 1]);
            }
        }

        private static string P(double x, double y) => "(" + N(x) + ", " + N(y) + ")";

        private static string N(double value) => Math.Round(value).ToString("0", CultureInfo.InvariantCulture);

        private static string Percent(double value) => Math.Round(value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>
    /// Tablica okna, na której model rysuje (warstwa Amadeusza, wektorowo, osobno od rysunków użytkownika). Metody są wołane
    /// z wątku w tle — implementacja w oknie przełącza się sama na wątek okna.
    /// </summary>
    public interface IWhiteboardSurface
    {
        /// <summary>Szerokość tablicy w pikselach (do sprawdzania współrzędnych).</summary>
        double Width { get; }

        /// <summary>Wysokość tablicy w pikselach.</summary>
        double Height { get; }

        /// <summary>Dokłada kształty na warstwę Amadeusza.</summary>
        Task DrawAsync(IReadOnlyList<BoardItem> items, CancellationToken cancellationToken);

        /// <summary>Czyści warstwę Amadeusza (rysunki użytkownika zostają).</summary>
        Task ClearModelLayerAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Wykonawca umiejętności <c>whiteboard</c>: <c>rysuj</c> (kształty na warstwie Amadeusza) i <c>wyczysc_rysunki</c>.
    /// Rysunki użytkownika są poza zasięgiem modelu — ani ich nie zmienia, ani nie kasuje.
    /// </summary>
    public sealed class WhiteboardExecutor : IToolExecutor
    {
        /// <summary>Narzędzie rysowania: argument <c>elementy</c> (lista kształtów).</summary>
        public const string DrawName = "rysuj";

        /// <summary>Narzędzie czyszczenia warstwy Amadeusza (bez argumentów).</summary>
        public const string ClearName = "wyczysc_rysunki";

        /// <summary>Najwięcej kształtów w jednym wywołaniu <c>rysuj</c>.</summary>
        public const int MaxItems = 60;

        /// <summary>Najkrótsza linia i strzałka w pikselach (krótsza to zwykle pomyłka modelu: początek = koniec).</summary>
        public const double MinLength = 15;

        private readonly IWhiteboardSurface _surface;

        /// <summary>Tworzy wykonawcę dla tablicy okna.</summary>
        public WhiteboardExecutor(IWhiteboardSurface surface)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            if (call.Name == ClearName)
            {
                await _surface.ClearModelLayerAsync(cancellationToken).ConfigureAwait(false);
                return ToolResult.Ok("Warstwa Amadeusza wyczyszczona (rysunki użytkownika zostały).");
            }

            if (call.Name != DrawName)
            {
                return ToolResult.Error("Tablica nie zna polecenia " + call.Name + ".");
            }

            var problems = new List<string>();
            var notes = new List<string>();
            var items = Parse(call.Arguments, _surface.Width, _surface.Height, problems, notes);
            if (items.Count == 0 || problems.Count > 0)
            {
                // Wszystko albo nic: model poprawia wywołanie i rysuje całość jeszcze raz, bez połowy rysunku na tablicy.
                return ToolResult.Error("Nic nie narysowano: " + (problems.Count == 0 ? "pusta lista elementy." : string.Join(" ", problems))
                    + " Popraw i wywołaj rysuj jeszcze raz ze wszystkimi elementami.");
            }

            await _surface.DrawAsync(items, cancellationToken).ConfigureAwait(false);
            var kinds = items.GroupBy(i => KindName(i.Kind)).Select(g => g.Count() == 1 ? g.Key : g.Key + " ×" + g.Count());
            return ToolResult.Ok("Narysowano na tablicy (warstwa Amadeusza): " + string.Join(", ", kinds) + "."
                + (notes.Count == 0 ? "" : " Uwagi: " + string.Join(" ", notes)));
        }

        /// <summary>
        /// Czyta argumenty <c>rysuj</c>: <c>{"elementy":[{"typ":"linia","x":…,"y":…,"x2":…,"y2":…,"kolor":"niebieski"},…]}</c>.
        /// Złe elementy (brak współrzędnych, nieznany typ, strzałka bez długości, prostokąt bez wysokości) pomija z opisem
        /// w <paramref name="problems"/>; współrzędne poza tablicą przycina do jej brzegu i pisze o tym w <paramref name="notes"/>.
        /// </summary>
        public static List<BoardItem> Parse(JsonElement arguments, double width, double height, IList<string> problems, IList<string> notes = null)
        {
            var items = new List<BoardItem>();
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("elementy", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                problems?.Add("Brak listy „elementy”.");
                return items;
            }

            var number = 0;
            var clipped = false;
            foreach (var element in list.EnumerateArray())
            {
                number++;
                if (items.Count >= MaxItems)
                {
                    notes?.Add("Narysowano tylko pierwsze " + MaxItems + " elementów.");
                    break;
                }

                if (element.ValueKind != JsonValueKind.Object)
                {
                    problems?.Add("Element " + number + " nie jest obiektem.");
                    continue;
                }

                var kind = ParseKind(Str(element, "typ"));
                if (kind == null)
                {
                    problems?.Add("Element " + number + ": nieznany typ „" + Str(element, "typ") + "” (linia, strzalka, prostokat, elipsa, lamana, tekst).");
                    continue;
                }

                var item = new BoardItem { Kind = kind.Value, Color = BoardColors.Parse(Str(element, "kolor")) ?? BoardColors.Default };
                var x = Num(element, "x");
                var y = Num(element, "y");
                if (kind == BoardItemKind.Polyline)
                {
                    var points = new List<double>();
                    if (element.TryGetProperty("punkty", out var raw) && raw.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var value in raw.EnumerateArray())
                        {
                            if (value.ValueKind == JsonValueKind.Number)
                            {
                                points.Add(value.GetDouble());
                            }
                            else if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 2
                                && value[0].ValueKind == JsonValueKind.Number && value[1].ValueKind == JsonValueKind.Number)
                            {
                                points.Add(value[0].GetDouble()); // [[x,y],[x,y]] też przyjmujemy
                                points.Add(value[1].GetDouble());
                            }
                        }
                    }

                    if (points.Count < 4 || points.Count % 2 != 0)
                    {
                        problems?.Add("Element " + number + " (łamana): „punkty” to lista x1, y1, x2, y2… — co najmniej dwa punkty.");
                        continue;
                    }

                    for (var i = 0; i < points.Count; i++)
                    {
                        points[i] = Clip(points[i], i % 2 == 0 ? width : height, ref clipped);
                    }

                    item.Points = points;
                    item.X = points[0];
                    item.Y = points[1];
                    item.X2 = points[points.Count - 2];
                    item.Y2 = points[points.Count - 1];
                    items.Add(item);
                    continue;
                }

                if (x == null || y == null)
                {
                    problems?.Add("Element " + number + " (" + KindName(kind.Value) + "): brak „x” albo „y”.");
                    continue;
                }

                item.X = Clip(x.Value, width, ref clipped);
                item.Y = Clip(y.Value, height, ref clipped);
                if (kind == BoardItemKind.Text)
                {
                    var text = Str(element, "tekst");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        problems?.Add("Element " + number + " (tekst): pusty „tekst”.");
                        continue;
                    }

                    item.Text = text.Length > 400 ? text.Substring(0, 400) + "…" : text;
                    var size = Num(element, "rozmiar");
                    item.FontSize = size == null ? (double?)null : Math.Max(8, Math.Min(72, size.Value));
                    item.X2 = item.X;
                    item.Y2 = item.Y;
                    items.Add(item);
                    continue;
                }

                var x2 = Num(element, "x2");
                var y2 = Num(element, "y2");
                if (x2 == null || y2 == null)
                {
                    problems?.Add("Element " + number + " (" + KindName(kind.Value) + "): brak „x2” albo „y2”.");
                    continue;
                }

                item.X2 = Clip(x2.Value, width, ref clipped);
                item.Y2 = Clip(y2.Value, height, ref clipped);
                var dx = item.X2 - item.X;
                var dy = item.Y2 - item.Y;
                if ((kind == BoardItemKind.Line || kind == BoardItemKind.Arrow) && Math.Sqrt(dx * dx + dy * dy) < MinLength)
                {
                    problems?.Add("Element " + number + " (" + KindName(kind.Value) + "): początek i koniec prawie w tym samym miejscu — "
                        + (kind == BoardItemKind.Arrow ? "strzałka idzie z wolnego miejsca (x, y) do grotu przy wskazywanym elemencie (x2, y2), " : "")
                        + "co najmniej " + MinLength + " px.");
                    continue;
                }

                if ((kind == BoardItemKind.Rectangle || kind == BoardItemKind.Ellipse) && (Math.Abs(dx) < 4 || Math.Abs(dy) < 4))
                {
                    problems?.Add("Element " + number + " (" + KindName(kind.Value) + "): x, y i x2, y2 to przeciwległe rogi — szerokość i wysokość muszą być większe od zera.");
                    continue;
                }

                items.Add(item);
            }

            if (clipped)
            {
                notes?.Add("Część współrzędnych była poza tablicą " + Math.Round(width) + "×" + Math.Round(height) + " px — przycięte do brzegu.");
            }

            return items;
        }

        /// <summary>Nazwa rodzaju po polsku (do wyników i opisów).</summary>
        public static string KindName(BoardItemKind kind)
        {
            switch (kind)
            {
                case BoardItemKind.Line: return "linia";
                case BoardItemKind.Arrow: return "strzałka";
                case BoardItemKind.Rectangle: return "prostokąt";
                case BoardItemKind.Ellipse: return "elipsa";
                case BoardItemKind.Polyline: return "łamana";
                case BoardItemKind.Text: return "tekst";
                case BoardItemKind.Pen: return "pióro";
                case BoardItemKind.Marker: return "marker";
                default: return "obraz";
            }
        }

        private static BoardItemKind? ParseKind(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "linia": return BoardItemKind.Line;
                case "strzalka":
                case "strzałka": return BoardItemKind.Arrow;
                case "prostokat":
                case "prostokąt": return BoardItemKind.Rectangle;
                case "elipsa":
                case "okrag":
                case "okrąg":
                case "kolo":
                case "koło": return BoardItemKind.Ellipse;
                case "lamana":
                case "łamana": return BoardItemKind.Polyline;
                case "tekst":
                case "napis": return BoardItemKind.Text;
                default: return null;
            }
        }

        private static double Clip(double value, double size, ref bool clipped)
        {
            if (size <= 0)
            {
                return value;
            }

            if (value < 0 || value > size)
            {
                clipped = true;
                return Math.Max(0, Math.Min(size, value));
            }

            return value;
        }

        private static string Str(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static double? Num(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDouble();
            }

            // Małe modele piszą czasem liczby w cudzysłowie („120”).
            return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (double?)null;
        }
    }
}
