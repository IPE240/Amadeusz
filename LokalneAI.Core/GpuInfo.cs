using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace LokalneAI.Core;

/// <summary>Karta graficzna komputera. Służy tylko do opisu, gdzie liczy model.</summary>
public sealed record GpuInfo(string Name, int? TotalMiB = null, int? UsedMiB = null)
{
    private const string NvidiaSmiQuery = "--query-gpu=name,memory.total,memory.used --format=csv,noheader,nounits";

    /// <summary>Np. „NVIDIA GeForce RTX 4060 (8 GB)”.</summary>
    public string Describe()
        => TotalMiB is int total ? string.Format(Formats.Pl, "{0} ({1:0.#} GB)", Name, total / 1024.0) : Name;

    /// <summary>Karty z wyniku <c>nvidia-smi --query-gpu=name,memory.total,memory.used --format=csv,noheader,nounits</c>.</summary>
    public static IReadOnlyList<GpuInfo> ParseNvidiaSmi(string? output)
    {
        var cards = new List<GpuInfo>();
        foreach (var line in (output ?? "").Split('\n'))
        {
            var fields = line.Trim().Split(',');
            var name = fields[0].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            cards.Add(new GpuInfo(name, ReadInt(fields, 1), ReadInt(fields, 2)));
        }

        return cards;
    }

    /// <summary>Najpierw <c>nvidia-smi</c> (dokładna pamięć), potem rejestr Windows (nazwy kart).</summary>
    public static IReadOnlyList<GpuInfo> Detect()
    {
        var nvidia = ParseNvidiaSmi(RunNvidiaSmi());
        return nvidia.Count > 0 ? nvidia : FromRegistry();
    }

    private static int? ReadInt(string[] fields, int index)
        => fields.Length > index && int.TryParse(fields[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string? RunNvidiaSmi()
    {
        try
        {
            var start = new ProcessStartInfo("nvidia-smi", NvidiaSmiQuery)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            if (!output.Wait(TimeSpan.FromSeconds(5)))
            {
                process.Kill();
                return null;
            }

            return process.WaitForExit(2000) && process.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Brak nvidia-smi (nie ma karty NVIDIA albo sterownika) — zostaje rejestr.
            return null;
        }
    }

    private static IReadOnlyList<GpuInfo> FromRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var cards = new List<GpuInfo>();
        try
        {
            using var displayClass = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (displayClass is null)
            {
                return cards;
            }

            foreach (var subName in displayClass.GetSubKeyNames())
            {
                if (subName.Length != 4 || !subName.All(char.IsAsciiDigit))
                {
                    continue;
                }

                try
                {
                    using var key = displayClass.OpenSubKey(subName);
                    if (key?.GetValue("DriverDesc") is not string name || string.IsNullOrWhiteSpace(name)
                        || name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    long? bytes = key.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long value => value,
                        byte[] raw when raw.Length >= 8 => BitConverter.ToInt64(raw, 0),
                        _ => null,
                    };
                    cards.Add(new GpuInfo(name.Trim(), bytes is > 0 ? (int)(bytes.Value / (1024 * 1024)) : null));
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // Niektóre podklucze są zastrzeżone dla administratora — pomijamy je.
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return cards;
        }

        // Karta dedykowana przed zintegrowaną: na niej Ollama liczy, jeśli może.
        return cards
            .OrderByDescending(c => IsDedicated(c.Name))
            .ThenByDescending(c => c.TotalMiB ?? 0)
            .ToList();
    }

    private static bool IsDedicated(string name)
        => name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Arc", StringComparison.Ordinal);
}
