using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

namespace EnergyView;

internal readonly record struct HardwarePowerSensor(
    string Id, HardwareType Type, string Device, string Name, double? Watts);

internal sealed class HardwarePowerSource : IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
    private readonly object gate = new();
    private Computer? computer;
    private long? lastFailure;
    private bool disposed;

    public IReadOnlyList<HardwarePowerSensor> Read()
    {
        lock (gate)
        {
            if (disposed || (lastFailure is long failure && Stopwatch.GetElapsedTime(failure) < RetryInterval))
                return [];

            try
            {
                if (computer is null)
                {
                    computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = true,
                        IsMotherboardEnabled = true,
                        IsStorageEnabled = true,
                        IsControllerEnabled = true,
                        IsNetworkEnabled = true,
                        IsPsuEnabled = true,
                        IsPowerMonitorEnabled = true
                    };
                    computer.Open();
                }

                var values = new List<HardwarePowerSensor>();
                foreach (IHardware hardware in computer.Hardware)
                    ReadDevice(hardware, values);

                lastFailure = null;
                return values;
            }
            catch (Exception error)
            {
                // A failed hardware library initialization must not stop the independent meters.
                Trace.TraceWarning("LibreHardwareMonitor initialization failed: {0}", error);
                lastFailure = Stopwatch.GetTimestamp();
                Close();
                return [];
            }
        }
    }

    public static bool IsCpuDriverInstalled => PawnIo.IsInstalled;

    private static void ReadDevice(IHardware hardware, List<HardwarePowerSensor> values)
    {
        if (hardware.HardwareType == HardwareType.Cpu && !IsCpuDriverInstalled)
            return;

        bool updated = false;
        try
        {
            hardware.Update();
            updated = true;
        }
        catch (Exception error)
        {
            Trace.TraceWarning("LibreHardwareMonitor device update failed: {0}", error);
        }

        ISensor[] sensors = hardware.Sensors;
        if (hardware.HardwareType == HardwareType.Cpu &&
            sensors.Any(sensor => sensor.SensorType == SensorType.Power && (sensor.Name is "Package" or "CPU Package") &&
                sensor.Value is not (> 0)))
            updated = false;

        foreach (ISensor sensor in sensors)
        {
            if (sensor.SensorType != SensorType.Power)
                continue;

            double? watts = updated && sensor.Value is float value && float.IsFinite(value) && value >= 0
                ? value : null;
            values.Add(new(sensor.Identifier.ToString(), hardware.HardwareType, hardware.Name, sensor.Name, watts));
        }

        foreach (IHardware child in hardware.SubHardware)
            ReadDevice(child, values);
    }

    private void Close()
    {
        try
        {
            computer?.Close();
        }
        catch (Exception error)
        {
            Trace.TraceWarning("LibreHardwareMonitor close failed: {0}", error);
        }
        finally
        {
            computer = null;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Close();
        }
    }
}
