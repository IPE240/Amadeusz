using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Ustawienia wykonawcy plików (profil „pliki”).</summary>
    public sealed class FileSystemOptions
    {
        /// <summary>Foldery, w których wolno działać (z podfolderami). Pusta lista = nigdzie.</summary>
        public IList<string> AllowedRoots { get; set; } = new List<string>();

        /// <summary>Folder bieżący dla ścieżek względnych („utwórz folder Raporty”).</summary>
        public string WorkingFolder { get; set; }

        /// <summary>Ile pozycji najwyżej pokazać przy liście zawartości.</summary>
        public int MaxListed { get; set; } = 60;

        /// <summary>Program PowerShella (<c>powershell.exe</c> = Windows PowerShell 5.1; może być <c>pwsh.exe</c>).</summary>
        public string PowerShellExecutable { get; set; } = "powershell.exe";

        /// <summary>Po tylu sekundach polecenie PowerShella jest przerywane.</summary>
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>Ile znaków wyniku polecenia albo pliku najwyżej trafia do modelu (reszta jest obcinana).</summary>
        public int MaxOutputChars { get; set; } = 4000;

        /// <summary>
        /// Foldery z zainstalowanymi programami: stąd wolno uruchamiać pliki wykonywalne (ale nie czytać ani zmieniać plików).
        /// Domyślnie Program Files, Windows i %LOCALAPPDATA%\Programs.
        /// </summary>
        public IList<string> ProgramRoots { get; set; } = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
    }

    /// <summary>
    /// Polecenia na komputerze dla profili „pliki” i „komputer”: utworzenie folderu, lista, kopiowanie, zmiana nazwy/przeniesienie,
    /// otwarcie w Eksploratorze, czytanie, zapis i wyszukiwanie plików, uruchamianie programów i polecenia PowerShella.
    /// Profil decyduje, które z nich model widzi. Każda ścieżka musi leżeć w jednym z dozwolonych folderów
    /// (<see cref="FileSystemOptions.AllowedRoots"/>); kopiowanie i przenoszenie niczego nie nadpisują.
    /// O zgodę pyta sesja według <see cref="Decide"/>: polecenia, które tylko czytają, idą bez pytania.
    /// </summary>
    public sealed class FileSystemExecutor : IToolExecutor, IConfirmingExecutor, IWorkingFolderProvider, IFolderAccess
    {
        /// <summary>Dodaje folder do dozwolonych (po zgodzie użytkownika w rozmowie, patrz <see cref="IFolderAccess"/>).</summary>
        public bool AllowFolder(string folder) => FolderAccess.AddTo(_options.AllowedRoots, folder);

        /// <summary>Folder ścieżek względnych (zmienia go <c>przejdz_do_folderu</c> i <c>cd</c>) — dla strażnika bezpieczeństwa.</summary>
        public string WorkingFolderFor(ToolCall call) => WorkingFolderOrRoot;

        private static readonly HashSet<string> ExecutableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".msi", ".msp", ".lnk", ".scr", ".jar",
            ".py", ".pyw", ".reg", ".hta", ".cpl", ".appref-ms", ".url",
        };

        private readonly FileSystemOptions _options;

        /// <summary>Tworzy wykonawcę. Pusta lista <see cref="FileSystemOptions.AllowedRoots"/> = żadne polecenie na plikach nie przejdzie.</summary>
        public FileSystemExecutor(FileSystemOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Folder bieżący (zmienia go polecenie <c>przejdz_do_folderu</c>).</summary>
        public string WorkingFolder => _options.WorkingFolder;

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
        {
            try
            {
                if (call.Name == "powershell")
                {
                    return await RunPowerShellAsync(call, cancellationToken).ConfigureAwait(false);
                }

                return Execute(call);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException
                                      || e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                return ToolResult.Error("BŁĄD: " + e.Message);
            }
        }

        /// <summary>Czy pytać o zgodę: zmiany i uruchamianie — tak; samo czytanie w dozwolonych folderach — nie; spoza nich — odrzucić.</summary>
        public ConfirmationDecision Decide(ToolCall call)
        {
            string problem;
            switch (call.Name)
            {
                case "utworz_folder":
                {
                    var path = Resolve(call.GetString("sciezka"), out problem);
                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    return Directory.Exists(path) ? ConfirmationDecision.NotNeeded : ConfirmationDecision.Ask("Utworzyć folder?\n\n" + path);
                }

                case "kopiuj":
                case "przenies":
                {
                    var source = Resolve(call.GetString("zrodlo"), out problem);
                    var target = problem == null ? Resolve(call.GetString("cel"), out problem) : null;
                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    return ConfirmationDecision.Ask((call.Name == "kopiuj" ? "Skopiować?" : "Przenieść albo zmienić nazwę?") + "\n\n" + source + "\n→ " + target);
                }

                case "uruchom":
                {
                    var target = ResolveProgram(call.GetString("program"), out var isExecutable, out problem);
                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    if (!isExecutable)
                    {
                        return ConfirmationDecision.NotNeeded; // dokument w dozwolonym folderze otwiera się domyślnym programem
                    }

                    var arguments = call.GetString("argumenty");
                    return ConfirmationDecision.Ask("Uruchomić program?\n\n" + target + (string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments));
                }

                case "zapisz_plik":
                {
                    var file = PrepareWrite(call, out var content, out var replaced, out var exists, out problem);
                    if (problem != null)
                    {
                        return ConfirmationDecision.Reject(problem);
                    }

                    var lines = content.Length == 0 ? 0 : content.TrimEnd('\n').Split('\n').Length;
                    var message = new StringBuilder(exists ? "Nadpisać plik? Obecna treść zniknie.\n\n" : "Zapisać nowy plik?\n\n").Append(file)
                        .Append("\n(linii: ").Append(lines)
                        .Append(replaced > 0 ? ", „\\n” od modelu zamienione na nowe linie: " + replaced : "")
                        .Append(")\n\n").Append(Text.Shorten(content, 1500));
                    return ConfirmationDecision.Ask(message.ToString(), exists);
                }

                case "powershell":
                {
                    var script = call.GetString("polecenie");
                    var analysis = PowerShellPolicy.Analyze(script, WorkingFolderOrRoot, _options.AllowedRoots, _options.ProgramRoots);
                    if (analysis.Verdict == ConfirmationKind.Reject)
                    {
                        return ConfirmationDecision.Reject(analysis.Reason);
                    }

                    if (analysis.Verdict == ConfirmationKind.None)
                    {
                        return ConfirmationDecision.NotNeeded;
                    }

                    var message = new StringBuilder("Wykonać polecenie PowerShella?\n\nFolder: ").Append(WorkingFolderOrRoot).Append("\n\n")
                        .Append(Text.Shorten(script, 1500));
                    var purpose = call.GetString("opis");
                    if (!string.IsNullOrWhiteSpace(purpose))
                    {
                        message.Append("\n\nCel (według modelu): ").Append(Text.Shorten(purpose, 300));
                    }

                    foreach (var warning in analysis.Warnings)
                    {
                        message.Append("\n\nUWAGA: ").Append(warning);
                    }

                    return ConfirmationDecision.Ask(message.ToString(), analysis.Deletes);
                }

                default:
                    return ConfirmationDecision.NotNeeded;
            }
        }

        private string WorkingFolderOrRoot
            => !string.IsNullOrWhiteSpace(_options.WorkingFolder)
                ? _options.WorkingFolder
                : PowerShellPolicy.NormalizeRoots(_options.AllowedRoots).Select(r => r.TrimEnd('\\')).FirstOrDefault() ?? Environment.CurrentDirectory;

        private async Task<ToolResult> RunPowerShellAsync(ToolCall call, CancellationToken cancellationToken)
        {
            var script = call.GetString("polecenie");
            var folder = WorkingFolderOrRoot;
            var analysis = PowerShellPolicy.Analyze(script, folder, _options.AllowedRoots, _options.ProgramRoots);
            if (analysis.Verdict == ConfirmationKind.Reject)
            {
                return ToolResult.Error(analysis.Reason);
            }

            var run = await PowerShellRunner.RunAsync(_options.PowerShellExecutable, script, folder, _options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            var text = new StringBuilder();
            var note = "";
            if (!string.IsNullOrWhiteSpace(run.FinalFolder) && !string.Equals(run.FinalFolder.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(run.FinalFolder) && PowerShellPolicy.IsInside(run.FinalFolder, PowerShellPolicy.NormalizeRoots(_options.AllowedRoots)))
                {
                    _options.WorkingFolder = run.FinalFolder;
                }
                else
                {
                    note = " (polecenie skończyło w " + run.FinalFolder + ", poza dozwolonymi folderami — folder bieżący się nie zmienia)";
                }
            }

            if (run.TimedOut)
            {
                text.Append("BŁĄD: polecenie przekroczyło limit ").Append((int)_options.CommandTimeout.TotalSeconds).Append(" s i zostało przerwane.");
            }
            else
            {
                text.Append(run.HasErrors ? "BŁĄD w poleceniu" : "OK");
                if (!string.Equals(WorkingFolderOrRoot.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    text.Append(", folder bieżący teraz: ").Append(WorkingFolderOrRoot);
                }

                text.Append(note);
            }

            if (run.Output.Length > 0)
            {
                text.Append('\n').Append(Truncate(run.Output, _options.MaxOutputChars));
            }
            return run.TimedOut || run.HasErrors ? ToolResult.Error(text.ToString()) : ToolResult.Ok(text.ToString());
        }

        /// <summary>Początek i koniec długiego tekstu z informacją, ile pominięto.</summary>
        internal static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max || max < 200)
            {
                return text ?? "";
            }

            var tail = max / 5;
            var head = max - tail;
            return text.Substring(0, head) + "\n… (pominięto " + (text.Length - head - tail) + " znaków) …\n" + text.Substring(text.Length - tail);
        }

        /// <summary>
        /// Program albo dokument do otwarcia: nazwa bez ścieżki (np. <c>notepad</c>, <c>excel</c>) albo plik w dozwolonym folderze;
        /// pliki wykonywalne także z folderów programów.
        /// </summary>
        private string ResolveProgram(string program, out bool isExecutable, out string problem)
        {
            isExecutable = true;
            problem = null;
            if (string.IsNullOrWhiteSpace(program))
            {
                problem = "BŁĄD: nie podano programu ani pliku.";
                return null;
            }

            var name = Environment.ExpandEnvironmentVariables(program.Trim().Trim('"'));
            var hasPath = name.IndexOfAny(new[] { '\\', '/' }) >= 0 || name.Contains(":");
            var inWorkingFolder = !hasPath && File.Exists(Path.Combine(WorkingFolderOrRoot, name));
            if (!hasPath && !inWorkingFolder)
            {
                return name; // program z PATH albo zarejestrowany w Windows (notepad, excel, explorer)
            }

            var full = Path.GetFullPath(hasPath && Path.IsPathRooted(name) ? name : Path.Combine(WorkingFolderOrRoot, name));
            isExecutable = ExecutableExtensions.Contains(Path.GetExtension(full));
            var inside = PowerShellPolicy.IsInside(full, PowerShellPolicy.NormalizeRoots(_options.AllowedRoots));
            var inPrograms = PowerShellPolicy.IsInside(full, PowerShellPolicy.NormalizeRoots(_options.ProgramRoots));
            if (!inside && !(isExecutable && inPrograms))
            {
                problem = "BŁĄD: " + full + " jest poza dozwolonymi folderami.";
                return null;
            }

            if (!File.Exists(full) && !Directory.Exists(full))
            {
                problem = "BŁĄD: nie ma " + full;
                return null;
            }

            if (Directory.Exists(full))
            {
                isExecutable = false; // folder — otworzy się w Eksploratorze
            }

            return full;
        }

        private ToolResult Execute(ToolCall call)
        {
            switch (call.Name)
            {
                case "utworz_folder":
                {
                    var path = Resolve(call.GetString("sciezka"), out var problem);
                    if (problem != null)
                    {
                        return ToolResult.Error(problem);
                    }

                    if (Directory.Exists(path))
                    {
                        return ToolResult.Ok("OK folder już istnieje: " + path);
                    }

                    Directory.CreateDirectory(path);
                    return ToolResult.Ok("OK utworzono folder: " + path);
                }

                case "pokaz_zawartosc":
                {
                    var path = Resolve(call.GetString("sciezka") ?? ".", out var problem);
                    if (problem != null)
                    {
                        return ToolResult.Error(problem);
                    }

                    if (!Directory.Exists(path))
                    {
                        return ToolResult.Error("BŁĄD: nie ma folderu " + path);
                    }

                    var pattern = string.IsNullOrWhiteSpace(call.GetString("wzorzec")) ? "*" : call.GetString("wzorzec");
                    var folders = Directory.GetDirectories(path, pattern).Select(d => "[folder] " + Path.GetFileName(d));
                    var files = Directory.GetFiles(path, pattern).Select(f => Path.GetFileName(f) + "  (" + (new FileInfo(f).Length / 1024) + " kB)");
                    var all = folders.Concat(files).ToList();
                    var text = new StringBuilder("OK ").Append(path).Append(": ").Append(all.Count).Append(" pozycji");
                    foreach (var item in all.Take(_options.MaxListed))
                    {
                        text.AppendLine().Append(item);
                    }

                    if (all.Count > _options.MaxListed)
                    {
                        text.AppendLine().Append("… i ").Append(all.Count - _options.MaxListed).Append(" więcej");
                    }

                    return ToolResult.Ok(text.ToString());
                }

                case "przejdz_do_folderu":
                {
                    var path = Resolve(call.GetString("sciezka"), out var problem);
                    if (problem != null)
                    {
                        return ToolResult.Error(problem);
                    }

                    if (!Directory.Exists(path))
                    {
                        return ToolResult.Error("BŁĄD: nie ma folderu " + path);
                    }

                    _options.WorkingFolder = path;
                    return ToolResult.Ok("OK folder bieżący: " + path);
                }

                case "kopiuj":
                case "przenies":
                {
                    var source = Resolve(call.GetString("zrodlo"), out var problem);
                    var target = problem == null ? Resolve(call.GetString("cel"), out problem) : null;
                    if (problem != null)
                    {
                        return ToolResult.Error(problem);
                    }

                    if (Directory.Exists(target))
                    {
                        target = Path.Combine(target, Path.GetFileName(source.TrimEnd('\\', '/')));
                    }

                    if (File.Exists(target) || Directory.Exists(target))
                    {
                        return ToolResult.Error("BŁĄD: cel już istnieje, niczego nie nadpisuję: " + target);
                    }

                    if (File.Exists(source))
                    {
                        if (call.Name == "kopiuj")
                        {
                            File.Copy(source, target);
                        }
                        else
                        {
                            File.Move(source, target);
                        }
                    }
                    else if (Directory.Exists(source))
                    {
                        if (call.Name == "kopiuj")
                        {
                            CopyDirectory(source, target);
                        }
                        else
                        {
                            Directory.Move(source, target);
                        }
                    }
                    else
                    {
                        return ToolResult.Error("BŁĄD: nie ma " + source);
                    }

                    return ToolResult.Ok("OK " + (call.Name == "kopiuj" ? "skopiowano" : "przeniesiono") + ": " + source + " → " + target);
                }

                case "otworz_w_eksploratorze":
                {
                    var path = Resolve(call.GetString("sciezka") ?? ".", out var problem);
                    if (problem != null)
                    {
                        return ToolResult.Error(problem);
                    }

                    if (!Directory.Exists(path) && !File.Exists(path))
                    {
                        return ToolResult.Error("BŁĄD: nie ma " + path);
                    }

                    Process.Start(new ProcessStartInfo("explorer.exe", File.Exists(path) ? "/select,\"" + path + "\"" : "\"" + path + "\"") { UseShellExecute = true });
                    return ToolResult.Ok("OK otwarto: " + path);
                }

                case "uruchom":
                {
                    var target = ResolveProgram(call.GetString("program"), out _, out var problem);
                    if (problem != null)
                    {
                        return ToolResult.Error(problem);
                    }

                    var arguments = call.GetString("argumenty") ?? "";
                    using (Process.Start(new ProcessStartInfo(target, arguments) { UseShellExecute = true, WorkingDirectory = WorkingFolderOrRoot }))
                    {
                    }

                    return ToolResult.Ok("OK uruchomiono: " + target + (arguments.Length > 0 ? " " + arguments : ""));
                }

                case "czytaj_plik":
                    return ReadFile(call);

                case "zapisz_plik":
                    return WriteFile(call);

                case "szukaj_plikow":
                    return FindFiles(call);

                default:
                    return ToolResult.Error("Nieobsługiwane polecenie: " + call.Name);
            }
        }

        /// <summary>Ścieżka bezwzględna w dozwolonym folderze; inaczej opis problemu.</summary>
        internal string Resolve(string path, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                problem = "BŁĄD: brak ścieżki.";
                return null;
            }

            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            var full = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(_options.WorkingFolder ?? Environment.CurrentDirectory, path));
            var allowed = _options.AllowedRoots
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => Path.GetFullPath(Environment.ExpandEnvironmentVariables(r)).TrimEnd('\\', '/') + Path.DirectorySeparatorChar)
                .ToList();
            var inside = allowed.Any(root => (full.TrimEnd('\\', '/') + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.OrdinalIgnoreCase));
            if (!inside)
            {
                problem = "BŁĄD: ścieżka " + full + " jest poza dozwolonymi folderami (" + string.Join("; ", allowed.Select(a => a.TrimEnd('\\'))) + ").";
                return null;
            }

            return full;
        }

        private ToolResult ReadFile(ToolCall call)
        {
            var path = Resolve(call.GetString("sciezka"), out var problem);
            if (problem != null)
            {
                return ToolResult.Error(problem);
            }

            if (!File.Exists(path))
            {
                return ToolResult.Error("BŁĄD: nie ma pliku " + path);
            }

            var info = new FileInfo(path);
            var head = new byte[Math.Min(8192, info.Length)];
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var read = 0;
                while (read < head.Length)
                {
                    var n = stream.Read(head, read, head.Length - read);
                    if (n == 0)
                    {
                        break;
                    }

                    read += n;
                }
            }

            var utf16 = head.Length >= 2 && (head[0] == 0xFF && head[1] == 0xFE || head[0] == 0xFE && head[1] == 0xFF);
            if (Array.IndexOf(head, (byte)0) >= 0 && !utf16)
            {
                return ToolResult.Ok("OK " + path + " to plik binarny (" + (info.Length / 1024) + " kB) — nie da się go przeczytać jako tekstu. Można go otworzyć poleceniem uruchom.");
            }

            var from = Math.Max(1, (int)(call.GetNumber("od_linii") ?? 1));
            var count = Math.Max(1, Math.Min(1000, (int)(call.GetNumber("ile_linii") ?? 200)));
            var text = new StringBuilder();
            var lineNo = 0;
            var shown = 0;
            var more = false;
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNo++;
                    if (lineNo < from)
                    {
                        continue;
                    }

                    if (shown >= count || text.Length > _options.MaxOutputChars)
                    {
                        more = true;
                        if (lineNo > from + count + 200000)
                        {
                            break;
                        }

                        continue; // liczymy dalej linie, żeby podać ich liczbę
                    }

                    text.Append(lineNo).Append(": ").Append(line).Append('\n');
                    shown++;
                }
            }

            var header = "OK " + path + " (" + (info.Length / 1024) + " kB, linii: " + lineNo + (shown > 0 ? ", pokazano " + from + "–" + (from + shown - 1) : "") + ")";
            if (shown == 0)
            {
                return ToolResult.Ok(header + "\n(brak linii od " + from + ")");
            }

            return ToolResult.Ok(header + "\n" + Truncate(text.ToString().TrimEnd('\n'), _options.MaxOutputChars)
                + (more ? "\n… dalsze linie: czytaj_plik z od_linii=" + (from + shown) : ""));
        }

        /// <summary>Najdłuższa treść pliku od modelu (znaki).</summary>
        private const int MaxWriteChars = 500_000;

        /// <summary>Rozszerzenia skryptów Windows: zapisywane z końcem linii CRLF.</summary>
        private static readonly HashSet<string> WindowsScriptExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".reg",
        };

        /// <summary>
        /// Sprawdza wywołanie <c>zapisz_plik</c>: ścieżka w dozwolonym folderze, treść, zgoda na nadpisanie.
        /// Zwraca pełną ścieżkę i treść do zapisu (z „\n” od modelu zamienionym na nowe linie).
        /// </summary>
        private string PrepareWrite(ToolCall call, out string content, out int replaced, out bool exists, out string problem)
        {
            content = null;
            replaced = 0;
            exists = false;
            var file = Resolve(call.GetString("sciezka"), out problem);
            if (problem != null)
            {
                return null;
            }

            var raw = call.GetString("tresc");
            if (raw == null)
            {
                problem = "BŁĄD: brak treści pliku (pole tresc).";
                return null;
            }

            if (raw.Length > MaxWriteChars)
            {
                problem = "BŁĄD: treść ma " + raw.Length + " znaków, najwyżej " + MaxWriteChars + ".";
                return null;
            }

            if (Directory.Exists(file))
            {
                problem = "BŁĄD: " + file + " to folder. Podaj nazwę pliku, np. " + Path.Combine(file, "skrypt.ps1");
                return null;
            }

            exists = File.Exists(file);
            if (exists && call.GetBool("nadpisz") != true)
            {
                problem = "BŁĄD: plik " + file + " już istnieje. Żeby go zastąpić, wywołaj zapisz_plik jeszcze raz z \"nadpisz\": true "
                    + "(użytkownik to potwierdzi), albo wybierz inną nazwę.";
                return null;
            }

            content = EscapedNewlines.Fix(raw, out replaced).Replace("\r\n", "\n");
            return file;
        }

        private ToolResult WriteFile(ToolCall call)
        {
            var file = PrepareWrite(call, out var content, out var replaced, out var exists, out var problem);
            if (problem != null)
            {
                return ToolResult.Error(problem);
            }

            var extension = Path.GetExtension(file);
            if (WindowsScriptExtensions.Contains(extension))
            {
                content = content.Replace("\n", "\r\n");
            }

            // Windows PowerShell 5.1 czyta skrypt bez BOM jako ANSI — polskie litery by się zepsuły.
            var withBom = string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".psm1", StringComparison.OrdinalIgnoreCase);
            var folder = Path.GetDirectoryName(file);
            var newFolder = !string.IsNullOrEmpty(folder) && !Directory.Exists(folder);
            if (newFolder)
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(file, content, new UTF8Encoding(withBom));
            var lines = content.Length == 0 ? 0 : content.TrimEnd('\n', '\r').Split('\n').Length;
            return ToolResult.Ok("OK " + (exists ? "nadpisano" : "zapisano") + ": " + file + " (linii: " + lines + ")"
                + (newFolder ? ", utworzono folder " + folder : "")
                + (replaced > 0 ? ". Uwaga: „\\n” zamienione na nowe linie (" + replaced + "×) — w polu tresc pisz zwykłe nowe linie." : ""));
        }

        private ToolResult FindFiles(ToolCall call)
        {
            var folder = Resolve(string.IsNullOrWhiteSpace(call.GetString("sciezka")) ? "." : call.GetString("sciezka"), out var problem);
            if (problem != null)
            {
                return ToolResult.Error(problem);
            }

            if (!Directory.Exists(folder))
            {
                return ToolResult.Error("BŁĄD: nie ma folderu " + folder);
            }

            var pattern = (call.GetString("wzorzec") ?? "").Trim();
            if (pattern.Length == 0)
            {
                pattern = "*";
            }
            else if (pattern.IndexOfAny(new[] { '*', '?' }) < 0)
            {
                pattern = "*" + pattern + "*";
            }

            const int maxResults = 60;
            var clock = Stopwatch.StartNew();
            var found = new List<string>();
            var pending = new Stack<string>();
            pending.Push(folder);
            var scanned = 0;
            var incomplete = false;
            while (pending.Count > 0)
            {
                if (found.Count >= maxResults || scanned > 50000 || clock.Elapsed.TotalSeconds > 20)
                {
                    incomplete = true;
                    break;
                }

                var current = pending.Pop();
                scanned++;
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current, pattern))
                    {
                        var relative = entry.Substring(folder.TrimEnd('\\').Length).TrimStart('\\');
                        found.Add(Directory.Exists(entry)
                            ? "[folder] " + relative
                            : relative + "  (" + (new FileInfo(entry).Length / 1024) + " kB, " + File.GetLastWriteTime(entry).ToString("yyyy-MM-dd HH:mm") + ")");
                        if (found.Count >= maxResults)
                        {
                            break;
                        }
                    }

                    foreach (var directory in Directory.EnumerateDirectories(current))
                    {
                        if ((new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(directory);
                        }
                    }
                }
                catch (Exception e) when (e is UnauthorizedAccessException || e is IOException)
                {
                    // Folder bez dostępu — pomijamy.
                }
            }

            var text = new StringBuilder("OK ").Append(folder).Append(", wzorzec ").Append(pattern).Append(": ")
                .Append(found.Count == 0 ? "nic nie znaleziono" : found.Count + (incomplete ? "+ wyników (przerwano — zawęź wzorzec albo folder)" : " wyników"));
            foreach (var item in found)
            {
                text.Append('\n').Append(item);
            }

            return ToolResult.Ok(text.ToString());
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }

            foreach (var directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
        }
    }
}
