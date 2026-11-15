using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace EnergyView;

internal sealed class PowerChart : Control
{
    private static readonly TimeSpan History = TimeSpan.FromSeconds(60);
    private readonly Queue<(long Time, double? Watts)> samples = new();
    private string? source;

    public PowerChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(14, 21, 18);
    }

    public void AddReading(double? watts, string readingSource)
    {
        if (source != readingSource)
        {
            samples.Clear();
            source = readingSource;
        }

        long now = Stopwatch.GetTimestamp();
        while (samples.TryPeek(out var oldest) && Stopwatch.GetElapsedTime(oldest.Time, now) > History)
            samples.Dequeue();

        samples.Enqueue((now, watts));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        Graphics canvas = args.Graphics;
        canvas.SmoothingMode = SmoothingMode.AntiAlias;
        using var grid = new Pen(Color.FromArgb(42, 54, 47));
        canvas.DrawRectangle(grid, 0, 0, Width - 1, Height - 1);
        canvas.DrawLine(grid, 0, Height / 2, Width, Height / 2);

        double peak = samples.Max(sample => sample.Watts) ?? 0;
        double scale = Math.Max(10, Math.Ceiling(peak / 10) * 10);
        using var line = new Pen(PowerPanel.Accent, 1.6f * DeviceDpi / 96f);
        using var dot = new SolidBrush(PowerPanel.Accent);
        long now = Stopwatch.GetTimestamp();
        PointF? previous = null;
        long previousTime = 0;
        foreach (var (time, watts) in samples)
        {
            double age = Stopwatch.GetElapsedTime(time, now).TotalSeconds;
            if (watts is not double value || age > History.TotalSeconds)
            {
                previous = null;
                continue;
            }

            var point = new PointF(4 + (float)(1 - age / History.TotalSeconds) * (Width - 8),
                Height - 5 - (float)(value / scale) * (Height - 10));
            if (previous is PointF last && Stopwatch.GetElapsedTime(previousTime, time).TotalSeconds <= 2.5)
                canvas.DrawLine(line, last, point);
            previous = point;
            previousTime = time;
        }

        if (previous is PointF latest)
            canvas.FillEllipse(dot, latest.X - 2, latest.Y - 2, 4, 4);
    }
}
