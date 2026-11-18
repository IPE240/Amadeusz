using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LokalneAI.Harness.Charts;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Tests;

/// <summary>
/// Opis wykresu z bloku ```wykres (<see cref="ChartSpec"/>): pola po polsku i angielsku, rodzaje, sortowanie i odwracanie,
/// kolory i grupy, wiersze danych, dane do Excela — oraz zgodność z zasadami umiejętności „formatowanie”.
/// </summary>
public class ChartSpecTests
{
    private static ChartSpec Parse(string json)
    {
        var spec = ChartSpec.Parse(json, out var error);
        Assert.True(spec != null, error);
        return spec;
    }

    private static string One(string type) => "{\"typ\": \"" + type + "\", \"etykiety\": [\"a\", \"b\"], \"serie\": [{\"nazwa\": \"s\", \"wartosci\": [1, 2]}]}";

    [Fact]
    public void Slupkowy_PolaPoPolsku()
    {
        var spec = Parse("""{"typ": "slupkowy", "tytuł": "Godziny", "os_y": "Godziny [h]", "etykiety": ["WSP", "EDO"], "serie": [{"nazwa": "Godziny", "wartosci": [120, "98,5"]}]}""");
        Assert.Equal(ChartKind.Bar, spec.Kind);
        Assert.Equal("Godziny", spec.Title);
        Assert.Equal("Godziny [h]", spec.YTitle);
        Assert.Equal(new[] { "WSP", "EDO" }, spec.Labels);
        Assert.Single(spec.Series);
        Assert.Equal(new double?[] { 120, 98.5 }, spec.Series[0].Values);
        Assert.Null(spec.ShowValues);
        Assert.False(spec.Horizontal);
        Assert.False(spec.Stacked);
        Assert.Equal(ChartSort.None, spec.Sort);
        Assert.Null(spec.Groups);
        Assert.Null(spec.PointColors);
        Assert.Equal(6, spec.MaxSlices);
    }

    [Fact]
    public void Typy_AliasyIFlagi()
    {
        Assert.Equal(ChartKind.Line, Parse(One("liniowy")).Kind);
        var area = Parse(One("obszarowy"));
        Assert.Equal(ChartKind.Line, area.Kind);
        Assert.True(area.Series[0].Fill);
        var pie = Parse(One("kołowy"));
        Assert.Equal(ChartKind.Pie, pie.Kind);
        Assert.False(pie.Donut);
        var donut = Parse(One("pierścieniowy"));
        Assert.Equal(ChartKind.Pie, donut.Kind);
        Assert.True(donut.Donut);
        var horizontal = Parse(One("poziomy"));
        Assert.Equal(ChartKind.Bar, horizontal.Kind);
        Assert.True(horizontal.Horizontal);
        Assert.True(Parse(One("słupkowy poziomy")).Horizontal);
        Assert.True(Parse(One("skumulowany")).Stacked);
        Assert.Equal(ChartKind.Bar, Parse(One("kolumnowy")).Kind);
        Assert.Equal(ChartKind.Bar, Parse(One("bar")).Kind);
        Assert.Equal(ChartKind.Pie, Parse(One("pie")).Kind);

        var flags = Parse("""{"typ": "slupkowy", "poziomy": "tak", "skumulowany": true, "pierscien": 0, "etykiety": ["a"], "serie": [{"wartosci": [1]}]}""");
        Assert.True(flags.Horizontal);
        Assert.True(flags.Stacked);
        Assert.False(flags.Donut);
    }

    [Fact]
    public void Sortuj_Malejaco_PrzestawiaEtykietySerieGrupyIKolory()
    {
        var spec = Parse("""
            {"etykiety": ["IPE 100", "HEA 100", "IPE 200", "HEA 200"], "grupy": ["IPE", "HEA", "IPE", "HEA"], "kolory": ["#111111", "#222222", "#333333", "#444444"],
             "serie": [{"nazwa": "masa", "wartosci": [8.1, 16.7, 22.4, 42.3]}, {"nazwa": "Wy", "wartosci": [34.2, 72.8, 194, 389]}], "sortuj": "malejąco"}
            """);
        Assert.Equal(new[] { "HEA 200", "IPE 200", "HEA 100", "IPE 100" }, spec.Labels);
        Assert.Equal(new double?[] { 42.3, 22.4, 16.7, 8.1 }, spec.Series[0].Values);
        Assert.Equal(new double?[] { 389, 194, 72.8, 34.2 }, spec.Series[1].Values);
        Assert.Equal(new[] { "HEA", "IPE", "HEA", "IPE" }, spec.Groups);
        Assert.Equal(new[] { "#444444", "#333333", "#222222", "#111111" }, spec.PointColors);
        Assert.Equal(ChartSort.Descending, spec.Sort);
        Assert.Equal(0, spec.SortBy);
    }

