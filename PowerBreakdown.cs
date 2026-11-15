using LibreHardwareMonitor.Hardware;

namespace EnergyView;

internal enum ComponentGroup { Cpu, Nvidia }

internal readonly record struct ComponentPower(string Component, double? Watts, string Origin,
    ComponentGroup? Group = null, bool IsDetail = false, string? FullName = null);

internal readonly record struct LivePower(double? Watts, string Label)
{
    public static LivePower FromComponents(double? cpu, double? gpu, double? monitors = null)
    {
        if (monitors is double monitorWatts)
        {
            string label = (cpu.HasValue, gpu.HasValue) switch
            {
                (true, true) => "CPU + GPU + monitores",
                (true, false) => "CPU + monitores",
                (false, true) => "GPU + monitores",
                _ => "Potência dos monitores"
            };
            return new(cpu.GetValueOrDefault() + gpu.GetValueOrDefault() + monitorWatts, label);
        }

        return (cpu, gpu) switch
        {
            (double cpuWatts, double gpuWatts) => new(cpuWatts + gpuWatts, "Potência de CPU + GPU"),
            (double cpuWatts, null) => new(cpuWatts, "Potência da CPU"),
            (null, double gpuWatts) => new(gpuWatts, "Potência da GPU"),
            _ => new(null, "Potência monitorada")
        };
    }
}

internal static class PowerBreakdown
{
    public static IReadOnlyList<ComponentPower> Build(IReadOnlyList<HardwarePowerSensor> sensors,
        bool cpuDriverInstalled, double? cpu, double? gpu, double? monitors = null)
    {
        var cpuPackages = sensors.Where(IsCpuPackage).ToArray();
        bool hasSingleNvidiaPackage = gpu.HasValue &&
            sensors.Count(IsNvidiaPackage) == 1;

        List<ComponentPower> rows =
        [
            new("Processador (CPU)", cpu, cpu.HasValue ? "CPU Package" :
                cpuDriverInstalled ? "Sem leitura" : "Requer PawnIO", ComponentGroup.Cpu),
            new("Placa de vídeo NVIDIA", gpu, gpu.HasValue ? "Sensor da GPU" : "Sem leitura", ComponentGroup.Nvidia)
        ];

        foreach (HardwarePowerSensor sensor in sensors.OrderBy(sensor => sensor.Id, StringComparer.Ordinal))
        {
            if ((cpuPackages.Length == 1 && IsCpuPackage(sensor)) ||
                (hasSingleNvidiaPackage && IsNvidiaPackage(sensor)))
                continue;

            ComponentGroup? group = sensor.Type switch
            {
                HardwareType.Cpu => ComponentGroup.Cpu,
                HardwareType.GpuNvidia => ComponentGroup.Nvidia,
                _ => null
            };
            string fullName = $"{sensor.Device} · {sensor.Name}";
            var row = new ComponentPower(group.HasValue ? sensor.Name : fullName, sensor.Watts,
                sensor.Watts.HasValue ? "Sensor" : "Sem leitura", group, group.HasValue, fullName);
            if (group.HasValue)
                rows.Insert(rows.FindLastIndex(existing => existing.Group == group) + 1, row);
            else
                rows.Add(row);
        }

        AddIfMissing("Memória RAM", HardwareType.Memory);
        AddIfMissing("Placa-mãe", HardwareType.Motherboard, HardwareType.SuperIO, HardwareType.EmbeddedController);
        AddIfMissing("SSD e HD", HardwareType.Storage);
        AddIfMissing("Ventoinhas e bomba", HardwareType.Cooler);
        AddIfMissing("Fonte de alimentação", HardwareType.Psu);
        rows.Add(new("USB alimentado pelo PC", null, "Sem leitura"));
        if (monitors.HasValue)
            rows.Add(new("Monitores cadastrados", monitors, "Estimado"));
        return rows;

        void AddIfMissing(string name, params HardwareType[] types)
        {
            if (!sensors.Any(sensor => types.Contains(sensor.Type)))
                rows.Add(new(name, null, "Sem leitura"));
        }
    }

    public static double? ReadCpuWatts(IEnumerable<HardwarePowerSensor> sensors) =>
        SumCpuPackages(sensors.Where(IsCpuPackage).ToArray());

    private static double? SumCpuPackages(HardwarePowerSensor[] packages) =>
        packages.Length > 0 && packages.All(sensor => sensor.Watts.HasValue)
            ? packages.Sum(sensor => sensor.Watts.GetValueOrDefault()) : null;

    private static bool IsCpuPackage(HardwarePowerSensor sensor) =>
        sensor.Type == HardwareType.Cpu && sensor.Name is "Package" or "CPU Package";

    private static bool IsNvidiaPackage(HardwarePowerSensor sensor) =>
        sensor.Type == HardwareType.GpuNvidia && sensor.Name == "GPU Package";
}
