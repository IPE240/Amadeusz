using System.Diagnostics;

namespace LokalneAI.Harness.Tests;

/// <summary>Repozytorium bez haseł i kluczy API — ten sam skrypt pilnuje commitów (hak pre-commit).</summary>
public sealed class SekretyTests
{
    [Fact]
    public void Repozytorium_BezHaselIKluczyApi()
    {
        var start = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(Paths.Root, "narzedzia", "sprawdz_sekrety.ps1") + "\" -Wszystkie")
        {
            WorkingDirectory = Paths.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, output);
    }
}