    [Fact]
    public void Sortuj_WgSeriiIPoNazwie_BrakWartosciNaKoncu()
    {
        var spec = Parse("""{"etykiety": ["b", "a", "c"], "serie": [{"nazwa": "x", "wartosci": [1, 2, 3]}, {"nazwa": "y", "wartosci": [5, null, 7]}], "sortuj": "rosnaco", "sortuj_wg": "y"}""");
        Assert.Equal(new[] { "b", "c", "a" }, spec.Labels);
        Assert.Equal(1, spec.SortBy);
        Assert.Equal(new double?[] { 5, 7, null }, spec.Series[1].Values);

        var byName = Parse("""{"etykiety": ["Żuraw", "Antena", "Łata"], "serie": [{"wartosci": [1, 2, 3]}], "sortuj": "nazwa"}""");
        Assert.Equal(new[] { "Antena", "Łata", "Żuraw" }, byName.Labels);
        Assert.Equal(new double?[] { 2, 3, 1 }, byName.Series[0].Values);
        Assert.Equal(ChartSort.Descending, Parse("""{"etykiety": ["a"], "serie": [{"wartosci": [1]}], "sortuj": true}""").Sort);
    }

    [Fact]
    public void OdwrocX_OdwracaKolejnoscPoSortowaniu()
    {
        var spec = Parse("""{"etykiety": ["a", "b", "c"], "serie": [{"wartosci": [3, 1, 2]}], "sortuj": "malejaco", "odwroc_x": true, "odwróć_y": true}""");
        Assert.Equal(new[] { "b", "c", "a" }, spec.Labels);
        Assert.Equal(new double?[] { 1, 2, 3 }, spec.Series[0].Values);
        Assert.True(spec.ReverseX);
        Assert.True(spec.ReverseY);
    }

    [Theory]
    [InlineData("niebieski", "#2a78d6")]
    [InlineData("Pomarańczowy", "#eb6834")]
    [InlineData("jasny niebieski", "#7fb3ea")]
    [InlineData("ciemno-zielony", "#008300")]
    [InlineData("#ABC", "#aabbcc")]
    [InlineData("#1F3A8A", "#1f3a8a")]
    [InlineData("green", "#1baf7a")]
    [InlineData("tęczowy", null)]
    [InlineData("", null)]
    public void Kolory_NazwyIHex(string text, string expected) => Assert.Equal(expected, ChartColors.Parse(text));

    [Fact]
    public void Kolory_SeriiPozycjiIGrup()
    {
        var grouped = Parse("""{"etykiety": ["IPE 100", "HEA 100", "IPE 200"], "grupy": ["IPE", "HEA", "IPE"], "serie": [{"nazwa": "masa", "wartosci": [8.1, 16.7, 22.4]}], "kolory_grup": {"HEA": "czerwony"}}""");
        Assert.Equal(new[] { "IPE", "HEA" }, grouped.GroupNames());
        Assert.Equal(ChartColors.Palette[0], grouped.GroupColor("IPE"));
        Assert.Equal("#e34948", grouped.GroupColor("HEA"));
        Assert.Equal(ChartColors.Palette[0], grouped.PointColor(0, 0));
        Assert.Equal("#e34948", grouped.PointColor(0, 1));

        var two = Parse("""{"etykiety": ["a", "b"], "grupy": ["g1", "g2"], "serie": [{"nazwa": "x", "wartosci": [1, 2], "kolor": "zielony"}, {"nazwa": "y", "wartosci": [3, 4]}]}""");
        Assert.Equal("#1baf7a", two.SeriesColor(0));
        Assert.Equal("#1baf7a", two.PointColor(0, 1));   // dwie serie słupków: kolor serii, nie grupy
        Assert.Equal(ChartColors.Palette[1], two.SeriesColor(1));

        var perSeries = Parse("""{"etykiety": ["a", "b"], "serie": [{"nazwa": "x", "wartosci": [1, 2]}, {"nazwa": "y", "wartosci": [3, 4]}], "kolory": ["szary", "czarny"]}""");
        Assert.Equal("#a3a8b0", perSeries.SeriesColor(0));
        Assert.Equal("#111827", perSeries.SeriesColor(1));
        Assert.Null(perSeries.PointColors);

        var single = Parse("""{"etykiety": ["a", "b"], "serie": [{"wartosci": [1, 2]}], "kolory": ["szary", "#000"]}""");
        Assert.Equal(new[] { "#a3a8b0", "#000000" }, single.PointColors);
        Assert.Equal("#000000", single.PointColor(0, 1));

        var whole = Parse("""{"etykiety": ["a", "b", "c"], "serie": [{"wartosci": [1, 2, 3]}], "kolory": ["czerwony"]}""");
        Assert.Null(whole.PointColors);                 // jeden kolor = cała seria, nie pierwszy słupek
        Assert.Equal("#e34948", whole.PointColor(0, 2));

        var named = Parse("""{"etykiety": ["a"], "serie": [{"nazwa": "x", "wartosci": [1]}], "kolory": {"x": "czerwony", "HEA": "zielony", "nic": "tęczowy"}}""");
        Assert.Equal("#e34948", named.SeriesColor(0));
        Assert.Equal("#1baf7a", named.GroupColors["HEA"]);
        Assert.False(named.GroupColors.ContainsKey("nic"));
    }

