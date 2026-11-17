using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LokalneAI.Harness.Tools;

namespace LokalneAI.Harness.Office
{
    /// <summary>Ustawienia wykonawcy Office’a (profil „office”).</summary>
    public sealed class OfficeOptions
    {
        /// <summary>Foldery, w których wolno czytać i zapisywać dokumenty (z podfolderami). Pusta lista = nigdzie.</summary>
        public IList<string> AllowedRoots { get; set; } = new List<string>();

        /// <summary>Folder bieżący dla ścieżek względnych („raport.docx”).</summary>
        public string WorkingFolder { get; set; }

        /// <summary>Ile znaków treści dokumentu, arkusza albo wiadomości najwyżej trafia do modelu (reszta jest obcinana).</summary>
        public int MaxOutputChars { get; set; } = 4000;

        /// <summary>
        /// Po edycji zostaw dokument otwarty i pokaż go użytkownikowi (Word, Excel, PowerPoint na wierzchu). False = zapisz, zamknij
        /// i zamknij program, jeśli to wykonawca go uruchomił — do testów i pracy w tle.
        /// </summary>
        public bool ShowAfterEdit { get; set; } = true;

        /// <summary>Po tylu sekundach wywołanie Office’a uznaje się za zawieszone (np. czeka na okno dialogowe).</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>Ile ostatnich wiadomości najwyżej przegląda <c>outlook_odebrane</c>, szukając frazy.</summary>
        public int InboxScanLimit { get; set; } = 300;
    }

    /// <summary>
    /// Polecenia profilu „office”: Word (czytanie, dopisywanie, zamiana tekstu), Excel (czytanie zakresu, wpisywanie wartości i formuł),
    /// PowerPoint (czytanie, nowy slajd), Outlook (szkic wiadomości, lista odebranych, treść wiadomości), otwieranie dokumentów
    /// i zapis kopii (np. PDF), a także <c>szukaj_plikow</c> i <c>pokaz_zawartosc</c> jak w profilu „komputer”.
    /// Pracuje przez automatyzację COM zainstalowanego Microsoft 365: dokument już otwarty u użytkownika jest edytowany na miejscu,
    /// a po edycji dokument zostaje otwarty na wierzchu (<see cref="OfficeOptions.ShowAfterEdit"/>). Ścieżki tylko w dozwolonych
    /// folderach (<see cref="OfficeOptions.AllowedRoots"/>). Czytanie idzie bez pytania, każda zmiana z pytaniem (<see cref="Decide"/>);
    /// szkic w Outlooku nigdy nie jest wysyłany.
    /// </summary>
    public sealed class OfficeExecutor : IToolExecutor, IConfirmingExecutor
    {
        private static readonly HashSet<string> SaveAsExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".doc", ".rtf", ".txt", ".odt", ".xlsx", ".xlsm", ".xls", ".csv", ".ods", ".pptx", ".ppt", ".odp",
        };

        private readonly OfficeOptions _options;
        private readonly FileSystemExecutor _files;
        private readonly List<MailRef> _lastInbox = new List<MailRef>();

        /// <summary>Tworzy wykonawcę. Pusta lista <see cref="OfficeOptions.AllowedRoots"/> = żadne polecenie na plikach nie przejdzie.</summary>
        public OfficeExecutor(OfficeOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _files = new FileSystemExecutor(new FileSystemOptions
            {
                AllowedRoots = _options.AllowedRoots,
                WorkingFolder = _options.WorkingFolder,
                MaxOutputChars = _options.MaxOutputChars,
            });
        }

        /// <summary>Które programy Office są zainstalowane (ProgID zarejestrowane w COM) — np. do podpowiedzi w oknie.</summary>
        public static string InstalledSummary()
        {
            var names = new List<string>();
            foreach (OfficeKind kind in Enum.GetValues(typeof(OfficeKind)))
            {
                if (Com.IsInstalled(OfficeHost.ProgId(kind)))
                {
                    names.Add(OfficeHost.Name(kind));
                }
            }

            return names.Count == 0 ? "brak programów Office (COM)" : string.Join(", ", names);
        }

