using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Office
{
    /// <summary>Operacje na skoroszytach Excela (wątek STA).</summary>
    internal static class ExcelAutomation
    {
        private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

        /// <summary>Adres jednej komórki, np. <c>B3</c>, <c>AA10</c>.</summary>
        public static readonly Regex CellAddress = new Regex(@"^\$?[A-Za-z]{1,3}\$?[0-9]{1,7}$", RegexOptions.Compiled);

        /// <summary>Adres zakresu, np. <c>A1:D20</c> albo jednej komórki.</summary>
        public static readonly Regex RangeAddress = new Regex(@"^\$?[A-Za-z]{1,3}\$?[0-9]{1,7}(:\$?[A-Za-z]{1,3}\$?[0-9]{1,7})?$", RegexOptions.Compiled);

        private static readonly Regex NumberText = new Regex(@"^-?\d{1,15}([.,]\d{1,10})?$", RegexOptions.Compiled);

        /// <summary>Największy zakres czytany naraz (wiersze × kolumny), żeby wynik mieścił się w kontekście.</summary>
        private const int MaxRows = 200;
        private const int MaxColumns = 40;

        /// <summary>Największy blok wpisywany naraz przez <c>excel_wstaw_wiersze</c>.</summary>
        public const int MaxWriteCells = 2000;

        /// <summary>Arkusz po nazwie albo numerze; pusta nazwa = aktywny arkusz. Null, gdy nie ma takiego (lista w <paramref name="names"/>).</summary>
        private static object Sheet(OfficeHost host, string name, out List<string> names)
        {
            var sheets = host.Track(Com.Get(host.Document, "Worksheets"));
            var count = Com.Int(sheets, "Count");
            names = new List<string>();
            object found = null;
            for (var i = 1; i <= count; i++)
            {
                var sheet = Com.Get(sheets, "Item", i);
                var sheetName = Com.Text(sheet, "Name");
                names.Add(sheetName);
                if (found == null && !string.IsNullOrWhiteSpace(name)
                    && (string.Equals(sheetName, name.Trim(), StringComparison.OrdinalIgnoreCase) || name.Trim() == i.ToString(CultureInfo.InvariantCulture)))
                {
                    found = host.Track(sheet);
                }
                else
                {
                    Com.Release(sheet);
                }
            }

            if (found == null && string.IsNullOrWhiteSpace(name))
            {
                found = host.Track(Com.Get(host.Document, "ActiveSheet"));
            }

            return found;
        }

        /// <summary>Wartość komórki jako tekst dla modelu: liczby po polsku (przecinek), daty ISO, błędy jako <c>#BŁĄD</c>.</summary>
        public static string Format(object value)
        {
            switch (value)
            {
                case null: return "";
                case string s: return s.Replace("\n", " ").Replace("\r", " ");
                case double d: return d.ToString("G15", Pl);
                case float f: return ((double)f).ToString("G15", Pl);
                case decimal m: return m.ToString(Pl);
                case bool b: return b ? "PRAWDA" : "FAŁSZ";
                case DateTime t: return t.TimeOfDay == TimeSpan.Zero ? t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                case int e: return "#BŁĄD(" + ErrorName(e) + ")";
                default: return Convert.ToString(value, Pl) ?? "";
            }
        }

        private static string ErrorName(int code)
        {
            switch (code)
            {
                case -2146826281: return "#DZIEL/0!";
                case -2146826246: return "#N/D";
                case -2146826259: return "#NAZWA?";
                case -2146826288: return "#ZERO!";
                case -2146826252: return "#LICZBA!";
                case -2146826265: return "#ADR!";
                case -2146826273: return "#ARG!";
                default: return code.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Litera kolumny z numeru (1 = A).</summary>
        public static string ColumnLetter(int column)
        {
            var text = "";
            while (column > 0)
            {
                column--;
                text = (char)('A' + column % 26) + text;
                column /= 26;
            }

            return text;
        }

        /// <summary>
        /// Czyta zakres (albo cały używany obszar arkusza) jako tabelę tekstową: nagłówek z literami kolumn, wiersze z numerami.
        /// <paramref name="formulas"/> = pokaż formuły zamiast wartości.
        /// </summary>
        public static string Read(OfficeHost host, string path, string sheetName, string rangeAddress, bool formulas, int maxChars)
        {
            host.Open(path, readOnly: true, visible: false);
            var sheet = Sheet(host, sheetName, out var names);
            if (sheet == null)
            {
                host.Finish(keepOpen: false);
                return "BŁĄD: nie ma arkusza „" + sheetName + "”. Arkusze: " + string.Join(", ", names) + ".";
            }

            object range;
            if (!string.IsNullOrWhiteSpace(rangeAddress))
            {
                range = host.Track(Com.Get(sheet, "Range", rangeAddress.Trim()));
            }
            else
            {
                range = host.Track(Com.Get(sheet, "UsedRange"));
            }

            var firstRow = Com.Int(range, "Row");
            var firstColumn = Com.Int(range, "Column");
            var rows = Com.Int(Com.Get(range, "Rows"), "Count");
            var columns = Com.Int(Com.Get(range, "Columns"), "Count");
            var header = new StringBuilder("OK ").Append(path).Append(", arkusz „").Append(Com.Text(sheet, "Name")).Append('”');
            if (names.Count > 1)
            {
                header.Append(" (arkusze: ").Append(string.Join(", ", names)).Append(')');
            }

            header.Append(", zakres ").Append(Com.Text(range, "Address", false, false)).Append(": wierszy ").Append(rows).Append(", kolumn ").Append(columns);
            var shownRows = Math.Min(rows, MaxRows);
            var shownColumns = Math.Min(columns, MaxColumns);
            if (shownRows < rows || shownColumns < columns)
            {
                header.Append(" (pokazano ").Append(shownRows).Append('×').Append(shownColumns).Append("; podaj zakres, np. A201:D400)");
                range = host.Track(Com.Get(range, "Resize", shownRows, shownColumns));   // Resize to właściwość z argumentami
            }

            var data = Com.Get(range, formulas ? "FormulaLocal" : "Value");
            var table = new StringBuilder();
            table.Append("\n    ");
            for (var c = 0; c < shownColumns; c++)
            {
                table.Append(" | ").Append(ColumnLetter(firstColumn + c));
            }

            var array = data as object[,];
            for (var r = 0; r < shownRows; r++)
            {
                table.Append('\n').Append((firstRow + r).ToString(CultureInfo.InvariantCulture).PadLeft(4));
                for (var c = 0; c < shownColumns; c++)
                {
                    var value = array != null ? array[array.GetLowerBound(0) + r, array.GetLowerBound(1) + c] : data;
                    table.Append(" | ").Append(Format(value));
                }

                if (table.Length > maxChars * 2)
                {
                    break;
                }
            }

            host.Finish(keepOpen: false);
            return header + Tools.FileSystemExecutor.Truncate(table.ToString(), maxChars);
        }

        /// <summary>
        /// Wpisuje wartość do komórki: tekst zaczynający się od <c>=</c> to formuła (najpierw polskie nazwy funkcji, potem angielskie),
        /// liczba (także z przecinkiem) to liczba, reszta to tekst; pusty tekst czyści komórkę. Zwraca tekst, jaki pokazuje komórka.
        /// </summary>
        public static string WriteCell(object sheet, string address, object value, out string formulaNote)
        {
            formulaNote = "";
            var cell = Com.Get(sheet, "Range", address);
            try
            {
                var text = value as string;
                if (value == null || (text != null && text.Length == 0))
                {
                    Com.Call(cell, "ClearContents");
                    return "";
                }

                if (text != null && text.StartsWith("=", StringComparison.Ordinal))
                {
                    WriteFormula(cell, text, out formulaNote);
                }
                else if (text != null && NumberText.IsMatch(text.Trim()))
                {
                    Com.Set(cell, "Value2", double.Parse(text.Trim().Replace(',', '.'), CultureInfo.InvariantCulture));
                }
                else if (text != null)
                {
                    // Apostrof na początku = Excel potraktuje jako tekst także coś, co wygląda jak liczba albo data.
                    Com.Set(cell, "Value2", text);
                }
                else
                {
                    Com.Set(cell, "Value2", value);
                }

                return Com.Text(cell, "Text");
            }
            finally
            {
                Com.Release(cell);
            }
        }

        private static void WriteFormula(object cell, string formula, out string note)
        {
            note = "";
            string firstError = null;
            try
            {
                Com.Set(cell, "FormulaLocal", formula);
                if (!(Com.Get(cell, "Value") is int))
                {
                    return;
                }

                firstError = Com.Text(cell, "Text");
            }
            catch (OfficeException e)
            {
                firstError = e.Message;
            }

            try
            {
                Com.Set(cell, "Formula", formula);
                if (!(Com.Get(cell, "Value") is int))
                {
                    note = " (formuła z angielskimi nazwami funkcji)";
                    return;
                }
            }
            catch (OfficeException)
            {
                // Zostaje pierwsza próba — niżej błąd dla modelu.
            }

            throw new OfficeException("BŁĄD: Excel nie przyjął formuły " + formula + " (" + firstError + "). Użyj polskich nazw funkcji (SUMA, ŚREDNIA, JEŻELI) i średnika między argumentami, np. =SUMA(B2:B9).");
        }

        /// <summary>Wpisuje jedną wartość (arkusz powstaje, gdy go nie ma? nie — błąd z listą; skoroszyt powstaje, gdy nie ma pliku).</summary>
        public static string Write(OfficeHost host, string path, string sheetName, string address, string value, bool keepOpen)
        {
            var created = !System.IO.File.Exists(path);
            if (created)
            {
                host.CreateNew(path, visible: keepOpen);
            }
            else
            {
                host.Open(path, readOnly: false, visible: keepOpen);
            }

            var sheet = Sheet(host, sheetName, out var names);
            if (sheet == null)
            {
                host.Finish(keepOpen: false);
                return "BŁĄD: nie ma arkusza „" + sheetName + "”. Arkusze: " + string.Join(", ", names) + ".";
            }

            var sheetTitle = Com.Text(sheet, "Name");   // przed Finish: po zamknięciu skoroszytu obiekt arkusza nie istnieje (błąd 424)
            var shown = WriteCell(sheet, address.ToUpperInvariant(), value, out var note);
            host.Save();
            host.Finish(keepOpen);
            return "OK " + (created ? "utworzono skoroszyt " + path + ", " : "") + sheetTitle + "!" + address.ToUpperInvariant() + " = " + shown
                + (value != null && value.StartsWith("=", StringComparison.Ordinal) ? " (formuła " + value + ")" : "") + note + (created ? "" : ", zapisano " + path);
        }

        /// <summary>Wpisuje blok wartości od lewej górnej komórki; wiersz = lista wartości (tekst, liczba albo formuła).</summary>
        public static string WriteRows(OfficeHost host, string path, string sheetName, string topLeft, IList<IList<object>> rows, bool keepOpen)
        {
            var created = !System.IO.File.Exists(path);
            if (created)
            {
                host.CreateNew(path, visible: keepOpen);
            }
            else
            {
                host.Open(path, readOnly: false, visible: keepOpen);
            }

            var sheet = Sheet(host, sheetName, out var names);
            if (sheet == null)
            {
                host.Finish(keepOpen: false);
                return "BŁĄD: nie ma arkusza „" + sheetName + "”. Arkusze: " + string.Join(", ", names) + ".";
            }

            var sheetTitle = Com.Text(sheet, "Name");   // przed Finish (patrz Write)
            var start = host.Track(Com.Get(sheet, "Range", topLeft.ToUpperInvariant()));
            var row0 = Com.Int(start, "Row");
            var column0 = Com.Int(start, "Column");
            var cells = 0;
            var widest = 0;
            var notes = new HashSet<string>();
            for (var r = 0; r < rows.Count; r++)
            {
                widest = Math.Max(widest, rows[r].Count);
                for (var c = 0; c < rows[r].Count; c++)
                {
                    var address = ColumnLetter(column0 + c) + (row0 + r).ToString(CultureInfo.InvariantCulture);
                    WriteCell(sheet, address, rows[r][c] is string s ? s : rows[r][c], out var note);
                    if (note.Length > 0)
                    {
                        notes.Add(note.Trim());
                    }

                    cells++;
                }
            }

            host.Save();
            host.Finish(keepOpen);
            var last = ColumnLetter(column0 + Math.Max(widest, 1) - 1) + (row0 + rows.Count - 1).ToString(CultureInfo.InvariantCulture);
            return "OK wpisano " + cells + " komórek: " + sheetTitle + "!" + topLeft.ToUpperInvariant() + ":" + last
                + (notes.Count > 0 ? " " + string.Join(" ", notes) : "") + ", " + (created ? "utworzono " : "zapisano ") + path;
        }

        /// <summary>Eksport do PDF (<c>ExportAsFixedFormat</c>) albo zapis kopii w innym formacie (<c>SaveAs</c>).</summary>
        public static string SaveAs(OfficeHost host, string path, string target)
        {
            host.Open(path, readOnly: true, visible: false);
            if (string.Equals(System.IO.Path.GetExtension(target), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                Com.Call(host.Document, "ExportAsFixedFormat", 0, target); // xlTypePDF
            }
            else
            {
                Com.Call(host.Document, "SaveAs", target, OfficeHost.ExcelFormat(target));
            }

            host.Finish(keepOpen: false);
            return "OK zapisano " + target;
        }
    }
}
