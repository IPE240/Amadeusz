using System;
using System.Collections.Generic;
using System.IO;

namespace LokalneAI.Harness.Office
{
    /// <summary>Program pakietu Office obsługiwany przez wykonawcę.</summary>
    internal enum OfficeKind
    {
        Word,
        Excel,
        PowerPoint,
        Outlook,
    }

    /// <summary>
    /// Jedno użycie programu Office na jedno polecenie: podłącza się do działającej instancji (dokumenty użytkownika zostają
    /// na miejscu) albo uruchamia nową; otwiera dokument (albo bierze już otwarty); na końcu zapisuje, pokazuje użytkownikowi
    /// albo zamyka i zwalnia obiekty COM. Używać wyłącznie w wątku STA (<see cref="Com.RunSta{T}"/>).
    /// </summary>
    internal sealed class OfficeHost : IDisposable
    {
        private readonly List<object> _owned = new List<object>();
        private object _previousDisplayAlerts;

        /// <summary>Po <see cref="Finish"/> z pokazaniem dokumentu: <see cref="Dispose"/> nie zamyka programu.</summary>
        private bool _keptOpen;

        private OfficeHost(OfficeKind kind, object app, bool created)
        {
            Kind = kind;
            App = app;
            Created = created;
        }

        /// <summary>Który program.</summary>
        public OfficeKind Kind { get; }

        /// <summary>Obiekt <c>Application</c>.</summary>
        public object App { get; }

        /// <summary>Instancja uruchomiona przez nas (a nie przez użytkownika) — wolno ją zamknąć.</summary>
        public bool Created { get; }

        /// <summary>Otwarty dokument, skoroszyt albo prezentacja; null przed <see cref="Open"/>.</summary>
        public object Document { get; private set; }

        /// <summary>Dokument był już otwarty w programie, zanim przyszło polecenie — nie zamykamy go.</summary>
        public bool DocumentWasOpen { get; private set; }

        /// <summary>Pełna ścieżka otwartego dokumentu.</summary>
        public string Path { get; private set; }

        /// <summary>ProgID programu.</summary>
        public static string ProgId(OfficeKind kind)
        {
            switch (kind)
            {
                case OfficeKind.Word: return "Word.Application";
                case OfficeKind.Excel: return "Excel.Application";
                case OfficeKind.PowerPoint: return "PowerPoint.Application";
                default: return "Outlook.Application";
            }
        }

        /// <summary>Nazwa programu dla ludzi.</summary>
        public static string Name(OfficeKind kind) => kind == OfficeKind.PowerPoint ? "PowerPoint" : kind.ToString();

        /// <summary>
        /// Podłącza się do działającego programu albo uruchamia nowy. Nowy Word i Excel startują niewidoczne, gdy
        /// <paramref name="visible"/> = false (samo czytanie); po edycji pokazuje je <see cref="Show"/>.
        /// </summary>
        public static OfficeHost Start(OfficeKind kind, bool visible)
        {
            var progId = ProgId(kind);
            var running = Com.Running(progId);
            var host = new OfficeHost(kind, running ?? Com.Create(progId), running == null);
            if (host.Created && (kind == OfficeKind.Word || kind == OfficeKind.Excel))
            {
                Com.Set(host.App, "Visible", visible);
            }

            if (kind == OfficeKind.Excel)
            {
                host._previousDisplayAlerts = Com.Get(host.App, "DisplayAlerts");
                Com.Set(host.App, "DisplayAlerts", false); // bez okienek „zapisać zmiany?” pod spodem — decyzje podejmuje wykonawca
            }

            return host;
        }

        /// <summary>Kolekcja dokumentów programu (<c>Documents</c>, <c>Workbooks</c>, <c>Presentations</c>).</summary>
        private object Collection()
        {
            switch (Kind)
            {
                case OfficeKind.Word: return Track(Com.Get(App, "Documents"));
                case OfficeKind.Excel: return Track(Com.Get(App, "Workbooks"));
                default: return Track(Com.Get(App, "Presentations"));
            }
        }

        /// <summary>Dokument już otwarty w tym programie pod tą ścieżką albo null.</summary>
        private object FindOpen(string path)
        {
            var collection = Collection();
            var count = Com.Int(collection, "Count");
            for (var i = 1; i <= count; i++)
            {
                var item = Com.Get(collection, "Item", i);
                string fullName;
                try
                {
                    fullName = Com.Text(item, "FullName");
                }
                catch (OfficeException)
                {
                    fullName = "";
                }

                if (string.Equals(fullName, path, StringComparison.OrdinalIgnoreCase))
                {
                    return Track(item);
                }

                Com.Release(item);
            }

            return null;
        }

        /// <summary>
        /// Otwiera dokument (albo bierze już otwarty). <paramref name="readOnly"/> = tylko czytanie; <paramref name="visible"/> = z oknem
        /// (Word: parametr <c>Visible</c>, PowerPoint: <c>WithWindow</c>; Excel pokazuje okno zawsze, gdy program jest widoczny).
        /// </summary>
        public object Open(string path, bool readOnly, bool visible)
        {
            Path = path;
            var open = FindOpen(path);
            if (open != null)
            {
                Document = open;
                DocumentWasOpen = true;
                return open;
            }

