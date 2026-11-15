using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnergyView;

internal readonly record struct MonitorEstimate(string Name, decimal Watts, bool Enabled);

internal sealed record EnergyPreferences(decimal? Tariff, MonitorEstimate[] Monitors)
{
    [JsonIgnore]
    public double? MonitorWatts
    {
        get
        {
            MonitorEstimate[] enabled = Monitors.Where(monitor => monitor.Enabled).ToArray();
            return enabled.Length == 0 ? null : enabled.Sum(monitor => (double)monitor.Watts);
        }
    }
}

internal sealed class EnergyPreferencesStore(string path)
{
    public EnergyPreferences Load()
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            EnergyPreferences? settings = JsonSerializer.Deserialize<EnergyPreferences>(stream);
            if (settings is null || settings.Tariff < 0 || settings.Monitors is null ||
                settings.Monitors.Any(monitor => string.IsNullOrWhiteSpace(monitor.Name) || monitor.Watts < 0))
                throw new JsonException("Configuração de energia inválida.");
            return settings;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(null, []);
        }
    }

    public void Save(EnergyPreferences settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        using (FileStream stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, settings);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
