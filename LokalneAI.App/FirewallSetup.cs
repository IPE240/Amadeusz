using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace LokalneAI.App;

/// <summary>
/// Zapora Windows dla bramy: reguły przychodzące TCP (brama) i UDP (wyszukiwanie) tylko z podsieci (<c>LocalSubnet</c>)
/// i tylko w sieciach prywatnych i domenowych. Odczyt działa bez uprawnień, zmiana wymaga zgody administratora (UAC).
/// </summary>
internal static class FirewallSetup
{
    public const string RulePrefix = "LokalneAI - brama";

    /// <summary>Opis stanu zapory i kategorii sieci do okna, np. „Zapora: reguły są (TCP 11435, UDP 11436). Sieć: prywatna.”</summary>
    public static async Task<string> DescribeAsync(int port, int discoveryPort)
    {
        var script = $$"""
            $rules = Get-NetFirewallRule -DisplayName '{{RulePrefix}}*' -ErrorAction SilentlyContinue | Where-Object { $_.Enabled -eq 'True' -and $_.Action -eq 'Allow' }
            $ports = @($rules | Get-NetFirewallPortFilter | ForEach-Object { $_.Protocol + ' ' + $_.LocalPort })
            $profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue | ForEach-Object { $_.InterfaceAlias + '=' + $_.NetworkCategory })
            'PORTY:' + ($ports -join ',')
            'SIECI:' + ($profiles -join ',')
            """;
        var output = await RunHiddenAsync(script);
        if (output is null)
        {
            return "Nie udało się odczytać ustawień zapory.";
        }

        var ports = Line(output, "PORTY:");
        var networks = Line(output, "SIECI:");
        var hasTcp = ports.Contains("TCP " + port, StringComparison.OrdinalIgnoreCase);
        var hasUdp = ports.Contains("UDP " + discoveryPort, StringComparison.OrdinalIgnoreCase);
        var firewall = hasTcp && hasUdp
            ? $"Zapora: reguły są (TCP {port}, UDP {discoveryPort}, tylko podsieć)."
            : hasTcp
                ? $"Zapora: brama otwarta (TCP {port}), ale bez wyszukiwania (UDP {discoveryPort}) — inni wpiszą adres ręcznie."
                : $"Zapora: brak reguły dla portu {port} — inne komputery się nie połączą. Kliknij „Otwórz port w zaporze”.";
        var publicNetworks = networks.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(n => n.EndsWith("=Public", StringComparison.OrdinalIgnoreCase)).ToList();
        var category = publicNetworks.Count > 0
            ? " Uwaga: sieć " + string.Join(", ", publicNetworks.Select(n => n.Split('=')[0])) + " jest oznaczona jako publiczna — tam reguła nie działa (Ustawienia Windows → Sieć → Profil sieci: Prywatna)."
            : "";
        return firewall + category;
    }

    /// <summary>
    /// Zakłada reguły (prosi o zgodę administratora). Usuwa też reguły blokujące ten program, które Windows tworzy
    /// po kliknięciu „Anuluj” w swoim okienku zapory — mają one pierwszeństwo przed zezwoleniem.
    /// Zwraca opis błędu albo null.
    /// </summary>
    public static async Task<string?> SetupAsync(int port, int discoveryPort)
    {
        var exe = Environment.ProcessPath ?? "";
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Get-NetFirewallRule -DisplayName '{{RulePrefix}}*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
            New-NetFirewallRule -DisplayName '{{RulePrefix}} (TCP {{port}})' -Direction Inbound -Protocol TCP -LocalPort {{port}} -RemoteAddress LocalSubnet -Profile Domain,Private -Action Allow | Out-Null
            New-NetFirewallRule -DisplayName '{{RulePrefix}} (UDP {{discoveryPort}}, wyszukiwanie)' -Direction Inbound -Protocol UDP -LocalPort {{discoveryPort}} -RemoteAddress LocalSubnet -Profile Domain,Private -Action Allow | Out-Null
            Get-NetFirewallApplicationFilter -Program '{{exe.Replace("'", "''")}}' -ErrorAction SilentlyContinue | Get-NetFirewallRule | Where-Object { $_.Action -eq 'Block' -and $_.Direction -eq 'Inbound' } | Remove-NetFirewallRule
            """;
        var file = Path.Combine(Path.GetTempPath(), "LokalneAI_zapora.ps1");
        await File.WriteAllTextAsync(file, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{file}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
            {
                return "Nie uruchomiłem PowerShella.";
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? null : $"PowerShell zakończył się kodem {process.ExitCode}.";
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return "Anulowano — bez zgody administratora zapora zostaje bez zmian.";
        }
        catch (Win32Exception e)
        {
            return e.Message;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Plik tymczasowy zostanie w %TEMP%.
            }
        }
    }

    private static string Line(string output, string prefix)
        => output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..] ?? "";

    private static async Task<string?> RunHiddenAsync(string script)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            });
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
