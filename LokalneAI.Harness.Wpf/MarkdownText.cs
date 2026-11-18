using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LokalneAI.Harness.Charts;

namespace LokalneAI.Harness.Wpf
{
    /// <summary>
    /// Odpowiedź modelu w Markdownie pokazana jak w czacie Claude'a: tabele z ramkami, nagłówki, listy, pogrubienia, kod, linki.
    /// Tekst można zaznaczać i kopiować; menu pod prawym przyciskiem ma „Kopiuj tabele do Excela” (kolumny rozdzielone tabulatorem).
    /// Ustaw <see cref="Markdown"/> (także wiązaniem); zwykły tekst bez znaczników wygląda jak dotąd.
    /// </summary>
    public class MarkdownText : RichTextBox
    {
        /// <summary>Tekst w Markdownie do pokazania.</summary>
        public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
            nameof(Markdown), typeof(string), typeof(MarkdownText), new PropertyMetadata("", (d, e) => ((MarkdownText)d).Render()));

        private readonly MenuItem _copyTables;

        /// <summary>Tworzy pole tylko do odczytu, bez ramki i tła.</summary>
        public MarkdownText()
        {
            IsReadOnly = true;
            IsDocumentEnabled = true;
            IsTabStop = false;
            BorderThickness = new Thickness(0);
            Background = Brushes.Transparent;
            Padding = new Thickness(0);
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _copyTables = new MenuItem { Header = "Kopiuj tabele i dane wykresów do Excela" };
            _copyTables.Click += (s, e) => CopyTables();
            ContextMenu = new ContextMenu();
            ContextMenu.Items.Add(new MenuItem { Header = "Kopiuj", Command = ApplicationCommands.Copy, CommandTarget = this });
            ContextMenu.Items.Add(new MenuItem { Header = "Zaznacz wszystko", Command = ApplicationCommands.SelectAll, CommandTarget = this });
            ContextMenu.Items.Add(new Separator());
            ContextMenu.Items.Add(_copyTables);
            ContextMenu.Opened += (s, e) => _copyTables.IsEnabled = TablesAsTsv.Length > 0;
            AddHandler(Hyperlink.RequestNavigateEvent, new System.Windows.Navigation.RequestNavigateEventHandler(OpenLink));
            Render();
        }

        /// <summary>Tekst w Markdownie.</summary>
        public string Markdown
        {
            get => (string)GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        /// <summary>Tabele z odpowiedzi jako tekst dla Excela (wiersze w liniach, komórki rozdzielone tabulatorem); pusty, gdy tabel nie ma.</summary>
        public string TablesAsTsv => string.Join("\r\n\r\n", Document.Blocks
            .Select(b => b is Table t ? t.Tag as string : (b as BlockUIContainer)?.Child is ChartView chart ? chart.Spec.ToTsv() : null)
            .Where(t => !string.IsNullOrEmpty(t)));

        private void CopyTables()
        {
            var tsv = TablesAsTsv;
            if (tsv.Length > 0)
            {
                Clipboard.SetText(tsv);
            }
        }

        private static void OpenLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            var uri = e.Uri;
            if (uri != null && uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception)
                {
                    // Brak przeglądarki albo zablokowane — link zostaje do skopiowania.
                }
            }