    [Fact]
    public void ZakresOsi_WysokoscCzesciIPodpisy()
    {
        var spec = Parse("""{"etykiety": ["a"], "serie": [{"wartosci": [1]}], "zakres_y": [0, 100], "wysokosc": 5000, "max_czesci": 20, "wartosci": "tak"}""");
        Assert.Equal(0, spec.YMin.Value);
        Assert.Equal(100, spec.YMax.Value);
        Assert.Equal(800, spec.Height.Value);
        Assert.Equal(10, spec.MaxSlices);
        Assert.True(spec.ShowValues);

        var ends = Parse("""{"etykiety": ["a"], "serie": [{"wartosci": [1]}], "os_y_od": -5, "os_y_do": 5, "wysokosc": 10, "max_czesci": 1, "wartosci": false}""");
        Assert.Equal(-5, ends.YMin.Value);
        Assert.Equal(5, ends.YMax.Value);
        Assert.Equal(160, ends.Height.Value);
        Assert.Equal(2, ends.MaxSlices);
        Assert.False(ends.ShowValues);

        var wrong = Parse("""{"etykiety": ["a"], "serie": [{"wartosci": [1]}], "zakres_y": [10, 5]}""");
        Assert.Null(wrong.YMin);
        Assert.Null(wrong.YMax);
    }

    [Fact]
    public void Linie_WypelnieniePrzerywanaIMiedzySeriami()
    {
        var spec = Parse("""
            {"typ": "liniowy", "etykiety": ["1", "2"], "serie": [{"nazwa": "min", "wartosci": [1, 2], "wypelnienie": true}, {"nazwa": "max", "wartosci": [3, 4], "przerywana": true},
             {"nazwa": "sr", "wartosci": [2, 3], "styl": "przerywana"}], "wypelnij_miedzy": ["min", "max"], "kolor_wypelnienia": "szary"}
            """);
        Assert.True(spec.Series[0].Fill);
        Assert.False(spec.Series[1].Fill);
        Assert.True(spec.Series[1].Dashed);
        Assert.True(spec.Series[2].Dashed);
        Assert.Equal(new[] { 0, 1 }, spec.FillBetween);
        Assert.Equal("#a3a8b0", spec.FillColor);

        var numbers = Parse("""{"typ": "liniowy", "etykiety": ["1"], "serie": [{"wartosci": [1]}, {"wartosci": [2]}], "wypelnij_miedzy": [1, 2]}""");
        Assert.Equal(new[] { 0, 1 }, numbers.FillBetween);
        Assert.Null(Parse("""{"typ": "liniowy", "etykiety": ["1"], "serie": [{"wartosci": [1]}], "wypelnij_miedzy": ["a", "b"]}""").FillBetween);

        var mixed = Parse("""{"etykiety": ["a"], "serie": [{"nazwa": "obciążenie", "wartosci": [1]}, {"nazwa": "nośność", "wartosci": [2], "typ": "liniowy"}]}""");
        Assert.False(mixed.Series[0].AsLine);
        Assert.True(mixed.Series[1].AsLine);
        Assert.Equal(1, mixed.BarSeriesCount);
    }

