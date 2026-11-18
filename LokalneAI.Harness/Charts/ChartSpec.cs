using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LokalneAI.Harness.Charts
{
    /// <summary>Rodzaj wykresu w odpowiedzi modelu.</summary>
    public enum ChartKind
    {
        /// <summary>Słupkowy (pionowy albo poziomy, kilka serii obok siebie albo skumulowane).</summary>
        Bar,
        /// <summary>Liniowy (zmiana w czasie), także z wypełnieniem pod linią (obszarowy).</summary>
        Line,
        /// <summary>Kołowy albo pierścieniowy (udziały w całości).</summary>
        Pie,
    }

    /// <summary>Porządek pozycji na wykresie (pole <c>sortuj</c> w bloku <c>```wykres</c>).</summary>
    public enum ChartSort
    {
        /// <summary>Kolejność z danych.</summary>
        None,
        /// <summary>Od najmniejszej wartości (pierwsza seria albo <c>sortuj_wg</c>).</summary>
        Ascending,
        /// <summary>Od największej wartości.</summary>
        Descending,
        /// <summary>Alfabetycznie po etykiecie.</summary>
        ByLabel,
        /// <summary>Alfabetycznie od końca.</summary>
        ByLabelDescending,
    }

    /// <summary>Seria danych wykresu.</summary>
    public sealed class ChartSeries
    {
        /// <summary>Nazwa serii (legenda).</summary>
        public string Name { get; set; } = "";

        /// <summary>Wartości po kolei dla <see cref="ChartSpec.Labels"/>; null = brak wartości.</summary>
        public List<double?> Values { get; set; } = new List<double?>();

        /// <summary>Kolor serii „#rrggbb” (pole <c>kolor</c>) albo null = kolejny kolor palety.</summary>
        public string Color { get; set; }

        /// <summary>Na wykresie słupkowym rysowana jako linia (seria z <c>"typ": "liniowy"</c>, np. granica nośności).</summary>
        public bool AsLine { get; set; }

        /// <summary>Wypełnienie pod linią do zera (<c>wypelnienie</c>; cały wykres „obszarowy” ustawia je każdej serii).</summary>
        public bool Fill { get; set; }

        /// <summary>Linia przerywana (<c>przerywana</c>).</summary>
        public bool Dashed { get; set; }
    }

    /// <summary>
    /// Wykres z bloku <c>```wykres</c> w odpowiedzi modelu (JSON). Pola po polsku (angielskie też są przyjmowane):
    /// <c>typ</c> (slupkowy, poziomy, skumulowany, liniowy, obszarowy, kolowy, pierscieniowy), <c>tytul</c>, <c>os_x</c>, <c>os_y</c>,
    /// <c>etykiety</c>, <c>serie[{nazwa, wartosci, kolor, typ, wypelnienie, przerywana}]</c>, <c>grupy</c> (grupa każdej etykiety: kolor
    /// i legenda po grupach), <c>kolory</c> (kolor każdej pozycji), <c>kolory_grup</c>, <c>sortuj</c> (malejaco, rosnaco, nazwa), <c>sortuj_wg</c>,
    /// <c>odwroc_x</c>, <c>odwroc_y</c>, <c>zakres_y</c> [od, do], <c>wartosci</c> (podpisy przy słupkach), <c>wypelnij_miedzy</c> [seria, seria],
    /// <c>kolor_wypelnienia</c>, <c>max_czesci</c> (kołowy), <c>wysokosc</c> (px). Zamiast <c>etykiety</c> + <c>serie</c> można podać
    /// <c>dane</c>: wiersze <c>["etykieta", wartość, …]</c> albo <c>{"nazwa": …, "wartosc": …}</c>.
    /// Sortowanie i odwrócenie kolejności są wykonywane już przy czytaniu — <see cref="Labels"/> i <see cref="Series"/> są w kolejności rysowania.
    /// </summary>
    public sealed class ChartSpec
    {
        /// <summary>Rodzaj wykresu.</summary>
        public ChartKind Kind { get; set; }

        /// <summary>Tytuł nad wykresem.</summary>
        public string Title { get; set; } = "";

        /// <summary>Opis osi X (kategorie).</summary>
        public string XTitle { get; set; } = "";

        /// <summary>Opis osi Y z jednostką, np. „Godziny [h]”.</summary>
        public string YTitle { get; set; } = "";

        /// <summary>Kategorie (oś X) albo nazwy kawałków (kołowy); w kolejności rysowania.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Serie danych.</summary>
        public List<ChartSeries> Series { get; set; } = new List<ChartSeries>();

        /// <summary>Grupa każdej etykiety (pole <c>grupy</c>, np. „IPE”, „HEA”): słupki w kolorze grupy, legenda po grupach; null = bez grup.</summary>
        public List<string> Groups { get; set; }

        /// <summary>Kolory grup „#rrggbb” (pole <c>kolory_grup</c>); grupa bez wpisu dostaje kolejny kolor palety.</summary>
        public Dictionary<string, string> GroupColors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Kolor każdej pozycji „#rrggbb” (pole <c>kolory</c>: słupki jednej serii, kawałki kołowego); null = bez własnych kolorów.</summary>
        public List<string> PointColors { get; set; }

        /// <summary>Słupki poziome (typ „poziomy” albo <c>poziomy: true</c>): kategorie od góry do dołu, wartości w prawo.</summary>
        public bool Horizontal { get; set; }

        /// <summary>Słupki serii jeden na drugim (typ „skumulowany” albo <c>skumulowany: true</c>).</summary>
        public bool Stacked { get; set; }

        /// <summary>Kołowy z dziurą (typ „pierscieniowy” albo <c>pierscien: true</c>); w środku suma.</summary>
        public bool Donut { get; set; }

        /// <summary>Porządek pozycji (pole <c>sortuj</c>); zastosowany już w <see cref="Labels"/>.</summary>
        public ChartSort Sort { get; set; }

        /// <summary>Indeks serii, po której sortowano (pole <c>sortuj_wg</c>; domyślnie 0).</summary>
        public int SortBy { get; set; }

        /// <summary>Kolejność kategorii odwrócona (pole <c>odwroc_x</c>); zastosowane już w <see cref="Labels"/>.</summary>
        public bool ReverseX { get; set; }

        /// <summary>Oś wartości odwrócona: rośnie w dół (pionowy) albo w lewo (poziomy) — np. głębokość, rzędna (pole <c>odwroc_y</c>).</summary>
        public bool ReverseY { get; set; }

        /// <summary>Początek osi wartości (pole <c>zakres_y</c> [od, do] albo <c>os_y_od</c>); null = od zera albo od najmniejszej ujemnej.</summary>
        public double? YMin { get; set; }

        /// <summary>Koniec osi wartości (<c>zakres_y</c> albo <c>os_y_do</c>); null = dobrany do danych.</summary>
        public double? YMax { get; set; }

        /// <summary>Podpisy z wartościami przy słupkach (pole <c>wartosci</c>); null = automatycznie (jedna seria, do 12 słupków).</summary>
        public bool? ShowValues { get; set; }

        /// <summary>Wysokość wykresu w pikselach (pole <c>wysokosc</c>, 160–800); null = domyślna.</summary>
        public double? Height { get; set; }

        /// <summary>Najwięcej kawałków kołowego (pole <c>max_czesci</c>, 2–10; domyślnie 6); reszta jako „Pozostałe”.</summary>
        public int MaxSlices { get; set; } = 6;

        /// <summary>Indeksy dwóch serii, między którymi liniami jest wypełnienie (pole <c>wypelnij_miedzy</c>); null = bez wypełnienia.</summary>
        public int[] FillBetween { get; set; }

        /// <summary>Kolor wypełnienia między seriami „#rrggbb” (pole <c>kolor_wypelnienia</c>); null = kolor pierwszej z serii.</summary>
        public string FillColor { get; set; }

        /// <summary>Liczba serii rysowanych jako słupki (bez <see cref="ChartSeries.AsLine"/>).</summary>
        public int BarSeriesCount => Series.Count(s => !s.AsLine);

        /// <summary>Czyta JSON z bloku <c>```wykres</c>; null i opis błędu, gdy się nie da.</summary>
        public static ChartSpec Parse(string json, out string error)
        {
            error = null;
            try
            {
                using (var document = JsonDocument.Parse(json ?? "", new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        error = "wykres to obiekt JSON { … }";
                        return null;
                    }

                    var spec = new ChartSpec();
                    spec.ReadKind(root);
                    spec.Title = Str(root, "tytul", "tytuł", "title") ?? "";
                    spec.XTitle = Str(root, "os_x", "oś_x", "x", "os_kategorii") ?? "";
                    spec.YTitle = Str(root, "os_y", "oś_y", "y", "jednostka", "unit", "os_wartosci") ?? "";
                    if (Find(root, "etykiety", "kategorie", "labels", "categories", "x") is JsonElement labels && labels.ValueKind == JsonValueKind.Array)
                    {
                        spec.Labels = labels.EnumerateArray().Select(Label).ToList();
                    }

                    spec.ReadSeries(root);
                    if (spec.Series.Count == 0)
                    {
                        spec.ReadRows(root);
                    }

                    spec.Series = spec.Series.Where(s => s.Values.Any(v => v.HasValue)).ToList();
                    if (spec.Series.Count == 0)
                    {
                        error = "brak wartości (pole „serie” z „wartosci”)";
                        return null;
                    }

                    var count = spec.Series.Max(s => s.Values.Count);
                    while (spec.Labels.Count < count)
                    {
                        spec.Labels.Add((spec.Labels.Count + 1).ToString(CultureInfo.InvariantCulture));
                    }

                    spec.ReadLook(root);
                    spec.ApplyOrder();
                    return spec;
                }
            }
            catch (JsonException e)
            {
                error = "niepoprawny JSON (" + e.Message.Split('.')[0] + ")";
                return null;
            }
        }

        /// <summary>Kolor serii „#rrggbb”: własny albo z palety według miejsca.</summary>
        public string SeriesColor(int series)
            => series >= 0 && series < Series.Count && Series[series].Color != null ? Series[series].Color : ChartColors.Of(series);

        /// <summary>Kolor grupy „#rrggbb”: z <see cref="GroupColors"/> albo z palety według kolejności pierwszego wystąpienia.</summary>
        public string GroupColor(string group)
        {
            if (group != null && GroupColors.TryGetValue(group, out var own))
            {
                return own;
            }

            var index = GroupNames().IndexOf(group ?? "");
            return ChartColors.Of(Math.Max(0, index));
        }

        /// <summary>Nazwy grup w kolejności pierwszego wystąpienia (pusta lista bez grup).</summary>
        public List<string> GroupNames()
            => Groups == null ? new List<string>() : Groups.Select(g => g ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// Kolor pozycji <paramref name="point"/> serii <paramref name="series"/>: własny kolor pozycji (<c>kolory</c>) i kolor grupy liczą się
        /// tylko przy jednej serii słupków (albo na kołowym); inaczej kolor serii.
        /// </summary>
        public string PointColor(int series, int point)
        {
            var single = Kind == ChartKind.Pie || BarSeriesCount <= 1;
            if (single && PointColors != null && point >= 0 && point < PointColors.Count && PointColors[point] != null)
            {
                return PointColors[point];
            }

            if (single && Groups != null && point >= 0 && point < Groups.Count && Series[series].Color == null)
            {
                return GroupColor(Groups[point]);
            }

            return SeriesColor(series);
        }

        /// <summary>Dane jako tekst dla Excela: nagłówek i wiersze rozdzielone tabulatorem (w kolejności rysowania, z kolumną grupy, gdy są grupy).</summary>
        public string ToTsv()
        {
            var builder = new StringBuilder();
            builder.Append(string.IsNullOrEmpty(XTitle) ? (Kind == ChartKind.Pie ? "Część" : "Kategoria") : XTitle);
            if (Groups != null)
            {
                builder.Append("\tGrupa");
            }

            foreach (var s in Series)
            {
                builder.Append('\t').Append(string.IsNullOrEmpty(s.Name) ? (string.IsNullOrEmpty(YTitle) ? "Wartość" : YTitle) : s.Name);
            }

            for (var i = 0; i < Labels.Count; i++)
            {
                builder.Append("\r\n").Append(Labels[i]);
                if (Groups != null)
                {
                    builder.Append('\t').Append(i < Groups.Count ? Groups[i] : "");
                }

                foreach (var s in Series)
                {
                    builder.Append('\t').Append(i < s.Values.Count && s.Values[i].HasValue ? s.Values[i].Value.ToString(Text.Pl) : "");
                }
            }

            return builder.ToString();
        }

        private void ReadKind(JsonElement root)
        {
            var kind = Text.Fold(Str(root, "typ", "type", "rodzaj") ?? "slupkowy").Trim();
            if (kind.StartsWith("lin", StringComparison.Ordinal))
            {
                Kind = ChartKind.Line;
            }
            else if (kind.StartsWith("obsz", StringComparison.Ordinal) || kind.StartsWith("area", StringComparison.Ordinal) || kind.StartsWith("warstw", StringComparison.Ordinal))
            {
                Kind = ChartKind.Line;
                _fillAll = true;
            }
            else if (kind.StartsWith("piers", StringComparison.Ordinal) || kind.StartsWith("donut", StringComparison.Ordinal))
            {
                Kind = ChartKind.Pie;
                Donut = true;
            }
            else if (kind.StartsWith("kolumn", StringComparison.Ordinal) || kind.StartsWith("column", StringComparison.Ordinal))
            {
                Kind = ChartKind.Bar; // kolumnowy = słupkowy pionowy
            }
            else if (kind.StartsWith("kol", StringComparison.Ordinal) || kind.StartsWith("pie", StringComparison.Ordinal) || kind.StartsWith("tort", StringComparison.Ordinal))
            {
                Kind = ChartKind.Pie;
            }
            else
            {
                Kind = ChartKind.Bar;
                Horizontal = kind.Contains("poziom") || kind.Contains("horizont");
                Stacked = kind.Contains("skum") || kind.Contains("stack");
            }
        }

        private bool _fillAll;

        private void ReadSeries(JsonElement root)
        {
            var series = Find(root, "serie", "series");
            if (series is JsonElement list && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Array)
                    {
                        Series.Add(new ChartSeries { Values = item.EnumerateArray().Select(Number).ToList() });
                        continue;
                    }

                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var s = new ChartSeries { Name = Str(item, "nazwa", "name", "seria") ?? "" };
                    if (Find(item, "wartosci", "wartości", "values", "dane", "data", "y") is JsonElement values && values.ValueKind == JsonValueKind.Array)
                    {
                        s.Values = values.EnumerateArray().Select(Number).ToList();
                    }

                    ReadSeriesLook(s, item);
                    Series.Add(s);
                }
            }
            else if (series is JsonElement map && map.ValueKind == JsonValueKind.Object)
            {
                // {"IPE": [..], "HEA": [..]} — nazwa serii jako klucz.
                foreach (var property in map.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        Series.Add(new ChartSeries { Name = property.Name, Values = property.Value.EnumerateArray().Select(Number).ToList() });
                    }
                }
            }
            else if (Find(root, "wartosci", "wartości", "values", "y") is JsonElement single && single.ValueKind == JsonValueKind.Array)
            {
                Series.Add(new ChartSeries { Name = YTitle, Values = single.EnumerateArray().Select(Number).ToList() });
            }
        }

        private void ReadSeriesLook(ChartSeries s, JsonElement item)
        {
            s.Color = ChartColors.Parse(Str(item, "kolor", "color", "barwa"));
            var kind = Text.Fold(Str(item, "typ", "type") ?? "");
            s.AsLine = kind.StartsWith("lin", StringComparison.Ordinal);
            s.Fill = Flag(item, "wypelnienie", "wypełnienie", "wypelnij", "obszar", "fill", "area") ?? false;
            s.Dashed = (Flag(item, "przerywana", "dashed", "kreskowana") ?? false)
                || Text.Fold(Str(item, "styl", "linia", "style") ?? "").StartsWith("przeryw", StringComparison.Ordinal);
            if (Find(item, "kolory", "colors") is JsonElement colors && colors.ValueKind == JsonValueKind.Array && PointColors == null)
            {
                PointColors = colors.EnumerateArray().Select(c => ChartColors.Parse(Label(c))).ToList();
            }
        }

        /// <summary>Wiersze <c>dane</c>: <c>["IPE 100", 8.1]</c> albo <c>{"nazwa": "IPE 100", "wartosc": 8.1}</c> (kilka liczb = kilka serii).</summary>
        private void ReadRows(JsonElement root)
        {
            if (!(Find(root, "dane", "wiersze", "punkty", "rows", "data", "pozycje") is JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var names = Find(root, "kolumny", "nazwy_serii", "columns", "serie_nazwy") is JsonElement columns && columns.ValueKind == JsonValueKind.Array
                ? columns.EnumerateArray().Select(Label).ToList()
                : new List<string>();
            var labels = new List<string>();
            var values = new List<List<double?>>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Array)
                {
                    var cells = row.EnumerateArray().ToList();
                    if (cells.Count == 0)
                    {
                        continue;
                    }

                    labels.Add(Label(cells[0]));
                    values.Add(cells.Skip(1).Select(Number).ToList());
                }
                else if (row.ValueKind == JsonValueKind.Object)
                {
                    // Etykieta z pola o znanej nazwie (albo z pierwszego pola tekstowego), liczby z pozostałych pól po kolei.
                    var labelNames = new[] { "nazwa", "etykieta", "kategoria", "x", "name", "label", "pozycja", "profil", "przekroj", "rok", "miesiac" };
                    string label = null;
                    var numbers = new List<double?>();
                    foreach (var property in row.EnumerateObject())
                    {
                        var folded = Text.Fold(property.Name);
                        if (label == null && (labelNames.Contains(folded) || (property.Value.ValueKind == JsonValueKind.String && !Number(property.Value).HasValue)))
                        {
                            label = Label(property.Value);
                            continue;
                        }

                        var number = Number(property.Value);
                        if (number.HasValue || property.Value.ValueKind == JsonValueKind.Null)
                        {
                            numbers.Add(number);
                            if (names.Count < numbers.Count)
                            {
                                names.Add(property.Name);
                            }
                        }
                    }

                    labels.Add(label ?? (labels.Count + 1).ToString(CultureInfo.InvariantCulture));
                    values.Add(numbers);
                }
            }

            var width = values.Count == 0 ? 0 : values.Max(v => v.Count);
            for (var s = 0; s < width; s++)
            {
                Series.Add(new ChartSeries
                {
                    Name = s < names.Count ? names[s] : width == 1 ? YTitle : "seria " + (s + 1),
                    Values = values.Select(v => s < v.Count ? v[s] : null).ToList(),
                });
            }

            if (Labels.Count == 0)
            {
                Labels = labels;
            }
        }

        private void ReadLook(JsonElement root)
        {
            if (_fillAll)
            {
                foreach (var s in Series)
                {
                    s.Fill = true;
                }
            }

            Horizontal |= Flag(root, "poziomy", "poziomo", "horizontal") ?? false;
            Stacked |= Flag(root, "skumulowany", "skumulowane", "stacked", "sumowane") ?? false;
            Donut |= Flag(root, "pierscien", "pierścień", "pierscieniowy", "donut") ?? false;
            ReverseX = Flag(root, "odwroc_x", "odwróć_x", "odwroc_kategorie", "reverse_x") ?? false;
            ReverseY = Flag(root, "odwroc_y", "odwróć_y", "odwroc_wartosci", "reverse_y") ?? false;
            ShowValues = Flag(root, "wartosci_na_slupkach", "pokaz_wartosci", "podpisy", "wartosci", "wartości", "show_values");
            if (Find(root, "grupy", "groups", "rodziny") is JsonElement groups && groups.ValueKind == JsonValueKind.Array)
            {
                Groups = groups.EnumerateArray().Select(Label).ToList();
                while (Groups.Count < Labels.Count)
                {
                    Groups.Add("");
                }
            }

            if (Find(root, "kolory_grup", "group_colors") is JsonElement groupColors && groupColors.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in groupColors.EnumerateObject())
                {
                    var color = ChartColors.Parse(Label(property.Value));
                    if (color != null)
                    {
                        GroupColors[property.Name] = color;
                    }
                }
            }

            if (Find(root, "kolory", "colors", "barwy") is JsonElement colors)
            {
                if (colors.ValueKind == JsonValueKind.Array)
                {
                    var list = colors.EnumerateArray().Select(c => ChartColors.Parse(Label(c))).ToList();
                    if (Series.Count > 1 && list.Count == Series.Count && Series.All(s => s.Color == null) && Kind != ChartKind.Pie)
                    {
                        for (var s = 0; s < Series.Count; s++)
                        {
                            Series[s].Color = list[s]; // tyle kolorów, ile serii = kolory serii
                        }
                    }
                    else if (list.Count == 1 && Labels.Count > 1 && Kind != ChartKind.Pie)
                    {
                        foreach (var s in Series.Where(s => s.Color == null))
                        {
                            s.Color = list[0]; // jeden kolor = cała seria, nie pierwszy słupek
                        }
                    }
                    else
                    {
                        PointColors = list;
                    }
                }
                else if (colors.ValueKind == JsonValueKind.Object)
                {
                    // {"IPE": "niebieski"} — kolor grupy albo kolor serii o tej nazwie.
                    foreach (var property in colors.EnumerateObject())
                    {
                        var color = ChartColors.Parse(Label(property.Value));
                        if (color == null)
                        {
                            continue;
                        }

                        var series = Series.FirstOrDefault(s => string.Equals(s.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                        if (series != null)
                        {
                            series.Color = color;
                        }
                        else
                        {
                            GroupColors[property.Name] = color;
                        }
                    }
                }
            }

            if (Find(root, "zakres_y", "zakres", "os_y_zakres", "y_range", "range") is JsonElement range)
            {
                if (range.ValueKind == JsonValueKind.Array)
                {
                    var ends = range.EnumerateArray().Select(Number).ToList();
                    YMin = ends.Count > 0 ? ends[0] : null;
                    YMax = ends.Count > 1 ? ends[1] : null;
                }
                else if (range.ValueKind == JsonValueKind.Object)
                {
                    YMin = Num(range, "od", "min", "from");
                    YMax = Num(range, "do", "max", "to");
                }
            }

            YMin = Num(root, "os_y_od", "y_min", "min_y", "od") ?? YMin;
            YMax = Num(root, "os_y_do", "y_max", "max_y", "do") ?? YMax;
            if (YMin.HasValue && YMax.HasValue && YMin.Value >= YMax.Value)
            {
                YMin = null;
                YMax = null;
            }

            if (Num(root, "wysokosc", "wysokość", "height") is double height)
            {
                Height = Math.Max(160, Math.Min(800, height));
            }

            if (Num(root, "max_czesci", "max_części", "max_slices", "najwyzej_czesci") is double slices)
            {
                MaxSlices = (int)Math.Max(2, Math.Min(10, Math.Round(slices)));
            }

            var sort = Find(root, "sortuj", "sort", "sortowanie", "kolejnosc");
            if (sort is JsonElement sortValue)
            {
                Sort = ParseSort(sortValue);
            }

            SortBy = SeriesIndex(Find(root, "sortuj_wg", "sort_by", "wg_serii")) ?? 0;
            if (Find(root, "wypelnij_miedzy", "wypełnij_między", "wypelnienie_miedzy", "fill_between", "miedzy") is JsonElement between && between.ValueKind == JsonValueKind.Array)
            {
                var pair = between.EnumerateArray().Select(e => SeriesIndex(e)).Where(i => i.HasValue).Select(i => i.Value).Distinct().ToList();
                if (pair.Count == 2)
                {
                    FillBetween = pair.ToArray();
                }
            }

            FillColor = ChartColors.Parse(Str(root, "kolor_wypelnienia", "kolor_wypełnienia", "fill_color"));
        }

        private static ChartSort ParseSort(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.True)
            {
                return ChartSort.Descending; // „sortuj: true” — ranking od największej
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                return ChartSort.None;
            }

            var text = Text.Fold(value.GetString() ?? "").Trim();
            if (text.StartsWith("malej", StringComparison.Ordinal) || text.StartsWith("desc", StringComparison.Ordinal) || text.StartsWith("od najw", StringComparison.Ordinal))
            {
                return ChartSort.Descending;
            }

            if (text.StartsWith("rosn", StringComparison.Ordinal) || text.StartsWith("asc", StringComparison.Ordinal) || text.StartsWith("od najm", StringComparison.Ordinal))
            {
                return ChartSort.Ascending;
            }

            if (text.StartsWith("nazw", StringComparison.Ordinal) || text.StartsWith("alfab", StringComparison.Ordinal) || text.StartsWith("etyk", StringComparison.Ordinal) || text.StartsWith("label", StringComparison.Ordinal))
            {
                return text.Contains("malej") || text.Contains("desc") || text.Contains("odwrot") ? ChartSort.ByLabelDescending : ChartSort.ByLabel;
            }

            return text == "tak" || text == "true" ? ChartSort.Descending : ChartSort.None;
        }

        /// <summary>Numer serii z nazwy albo liczby (1 = pierwsza); null, gdy nie ma takiej.</summary>
        private int? SeriesIndex(JsonElement? value)
        {
            if (!(value is JsonElement element))
            {
                return null;
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                var number = (int)Math.Round(element.GetDouble());
                return number >= 1 && number <= Series.Count ? number - 1 : (int?)null;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var name = element.GetString() ?? "";
                var index = Series.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    return index;
                }

                if (int.TryParse(name.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1 && parsed <= Series.Count)
                {
                    return parsed - 1;
                }
            }

            return null;
        }

        /// <summary>Sortuje i odwraca etykiety razem ze wszystkimi seriami, grupami i kolorami pozycji.</summary>
        private void ApplyOrder()
        {
            var order = Enumerable.Range(0, Labels.Count).ToList();
            if (Sort != ChartSort.None)
            {
                var by = SortBy >= 0 && SortBy < Series.Count ? Series[SortBy].Values : Series[0].Values;
                Func<int, double?> value = i => i < by.Count ? by[i] : null;
                switch (Sort)
                {
                    case ChartSort.Ascending:
                        order = order.OrderBy(i => !value(i).HasValue).ThenBy(i => value(i) ?? 0).ToList();
                        break;
                    case ChartSort.Descending:
                        order = order.OrderBy(i => !value(i).HasValue).ThenByDescending(i => value(i) ?? 0).ToList();
                        break;
                    case ChartSort.ByLabel:
                        order = order.OrderBy(i => Labels[i], StringComparer.Create(Text.Pl, true)).ToList();
                        break;
                    case ChartSort.ByLabelDescending:
                        order = order.OrderByDescending(i => Labels[i], StringComparer.Create(Text.Pl, true)).ToList();
                        break;
                }
            }

            if (ReverseX)
            {
                order.Reverse();
            }

            if (order.SequenceEqual(Enumerable.Range(0, Labels.Count)))
            {
                return;
            }

            Labels = order.Select(i => Labels[i]).ToList();
            foreach (var s in Series)
            {
                s.Values = order.Select(i => i < s.Values.Count ? s.Values[i] : null).ToList();
            }

            if (Groups != null)
            {
                Groups = order.Select(i => i < Groups.Count ? Groups[i] : "").ToList();
            }

            if (PointColors != null)
            {
                PointColors = order.Select(i => i < PointColors.Count ? PointColors[i] : null).ToList();
            }
        }

        private static JsonElement? Find(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Nazwy po kolei ważności („os_y” przed „y”); bez polskich znaków: „tytuł” pasuje do „tytul”.
            foreach (var name in names)
            {
                var folded = Text.Fold(name);
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) || Text.Fold(property.Name) == folded)
                    {
                        return property.Value;
                    }
                }
            }

            return null;
        }

        /// <summary>Tekst pola; tablica i obiekt to nie tekst (np. „x” jako lista etykiet).</summary>
        private static string Str(JsonElement element, params string[] names)
            => Find(element, names) is JsonElement value && value.ValueKind != JsonValueKind.Array && value.ValueKind != JsonValueKind.Object ? Label(value) : null;

        private static double? Num(JsonElement element, params string[] names)
            => Find(element, names) is JsonElement value ? Number(value) : null;

        private static bool? Flag(JsonElement element, params string[] names)
        {
            if (!(Find(element, names) is JsonElement value))
            {
                return null;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Number:
                    return value.GetDouble() != 0;
                case JsonValueKind.String:
                    var text = Text.Fold(value.GetString() ?? "").Trim();
                    return text == "tak" || text == "true" || text == "1" ? true : text == "nie" || text == "false" || text == "0" ? false : (bool?)null;
                default:
                    return null;
            }
        }

        private static string Label(JsonElement value)
            => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Null ? "" : value.GetRawText();

        private static double? Number(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDouble();
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = (value.GetString() ?? "").Replace(" ", "").Replace(" ", "").Replace(',', '.');
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
    }

    /// <summary>Kolory wykresów: stała paleta (sprawdzona pod kątem daltonizmu) i nazwy po polsku, np. „niebieski”, „#2a78d6”.</summary>
    public static class ChartColors
    {
        /// <summary>Kolejne kolory serii „#rrggbb”.</summary>
        public static readonly IReadOnlyList<string> Palette = new[]
        {
            "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948", "#0097a7", "#8c6d31",
        };

        /// <summary>Szary dla „Pozostałe” i pozycji bez znaczenia.</summary>
        public const string Rest = "#a3a8b0";

        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["niebieski"] = "#2a78d6", ["blekitny"] = "#2a78d6", ["blue"] = "#2a78d6",
            ["pomaranczowy"] = "#eb6834", ["orange"] = "#eb6834",
            ["zielony"] = "#1baf7a", ["green"] = "#1baf7a",
            ["zolty"] = "#eda100", ["yellow"] = "#eda100", ["zloty"] = "#eda100", ["gold"] = "#eda100",
            ["rozowy"] = "#e87ba4", ["pink"] = "#e87ba4",
            ["ciemnozielony"] = "#008300", ["darkgreen"] = "#008300",
            ["fioletowy"] = "#6250d6", ["purple"] = "#6250d6", ["violet"] = "#6250d6", ["liliowy"] = "#6250d6",
            ["czerwony"] = "#e34948", ["red"] = "#e34948",
            ["turkusowy"] = "#0097a7", ["morski"] = "#0097a7", ["teal"] = "#0097a7", ["cyan"] = "#0097a7",
            ["brazowy"] = "#8c6d31", ["brown"] = "#8c6d31",
            ["szary"] = "#a3a8b0", ["gray"] = "#a3a8b0", ["grey"] = "#a3a8b0",
            ["ciemnoszary"] = "#6b7280", ["darkgray"] = "#6b7280",
            ["jasnoszary"] = "#d1d5db", ["lightgray"] = "#d1d5db",
            ["czarny"] = "#111827", ["black"] = "#111827",
            ["granatowy"] = "#1f3a8a", ["ciemnoniebieski"] = "#1f3a8a", ["navy"] = "#1f3a8a", ["darkblue"] = "#1f3a8a",
            ["jasnoniebieski"] = "#7fb3ea", ["lightblue"] = "#7fb3ea",
            ["jasnozielony"] = "#8fd6b0", ["lightgreen"] = "#8fd6b0", ["seledynowy"] = "#8fd6b0",
            ["ciemnoczerwony"] = "#9b1c1c", ["bordowy"] = "#9b1c1c", ["darkred"] = "#9b1c1c",
            ["bezowy"] = "#d9c3a0", ["beige"] = "#d9c3a0",
            ["oliwkowy"] = "#7a8a2e", ["olive"] = "#7a8a2e",
        };

        /// <summary>Kolor miejsca w palecie (cyklicznie); ujemne miejsce = <see cref="Rest"/>.</summary>
        public static string Of(int slot) => slot < 0 ? Rest : Palette[slot % Palette.Count];

        /// <summary>„#rrggbb” z nazwy po polsku albo angielsku („pomarańczowy”, „green”) albo z zapisu „#rgb”/„#rrggbb”; null = nieznany.</summary>
        public static string Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var value = text.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                var hex = value.Substring(1);
                if (hex.Length == 3 && hex.All(IsHex))
                {
                    return "#" + string.Concat(hex.Select(c => new string(char.ToLowerInvariant(c), 2)));
                }

                return hex.Length == 6 && hex.All(IsHex) ? "#" + hex.ToLowerInvariant() : null;
            }

            if (value.Length == 6 && value.All(IsHex) && value.Any(char.IsDigit))
            {
                return "#" + value.ToLowerInvariant();
            }

            var key = Text.Fold(value).Replace("-", "").Replace(" ", "").Replace("_", "");
            if (Names.TryGetValue(key, out var named))
            {
                return named;
            }

            // „jasny niebieski”, „ciemny zielony” → jasnoniebieski, ciemnozielony
            key = key.Replace("jasny", "jasno").Replace("jasna", "jasno").Replace("ciemny", "ciemno").Replace("ciemna", "ciemno");
            return Names.TryGetValue(key, out named) ? named : null;
        }

        /// <summary>Składowe R, G, B z „#rrggbb”.</summary>
        public static void Split(string hex, out byte r, out byte g, out byte b)
        {
            var value = Parse(hex) ?? Rest;
            r = byte.Parse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            g = byte.Parse(value.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            b = byte.Parse(value.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
