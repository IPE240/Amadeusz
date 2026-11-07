using System.Diagnostics;

namespace LokalneAI.App;

/// <summary>Szuka zainstalowanej Ollamy i ją uruchamia, gdy nie działa.</summary>
internal static class OllamaLauncher
{
    public const string InstallCommand = "irm https://ollama.com/install.ps1 | iex";

    /// <summary>
    /// Ścieżka do programu Ollamy: najpierw aplikacja z ikoną przy zegarze (tak instaluje
    /// <c>install.ps1</c>), potem <c>ollama.exe</c> z folderu instalacji albo z PATH.
    /// </summary>
    public static string? Find()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama");
        foreach (var candidate in new[] { Path.Combine(folder, "ollama app.exe"), Path.Combine(folder, "ollama.exe") })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "ollama.exe");
                if (dir.Trim().Length > 0 && File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Uszkodzony wpis w PATH — pomijamy.
            }
        }

        return null;
    }

    public static void Start(string exe)
    {
        var start = exe.EndsWith("ollama app.exe", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo(exe) { UseShellExecute = true }
            : new ProcessStartInfo(exe, "serve") { UseShellExecute = false, CreateNoWindow = true };
        Process.Start(start);
    }
}
