using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Wynik oceny polecenia PowerShella przed wykonaniem.</summary>
    public sealed class CommandAnalysis
    {
        /// <summary>Decyzja: bez pytania, pytanie albo odrzucenie.</summary>
        public ConfirmationKind Verdict { get; internal set; } = ConfirmationKind.None;

        /// <summary>Powód odrzucenia (dla <see cref="ConfirmationKind.Reject"/>).</summary>
        public string Reason { get; internal set; }

        /// <summary>Ostrzeżenia do okna pytania (kasuje pliki, pobiera z internetu…).</summary>
        public IList<string> Warnings { get; } = new List<string>();

        /// <summary>Dlaczego polecenie nie przeszło jako „tylko czyta” (do testów i dziennika).</summary>
        public IList<string> AskReasons { get; } = new List<string>();

        /// <summary>Polecenie kasuje albo czyści pliki.</summary>
        public bool Deletes { get; internal set; }

        internal void Ask(string reason)
        {
            if (Verdict == ConfirmationKind.None)
            {
                Verdict = ConfirmationKind.Ask;
            }

            if (!AskReasons.Contains(reason))
            {
                AskReasons.Add(reason);
            }
        }

        internal void Warn(string warning)
        {
            Ask(warning);
            if (!Warnings.Contains(warning))
            {
                Warnings.Add(warning);
            }
        }

        internal void RejectWith(string reason)
        {
            if (Verdict != ConfirmationKind.Reject)
            {
                Verdict = ConfirmationKind.Reject;
                Reason = reason;
            }
        }
    }

    /// <summary>
    /// Ocena polecenia PowerShella od modelu, zanim trafi do okna pytania:
    /// <list type="bullet">
    /// <item>„tylko czyta” (Get-ChildItem, Select-String, Measure-Object…) w dozwolonych folderach → bez pytania;</item>
    /// <item>wszystko inne (tworzy, kopiuje, kasuje, uruchamia programy, pobiera) → pytanie z ostrzeżeniami;</item>
    /// <item>ścieżki poza dozwolonymi folderami, polecenia dyskowe i wyłączanie komputera → odrzucenie.</item>
    /// </list>
    /// To nie jest piaskownica: PowerShell potrafi zbudować ścieżkę w locie. Ocena chroni przed pomyłkami modelu
    /// i w razie wątpliwości każe pytać; prawdziwą granicą jest zgoda użytkownika i uprawnienia jego konta Windows.
    /// </summary>
    public static class PowerShellPolicy
    {
        private static readonly HashSet<string> ReadOnlyVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "get", "test", "select", "measure", "sort", "where", "format", "group", "compare", "resolve", "convertto", "convertfrom",
        };

        private static readonly HashSet<string> ReadOnlyCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "foreach-object", "where-object", "out-string", "out-host", "out-null", "write-output", "write-host", "write-verbose",
            "write-warning", "write-information", "join-path", "split-path", "set-location", "push-location", "pop-location",
            // aliasy
            "ls", "dir", "gci", "cat", "type", "gc", "gi", "gp", "gpv", "cd", "chdir", "sl", "pwd", "gl", "echo", "write", "select",
            "where", "?", "%", "foreach", "sort", "measure", "ft", "fl", "fw", "fc", "group", "sls", "pushd", "popd", "gcm", "gm",
            "gps", "ps", "gsv", "gv", "compare", "diff", "rvpa", "tree", "whoami", "hostname", "findstr",
        };

        private static readonly HashSet<string> NotReadOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // „Get-”/„Format-”, które jednak coś robią albo czekają na człowieka.
            "get-credential", "format-volume",
        };

        private static readonly HashSet<string> Blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "format-volume", "clear-disk", "initialize-disk", "remove-partition", "set-partition", "resize-partition", "diskpart",
            "format", "format.com", "bcdedit", "vssadmin", "wbadmin", "cipher", "stop-computer", "restart-computer", "shutdown",
            "set-executionpolicy", "disable-computerrestore", "remove-computerrestorepoint",
        };

        private static readonly HashSet<string> DeleteCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "remove-item", "rm", "del", "erase", "rd", "rmdir", "ri", "clear-content", "clc", "clear-item", "cli", "clear-recyclebin",
        };

        private static readonly HashSet<string> NetworkCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "invoke-webrequest", "iwr", "invoke-restmethod", "irm", "curl", "wget", "start-bitstransfer", "curl.exe",
        };

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "if", "elseif", "else", "foreach", "for", "while", "do", "until", "switch", "try", "catch", "finally", "return", "break",
            "continue", "param", "begin", "process", "end", "function", "filter", "in", "throw", "exit", "trap",
        };

        private static readonly HashSet<string> SafeStaticTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "math", "system.math", "io.path", "system.io.path", "string", "system.string", "datetime", "system.datetime",
            "char", "int", "double", "decimal", "timespan", "system.timespan",
        };

        private static readonly HashSet<string> SafeMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tostring", "trim", "trimstart", "trimend", "replace", "split", "substring", "toupper", "tolower", "contains", "startswith",
            "endswith", "indexof", "lastindexof", "padleft", "padright", "gettype", "equals", "compareto", "adddays", "addhours",
            "addminutes", "toshortdatestring", "tolongdatestring", "getfilename", "getextension", "getfilenamewithoutextension",
            "getdirectoryname", "count", "round",
        };

        private static readonly Regex EnvVariable = new Regex(@"\$\{?env:([A-Za-z_][A-Za-z0-9_()]*)\}?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HomeVariable = new Regex(@"\$HOME\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex VerbNoun = new Regex(@"^([A-Za-z]+)-([A-Za-z][A-Za-z0-9]*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Provider = new Regex(@"^(?:Registry::|HKLM:|HKCU:|HKCR:|HKU:|HKCC:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex OtherProvider = new Regex(@"^(?:Env|Variable|Function|Alias|Cert|WSMan):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Ocenia polecenie. <paramref name="allowedRoots"/> — foldery do pracy (czytanie i zmiany);
        /// <paramref name="programRoots"/> — foldery z zainstalowanymi programami, z których wolno uruchamiać pliki wykonywalne.
        /// </summary>
        public static CommandAnalysis Analyze(string script, string workingFolder, IEnumerable<string> allowedRoots, IEnumerable<string> programRoots = null)
        {
            var analysis = new CommandAnalysis();
            if (string.IsNullOrWhiteSpace(script))
            {
                analysis.RejectWith("BŁĄD: puste polecenie.");
                return analysis;
            }

            var newlines = EscapedNewlines.PowerShellProblem(script);
            if (newlines != null)
            {
                analysis.RejectWith(newlines);
                return analysis;
            }

            var roots = NormalizeRoots(allowedRoots);
            var programs = NormalizeRoots(programRoots);
            List<Token> tokens;
            try
            {
                tokens = Tokenize(script);
            }
            catch (FormatException e)
            {
                analysis.Ask("nie udało się przeczytać polecenia: " + e.Message);
                return analysis;
            }

            var outside = new List<string>();
            var expectCommand = true;
            var afterCall = false;
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
                if (token.Kind == TokenKind.Operator)
                {
                    CheckOperator(token, next, analysis);
                    afterCall = token.Text == "&";
                    expectCommand = token.StartsCommand;
                    continue;
                }

                if (token.Kind == TokenKind.String)
                {
                    if (token.HasSubexpression)
                    {
                        analysis.Ask("tekst z podwyrażeniem $( )");
                    }

                    if (token.IsHereString)
                    {
                        analysis.Ask("tekst wielowierszowy @\" \"@");
                    }

                    CheckPath(token.Text, workingFolder, roots, programs, afterCall || expectCommand, outside, analysis);
                    afterCall = false;
                    expectCommand = false;
                    continue;
                }

                var word = token.Text;
                if (expectCommand)
                {
                    CheckCommand(word, next, analysis);
                }

                CheckWord(word, next, analysis);
                CheckPath(word, workingFolder, roots, programs, afterCall || (expectCommand && LooksLikePath(word)), outside, analysis);
                afterCall = false;
                expectCommand = false;
            }

            if (analysis.Deletes)
            {
                CheckDeleteTargets(tokens, workingFolder, roots, analysis);
            }

            if (outside.Count > 0)
            {
                analysis.RejectWith("BŁĄD: polecenie sięga poza dozwolone foldery: " + string.Join("; ", outside.Distinct(StringComparer.OrdinalIgnoreCase))
                    + ". Dozwolone: " + string.Join("; ", roots.Select(r => r.TrimEnd('\\'))) + ". Folder można dodać w ustawieniach programu.");
            }

            return analysis;
        }

        private static void CheckOperator(Token token, Token next, CommandAnalysis analysis)
        {
            switch (token.Text)
            {
                case "&":
                    analysis.Ask("operator wywołania &");
                    break;
                case ">":
                case ">>":
                    var target = next?.Text ?? "";
                    if (!target.Equals("$null", StringComparison.OrdinalIgnoreCase) && !target.StartsWith("&", StringComparison.Ordinal))
                    {
                        analysis.Warn("zapisuje wynik do pliku (>)");
                    }

                    break;
            }
        }

        private static void CheckCommand(string word, Token next, CommandAnalysis analysis)
        {
            if (word.StartsWith("$", StringComparison.Ordinal) || word.StartsWith("-", StringComparison.Ordinal)
                || word.StartsWith("[", StringComparison.Ordinal) || char.IsDigit(word[0]))
            {
                return;
            }

            if (next != null && next.Kind == TokenKind.Operator && next.Text == "=")
            {
                return; // klucz tabeli @{ n = …; e = … } albo przypisanie
            }

            var isForEach = word == "%" || word.Equals("foreach-object", StringComparison.OrdinalIgnoreCase)
                || word.Equals("foreach", StringComparison.OrdinalIgnoreCase) && !(next != null && next.Kind == TokenKind.Operator && next.Text == "(");
            if (isForEach)
            {
                // „gci | % Delete” albo „-MemberName Delete” wywołuje metodę na każdym elemencie.
                if (next != null && next.Kind == TokenKind.Word)
                {
                    analysis.Ask("ForEach-Object z nazwą metody");
                }

                return;
            }

            if (Keywords.Contains(word))
            {
                return;
            }

            if (word == ".")
            {
                analysis.Ask("dołączenie skryptu (. plik)");
                return;
            }

            var name = word.Trim('"', '\'');
            var bare = Path.GetFileName(name.Replace('/', '\\'));
            var lower = bare.ToLowerInvariant();
            var withoutExe = lower.EndsWith(".exe", StringComparison.Ordinal) ? lower.Substring(0, lower.Length - 4) : lower;
            if (Blocked.Contains(lower) || Blocked.Contains(withoutExe))
            {
                analysis.RejectWith("BŁĄD: polecenie „" + bare + "” jest zablokowane (dyski, rozruch, wyłączanie komputera, zasady systemu). Tego asystent nie robi.");
                return;
            }

            if (DeleteCommands.Contains(lower))
            {
                analysis.Deletes = true;
                analysis.Warn("KASUJE pliki albo ich zawartość");
            }

            if (NetworkCommands.Contains(lower))
            {
                analysis.Warn("pobiera dane z internetu");
            }

            if (lower == "iex" || lower == "invoke-expression")
            {
                analysis.Warn("wykonuje tekst jako kod (Invoke-Expression)");
            }

            if (name.IndexOfAny(new[] { '\\', '/' }) >= 0)
            {
                analysis.Ask("uruchamia plik " + name);
                return;
            }

            if (!IsReadOnlyCommand(lower))
            {
                analysis.Ask("polecenie " + bare);
            }
        }

        private static bool IsReadOnlyCommand(string lower)
        {
            if (NotReadOnly.Contains(lower))
            {
                return false;
            }

            if (ReadOnlyCommands.Contains(lower))
            {
                return true;
            }

            var match = VerbNoun.Match(lower);
            return match.Success && ReadOnlyVerbs.Contains(match.Groups[1].Value);
        }

        private static void CheckWord(string word, Token next, CommandAnalysis analysis)
        {
            if (word.StartsWith("-", StringComparison.Ordinal))
            {
                var parameter = word.TrimStart('-');
                if (parameter.Equals("EncodedCommand", StringComparison.OrdinalIgnoreCase) || parameter.Equals("enc", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.RejectWith("BŁĄD: polecenia zakodowane (base64) są zablokowane — napisz polecenie wprost.");
                }

                return;
            }

            // Nazwa polecenia w środku (np. jako argument albo po „-Process”) — ostrożnie traktujemy jak wywołanie.
            var verbNoun = VerbNoun.Match(word);
            if (verbNoun.Success)
            {
                var lower = word.ToLowerInvariant();
                if (Blocked.Contains(lower))
                {
                    analysis.RejectWith("BŁĄD: polecenie „" + word + "” jest zablokowane.");
                }
                else if (DeleteCommands.Contains(lower))
                {
                    analysis.Deletes = true;
                    analysis.Warn("KASUJE pliki albo ich zawartość");
                }
                else if (!IsReadOnlyCommand(lower) && IsKnownVerb(verbNoun.Groups[1].Value))
                {
                    analysis.Ask("polecenie " + word);
                }
            }

            var staticCall = word.IndexOf("]::", StringComparison.Ordinal);
            if (staticCall > 0 && word.StartsWith("[", StringComparison.Ordinal))
            {
                var type = word.Substring(1, staticCall - 1);
                if (!SafeStaticTypes.Contains(type))
                {
                    analysis.Ask("wywołanie .NET [" + type + "]");
                }

                return;
            }

            if (next != null && next.Kind == TokenKind.Operator && next.Text == "(" && !next.SpaceBefore)
            {
                var dot = word.LastIndexOf('.');
                if (dot >= 0 && dot < word.Length - 1)
                {
                    var method = word.Substring(dot + 1);
                    if (!SafeMethods.Contains(method))
                    {
                        analysis.Ask("wywołanie metody ." + method + "()");
                    }
                }
            }
        }

        private static readonly HashSet<string> KnownVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "add", "clear", "close", "copy", "enter", "exit", "hide", "lock", "move", "new", "open", "optimize", "pop", "push", "redo",
            "remove", "rename", "reset", "resize", "search", "set", "show", "skip", "step", "switch", "undo", "unlock", "watch",
            "backup", "checkpoint", "compress", "convert", "dismount", "edit", "expand", "export", "import", "initialize", "limit",
            "merge", "mount", "out", "publish", "restore", "save", "sync", "unpublish", "update", "approve", "assert", "complete",
            "confirm", "deny", "disable", "enable", "install", "invoke", "register", "request", "restart", "resume", "start", "stop",
            "submit", "suspend", "uninstall", "unregister", "wait", "debug", "repair", "block", "grant", "protect", "revoke", "unblock",
            "unprotect", "send", "connect", "disconnect", "receive", "write", "read", "use", "trace", "ping", "find",
        };

        private static bool IsKnownVerb(string verb) => KnownVerbs.Contains(verb);

        private static bool LooksLikePath(string word) => word.IndexOfAny(new[] { '\\', '/' }) >= 0 || word.Length >= 2 && word[1] == ':';

        /// <summary>Sprawdza, czy słowo wygląda na ścieżkę spoza dozwolonych folderów.</summary>
        private static void CheckPath(string text, string workingFolder, List<string> roots, List<string> programs, bool executablePosition,
            List<string> outside, CommandAnalysis analysis)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Contains("://"))
            {
                return;
            }

            var candidate = text.Trim();
            var parameterValue = Regex.Match(candidate, @"^-[A-Za-z]+:(.+)$");
            if (parameterValue.Success)
            {
                candidate = parameterValue.Groups[1].Value;
            }
            else if (candidate.StartsWith("-", StringComparison.Ordinal) || candidate.StartsWith("/", StringComparison.Ordinal))
            {
                return;
            }

            if (Provider.IsMatch(candidate))
            {
                analysis.Warn("sięga do rejestru Windows");
                return;
            }

            if (OtherProvider.IsMatch(candidate))
            {
                return;
            }

            candidate = EnvVariable.Replace(candidate, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
            candidate = HomeVariable.Replace(candidate, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (candidate == "~" || candidate.StartsWith("~\\", StringComparison.Ordinal) || candidate.StartsWith("~/", StringComparison.Ordinal))
            {
                candidate = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + candidate.Substring(1);
            }

            var absolute = candidate.Length >= 2 && char.IsLetter(candidate[0]) && candidate[1] == ':'
                           || candidate.StartsWith("\\", StringComparison.Ordinal);
            var climbs = Regex.IsMatch(candidate, @"(^|[\\/])\.\.([\\/]|$)");
            if (!absolute && !climbs)
            {
                return; // ścieżka względna w głąb folderu bieżącego (albo zwykłe słowo)
            }

            if (candidate.IndexOf('$') >= 0)
            {
                analysis.Ask("ścieżka zależna od zmiennej: " + text);
                return;
            }

            var full = FullPath(candidate, workingFolder);
            if (full == null)
            {
                analysis.Ask("niezrozumiała ścieżka: " + text);
                return;
            }

            if (IsInside(full, roots))
            {
                return;
            }

            if (executablePosition && IsInside(full, programs))
            {
                return;
            }

            outside.Add(full);
        }

        /// <summary>Kasowanie całego dozwolonego folderu, dysku albo „*” w folderze głównym — zawsze odrzucane.</summary>
        private static void CheckDeleteTargets(List<Token> tokens, string workingFolder, List<string> roots, CommandAnalysis analysis)
        {
            var wd = (workingFolder ?? "").TrimEnd('\\', '/') + "\\";
            var wdIsRoot = roots.Any(r => r.Equals(wd, StringComparison.OrdinalIgnoreCase)) || wd.Length <= 3;
            foreach (var token in tokens.Where(t => t.Kind != TokenKind.Operator))
            {
                var text = token.Text.Trim();
                if (wdIsRoot && (text == "*" || text == "*.*" || text == ".\\*" || text == "./*" || text == "."))
                {
                    analysis.RejectWith("BŁĄD: kasowanie całej zawartości folderu głównego (" + wd.TrimEnd('\\') + ") jest zablokowane. Wskaż konkretne pliki.");
                    return;
                }

                if (text.StartsWith("-", StringComparison.Ordinal) || !LooksLikePath(text) && text != "..")
                {
                    continue;
                }

                var expanded = EnvVariable.Replace(text, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
                var full = FullPath(expanded, workingFolder);
                if (full == null)
                {
                    continue;
                }

                var normalized = full.TrimEnd('\\', '/') + "\\";
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\') + "\\";
                if (normalized.Length <= 3 || roots.Any(r => r.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    || normalized.Equals(profile, StringComparison.OrdinalIgnoreCase))
                {
                    analysis.RejectWith("BŁĄD: kasowanie folderu " + full.TrimEnd('\\') + " w całości jest zablokowane. Wskaż konkretne pliki.");
                    return;
                }
            }
        }

        private static string FullPath(string candidate, string workingFolder)
        {
            try
            {
                var text = candidate.Trim().Trim('"', '\'');
                var wildcard = text.IndexOfAny(new[] { '*', '?' });
                if (wildcard >= 0)
                {
                    var cut = text.LastIndexOfAny(new[] { '\\', '/' }, wildcard);
                    text = cut >= 0 ? text.Substring(0, cut + 1) : ".";
                }

                if (text.Length == 2 && text[1] == ':')
                {
                    text += "\\";
                }

                var basePath = string.IsNullOrWhiteSpace(workingFolder) ? Environment.CurrentDirectory : workingFolder;
                return Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(basePath, text));
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is IOException)
            {
                return null;
            }
        }

        internal static List<string> NormalizeRoots(IEnumerable<string> roots)
            => (roots ?? Enumerable.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r =>
                {
                    try
                    {
                        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(r.Trim())).TrimEnd('\\', '/') + "\\";
                    }
                    catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
                    {
                        return null;
                    }
                })
                .Where(r => r != null)
                .ToList();

        internal static bool IsInside(string fullPath, IEnumerable<string> normalizedRoots)
        {
            var path = fullPath.TrimEnd('\\', '/') + "\\";
            return normalizedRoots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        // ---------------------------------------------------------------- fakty dla strażnika bezpieczeństwa

        /// <summary>Argument wywołania: tekst i czy jest wyliczany w locie (zmienna, wyrażenie, tekst ze zmienną).</summary>
        internal sealed class ScriptArgument
        {
            public ScriptArgument(string text, bool dynamic)
            {
                Text = text;
                Dynamic = dynamic;
            }

            public string Text { get; }

            public bool Dynamic { get; }

            public override string ToString() => (Dynamic ? "~" : "") + Text;
        }

        /// <summary>Wywołanie w skrypcie: polecenie (program, plik) i jego argumenty do końca polecenia.</summary>
        internal sealed class ScriptInvocation
        {
            public ScriptInvocation(string name, bool viaCallOperator)
            {
                Name = name;
                ViaCallOperator = viaCallOperator;
            }

            /// <summary>Nazwa polecenia albo ścieżka programu (zmienne środowiskowe rozwinięte).</summary>
            public string Name { get; }

            /// <summary>Wywołane operatorem &amp; albo . (kropką), nie wprost.</summary>
            public bool ViaCallOperator { get; }

            public List<ScriptArgument> Arguments { get; } = new List<ScriptArgument>();

            public override string ToString() => Name + " " + string.Join(" ", Arguments);
        }

        /// <summary>Co skrypt wywołuje — dla <see cref="SafetyRules"/>.</summary>
        internal sealed class ScriptFacts
        {
            public List<ScriptInvocation> Invocations { get; } = new List<ScriptInvocation>();

            /// <summary>Opis wywołania, którego celu nie da się odczytać z tekstu (&amp; $x, &amp; (…), . $x); null = brak.</summary>
            public string Dynamic { get; set; }

            /// <summary>Skryptu nie udało się podzielić na słowa.</summary>
            public bool Unreadable { get; set; }
        }

        /// <summary>
        /// Wywołania w skrypcie: polecenia w pozycji polecenia, cele operatorów &amp; i . oraz ich argumenty. Proste stałe
        /// (<c>$exe = 'C:\…\7z.exe'; &amp; $exe</c>, jedno przypisanie tekstu) są podstawiane; inne wywołanie przez zmienną albo
        /// wyrażenie to <see cref="ScriptFacts.Dynamic"/>.
        /// </summary>
        internal static ScriptFacts Inspect(string script)
        {
            var facts = new ScriptFacts();
            List<Token> tokens;
            try
            {
                tokens = Tokenize(script ?? "");
            }
            catch (FormatException)
            {
                facts.Unreadable = true;
                return facts;
            }

            var constants = FindConstants(tokens);
            if (Regex.IsMatch(script ?? "", @"\b(set-variable|new-variable|sv|nv)\b|\[ref\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                constants.Clear(); // zmienne zmieniane poleceniem — wartości z przypisań przestają być pewne
            }

            var expectCommand = true;
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.String && token.Expandable && token.HasSubexpression)
                {
                    // "… $(Get-Content x) …" — polecenia w podwyrażeniu też się wykonują.
                    foreach (var inner in Subexpressions(token.Text))
                    {
                        var nested = Inspect(inner);
                        facts.Invocations.AddRange(nested.Invocations);
                        facts.Dynamic = facts.Dynamic ?? nested.Dynamic;
                    }
                }

                if (token.Kind == TokenKind.Operator)
                {
                    if (token.Text == "&")
                    {
                        i = ReadCallTarget(tokens, i + 1, constants, facts);
                        expectCommand = false;
                        continue;
                    }

                    expectCommand = token.StartsCommand;
                    continue;
                }

                if (!expectCommand)
                {
                    continue;
                }

                expectCommand = false;
                if (token.Kind == TokenKind.Word && token.Text == ".")
                {
                    i = ReadCallTarget(tokens, i + 1, constants, facts);
                    continue;
                }

                if (token.Kind == TokenKind.String)
                {
                    continue; // tekst na początku polecenia to wartość (np. 'a' | Out-File), nie wywołanie
                }

                var word = token.Text;
                if (word.StartsWith("$", StringComparison.Ordinal) || word.StartsWith("[", StringComparison.Ordinal)
                    || word.StartsWith("-", StringComparison.Ordinal) || char.IsDigit(word[0]) || Keywords.Contains(word))
                {
                    if (Keywords.Contains(word))
                    {
                        expectCommand = true; // foreach (…) { polecenie }
                    }

                    continue;
                }

                var invocation = new ScriptInvocation(ExpandEnvironment(word.Trim('"', '\'')) ?? word, false);
                ReadArguments(tokens, i + 1, invocation, constants);
                facts.Invocations.Add(invocation);
            }

            return facts;
        }

        /// <summary>Cel operatora &amp; albo kropki zaczynający się na pozycji <paramref name="index"/>; zwraca indeks ostatniego zużytego tokenu.</summary>
        private static int ReadCallTarget(List<Token> tokens, int index, Dictionary<string, string> constants, ScriptFacts facts)
        {
            if (index >= tokens.Count)
            {
                return index - 1;
            }

            var target = tokens[index];
            string name = null;
            if (target.Kind == TokenKind.String)
            {
                name = Literal(target, constants);
            }
            else if (target.Kind == TokenKind.Word)
            {
                name = target.Text.StartsWith("$", StringComparison.Ordinal)
                    ? constants.TryGetValue(target.Text, out var constant) ? constant : null
                    : ExpandEnvironment(target.Text);
            }
            else if (target.Text == "{")
            {
                return index - 1; // & { … } — blok w tekście skryptu, oceniany razem z resztą
            }

            if (name == null)
            {
                facts.Dynamic = facts.Dynamic ?? ("wywołanie przez zmienną albo wyrażenie: " + Text.Shorten(target.Text, 60));
                return index;
            }

            var invocation = new ScriptInvocation(name, true);
            ReadArguments(tokens, index + 1, invocation, constants);
            facts.Invocations.Add(invocation);
            return index;
        }

        private static void ReadArguments(List<Token> tokens, int start, ScriptInvocation invocation, Dictionary<string, string> constants)
        {
            var depth = 0;
            for (var j = start; j < tokens.Count; j++)
            {
                var token = tokens[j];
                if (token.Kind == TokenKind.Operator)
                {
                    if (depth == 0 && (token.Text == "\n" || token.Text == ";" || token.Text == "|" || token.Text == "||" || token.Text == "&&"
                                       || token.Text == "}" || token.Text == ")"))
                    {
                        return;
                    }

                    if (token.Text == "(" || token.Text == "$(" || token.Text == "@(" || token.Text == "{" || token.Text == "@{")
                    {
                        if (depth == 0)
                        {
                            invocation.Arguments.Add(new ScriptArgument(token.Text, true));
                        }

                        depth++;
                    }
                    else if ((token.Text == ")" || token.Text == "}") && depth > 0)
                    {
                        depth--;
                    }

                    continue;
                }

                if (depth > 0)
                {
                    continue;
                }

                if (token.Kind == TokenKind.String)
                {
                    var literal = Literal(token, constants);
                    invocation.Arguments.Add(new ScriptArgument(literal ?? token.Text, literal == null));
                }
                else if (token.Text.StartsWith("$", StringComparison.Ordinal))
                {
                    invocation.Arguments.Add(constants.TryGetValue(token.Text, out var constant)
                        ? new ScriptArgument(constant, false)
                        : new ScriptArgument(token.Text, !IsEnvironmentOnly(token.Text)));
                }
                else
                {
                    var expanded = ExpandEnvironment(token.Text);
                    invocation.Arguments.Add(new ScriptArgument(expanded ?? token.Text, expanded == null));
                }
            }
        }

        /// <summary>Treść podwyrażeń <c>$( … )</c> w tekście (z zagnieżdżonymi nawiasami).</summary>
        private static IEnumerable<string> Subexpressions(string text)
        {
            for (var start = text.IndexOf("$(", StringComparison.Ordinal); start >= 0; start = text.IndexOf("$(", start + 2, StringComparison.Ordinal))
            {
                // Niezamknięte podwyrażenie też oddajemy (do końca tekstu) — lepiej ocenić za dużo niż przeoczyć polecenie.
                var end = SkipSubexpression(text, start + 1);
                var length = end - start - 2 - (end <= text.Length && text[end - 1] == ')' ? 1 : 0);
                if (length > 0)
                {
                    yield return text.Substring(start + 2, length);
                }
            }
        }

        /// <summary>Stałe: <c>$nazwa = 'tekst'</c> przypisane w skrypcie dokładnie raz (bez zmiennych w tekście).</summary>
        private static Dictionary<string, string> FindConstants(List<Token> tokens)
        {
            var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var assigned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.Word && token.Text.StartsWith("$", StringComparison.Ordinal))
                {
                    // $x += …, $x+=… — każde inne przypisanie niż „=” unieważnia stałą.
                    var bare = token.Text.TrimEnd('+', '-', '*', '/', '%');
                    var compound = bare != token.Text && i + 1 < tokens.Count && tokens[i + 1].Text == "="
                                   || i + 2 < tokens.Count && tokens[i + 1].Kind == TokenKind.Word && "+-*/%".IndexOf(tokens[i + 1].Text, StringComparison.Ordinal) >= 0
                                   && tokens[i + 1].Text.Length == 1 && tokens[i + 2].Text == "=";
                    if (compound)
                    {
                        assigned[bare] = 2;
                    }
                }

                if (token.Kind != TokenKind.Word || !token.Text.StartsWith("$", StringComparison.Ordinal)
                    || i + 1 >= tokens.Count || tokens[i + 1].Kind != TokenKind.Operator || tokens[i + 1].Text != "=")
                {
                    continue;
                }

                assigned[token.Text] = assigned.TryGetValue(token.Text, out var count) ? count + 1 : 1;
                if (assigned[token.Text] > 1)
                {
                    continue;
                }
                var value = i + 2 < tokens.Count ? tokens[i + 2] : null;
                var after = i + 3 < tokens.Count ? tokens[i + 3] : null;
                var single = after == null || after.Kind == TokenKind.Operator && (after.Text == "\n" || after.Text == ";" || after.Text == "}");
                var literal = value != null && value.Kind == TokenKind.String && single ? Literal(value, constants) : null;
                if (literal != null)
                {
                    constants[token.Text] = literal;
                }
            }

            foreach (var twice in assigned.Where(a => a.Value > 1).Select(a => a.Key).ToList())
            {
                constants.Remove(twice);
            }

            return constants;
        }

        /// <summary>Wartość tekstu albo null, gdy zależy od zmiennych (poza środowiskowymi i stałymi).</summary>
        private static string Literal(Token token, Dictionary<string, string> constants)
        {
            if (!token.Expandable)
            {
                return token.Text;
            }

            if (token.HasSubexpression)
            {
                return null;
            }

            var text = Regex.Replace(token.Text, @"\$\{?([A-Za-z_][A-Za-z0-9_]*)\}?", m =>
                constants.TryGetValue("$" + m.Groups[1].Value, out var constant) ? constant : m.Value);
            return ExpandEnvironment(text);
        }

        /// <summary>Rozwija <c>$env:NAZWA</c> i <c>%NAZWA%</c>; null, gdy zostały inne zmienne.</summary>
        internal static string ExpandEnvironment(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var expanded = EnvVariable.Replace(text, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
            expanded = HomeVariable.Replace(expanded, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            expanded = Environment.ExpandEnvironmentVariables(expanded);
            return expanded.IndexOf('$') >= 0 ? null : expanded;
        }

        private static bool IsEnvironmentOnly(string word) => ExpandEnvironment(word) != null;

        // ---------------------------------------------------------------- tokenizer

        private enum TokenKind
        {
            Word,
            String,
            Operator,
        }

        private sealed class Token
        {
            public TokenKind Kind;
            public string Text;
            public bool SpaceBefore;
            public bool HasSubexpression;
            public bool IsHereString;

            /// <summary>Tekst w cudzysłowie podwójnym (PowerShell podstawia w nim zmienne).</summary>
            public bool Expandable;

            /// <summary>Po tym operatorze zaczyna się nowe polecenie.</summary>
            public bool StartsCommand => Kind == TokenKind.Operator
                && (Text == "\n" || Text == ";" || Text == "|" || Text == "||" || Text == "&&" || Text == "{" || Text == "("
                    || Text == "$(" || Text == "@(" || Text == "=" || Text == "&");

            public override string ToString() => Kind + ":" + Text;
        }

        /// <summary>Indeks za nawiasem zamykającym podwyrażenie, które otwiera nawias na pozycji <paramref name="open"/> (z napisami w środku).</summary>
        private static int SkipSubexpression(string script, int open)
        {
            var depth = 0;
            for (var j = open; j < script.Length; j++)
            {
                var c = script[j];
                if (c == '`')
                {
                    j++;
                }
                else if (c == '(')
                {
                    depth++;
                }
                else if (c == ')' && --depth == 0)
                {
                    return j + 1;
                }
                else if (IsDoubleQuote(c) || IsSingleQuote(c))
                {
                    j = SkipQuoted(script, j) - 1;
                }
            }

            return script.Length;
        }

        /// <summary>Indeks za napisem zaczynającym się cudzysłowem na pozycji <paramref name="start"/>.</summary>
        private static int SkipQuoted(string script, int start)
        {
            var isDouble = IsDoubleQuote(script[start]);
            for (var k = start + 1; k < script.Length; k++)
            {
                var c = script[k];
                if (isDouble && c == '`')
                {
                    k++;
                }
                else if (isDouble && c == '$' && k + 1 < script.Length && script[k + 1] == '(')
                {
                    k = SkipSubexpression(script, k + 1) - 1;
                }
                else if (isDouble ? IsDoubleQuote(c) : IsSingleQuote(c))
                {
                    if (k + 1 < script.Length && (isDouble ? IsDoubleQuote(script[k + 1]) : IsSingleQuote(script[k + 1])))
                    {
                        k++; // podwojony cudzysłów w napisie
                        continue;
                    }

                    return k + 1;
                }
            }

            return script.Length;
        }

        private static bool IsSingleQuote(char c) => c == '\'' || c == '‘' || c == '’' || c == '‚' || c == '‛';

        private static bool IsDoubleQuote(char c) => c == '"' || c == '“' || c == '”' || c == '„';

        private static List<Token> Tokenize(string script)
        {
            var tokens = new List<Token>();
            var i = 0;
            var space = true;
            while (i < script.Length)
            {
                var c = script[i];
                if (c == ' ' || c == '\t' || c == '\r')
                {
                    space = true;
                    i++;
                    continue;
                }

                if (c == '`' && i + 1 < script.Length && (script[i + 1] == '\n' || script[i + 1] == '\r'))
                {
                    i += 2; // kontynuacja linii
                    space = true;
                    continue;
                }

                if (c == '<' && i + 1 < script.Length && script[i + 1] == '#')
                {
                    var end = script.IndexOf("#>", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? script.Length : end + 2;
                    space = true;
                    continue;
                }

                if (c == '#' && space)
                {
                    while (i < script.Length && script[i] != '\n')
                    {
                        i++;
                    }

                    continue;
                }

                var token = new Token { SpaceBefore = space };
                space = false;
                if (c == '@' && i + 1 < script.Length && (IsDoubleQuote(script[i + 1]) || IsSingleQuote(script[i + 1])))
                {
                    var closing = (IsDoubleQuote(script[i + 1]) ? "\"" : "'") + "@";
                    var end = script.IndexOf("\n" + closing, i + 2, StringComparison.Ordinal);
                    var stop = end < 0 ? script.Length : end + 1 + closing.Length;
                    token.Kind = TokenKind.String;
                    token.IsHereString = true;
                    token.Text = script.Substring(i + 2, Math.Max(0, (end < 0 ? script.Length : end) - (i + 2)));
                    token.HasSubexpression = IsDoubleQuote(script[i + 1]) && token.Text.Contains("$(");
                    token.Expandable = IsDoubleQuote(script[i + 1]);
                    tokens.Add(token);
                    i = stop;
                    continue;
                }

                if (IsSingleQuote(c))
                {
                    var text = new StringBuilder();
                    i++;
                    while (i < script.Length)
                    {
                        if (IsSingleQuote(script[i]))
                        {
                            if (i + 1 < script.Length && IsSingleQuote(script[i + 1]))
                            {
                                text.Append(script[i]);
                                i += 2;
                                continue;
                            }

                            i++;
                            break;
                        }

                        text.Append(script[i++]);
                    }

                    token.Kind = TokenKind.String;
                    token.Text = text.ToString();
                    tokens.Add(token);
                    continue;
                }

                if (IsDoubleQuote(c))
                {
                    var text = new StringBuilder();
                    i++;
                    while (i < script.Length)
                    {
                        var d = script[i];
                        if (d == '`' && i + 1 < script.Length)
                        {
                            text.Append(script[i + 1]);
                            i += 2;
                            continue;
                        }

                        if (d == '$' && i + 1 < script.Length && script[i + 1] == '(')
                        {
                            // "…$(Get-Date -Format "yyyy")…" — cudzysłów w podwyrażeniu nie kończy napisu.
                            var end = SkipSubexpression(script, i + 1);
                            text.Append(script, i, end - i);
                            i = end;
                            continue;
                        }

                        if (IsDoubleQuote(d))
                        {
                            if (i + 1 < script.Length && IsDoubleQuote(script[i + 1]))
                            {
                                text.Append(d);
                                i += 2;
                                continue;
                            }

                            i++;
                            break;
                        }

                        text.Append(d);
                        i++;
                    }

                    token.Kind = TokenKind.String;
                    token.Text = text.ToString();
                    token.HasSubexpression = token.Text.Contains("$(");
                    token.Expandable = true;
                    tokens.Add(token);
                    continue;
                }

                token.Kind = TokenKind.Operator;
                if (c == '\n' || c == ';' || c == '(' || c == ')' || c == '{' || c == '}' || c == ',' || c == '=')
                {
                    token.Text = c.ToString();
                    tokens.Add(token);
                    i++;
                    continue;
                }

                if (c == '|')
                {
                    token.Text = i + 1 < script.Length && script[i + 1] == '|' ? "||" : "|";
                    i += token.Text.Length;
                    tokens.Add(token);
                    continue;
                }

                if (c == '&')
                {
                    token.Text = i + 1 < script.Length && script[i + 1] == '&' ? "&&" : "&";
                    i += token.Text.Length;
                    tokens.Add(token);
                    continue;
                }

                if ((c == '$' || c == '@') && i + 1 < script.Length && script[i + 1] == '(')
                {
                    token.Text = c + "(";
                    i += 2;
                    tokens.Add(token);
                    continue;
                }

                if (c == '@' && i + 1 < script.Length && script[i + 1] == '{')
                {
                    token.Text = "@{";
                    i += 2;
                    tokens.Add(token);
                    continue;
                }

                if (c == '>' || (char.IsDigit(c) || c == '*') && i + 1 < script.Length && script[i + 1] == '>')
                {
                    var start = i;
                    if (c != '>')
                    {
                        i++;
                    }

                    i++;
                    if (i < script.Length && script[i] == '>')
                    {
                        i++;
                    }

                    // 2>&1, *>&1 — przekierowanie strumieni, bez pliku.
                    if (i + 1 < script.Length && script[i] == '&' && char.IsDigit(script[i + 1]))
                    {
                        i += 2;
                        continue;
                    }

                    token.Text = script.Substring(start, i - start).EndsWith(">>", StringComparison.Ordinal) ? ">>" : ">";
                    tokens.Add(token);
                    continue;
                }

                // Słowo: do spacji albo znaku specjalnego.
                token.Kind = TokenKind.Word;
                var word = new StringBuilder();
                while (i < script.Length)
                {
                    var d = script[i];
                    if (d == ' ' || d == '\t' || d == '\r' || d == '\n' || d == ';' || d == '|' || d == '&' || d == '(' || d == ')'
                        || d == '{' || d == '}' || d == ',' || d == '=' || d == '>' || IsSingleQuote(d) || IsDoubleQuote(d))
                    {
                        break;
                    }

                    if (d == '$' && i + 1 < script.Length && script[i + 1] == '(' && word.Length > 0)
                    {
                        break;
                    }

                    if (d == '`' && i + 1 < script.Length)
                    {
                        word.Append(script[i + 1]);
                        i += 2;
                        continue;
                    }

                    // [typ]::Metoda — nawiasy kwadratowe należą do słowa.
                    word.Append(d);
                    i++;
                }

                if (word.Length == 0)
                {
                    // Pojedynczy znak, którego nie rozpoznaliśmy — pomijamy, ale ostrożnie.
                    token.Text = script[i].ToString();
                    i++;
                }
                else
                {
                    token.Text = word.ToString();
                }

                tokens.Add(token);
            }

            return tokens;
        }
    }
}
