using System.Diagnostics;
using System.Text.Json;

namespace EnergyView;

internal readonly record struct EnergyTotals(double? MeasuredKwh, double? ComponentsKwh)
{
    public EnergyTotals Add(EnergyTotals other) => new(
        AddValue(MeasuredKwh, other.MeasuredKwh), AddValue(ComponentsKwh, other.ComponentsKwh));

    private static double? AddValue(double? first, double? second) =>
        first.HasValue || second.HasValue ? first.GetValueOrDefault() + second.GetValueOrDefault() : null;
}

internal readonly record struct EnergySummary(EnergyTotals Hour, EnergyTotals Day, EnergyTotals Month);

internal sealed class EnergyHistory
{
    private const double MaxGapSeconds = 5;
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(15);
    private readonly string path;
    private readonly TimeZoneInfo timeZone;
    private readonly Dictionary<DateTimeOffset, EnergyTotals> hourly = [];
    private readonly bool historyCouldBeRead = true;
    private long lastSaveAttempt = Stopwatch.GetTimestamp();
    private bool dirty;
    private Sample? previous;

    public string? SaveError { get; private set; }

    public EnergyHistory(string path, TimeZoneInfo timeZone)
    {
        this.path = path;
        this.timeZone = timeZone;
        try
        {
            using var stream = File.OpenRead(path);
            hourly = JsonSerializer.Deserialize<Dictionary<DateTimeOffset, EnergyTotals>>(stream)
                ?? throw new JsonException("Empty energy history.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            // The first interval creates the history file.
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            historyCouldBeRead = false;
            SaveError = "O histórico salvo não pôde ser lido.";
            Trace.TraceWarning("Energy history load failed: {0}", error);
        }
    }

    public void Record(DateTimeOffset time, double? measuredWatts, double? componentsWatts)
    {
        var current = new Sample(time, measuredWatts, componentsWatts);
        Sample? last = previous;
        previous = current;
        if (last is not Sample start)
            return;

        double elapsed = (current.Time - start.Time).TotalSeconds;
        if (elapsed <= 0 || elapsed > MaxGapSeconds)
            return;

        for (DateTimeOffset cursor = start.Time; cursor < current.Time;)
        {
            DateTimeOffset hour = GetHour(cursor);
            DateTimeOffset boundary = hour.AddHours(1);
            DateTimeOffset end = current.Time < boundary ? current.Time : boundary;
            double firstFraction = (cursor - start.Time).TotalSeconds / elapsed;
            double lastFraction = (end - start.Time).TotalSeconds / elapsed;
            double seconds = (end - cursor).TotalSeconds;
            var energy = new EnergyTotals(
                Integrate(start.MeasuredWatts, current.MeasuredWatts, firstFraction, lastFraction, seconds),
                Integrate(start.ComponentsWatts, current.ComponentsWatts, firstFraction, lastFraction, seconds));

            if (energy != default)
            {
                hourly[hour] = hourly.GetValueOrDefault(hour).Add(energy);
                dirty = true;
            }
            cursor = end;
        }
    }

    public EnergySummary Summarize(DateTimeOffset now)
    {
        DateTimeOffset hour = GetHour(now);
        EnergyTotals day = default;
        EnergyTotals month = default;
        foreach (var (date, energy) in hourly)
        {
            if (date > hour || date.Year != hour.Year || date.Month != hour.Month)
                continue;

            month = month.Add(energy);
            if (date.Day == hour.Day)
                day = day.Add(energy);
        }

        return new EnergySummary(hourly.GetValueOrDefault(hour), day, month);
    }

    public void Save(bool force = false)
    {
        if (!dirty || !historyCouldBeRead || (!force && Stopwatch.GetElapsedTime(lastSaveAttempt) < SaveInterval))
            return;

        lastSaveAttempt = Stopwatch.GetTimestamp();
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, hourly);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            dirty = false;
            SaveError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SaveError = "Não foi possível gravar o histórico.";
            Trace.TraceWarning("Energy history save failed: {0}", error);
        }
    }

    private DateTimeOffset GetHour(DateTimeOffset time)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(time, timeZone);
        return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset);
    }

    private static double? Integrate(double? from, double? to, double first, double last, double seconds)
    {
        if (from is not double start || to is not double end)
            return null;

        double midpoint = (first + last) / 2;
        double averageWatts = start + (end - start) * midpoint;
        return averageWatts * (seconds / 3_600_000d);
    }

    private readonly record struct Sample(DateTimeOffset Time, double? MeasuredWatts, double? ComponentsWatts);
}