            e.Handled = true;
        }

        private void Render()
        {
            var document = MarkdownRenderer.Render(Markdown ?? "");
            document.PagePadding = new Thickness(0);
            document.FontFamily = FontFamily;
            document.FontSize = FontSize;
            document.Foreground = Foreground;
            Document = document;
        }

        /// <inheritdoc />
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (Document != null && (e.Property == ForegroundProperty || e.Property == FontSizeProperty || e.Property == FontFamilyProperty))
            {
                Document.Foreground = Foreground;
                Document.FontSize = FontSize;
                Document.FontFamily = FontFamily;
            }
        }
    }

    /// <summary>
    /// Markdown → <see cref="FlowDocument"/> (podzbiór, którego używają modele): nagłówki <c>#</c>, akapity, listy <c>-</c>/<c>1.</c>
    /// (z wcięciem), tabele <c>| a | b |</c> z wierszem <c>|---|</c>, cytaty <c>&gt;</c>, bloki kodu <c>```</c>, linia <c>---</c>;
    /// w tekście <c>**pogrubienie**</c>, <c>*kursywa*</c>, <c>`kod`</c>, <c>~~skreślenie~~</c>, <c>[tekst](https://…)</c>.
    /// Pojedynczy znak nowej linii zostaje nową linią (jak w zwykłym tekście odpowiedzi).
    /// </summary>
    public static class MarkdownRenderer
    {
        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*?)\s*#*\s*$", RegexOptions.Compiled);
        private static readonly Regex ListItem = new Regex(@"^(\s*)([-*+•]|\d{1,3}[.)])\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex TableSeparator = new Regex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
        private static readonly Regex Rule = new Regex(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
        private static readonly Regex Number = new Regex(@"^[\s~≈<>±+−-]*[\d\s.,]+\s*(%|[a-zA-Złµ²³/°]+)?\s*$", RegexOptions.Compiled);
        private static readonly Regex Inline = new Regex(
            @"(\*\*(?<b>.+?)\*\*)|(__(?<b>.+?)__)|(`(?<c>[^`]+)`)|(\[(?<lt>[^\]]+)\]\((?<lu>https?://[^)\s]+)\))|(~~(?<s>.+?)~~)|((?<![\w*])\*(?!\s)(?<i>.+?)(?<!\s)\*(?![\w*]))|((?<!\w)_(?!\s)(?<i>.+?)(?<!\s)_(?!\w))|(\$(?!\s)(?<m>[^$\n]+?)(?<!\s)\$(?!\d))",
            RegexOptions.Compiled);

        private static readonly Brush CodeBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)));
        private static readonly Brush TableLine = Freeze(new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)));
        private static readonly Brush HeaderBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)));
        private static readonly Brush QuoteLine = Freeze(new SolidColorBrush(Color.FromRgb(0xC7, 0xCE, 0xD8)));
        private static readonly FontFamily Mono = new FontFamily("Consolas");

        /// <summary>Składa dokument z tekstu w Markdownie.</summary>
        public static FlowDocument Render(string markdown)
        {
            var document = new FlowDocument();
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
            var paragraph = new List<string>();
            var i = 0;

            void FlushParagraph()
            {
                if (paragraph.Count == 0)
                {
                    return;
                }

                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                for (var k = 0; k < paragraph.Count; k++)
                {
                    if (k > 0)
                    {
                        p.Inlines.Add(new LineBreak());
                    }

                    AddInlines(p.Inlines, paragraph[k]);
                }

                document.Blocks.Add(p);
                paragraph.Clear();
            }

            while (i < lines.Length)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    var language = trimmed.Substring(3).Trim().ToLowerInvariant();
                    var code = new List<string>();
                    i++;
                    while (i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                    {
                        code.Add(lines[i]);
                        i++;
                    }

                    var closed = i < lines.Length;
                    i++; // zamykające ```
                    if (language == "wykres" || language == "chart")
                    {
                        var spec = ChartSpec.Parse(string.Join("\n", code), out var problem);
                        if (spec != null)
                        {
                            document.Blocks.Add(new BlockUIContainer(new ChartView(spec)) { Margin = new Thickness(0, 0, 0, 8) });
                            continue;
                        }

                        if (!closed)
                        {
                            // Model jeszcze pisze wykres — zamiast połowy JSON-a krótka informacja.
                            document.Blocks.Add(new Paragraph(new Italic(new Run("rysuję wykres…"))) { Margin = new Thickness(0, 0, 0, 8) });
                            continue;
                        }

                        document.Blocks.Add(new Paragraph(new Italic(new Run("Nie mogę narysować wykresu: " + problem + ". Dane:"))) { Margin = new Thickness(0, 0, 0, 4) });
                    }
                    document.Blocks.Add(new Paragraph(new Run(string.Join("\n", code)))
                    {
                        FontFamily = Mono,
                        FontSize = 12,
                        Background = CodeBackground,
                        Padding = new Thickness(8, 6, 8, 6),
                        Margin = new Thickness(0, 0, 0, 8),
                    });
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    FlushParagraph();
                    i++;
                    continue;
                }

                var heading = Heading.Match(trimmed);
                if (heading.Success)
                {
                    FlushParagraph();
                    var level = heading.Groups[1].Value.Length;
                    var h = new Paragraph
                    {
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, level <= 2 ? 6 : 2, 0, 6),
                    };
                    h.FontSize = level == 1 ? 18 : level == 2 ? 16 : 14;
                    AddInlines(h.Inlines, heading.Groups[2].Value);
                    document.Blocks.Add(h);
                    i++;
                    continue;
                }

                if (trimmed.Contains("|") && i + 1 < lines.Length && TableSeparator.IsMatch(lines[i + 1]) && lines[i + 1].Contains("-"))
                {
                    FlushParagraph();
                    var header = Cells(trimmed);
                    var aligns = Cells(lines[i + 1].Trim()).Select(Alignment).ToList();
                    var rows = new List<List<string>>();
                    i += 2;
                    while (i < lines.Length && lines[i].Trim().Length > 0 && lines[i].Contains("|"))
                    {
                        rows.Add(Cells(lines[i].Trim()));
                        i++;
                    }

                    document.Blocks.Add(BuildTable(header, aligns, rows));
                    continue;
                }

                if (Rule.IsMatch(trimmed))
                {
                    FlushParagraph();
                    document.Blocks.Add(new Paragraph
                    {
                        BorderBrush = TableLine,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Margin = new Thickness(0, 4, 0, 10),
                        FontSize = 2,
                    });
                    i++;
                    continue;
                }

                if (trimmed.StartsWith(">", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    var quote = new List<string>();
                    while (i < lines.Length && lines[i].Trim().StartsWith(">", StringComparison.Ordinal))
                    {
                        quote.Add(lines[i].Trim().Substring(1).TrimStart());
                        i++;
                    }

                    var inner = Render(string.Join("\n", quote));
                    var section = new Section
                    {
                        BorderBrush = QuoteLine,
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(10, 0, 0, 0),
                        Margin = new Thickness(0, 0, 0, 8),
                    };
                    var moved = inner.Blocks.ToList();
                    inner.Blocks.Clear();
                    section.Blocks.AddRange(moved);
                    document.Blocks.Add(section);
                    continue;
                }

                if (ListItem.IsMatch(line))
                {
                    FlushParagraph();
                    var items = new List<string>();
                    while (i < lines.Length && (ListItem.IsMatch(lines[i]) || (items.Count > 0 && lines[i].StartsWith("  ", StringComparison.Ordinal) && lines[i].Trim().Length > 0)))
                    {
                        items.Add(lines[i]);
                        i++;
                    }

                    document.Blocks.Add(BuildList(items));
                    continue;
                }

                paragraph.Add(trimmed);
                i++;
            }

            FlushParagraph();
            if (document.Blocks.LastBlock is Block last)
            {
                last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
            }

            return document;
        }

        /// <summary>Lista z wcięciami: każdy poziom = 2 spacje więcej (albo tabulator).</summary>
        private static List BuildList(List<string> lines)
        {
            var parsed = new List<Tuple<int, bool, string>>();
            foreach (var line in lines)
            {
                var match = ListItem.Match(line);
                if (match.Success)
                {
                    var indent = match.Groups[1].Value.Replace("\t", "    ").Length;
                    parsed.Add(Tuple.Create(indent, char.IsDigit(match.Groups[2].Value[0]), match.Groups[3].Value));
                }
                else if (parsed.Count > 0)
                {
                    // Dalszy ciąg punktu w następnej linii.
                    var lastItem = parsed[parsed.Count - 1];
                    parsed[parsed.Count - 1] = Tuple.Create(lastItem.Item1, lastItem.Item2, lastItem.Item3 + "\n" + line.Trim());
                }
            }

            var position = 0;
            return BuildLevel(parsed, ref position, parsed.Count > 0 ? parsed[0].Item1 : 0);
        }

        private static List BuildLevel(List<Tuple<int, bool, string>> items, ref int position, int indent)
        {
            var list = new List
            {
                MarkerStyle = items[position].Item2 ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(22, 0, 0, 0),
            };
            ListItem current = null;
            while (position < items.Count)
            {
                var item = items[position];
                if (item.Item1 < indent)
                {
                    break;
                }

                if (item.Item1 >= indent + 2 && current != null)
                {
                    var nested = BuildLevel(items, ref position, item.Item1);
                    nested.Margin = new Thickness(0, 2, 0, 2);
                    current.Blocks.Add(nested);
                    continue;
                }

                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                var parts = item.Item3.Split('\n');
                for (var k = 0; k < parts.Length; k++)
                {
                    if (k > 0)
                    {
                        paragraph.Inlines.Add(new LineBreak());
                    }

                    AddInlines(paragraph.Inlines, parts[k]);
                }

                current = new ListItem(paragraph);
                list.ListItems.Add(current);
                position++;
            }

            return list;
        }

        private static Table BuildTable(List<string> header, List<TextAlignment?> aligns, List<List<string>> rows)
        {
            var columns = Math.Max(header.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Count));
            var table = new Table
            {
                CellSpacing = 0,
                BorderBrush = TableLine,
                BorderThickness = new Thickness(1, 1, 0, 0),
                Margin = new Thickness(0, 2, 0, 10),
            };

            // Szerokości kolumn proporcjonalne do najdłuższej treści (FlowDocument nie ma kolumn „Auto”).
            for (var c = 0; c < columns; c++)
            {
                var longest = new[] { header }.Concat(rows).Select(r => c < r.Count ? Plain(r[c]).Length : 0).DefaultIfEmpty(0).Max();
                table.Columns.Add(new TableColumn { Width = new GridLength(Math.Max(4, Math.Min(longest, 48)), GridUnitType.Star) });
            }

            var numeric = Enumerable.Range(0, columns)
                .Select(c => rows.Count > 0 && rows.All(r => c >= r.Count || Plain(r[c]).Trim().Length == 0 || Number.IsMatch(Plain(r[c]))))
                .ToList();
            var group = new TableRowGroup();
            group.Rows.Add(BuildRow(header, columns, aligns, numeric, true));
            foreach (var row in rows)
            {
                group.Rows.Add(BuildRow(row, columns, aligns, numeric, false));
            }

            table.RowGroups.Add(group);
            var tsv = new StringBuilder();
            foreach (var row in new[] { header }.Concat(rows))
            {
                tsv.Append(string.Join("\t", Enumerable.Range(0, columns).Select(c => c < row.Count ? Plain(row[c]).Trim() : ""))).Append("\r\n");
            }

            table.Tag = tsv.ToString().TrimEnd();
            return table;
        }

        private static TableRow BuildRow(List<string> cells, int columns, List<TextAlignment?> aligns, List<bool> numeric, bool header)
        {
            var row = new TableRow();
            if (header)
            {
                row.Background = HeaderBackground;
                row.FontWeight = FontWeights.SemiBold;
            }

            for (var c = 0; c < columns; c++)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                var align = c < aligns.Count ? aligns[c] : null;
                // Liczby do prawej, także gdy model z przyzwyczajenia pisze „:---” (do lewej) — tylko środek i prawo są wyborem świadomym.
                paragraph.TextAlignment = numeric[c] && (align == null || align == TextAlignment.Left) ? TextAlignment.Right : align ?? TextAlignment.Left;
                AddInlines(paragraph.Inlines, c < cells.Count ? cells[c].Trim() : "");
                row.Cells.Add(new TableCell(paragraph)
                {
                    BorderBrush = TableLine,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(6, 3, 6, 3),
                });
            }

            return row;
        }

        /// <summary>Komórki wiersza tabeli; <c>\|</c> w treści to zwykła kreska.</summary>
        private static List<string> Cells(string line)
        {
            var text = line.Trim();
            if (text.StartsWith("|", StringComparison.Ordinal))
            {
                text = text.Substring(1);
            }

            if (text.EndsWith("|", StringComparison.Ordinal) && !text.EndsWith("\\|", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1);
            }

            return Regex.Split(text, @"(?<!\\)\|").Select(c => c.Replace("\\|", "|").Trim()).ToList();
        }

        private static TextAlignment? Alignment(string separator)
        {
            var s = separator.Trim();
            var left = s.StartsWith(":", StringComparison.Ordinal);
            var right = s.EndsWith(":", StringComparison.Ordinal);
            return left && right ? TextAlignment.Center : right ? TextAlignment.Right : left ? TextAlignment.Left : (TextAlignment?)null;
        }

        /// <summary>Tekst bez znaczników (do szerokości kolumn i kopiowania do Excela).</summary>
        private static string Plain(string text)
            => Inline.Replace(text ?? "", m => m.Groups["b"].Success ? m.Groups["b"].Value
                : m.Groups["c"].Success ? m.Groups["c"].Value
                : m.Groups["lt"].Success ? m.Groups["lt"].Value
                : m.Groups["s"].Success ? m.Groups["s"].Value
                : m.Groups["m"].Success ? m.Groups["m"].Value.Replace("{", "").Replace("}", "").Replace("\\", "")
                : m.Groups["i"].Value);

        private static void AddInlines(InlineCollection target, string text)
        {
            var position = 0;
            foreach (Match match in Inline.Matches(text ?? ""))
            {
                if (match.Index > position)
                {
                    target.Add(new Run(text.Substring(position, match.Index - position)));
                }

                if (match.Groups["b"].Success)
                {
                    var bold = new Bold();
                    AddInlines(bold.Inlines, match.Groups["b"].Value);
                    target.Add(bold);
                }
                else if (match.Groups["c"].Success)
                {
                    target.Add(new Run(match.Groups["c"].Value) { FontFamily = Mono, Background = CodeBackground });
                }
                else if (match.Groups["lt"].Success)
                {
                    var link = new Hyperlink(new Run(match.Groups["lt"].Value)) { ToolTip = match.Groups["lu"].Value };
                    if (Uri.TryCreate(match.Groups["lu"].Value, UriKind.Absolute, out var uri))
                    {
                        link.NavigateUri = uri;
                    }

                    target.Add(link);
                }
                else if (match.Groups["m"].Success)
                {
                    // Wzór LaTeX z modelu ($f_y$, $\gamma_{M0}$): kursywa, indeks dolny po „_”, bez znaczników.
                    target.Add(Formula(match.Groups["m"].Value));
                }
                else if (match.Groups["s"].Success)
                {
                    var strike = new Span { TextDecorations = TextDecorations.Strikethrough };
                    AddInlines(strike.Inlines, match.Groups["s"].Value);
                    target.Add(strike);
                }
                else
                {
                    var italic = new Italic();
                    AddInlines(italic.Inlines, match.Groups["i"].Value);
                    target.Add(italic);
                }

                position = match.Index + match.Length;
            }

            if (position < (text ?? "").Length)
            {
                target.Add(new Run(text.Substring(position)));
            }
        }

        private static readonly Dictionary<string, string> Greek = new Dictionary<string, string>
        {
            ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["Delta"] = "Δ", ["epsilon"] = "ε", ["lambda"] = "λ",
            ["mu"] = "μ", ["nu"] = "ν", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ", ["phi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ",
            ["omega"] = "ω", ["eta"] = "η", ["theta"] = "θ", ["cdot"] = "·", ["times"] = "×", ["leq"] = "≤", ["geq"] = "≥", ["le"] = "≤", ["ge"] = "≥",
        };

        /// <summary>Prosty wzór: litery greckie (\gamma), indeks dolny (_y, _{M0}) i górny (^2), reszta kursywą.</summary>
        private static Inline Formula(string tex)
        {
            var text = Regex.Replace(tex, @"\\([A-Za-z]+)", m => Greek.TryGetValue(m.Groups[1].Value, out var g) ? g : m.Groups[1].Value);
            var span = new Italic();
            var parts = Regex.Split(text, @"([_^](?:\{[^}]*\}|.))");
            foreach (var part in parts.Where(p => p.Length > 0))
            {
                if ((part[0] == '_' || part[0] == '^') && part.Length > 1)
                {
                    var body = part.Substring(1).Trim('{', '}');
                    var run = new Run(body) { FontSize = 10 };
                    run.BaselineAlignment = part[0] == '_' ? BaselineAlignment.Subscript : BaselineAlignment.Superscript;
                    span.Inlines.Add(run);
                }
                else
                {
                    span.Inlines.Add(new Run(part.Replace("{", "").Replace("}", "")));
                }
            }

            return span;
        }

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