            var collection = Collection();
            object document;
            var missing = Type.Missing;
            switch (Kind)
            {
                case OfficeKind.Word:
                    // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument, PasswordTemplate, Revert,
                    //      WritePasswordDocument, WritePasswordTemplate, Format, Encoding, Visible)
                    document = Com.Call(collection, "Open", path, false, readOnly, false, missing, missing, false, missing, missing, missing, missing, visible);
                    break;
                case OfficeKind.Excel:
                    // Open(FileName, UpdateLinks, ReadOnly)
                    document = Com.Call(collection, "Open", path, 0, readOnly);
                    break;
                default:
                    // Open(FileName, ReadOnly, Untitled, WithWindow) — msoTrue = -1, msoFalse = 0
                    document = Com.Call(collection, "Open", path, readOnly ? -1 : 0, 0, visible ? -1 : 0);
                    break;
            }

            Document = Track(document);
            return document;
        }

        /// <summary>Nowy pusty dokument zapisany od razu pod tą ścieżką (folder musi istnieć).</summary>
        public object CreateNew(string path, bool visible)
        {
            Path = path;
            var collection = Collection();
            object document;
            switch (Kind)
            {
                case OfficeKind.Word:
                    document = Com.Call(collection, "Add");
                    Document = Track(document);
                    Com.Call(document, "SaveAs2", path, WordFormat(path));
                    break;
                case OfficeKind.Excel:
                    document = Com.Call(collection, "Add");
                    Document = Track(document);
                    Com.Call(document, "SaveAs", path, ExcelFormat(path));
                    break;
                default:
                    document = Com.Call(collection, "Add", visible ? -1 : 0);
                    Document = Track(document);
                    Com.Call(document, "SaveAs", path, PowerPointFormat(path));
                    break;
            }

            return document;
        }

        /// <summary>Zapisuje otwarty dokument pod jego ścieżką.</summary>
        public void Save()
        {
            Com.Call(Document, "Save");
        }

        /// <summary>Pokazuje program i otwarty dokument użytkownikowi (po edycji albo na „otwórz”).</summary>
        public void Show()
        {
            try
            {
                if (Kind == OfficeKind.PowerPoint)
                {
                    Com.Set(App, "Visible", -1);
                    var windows = Com.Get(Document, "Windows");
                    if (Com.Int(windows, "Count") == 0)
                    {
                        Com.Call(Document, "NewWindow");
                    }

                    Com.Release(windows);
                }
                else
                {
                    Com.Set(App, "Visible", true);
                }

                Com.Call(Document, "Activate");
                if (Kind == OfficeKind.Word)
                {
                    var window = Com.Get(App, "ActiveWindow");
                    Com.Set(window, "Visible", true);
                    Com.Call(window, "Activate");
                    Com.Release(window);
                }

                Com.Call(App, "Activate");
            }
            catch (OfficeException)
            {
                // Program bez okna (np. Outlook w tle) — pokazanie jest tylko wygodą, nie częścią polecenia.
            }
        }

        /// <summary>Zamyka dokument otwarty przez nas (dokument użytkownika zostaje); <paramref name="save"/> = z zapisem.</summary>
        public void CloseDocument(bool save)
        {
            if (Document == null || DocumentWasOpen)
            {
                return;
            }

            switch (Kind)
            {
                case OfficeKind.Word:
                    Com.Call(Document, "Close", save ? -1 : 0);   // wdSaveChanges = -1, wdDoNotSaveChanges = 0
                    break;
                case OfficeKind.Excel:
                    Com.Call(Document, "Close", save);
                    break;
                default:
                    if (save)
                    {
                        Com.Call(Document, "Save");
                    }

                    Com.Call(Document, "Close");
                    break;
            }

            Document = null;
        }

        /// <summary>
        /// Koniec polecenia: <paramref name="keepOpen"/> = zostaw dokument otwarty i pokaż (edycja, „otwórz”);
        /// inaczej zamknij dokument otwarty przez nas i zamknij program, jeśli to my go uruchomiliśmy.
        /// </summary>
        public void Finish(bool keepOpen)
        {
            if (keepOpen)
            {
                _keptOpen = true;
                Show();
                return;
            }

            CloseDocument(false);
            Quit();
        }