        /// <summary>Czy pakiet Office (co najmniej Word albo Excel) jest zainstalowany na tym komputerze.</summary>
        public static bool IsOfficeInstalled() => Com.IsInstalled("Word.Application") || Com.IsInstalled("Excel.Application");

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            switch (call.Name)
            {
                case "szukaj_plikow":
                case "pokaz_zawartosc":
                    return await _files.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var text = await Task.Run(() => Execute(call, cancellationToken), cancellationToken).ConfigureAwait(false);
                return text.StartsWith("BŁĄD", StringComparison.Ordinal) ? ToolResult.Error(text) : ToolResult.Ok(text);
            }
            catch (OfficeException e)
            {
                return ToolResult.Error(e.Message);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                                      || e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                return ToolResult.Error("BŁĄD: " + e.Message);
            }
        }

        /// <summary>Czytanie bez pytania, każda zmiana dokumentu z pytaniem, ścieżka spoza dozwolonych folderów albo zły typ pliku — odrzucenie.</summary>
        public ConfirmationDecision Decide(ToolCall call)
        {
            string problem;
            switch (call.Name)
            {
                case "szukaj_plikow":
                case "pokaz_zawartosc":
                    return _files.Decide(call);

                case "office_otworz":
                {
                    ResolveToOpen(call, out problem);
                    return problem != null ? ConfirmationDecision.Reject(problem) : ConfirmationDecision.NotNeeded;
                }

                case "word_czytaj":
                case "excel_czytaj":
                case "powerpoint_czytaj":
                {
                    ResolveExisting(call, KindForTool(call.Name), out problem);
                    return problem != null ? ConfirmationDecision.Reject(problem) : ConfirmationDecision.NotNeeded;
                }

                case "word_zamien":
                {
                    var path = ResolveExisting(call, OfficeKind.Word, out problem);
                    if (problem == null && string.IsNullOrEmpty(call.GetString("szukaj")))
                    {
                        problem = "BŁĄD: podaj tekst do znalezienia (szukaj).";
                    }

                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    var all = call.GetBool("wszystkie") != false;
                    return ConfirmationDecision.Ask("Zamienić tekst w dokumencie Worda i zapisać?\n\n" + path + "\n„" + call.GetString("szukaj") + "” → „"
                        + (call.GetString("zamien_na") ?? "") + "”" + (all ? " (wszystkie wystąpienia)" : " (tylko pierwsze wystąpienie)"));
                }

                case "word_dopisz":
                {
                    var path = ResolveForWrite(call, OfficeKind.Word, out var exists, out problem);
                    var text = Content(call, "tekst", out var replaced);
                    if (problem == null && string.IsNullOrWhiteSpace(text))
                    {
                        problem = "BŁĄD: podaj tekst do dopisania (tekst).";
                    }

                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    var where = call.GetString("gdzie") ?? "koniec";
                    var message = new StringBuilder(exists ? "Dopisać do dokumentu Worda i zapisać?\n\n" : "Utworzyć nowy dokument Worda?\n\n").Append(path)
                        .Append("\nGdzie: ").Append(where == "po_akapicie" ? "po akapicie " + (int)(call.GetNumber("akapit") ?? 0) : where == "poczatek" ? "na początku" : "na końcu");
                    var style = call.GetString("styl");
                    if (!string.IsNullOrWhiteSpace(style))
                    {
                        message.Append(", styl: ").Append(style);
                    }

                    message.Append(replaced > 0 ? "\n(„\\n” od modelu zamienione na nowe linie: " + replaced + ")" : "").Append("\n\n").Append(Text.Shorten(text, 1500));
                    return ConfirmationDecision.Ask(message.ToString());
                }

                case "excel_zapisz":
                {
                    var path = ResolveForWrite(call, OfficeKind.Excel, out var exists, out problem);
                    var cell = (call.GetString("komorka") ?? "").Trim();
                    if (problem == null && !ExcelAutomation.CellAddress.IsMatch(cell))
                    {
                        problem = "BŁĄD: komorka ma być adresem jednej komórki, np. B3 (jest „" + cell + "”). Blok wartości wpisuje excel_wstaw_wiersze.";
                    }

                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    var sheet = call.GetString("arkusz");
                    return ConfirmationDecision.Ask((exists ? "Wpisać do skoroszytu Excela i zapisać?\n\n" : "Utworzyć nowy skoroszyt Excela?\n\n") + path + "\n"
                        + (string.IsNullOrWhiteSpace(sheet) ? "" : "arkusz " + sheet + ", ") + cell.ToUpperInvariant() + " = " + (call.GetString("wartosc") ?? ""));
                }

                case "excel_wstaw_wiersze":
                {
                    var path = ResolveForWrite(call, OfficeKind.Excel, out var exists, out problem);
                    var cell = (call.GetString("komorka") ?? "").Trim();
                    var rows = Rows(call, out var rowsProblem);
                    if (problem == null && !ExcelAutomation.CellAddress.IsMatch(cell))
                    {
                        problem = "BŁĄD: komorka ma być adresem lewej górnej komórki bloku, np. A2 (jest „" + cell + "”).";
                    }

                    if (problem == null)
                    {
                        problem = rowsProblem;
                    }

                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    var preview = new StringBuilder();
                    foreach (var row in rows.Take(8))
                    {
                        preview.Append('\n').Append(string.Join(" | ", row.Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))));
                    }

                    if (rows.Count > 8)
                    {
                        preview.Append("\n… i ").Append(rows.Count - 8).Append(" wierszy więcej");
                    }

                    var sheet = call.GetString("arkusz");
                    return ConfirmationDecision.Ask((exists ? "Wpisać do skoroszytu Excela i zapisać?\n\n" : "Utworzyć nowy skoroszyt Excela?\n\n") + path + "\n"
                        + (string.IsNullOrWhiteSpace(sheet) ? "" : "arkusz " + sheet + ", ") + rows.Count + " wierszy od " + cell.ToUpperInvariant() + ":" + Text.Shorten(preview.ToString(), 1500));
                }

                case "powerpoint_dodaj_slajd":
                {
                    var path = ResolveForWrite(call, OfficeKind.PowerPoint, out var exists, out problem);
                    if (problem == null && string.IsNullOrWhiteSpace(call.GetString("tytul")))
                    {
                        problem = "BŁĄD: podaj tytuł slajdu (tytul).";
                    }

                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    var body = Content(call, "tekst", out _);
                    return ConfirmationDecision.Ask((exists ? "Dodać slajd do prezentacji i zapisać?\n\n" : "Utworzyć nową prezentację?\n\n") + path
                        + "\nTytuł: " + call.GetString("tytul") + (string.IsNullOrWhiteSpace(body) ? "" : "\n\n" + Text.Shorten(body, 1500)));
                }

                case "office_zapisz_jako":
                {
                    var source = ResolveExisting(call, null, out problem);
                    var target = problem == null ? ResolveTarget(call.GetString("nowa_sciezka"), out problem) : null;
                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    var targetExists = File.Exists(target);
                    return ConfirmationDecision.Ask((targetExists ? "Nadpisać istniejący plik kopią dokumentu?\n\n" : "Zapisać kopię dokumentu?\n\n") + source + "\n→ " + target, targetExists);
                }

                case "outlook_szkic":
                {
                    Attachments(call, out problem);
                    if (problem == null && string.IsNullOrWhiteSpace(call.GetString("temat")) && string.IsNullOrWhiteSpace(Content(call, "tresc", out _)))
                    {
                        problem = "BŁĄD: podaj temat albo treść wiadomości.";
                    }

                    return problem != null ? ConfirmationDecision.Reject(problem) : ConfirmationDecision.NotNeeded;
                }

                case "outlook_czytaj":
                {
                    MailFromList(call, out problem);
                    return problem != null ? ConfirmationDecision.Reject(problem) : ConfirmationDecision.NotNeeded;
                }

                default:
                    return ConfirmationDecision.NotNeeded;
            }
        }

        private string Execute(ToolCall call, CancellationToken cancellationToken)
        {
            string problem;
            switch (call.Name)
            {
                case "office_otworz":
                {
                    var path = ResolveToOpen(call, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var kind = OfficeHost.KindFor(path);
                    if (kind == null || kind == OfficeKind.Outlook)
                    {
                        // PDF, TXT, MSG… — program domyślny (jak „uruchom” w profilu komputer; pliki wykonywalne odrzuca ResolveToOpen).
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        return "OK otwarto domyślnym programem: " + path;
                    }

                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(kind.Value, visible: true))
                        {
                            host.Open(path, readOnly: false, visible: true);
                            host.Finish(keepOpen: true);
                            return "OK otwarto w programie " + OfficeHost.Name(kind.Value) + ": " + path;
                        }
                    }, cancellationToken);
                }

                case "word_czytaj":
                {
                    var path = ResolveExisting(call, OfficeKind.Word, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var from = Math.Max(1, (int)(call.GetNumber("od_akapitu") ?? 1));
                    var count = Math.Max(1, Math.Min(2000, (int)(call.GetNumber("ile_akapitow") ?? 80)));
                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(OfficeKind.Word, visible: false))
                        {
                            try
                            {
                                return WordAutomation.Read(host, path, from, count, _options.MaxOutputChars);
                            }
                            finally
                            {
                                host.Finish(keepOpen: false);
                            }
                        }
                    }, cancellationToken);
                }

                case "word_zamien":
                {
                    var path = ResolveExisting(call, OfficeKind.Word, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var find = call.GetString("szukaj") ?? "";
                    var replaceWith = call.GetString("zamien_na") ?? "";
                    var all = call.GetBool("wszystkie") != false;
                    var matchCase = call.GetBool("wielkosc_liter") == true;
                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(OfficeKind.Word, visible: _options.ShowAfterEdit))
                        {
                            return WordAutomation.Replace(host, path, find, replaceWith, all, matchCase, _options.ShowAfterEdit);
                        }
                    }, cancellationToken);
                }

                case "word_dopisz":
                {
                    var path = ResolveForWrite(call, OfficeKind.Word, out _, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var text = Content(call, "tekst", out _);
                    var where = call.GetString("gdzie") ?? "koniec";
                    var after = (int)(call.GetNumber("akapit") ?? 0);
                    var style = call.GetString("styl");
                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(OfficeKind.Word, visible: _options.ShowAfterEdit))
                        {
                            return WordAutomation.Append(host, path, text, where, after, style, _options.ShowAfterEdit);
                        }
                    }, cancellationToken);
                }

                case "excel_czytaj":
                {
                    var path = ResolveExisting(call, OfficeKind.Excel, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var range = (call.GetString("zakres") ?? "").Trim();
                    if (range.Length > 0 && !ExcelAutomation.RangeAddress.IsMatch(range))
                    {
                        return "BŁĄD: zakres ma być adresem, np. A1:D20 (jest „" + range + "”).";
                    }

                    var sheet = call.GetString("arkusz");
                    var formulas = call.GetBool("formuly") == true;
                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(OfficeKind.Excel, visible: false))
                        {
                            try
                            {
                                return ExcelAutomation.Read(host, path, sheet, range, formulas, _options.MaxOutputChars);
                            }
                            finally
                            {
                                host.Finish(keepOpen: false);
                            }
                        }
                    }, cancellationToken);
                }

                case "excel_zapisz":
                {
                    var path = ResolveForWrite(call, OfficeKind.Excel, out _, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var cell = (call.GetString("komorka") ?? "").Trim();
                    if (!ExcelAutomation.CellAddress.IsMatch(cell))
                    {
                        return "BŁĄD: komorka ma być adresem jednej komórki, np. B3 (jest „" + cell + "”).";
                    }

                    var sheet = call.GetString("arkusz");
                    var value = call.GetString("wartosc") ?? (call.GetNumber("wartosc")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(OfficeKind.Excel, visible: _options.ShowAfterEdit))
                        {
                            return ExcelAutomation.Write(host, path, sheet, cell, value, _options.ShowAfterEdit);
                        }
                    }, cancellationToken);
                }

                case "excel_wstaw_wiersze":
                {
                    var path = ResolveForWrite(call, OfficeKind.Excel, out _, out problem);
                    var rows = Rows(call, out var rowsProblem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    if (rowsProblem != null)
                    {
                        return rowsProblem;
                    }

                    var cell = (call.GetString("komorka") ?? "").Trim();
                    if (!ExcelAutomation.CellAddress.IsMatch(cell))
                    {
                        return "BŁĄD: komorka ma być adresem lewej górnej komórki bloku, np. A2 (jest „" + cell + "”).";
                    }

                    var sheet = call.GetString("arkusz");
                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(OfficeKind.Excel, visible: _options.ShowAfterEdit))
                        {
                            return ExcelAutomation.WriteRows(host, path, sheet, cell, rows, _options.ShowAfterEdit);
                        }
                    }, cancellationToken);
                }

                case "powerpoint_czytaj":
                {
                    var path = ResolveExisting(call, OfficeKind.PowerPoint, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(OfficeKind.PowerPoint, visible: false))
                        {
                            try
                            {
                                return PowerPointAutomation.Read(host, path, _options.MaxOutputChars);
                            }
                            finally
                            {
                                host.Finish(keepOpen: false);
                            }
                        }
                    }, cancellationToken);
                }

                case "powerpoint_dodaj_slajd":
                {
                    var path = ResolveForWrite(call, OfficeKind.PowerPoint, out _, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var title = call.GetString("tytul") ?? "";
                    var body = Content(call, "tekst", out _);
                    var position = (int)(call.GetNumber("pozycja") ?? 0);
                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(OfficeKind.PowerPoint, visible: _options.ShowAfterEdit))
                        {
                            return PowerPointAutomation.AddSlide(host, path, title, body, position, _options.ShowAfterEdit);
                        }
                    }, cancellationToken);
                }

                case "office_zapisz_jako":
                {
                    var source = ResolveExisting(call, null, out problem);
                    var target = problem == null ? ResolveTarget(call.GetString("nowa_sciezka"), out problem) : null;
                    if (problem != null)
                    {
                        return problem;
                    }

                    var kind = OfficeHost.KindFor(source).Value;
                    return Sta(() =>
                    {
                        using (var host = OfficeHost.Start(kind, visible: false))
                        {
                            try
                            {
                                switch (kind)
                                {
                                    case OfficeKind.Word: return WordAutomation.SaveAs(host, source, target);
                                    case OfficeKind.Excel: return ExcelAutomation.SaveAs(host, source, target);
                                    default: return PowerPointAutomation.SaveAs(host, source, target);
                                }
                            }
                            finally
                            {
                                host.Finish(keepOpen: false);
                            }
                        }
                    }, cancellationToken);
                }

                case "outlook_szkic":
                {
                    var attachments = Attachments(call, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    var to = call.GetString("do") ?? "";
                    var cc = call.GetString("dw");
                    var subject = call.GetString("temat") ?? "";
                    var body = Content(call, "tresc", out _);
                    return Sta(() => OutlookAutomation.Draft(to, cc, subject, body, attachments), cancellationToken);
                }

                case "outlook_odebrane":
                {
                    var take = Math.Max(1, Math.Min(50, (int)(call.GetNumber("ile") ?? 10)));
                    var phrase = call.GetString("fraza");
                    return Sta(() => OutlookAutomation.Inbox(take, phrase, _options.InboxScanLimit, _lastInbox), cancellationToken);
                }

                case "outlook_czytaj":
                {
                    var mail = MailFromList(call, out problem);
                    if (problem != null)
                    {
                        return problem;
                    }

                    return Sta(() => OutlookAutomation.Read(mail, _options.MaxOutputChars), cancellationToken);
                }

                default:
                    return "BŁĄD: nieobsługiwane polecenie: " + call.Name;
            }
        }

        private string Sta(Func<string> work, CancellationToken cancellationToken) => Com.RunSta(work, cancellationToken, _options.Timeout);

        private static OfficeKind? KindForTool(string tool)
        {
            if (tool.StartsWith("word_", StringComparison.Ordinal))
            {
                return OfficeKind.Word;
            }

            if (tool.StartsWith("excel_", StringComparison.Ordinal))
            {
                return OfficeKind.Excel;
            }

            if (tool.StartsWith("powerpoint_", StringComparison.Ordinal))
            {
                return OfficeKind.PowerPoint;
            }

            return null;
        }

        /// <summary>Pliki, których <c>office_otworz</c> nie uruchamia: programy i skrypty (to robi <c>uruchom</c> z profilu „komputer”, z pytaniem).</summary>
        private static readonly HashSet<string> ExecutableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".msi", ".msp", ".lnk", ".scr", ".jar",
            ".py", ".pyw", ".reg", ".hta", ".cpl", ".appref-ms", ".url",
        };

        /// <summary>Ścieżka istniejącego pliku do otwarcia (<c>office_otworz</c>): dokument Office’a albo inny dokument, nigdy program.</summary>
        private string ResolveToOpen(ToolCall call, out string problem)
        {
            var path = _files.Resolve(call.GetString("sciezka"), out problem);
            if (problem != null)
            {
                return null;
            }

            if (!File.Exists(path))
            {
                problem = Directory.Exists(path) ? "BŁĄD: " + path + " to folder, nie dokument." : "BŁĄD: nie ma pliku " + path + " (szukaj_plikow pokaże, co jest w folderze).";
                return null;
            }

            if (ExecutableExtensions.Contains(Path.GetExtension(path)))
            {
                problem = "BŁĄD: " + Path.GetFileName(path) + " to program albo skrypt, nie dokument — office_otworz go nie uruchamia.";
                return null;
            }

            return path;
        }

        /// <summary>Ścieżka istniejącego dokumentu w dozwolonym folderze, właściwego programu (<paramref name="kind"/> null = dowolny Office).</summary>
        private string ResolveExisting(ToolCall call, OfficeKind? kind, out string problem)
        {
            var path = _files.Resolve(call.GetString("sciezka"), out problem);
            if (problem != null)
            {
                return null;
            }

            if (!File.Exists(path))
            {
                problem = Directory.Exists(path) ? "BŁĄD: " + path + " to folder, nie dokument." : "BŁĄD: nie ma pliku " + path + " (szukaj_plikow pokaże, co jest w folderze).";
                return null;
            }

            problem = CheckKind(path, kind);
            return problem == null ? path : null;
        }

        /// <summary>Ścieżka dokumentu do zapisu: istniejący albo nowy w istniejącym folderze, z rozszerzeniem właściwego programu.</summary>
        private string ResolveForWrite(ToolCall call, OfficeKind kind, out bool exists, out string problem)
        {
            exists = false;
            var path = _files.Resolve(call.GetString("sciezka"), out problem);
            if (problem != null)
            {
                return null;
            }

            if (Directory.Exists(path))
            {
                problem = "BŁĄD: " + path + " to folder. Podaj nazwę pliku, np. " + Path.Combine(path, DefaultName(kind));
                return null;
            }

            exists = File.Exists(path);
            if (!exists && !Directory.Exists(Path.GetDirectoryName(path)))
            {
                problem = "BŁĄD: nie ma folderu " + Path.GetDirectoryName(path) + " (nowy dokument powstaje tylko w istniejącym folderze).";
                return null;
            }

            problem = CheckKind(path, kind);
            return problem == null ? path : null;
        }

        private static string DefaultName(OfficeKind kind)
        {
            switch (kind)
            {
                case OfficeKind.Word: return "dokument.docx";
                case OfficeKind.Excel: return "zeszyt.xlsx";
                default: return "prezentacja.pptx";
            }
        }

        private static string CheckKind(string path, OfficeKind? kind)
        {
            var actual = OfficeHost.KindFor(path);
            if (actual == null || actual == OfficeKind.Outlook)
            {
                return "BŁĄD: " + Path.GetFileName(path) + " nie jest dokumentem Worda, Excela ani PowerPointa (obsługiwane: docx, doc, rtf, odt, xlsx, xlsm, xls, csv, pptx, ppt).";
            }

            if (kind != null && actual != kind)
            {
                return "BŁĄD: " + Path.GetFileName(path) + " to plik programu " + OfficeHost.Name(actual.Value) + ", a polecenie jest dla programu " + OfficeHost.Name(kind.Value)
                    + ". Użyj " + OfficeHost.Name(actual.Value).ToLowerInvariant() + "_czytaj / " + OfficeHost.Name(actual.Value).ToLowerInvariant() + "_…";
            }

            return null;
        }

        /// <summary>Ścieżka docelowa kopii: w dozwolonym folderze, folder istnieje, rozszerzenie z listy (PDF albo format Office’a).</summary>
        private string ResolveTarget(string target, out string problem)
        {
            var path = _files.Resolve(target, out problem);
            if (problem != null)
            {
                return null;
            }

            if (Directory.Exists(path))
            {
                problem = "BŁĄD: " + path + " to folder. Podaj nazwę pliku z rozszerzeniem, np. raport.pdf.";
                return null;
            }

            if (!Directory.Exists(Path.GetDirectoryName(path)))
            {
                problem = "BŁĄD: nie ma folderu " + Path.GetDirectoryName(path) + ".";
                return null;
            }

            if (!SaveAsExtensions.Contains(Path.GetExtension(path)))
            {
                problem = "BŁĄD: nieobsługiwane rozszerzenie „" + Path.GetExtension(path) + "”. Dozwolone: " + string.Join(", ", SaveAsExtensions) + ".";
                return null;
            }

            return path;
        }

        /// <summary>Tekst od modelu z naprawionym „\n” (małe modele piszą je dosłownie).</summary>
        private static string Content(ToolCall call, string name, out int replaced)
        {
            replaced = 0;
            var raw = call.GetString(name);
            return raw == null ? "" : EscapedNewlines.Fix(raw, out replaced);
        }

        private static IList<IList<object>> Rows(ToolCall call, out string problem)
        {
            problem = null;
            var rows = new List<IList<object>>();
            if (!call.Arguments.TryGetProperty("wiersze", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                problem = "BŁĄD: podaj wiersze jako listę list, np. [[\"Pozycja\",\"Masa kg\"],[\"HEB300\",117]].";
                return rows;
            }

            var cells = 0;
            foreach (var row in array.EnumerateArray())
            {
                var values = new List<object>();
                if (row.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cell in row.EnumerateArray())
                    {
                        values.Add(Scalar(cell));
                        cells++;
                    }
                }
                else
                {
                    values.Add(Scalar(row)); // jedna wartość w wierszu — też przyjmujemy
                    cells++;
                }

                rows.Add(values);
            }

            if (rows.Count == 0)
            {
                problem = "BŁĄD: lista wierszy jest pusta.";
            }
            else if (cells > ExcelAutomation.MaxWriteCells)
            {
                problem = "BŁĄD: za dużo komórek naraz (" + cells + ", najwyżej " + ExcelAutomation.MaxWriteCells + "). Podziel na kilka wywołań.";
            }

            return rows;
        }

        private static object Scalar(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number: return element.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Null: return "";
                default: return element.GetRawText();
            }
        }

        private List<string> Attachments(ToolCall call, out string problem)
        {
            problem = null;
            var files = new List<string>();
            if (!call.Arguments.TryGetProperty("zalaczniki", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return files;
            }

            foreach (var item in array.EnumerateArray())
            {
                var path = _files.Resolve(item.ValueKind == JsonValueKind.String ? item.GetString() : null, out problem);
                if (problem != null)
                {
                    return files;
                }

                if (!File.Exists(path))
                {
                    problem = "BŁĄD: nie ma załącznika " + path;
                    return files;
                }

                files.Add(path);
            }

            return files;
        }

        private MailRef MailFromList(ToolCall call, out string problem)
        {
            problem = null;
            var number = (int)(call.GetNumber("numer") ?? 0);
            if (_lastInbox.Count == 0)
            {
                problem = "BŁĄD: najpierw wywołaj outlook_odebrane — numer wiadomości pochodzi z tej listy.";
                return null;
            }

            if (number < 1 || number > _lastInbox.Count)
            {
                problem = "BŁĄD: na ostatniej liście są wiadomości 1–" + _lastInbox.Count + ", nie ma numeru " + number + ".";
                return null;
            }

            return _lastInbox[number - 1];
        }
    }
}
