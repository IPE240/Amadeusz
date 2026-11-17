using System.Runtime.InteropServices;

namespace LokalneAI.Core.Hardware;

/// <summary>
/// Temperatura kart NVIDIA przez NVML (<c>nvml.dll</c> ze sterownika — ta sama, której używa <c>nvidia-smi</c>), bez praw
/// administratora i bez uruchamiania procesów. Model liczy na karcie, więc to jej temperatura mówi, jak bardzo pracuje komputer.
/// </summary>
public static class GpuTemperature
{
    private const int NvmlSuccess = 0;
    private const int NvmlTemperatureGpu = 0;
    private static readonly object Gate = new();
    private static bool _initialized;
    private static bool _unavailable;

    /// <summary>Najwyższa temperatura kart NVIDIA w °C; null, gdy nie ma karty NVIDIA albo sterownika.</summary>
    public static int? Read()
    {
        lock (Gate)
        {
            if (_unavailable)
            {
                return null;
            }

            try
            {
                if (!_initialized)
                {
                    if (NvmlInit() != NvmlSuccess)
                    {
                        _unavailable = true;
                        return null;
                    }

                    _initialized = true;
                }

                if (NvmlDeviceGetCount(out var count) != NvmlSuccess)
                {
                    return null;
                }

                int? max = null;
                for (uint i = 0; i < count; i++)
                {
                    if (NvmlDeviceGetHandleByIndex(i, out var device) == NvmlSuccess
                        && NvmlDeviceGetTemperature(device, NvmlTemperatureGpu, out var celsius) == NvmlSuccess
                        && celsius is > 0 and < 150)
                    {
                        max = Math.Max(max ?? 0, (int)celsius);
                    }
                }

                return max;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                _unavailable = true;
                return null;
            }
        }
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    private static extern int NvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")]
    private static extern int NvmlDeviceGetCount(out uint count);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int NvmlDeviceGetTemperature(IntPtr device, int sensor, out uint celsius);
}