        /// <summary>
        /// Zamyka program, jeśli to my go uruchomiliśmy i nie ma w nim innych otwartych dokumentów. Instancji, do której tylko
        /// się podłączyliśmy, nie zamykamy nigdy — nawet niewidocznej i pustej: może należeć do innego programu, który automatyzuje
        /// Office w tle (między jego operacjami licznik dokumentów bywa zerowy). Cena: dwa procesy automatyzujące Office naraz
        /// (np. dwa przebiegi testów) mogą zostawić ukrytą instancję — sprzątanie opisane w AGENTS.md.
        /// </summary>
        public void Quit()
        {
            if (!Created)
            {
                return;
            }

            try
            {
                if (IsVisible())
                {
                    return; // użytkownik już pracuje w oknie, które pokazaliśmy
                }

                var collection = Collection();
                if (Com.Int(collection, "Count") > 0)
                {
                    return; // ktoś (albo my w trybie „pokaż”) ma otwarte dokumenty — nie zamykamy
                }

                Com.Call(App, "Quit");
            }
            catch (OfficeException)
            {
                // Program już zamknięty albo nie odpowiada — zwolnienie obiektów wystarczy.
            }
        }

        /// <summary>Czy program ma widoczne okno (Word, Excel: <c>Visible</c>; PowerPoint: msoTrue = -1). Outlook: tak (nie zamykamy poczty).</summary>
        private bool IsVisible()
        {
            if (Kind == OfficeKind.Outlook)
            {
                return true;
            }

            try
            {
                var visible = Com.Get(App, "Visible");
                return visible is bool b ? b : Convert.ToInt32(visible ?? 0, System.Globalization.CultureInfo.InvariantCulture) != 0;
            }
            catch (OfficeException)
            {
                return true; // nie wiadomo — bezpieczniej nie zamykać
            }
        }

        /// <summary>Zapamiętuje obiekt COM do zwolnienia przy <see cref="Dispose"/>.</summary>
        public object Track(object comObject)
        {
            if (comObject != null)
            {
                _owned.Add(comObject);
            }

            return comObject;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_keptOpen)
            {
                // Polecenie przerwane wyjątkiem (np. błąd zapisu): nie zostawiamy niewidocznej instancji z otwartym dokumentem.
                try
                {
                    CloseDocument(false);
                    Quit();
                }
                catch (OfficeException)
                {
                    // Program już nie odpowiada — zwolnienie obiektów wystarczy.
                }
            }

            if (Kind == OfficeKind.Excel && _previousDisplayAlerts != null && !Created)
            {
                try
                {
                    Com.Set(App, "DisplayAlerts", _previousDisplayAlerts);
                }
                catch (OfficeException)
                {
                    // Excel zamknięty w międzyczasie.
                }
            }

            Com.Release(_owned);
            _owned.Clear();
            Com.Release(App);
        }

        /// <summary>Format zapisu Worda z rozszerzenia (<c>SaveAs2</c>).</summary>
        public static int WordFormat(string path)
        {
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
            {
                case ".doc": return 0;      // wdFormatDocument97
                case ".docm": return 13;    // wdFormatXMLDocumentMacroEnabled
                case ".rtf": return 6;      // wdFormatRTF
                case ".txt": return 2;      // wdFormatText
                case ".odt": return 23;     // wdFormatOpenDocumentText
                case ".pdf": return 17;     // wdFormatPDF
                default: return 12;         // wdFormatXMLDocument (.docx)
            }
        }

        /// <summary>Format zapisu Excela z rozszerzenia (<c>SaveAs</c>).</summary>
        public static int ExcelFormat(string path)
        {
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
            {
                case ".xls": return 56;     // xlExcel8
                case ".xlsm": return 52;    // xlOpenXMLWorkbookMacroEnabled
                case ".csv": return 62;     // xlCSVUTF8
                case ".ods": return 60;     // xlOpenDocumentSpreadsheet
                default: return 51;         // xlOpenXMLWorkbook (.xlsx)
            }
        }

        /// <summary>Format zapisu PowerPointa z rozszerzenia (<c>SaveAs</c>).</summary>
        public static int PowerPointFormat(string path)
        {
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ppt": return 1;      // ppSaveAsPresentation
                case ".pptm": return 25;    // ppSaveAsOpenXMLPresentationMacroEnabled
                case ".pdf": return 32;     // ppSaveAsPDF
                case ".odp": return 35;     // ppSaveAsOpenDocumentPresentation
                default: return 24;         // ppSaveAsOpenXMLPresentation (.pptx)
            }
        }

        /// <summary>Program dla rozszerzenia pliku albo null (np. <c>.pdf</c>, <c>.txt</c>).</summary>
        public static OfficeKind? KindFor(string path)
        {
            switch (System.IO.Path.GetExtension(path ?? "").ToLowerInvariant())
            {
                case ".docx":
                case ".docm":
                case ".doc":
                case ".dotx":
                case ".rtf":
                case ".odt":
                    return OfficeKind.Word;
                case ".xlsx":
                case ".xlsm":
                case ".xls":
                case ".xltx":
                case ".csv":
                case ".ods":
                    return OfficeKind.Excel;
                case ".pptx":
                case ".pptm":
                case ".ppt":
                case ".potx":
                case ".odp":
                    return OfficeKind.PowerPoint;
                case ".msg":
                    return OfficeKind.Outlook;
                default:
                    return null;
            }
        }
    }
}
