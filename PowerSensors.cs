using System.Runtime.InteropServices;

namespace EnergyView;

internal readonly record struct PowerReading(double? BatteryWatts, double? GpuWatts);

internal sealed class PowerSensors : IDisposable
{
    private const int SystemBatteryState = 5;
    private const int UnknownBatteryRate = int.MinValue;
    private readonly bool nvidiaAvailable;
    private readonly List<nint> gpuHandles = [];

    public PowerSensors()
    {
        try
        {
            nvidiaAvailable = NvmlInit() == 0;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return;
        }

        if (!nvidiaAvailable || NvmlGetCount(out uint count) != 0)
            return;

        for (uint index = 0; index < count; index++)
        {
            if (NvmlGetHandle(index, out nint handle) == 0)
                gpuHandles.Add(handle);
        }

        // A sum is only valid when every detected NVIDIA GPU can be read.
        if (gpuHandles.Count != count)
            gpuHandles.Clear();
    }

    public PowerReading Read() => new(ReadBattery(), ReadGpu());

    private double? ReadGpu()
    {
        if (gpuHandles.Count == 0)
            return null;

        double total = 0;
        foreach (nint handle in gpuHandles)
        {
            if (NvmlGetPowerUsage(handle, out uint milliwatts) != 0)
                return null;
            total += milliwatts / 1000d;
        }
        return total;
    }

    private static double? ReadBattery()
    {
        int result = CallNtPowerInformation(SystemBatteryState, 0, 0,
            out BatteryState state, (uint)Marshal.SizeOf<BatteryState>());

        if (result != 0 || state.AcOnline != 0 || state.BatteryPresent == 0 ||
            state.Discharging == 0 || state.Rate >= 0 || state.Rate == UnknownBatteryRate)
            return null;

        return state.Rate / -1000d;
    }

    public void Dispose()
    {
        if (nvidiaAvailable)
            NvmlShutdown();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryState
    {
        public byte AcOnline;
        public byte BatteryPresent;
        public byte Charging;
        public byte Discharging;
        public uint ReservedAndTag;
        public uint MaxCapacity;
        public uint RemainingCapacity;
        public int Rate;
        public uint EstimatedTime;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
    }

    [DllImport("powrprof.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CallNtPowerInformation(int level, nint input, uint inputSize,
        out BatteryState output, uint outputSize);

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NvmlShutdown();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NvmlGetCount(out uint count);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NvmlGetHandle(uint index, out nint device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NvmlGetPowerUsage(nint device, out uint milliwatts);
}
