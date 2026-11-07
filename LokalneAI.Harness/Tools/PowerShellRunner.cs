using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LokalneAI.Harness.Tools
{
    /// <summary>Wynik polecenia PowerShella.</summary>
    public sealed class CommandRunResult
    {
        /// <summary>Wyjście (wyniki, komunikaty, błędy jako linie „[błąd] …”), bez znaczników programu.</summary>
        public string Output { get; internal set; } = "";

        /// <summary>Folder, w którym PowerShell skończył (po <c>cd</c>); null = nieznany.</summary>
        public string FinalFolder { get; internal set; }

        /// <summary>Kod wyjścia ostatniego programu zewnętrznego (<c>$LASTEXITCODE</c>), jeśli był różny od 0.</summary>
        public int? NativeExitCode { get; internal set; }

        public bool TimedOut { get; internal set; }

        /// <summary>W wyniku są błędy (PowerShell albo program zewnętrzny).</summary>
        public bool HasErrors { get; internal set; }
    }

    /// <summary>
    /// Uruchamia polecenie w osobnym procesie Windows PowerShell (bez profilu, bez okna, bez czekania na klawiaturę).
    /// Polecenie trafia przez <c>-EncodedCommand</c>, więc nie zależy od cudzysłowów ani zasad uruchamiania skryptów.
    /// Błędy, ostrzeżenia i <c>Write-Host</c> są wplecione w wynik w kolejności, a folder po <c>cd</c> wraca do wywołującego.
    /// </summary>
    public static class PowerShellRunner
    {
        internal const string FolderMarker = "<<<LAI_CWD>>>";
        internal const string ExitMarker = "<<<LAI_EXIT>>>";
        private const int MaxCaptured = 400000;

        /// <summary>Skrypt-opakowanie: kodowanie UTF-8, folder startowy, zamiana błędów na linie, znacznik folderu na końcu.</summary>
        public static string BuildWrapper(string script, string workingFolder)
        {
            return "$ProgressPreference = 'SilentlyContinue'\n"
                + "$ErrorActionPreference = 'Continue'\n"
                + "try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch {}\n"
                + "$OutputEncoding = New-Object System.Text.UTF8Encoding $false\n"
                + (string.IsNullOrWhiteSpace(workingFolder) ? "" : "Set-Location -LiteralPath " + Quote(workingFolder) + "\n")
                + "$__lai = " + Quote(script) + "\n"
                + "try {\n"
                + "    . ([ScriptBlock]::Create($__lai)) *>&1 | ForEach-Object {\n"
                + "        if ($_ -is [System.Management.Automation.ErrorRecord]) { '[błąd] ' + $_.Exception.Message }\n"
                + "        elseif ($_ -is [System.Management.Automation.WarningRecord]) { '[uwaga] ' + $_.Message }\n"
                + "        elseif ($_ -is [System.Management.Automation.InformationRecord]) { [string]$_.MessageData }\n"
                + "        elseif ($_ -is [System.Management.Automation.VerboseRecord] -or $_ -is [System.Management.Automation.DebugRecord]) { }\n"
                + "        else { $_ }\n"
                + "    } | Out-String -Width 200 | ForEach-Object { [Console]::Out.Write($_) }\n"
                + "} catch {\n"
                + "    [Console]::Out.WriteLine('[błąd] ' + $_.Exception.Message)\n"
                + "} finally {\n"
                + "    [Console]::Out.WriteLine('" + FolderMarker + "' + (Get-Location).ProviderPath)\n"
                + "    if ($LASTEXITCODE) { [Console]::Out.WriteLine('" + ExitMarker + "' + $LASTEXITCODE) }\n"
                + "}\n";
        }

        /// <summary>Tekst w apostrofach PowerShella (apostrofy, także typograficzne, podwojone).</summary>
        internal static string Quote(string text)
        {
            var builder = new StringBuilder("'");
            foreach (var c in text ?? "")
            {
                builder.Append(c);
                if (c == '\'' || c == '‘' || c == '’' || c == '‚' || c == '‛')
                {
                    builder.Append(c);
                }
            }

            return builder.Append('\'').ToString();
        }

        public static async Task<CommandRunResult> RunAsync(string executable, string script, string workingFolder, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildWrapper(script, workingFolder)));
            if (encoded.Length > 30000)
            {
                throw new ArgumentException("Polecenie jest za długie (najwyżej ok. 10 tys. znaków). Podziel je na kilka.");
            }

            var start = new ProcessStartInfo(string.IsNullOrWhiteSpace(executable) ? "powershell.exe" : executable,
                "-NoProfile -NonInteractive -NoLogo -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Directory.Exists(workingFolder ?? "") ? workingFolder : Environment.CurrentDirectory,
            };

            var output = new StringBuilder();
            var errors = new StringBuilder();
            var exited = new TaskCompletionSource<bool>();
            using (var process = new Process { StartInfo = start, EnableRaisingEvents = true })
            {
                process.OutputDataReceived += (s, e) => Append(output, e.Data);
                process.ErrorDataReceived += (s, e) => Append(errors, e.Data);
                process.Exited += (s, e) => exited.TrySetResult(true);
                try
                {
                    process.Start();
                }
                catch (Win32Exception e)
                {
                    throw new InvalidOperationException("Nie udało się uruchomić " + start.FileName + ": " + e.Message, e);
                }

                process.StandardInput.Close();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var timedOut = false;
                using (cancellationToken.Register(() => exited.TrySetResult(false)))
                {
                    var finished = await Task.WhenAny(exited.Task, Task.Delay(timeout)).ConfigureAwait(false);
                    if (finished != exited.Task || !exited.Task.Result)
                    {
                        timedOut = finished != exited.Task;
                        KillTree(process);
                    }
                }

                // Dopisanie resztek wyjścia; nie czekamy w nieskończoność, bo program uruchomiony w tle może trzymać strumień.
                await Task.Run(() => process.WaitForExit(3000)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                string text;
                lock (output)
                {
                    text = output.ToString();
                }

                string errorText;
                lock (errors)
                {
                    errorText = errors.ToString();
                }

                return Parse(text, errorText, timedOut);
            }
        }

        private static void Append(StringBuilder target, string line)
        {
            if (line == null)
            {
                return;
            }

            lock (target)
            {
                if (target.Length < MaxCaptured)
                {
                    target.Append(line).Append('\n');
                }
            }
        }

        internal static CommandRunResult Parse(string output, string errors, bool timedOut)
        {
            var result = new CommandRunResult { TimedOut = timedOut };
            var lines = (output ?? "").Replace("\r", "").Split('\n').ToList();
            var kept = new StringBuilder();
            foreach (var line in lines)
            {
                if (line.StartsWith(FolderMarker, StringComparison.Ordinal))
                {
                    result.FinalFolder = line.Substring(FolderMarker.Length).Trim();
                    continue;
                }

                if (line.StartsWith(ExitMarker, StringComparison.Ordinal))
                {
                    if (int.TryParse(line.Substring(ExitMarker.Length).Trim(), out var code) && code != 0)
                    {
                        result.NativeExitCode = code;
                    }

                    continue;
                }

                if (line.StartsWith("[błąd] ", StringComparison.Ordinal))
                {
                    result.HasErrors = true;
                }

                kept.Append(line.TrimEnd()).Append('\n');
            }

            // Na stderr zostaje tylko to, czego opakowanie nie przechwyciło (np. błąd składni całego polecenia).
            var stderr = string.Join("\n", (errors ?? "").Replace("\r", "").Split('\n')
                .Where(l => l.Length > 0 && !l.StartsWith("#< CLIXML", StringComparison.Ordinal) && !l.StartsWith("<Objs", StringComparison.Ordinal)));
            if (stderr.Length > 0)
            {
                result.HasErrors = true;
                kept.Append("[błąd] ").Append(stderr).Append('\n');
            }

            if (result.NativeExitCode != null)
            {
                result.HasErrors = true;
                kept.Append("[kod wyjścia programu: ").Append(result.NativeExitCode).Append("]\n");
            }

            result.Output = CollapseBlankLines(kept.ToString()).Trim('\n');
            return result;
        }

        private static string CollapseBlankLines(string text)
        {
            var builder = new StringBuilder();
            var blank = 0;
            foreach (var line in text.Split('\n'))
            {
                blank = line.Trim().Length == 0 ? blank + 1 : 0;
                if (blank <= 1)
                {
                    builder.Append(line).Append('\n');
                }
            }

            return builder.ToString();
        }

        /// <summary>Zamyka proces razem z procesami potomnymi (netstandard2.0 nie ma <c>Kill(true)</c>).</summary>
        private static void KillTree(Process process)
        {
            try
            {
                if (process.HasExited)
                {
                    return;
                }

                using (var killer = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }))
                {
                    killer?.WaitForExit(5000);
                }

                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is Win32Exception)
            {
                // Proces skończył się w międzyczasie.
            }
        }
    }
}