    [Fact]
    public void Dane_WierszeTabliceObiektyIMapaSerii()
    {
        var rows = Parse("""{"dane": [["IPE 100", 8.1, 34.2], ["IPE 200", 22.4, 194]], "kolumny": ["masa", "Wy"]}""");
        Assert.Equal(new[] { "IPE 100", "IPE 200" }, rows.Labels);
        Assert.Equal(2, rows.Series.Count);
        Assert.Equal("Wy", rows.Series[1].Name);
        Assert.Equal(194, rows.Series[1].Values[1].Value);

        var objects = Parse("""{"os_y": "kg/m", "dane": [{"nazwa": "IPE 100", "masa": 8.1}, {"nazwa": "IPE 200", "masa": "22,4"}]}""");
        Assert.Equal(new[] { "IPE 100", "IPE 200" }, objects.Labels);
        Assert.Equal("masa", objects.Series[0].Name);
        Assert.Equal(22.4, objects.Series[0].Values[1].Value);

        var map = Parse("""{"etykiety": ["a", "b"], "serie": {"IPE": [1, 2], "HEA": [3, null]}}""");
        Assert.Equal(new[] { "IPE", "HEA" }, map.Series.Select(s => s.Name));
        Assert.Null(map.Series[1].Values[1]);

        var xy = Parse("""{"x": ["2024", "2025"], "y": [10, 12], "os_y": "t"}""");
        Assert.Equal(new[] { "2024", "2025" }, xy.Labels);
        Assert.Equal("t", xy.YTitle);
        Assert.Equal("", xy.XTitle);
        Assert.Equal(12, xy.Series[0].Values[1].Value);
    }

    [Fact]
    public void ToTsv_WKolejnosciRysowaniaZGrupa()
    {
        var spec = Parse("""{"os_x": "Profil", "etykiety": ["IPE 100", "HEA 100"], "grupy": ["IPE", "HEA"], "serie": [{"nazwa": "masa", "wartosci": [8.1, 16.7]}], "sortuj": "malejaco"}""");
        Assert.Equal("Profil\tGrupa\tmasa\r\nHEA 100\tHEA\t16,7\r\nIPE 100\tIPE\t8,1", spec.ToTsv());
    }

    [Fact]
    public void Bledy_BezWartosciNieObiektZlyJson()
    {
        Assert.Null(ChartSpec.Parse("""{"typ": "slupkowy", "etykiety": ["a"]}""", out var error));
        Assert.Contains("wartości", error);
        Assert.Null(ChartSpec.Parse("[1, 2]", out error));
        Assert.Contains("obiekt", error);
        Assert.Null(ChartSpec.Parse("{\"typ\": ", out error));
        Assert.Contains("JSON", error);
    }

    [Fact]
    public void Formatowanie_OpisujeWszystkieOpcjeAPrzykladySieCzytaja()
    {
        var system = File.ReadAllText(Path.Combine(Paths.Profile("formatowanie"), "system.md"), Encoding.UTF8);
        foreach (var key in new[]
        {
            "slupkowy", "poziomy", "skumulowany", "liniowy", "obszarowy", "kolowy", "pierscieniowy", "etykiety", "serie", "kolor", "kolory", "grupy",
            "kolory_grup", "sortuj", "sortuj_wg", "odwroc_x", "odwroc_y", "zakres_y", "wartosci", "wysokosc", "przerywana", "wypelnienie", "wypelnij_miedzy",
        })
        {
            Assert.True(system.Contains("\"" + key + "\""), "system.md umiejętności formatowanie nie opisuje pola \"" + key + "\"");
        }

        var blocks = Regex.Matches(system, "```wykres\\s*(\\{.*?\\})\\s*```", RegexOptions.Singleline);
        Assert.True(blocks.Count >= 1);
        foreach (Match block in blocks)
        {
            Assert.True(ChartSpec.Parse(block.Groups[1].Value, out var error) != null, "przykład w system.md: " + error);
        }
    }

    [Fact]
    public void SzukajWiedzy_IleFragmentow()
    {
        var tool = BuiltInTools.Get(BuiltInTools.KnowledgeName);
        Assert.Contains("\"ile\"", tool.Parameters.GetRawText());

        var index = new KnowledgeIndex();
        for (var i = 1; i <= 12; i++)
        {
            index.Add("przekroje.md", "# IPE " + (i * 100) + "\n\nIPE " + (i * 100) + ": masa " + i + " kg/m.\n");
        }

        Assert.Equal(BuiltInTools.DefaultKnowledgeHits, Hits(index, """{"fraza": "IPE masa"}"""));
        Assert.Equal(6, Hits(index, """{"fraza": "IPE masa", "ile": 6}"""));
        Assert.Equal(BuiltInTools.MaxKnowledgeHits, Hits(index, """{"fraza": "IPE masa", "ile": 50}"""));
        Assert.Equal(1, Hits(index, """{"fraza": "IPE masa", "ile": 0}"""));
    }

    private static int Hits(KnowledgeIndex index, string arguments)
    {
        var result = BuiltInTools.TryExecute(BuiltInTools.KnowledgeName, JsonDocument.Parse(arguments).RootElement, index);
        Assert.True(result.Success, result.Text);
        return result.Text.Split(new[] { "\n\n" }, StringSplitOptions.None).Length;
    }
}
