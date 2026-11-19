using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Tools
{
    /// <summary>
    /// Wykonawca, któremu w trakcie rozmowy można dodać folder do dozwolonych — po zgodzie użytkownika
    /// (sesja pyta przez <see cref="Session.SessionOptions.ConfirmMutation"/> z <see cref="ToolCall.AccessFolder"/>).
    /// </summary>
    public interface IFolderAccess
    {
        /// <summary>Dodaje folder do dozwolonych do końca życia wykonawcy; false = nie da się (np. stała lista folderów).</summary>
        bool AllowFolder(string folder);
    }

    /// <summary>
    /// Brak dostępu do folderu nie kończy rozmowy: sesja rozpoznaje błąd „poza dozwolonymi folderami” (<see cref="MissingFolders"/>),
    /// pyta użytkownika o dostęp i po zgodzie ponawia polecenie. Tu są wzorce tych błędów i teksty dla modelu oraz okna zgody.
    /// </summary>
    public static class FolderAccess
    {
        // FileSystemExecutor.Resolve / ResolveProgram: „BŁĄD: [ścieżka ]C:\x jest poza dozwolonymi folderami”.
        private static readonly Regex OutsidePath = new Regex(
            @"BŁĄD: (?:ścieżka )?(?<p>(?:[A-Za-z]:\\|\\\\)[^\r\n]*?) jest poza dozwolonymi folderami",
            RegexOptions.Compiled);

        // PowerShellPolicy: „BŁĄD: polecenie sięga poza dozwolone foldery: C:\a; C:\b. Dozwolone: …”.
        private static readonly Regex OutsideCommand = new Regex(
            @"sięga poza dozwolone foldery: (?<list>[^\r\n]+?)\. Dozwolone:",
            RegexOptions.Compiled);

        /// <summary>
        /// Foldery, których zabrakło, z tekstu błędu wykonawcy (puste, gdy błąd nie dotyczy dozwolonych folderów
        /// albo sięga do folderu systemowego — <see cref="IsSystem"/>). Dla ścieżki pliku — folder, w którym leży.
        /// </summary>
        public static IReadOnlyList<string> MissingFolders(string error)
        {
            var folders = new List<string>();
            if (string.IsNullOrWhiteSpace(error))
            {
                return folders;
            }

            var paths = OutsidePath.Matches(error).Cast<Match>().Select(m => m.Groups["p"].Value)
                .Concat(OutsideCommand.Matches(error).Cast<Match>().SelectMany(m => m.Groups["list"].Value.Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries)));
            foreach (var path in paths)
            {
                var folder = FolderOf(path.Trim());
                if (folder != null && IsSystem(folder))
                {
                    return new List<string>(); // folder systemowy — bez pytania o dostęp, polecenie zostaje odrzucone
                }

                if (folder != null && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(folder);
                }
            }

            return folders;
        }

        /// <summary>Folder dla ścieżki: sam folder, gdy istnieje albo nie ma rozszerzenia; dla pliku — folder nadrzędny.</summary>
        internal static string FolderOf(string path)
        {
            try
            {
                var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
                var folder = Directory.Exists(full) || !Path.HasExtension(full) ? full : Path.GetDirectoryName(full);
                return Normalize(folder);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is System.Security.SecurityException)
            {
                return null;
            }
        }

        /// <summary>Pełna ścieżka bez końcowego ukośnika (poza „C:\”).</summary>
        internal static string Normalize(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }

            var full = Path.GetFullPath(folder);
            var root = Path.GetPathRoot(full);
            return string.Equals(full.TrimEnd('\\', '/') + "\\", root, StringComparison.OrdinalIgnoreCase) ? root : full.TrimEnd('\\', '/');
        }

        /// <summary>
        /// Folder systemu albo programów (Windows, Program Files, ProgramData) — o dostęp do niego program nie pyta:
        /// błąd zostaje odrzuceniem, jak dotąd.
        /// </summary>
        public static bool IsSystem(string folder)
        {
            var normalized = Normalize(folder);
            if (normalized == null)
            {
                return false;
            }

            foreach (var special in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
            {
                var path = Environment.GetFolderPath(special);
                if (!string.IsNullOrEmpty(path) && (normalized + "\\").StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Cały dysk albo udział sieciowy — okno zgody domyślnie odmawia.</summary>
        public static bool IsBroad(string folder)
        {
            var normalized = Normalize(folder);
            return normalized != null && string.Equals(normalized.TrimEnd('\\'), (Path.GetPathRoot(normalized) ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Dodaje folder do listy dozwolonych (bez powtórzeń); false, gdy lista jest tylko do odczytu.</summary>
        public static bool AddTo(IList<string> roots, string folder)
        {
            var normalized = Normalize(folder);
            if (roots == null || normalized == null)
            {
                return false;
            }

            if (roots.Any(r => !string.IsNullOrWhiteSpace(r) && string.Equals(Normalize(Environment.ExpandEnvironmentVariables(r)), normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            try
            {
                roots.Add(normalized);
                return true;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Wynik polecenia mówi o braku dostępu do folderu (poza dozwolonymi albo odmowa użytkownika) — wtedy „nie mam dostępu” modelu
        /// to pytanie o dostęp, nie brak narzędzia (katalog umiejętności nie ma tu nic do dodania).
        /// </summary>
        public static bool IsAccessProblem(string result)
            => !string.IsNullOrEmpty(result)
                && (result.Contains("poza dozwolonymi folderami") || result.Contains("poza dozwolone foldery") || result.Contains("nie dał dostępu do folderu"));

        /// <summary>Treść pytania w oknie zgody.</summary>
        public static string Question(string folder, ToolCall call)
            => "Amadeusz potrzebuje dostępu do folderu:\n" + folder
                + "\n\nPolecenie: " + Text.Shorten(call?.ToString() ?? "", 300)
                + "\n\nTen folder jest poza „Dozwolonymi folderami”. Dać dostęp do końca tej rozmowy? (Na stałe: dopisz go w polu „Dozwolone foldery”.)"
                + (IsBroad(folder) ? "\n\nUWAGA: to cały dysk albo udział sieciowy." : "");

        /// <summary>Wynik dla modelu, gdy użytkownik nie dał dostępu.</summary>
        public static string Denied(string folder)
            => "ODRZUCONE — użytkownik nie dał dostępu do folderu " + folder + ". Nie próbuj tej ścieżki ponownie. "
                + "Zapytaj użytkownika (krok pytanie), gdzie indziej leży potrzebny plik albo czy może go skopiować do dozwolonego folderu, albo odpowiedz bez niego.";

        /// <summary>Dopisek do błędu, gdy program nie może sam zapytać o dostęp (brak okna zgody albo stała lista folderów).</summary>
        public const string AskUserHint =
            " Nie kończ na tym: zapytaj użytkownika (krok pytanie), czy doda ten folder w „Dozwolone foldery”, albo poproś o ścieżkę w dozwolonym folderze.";
    }
}
