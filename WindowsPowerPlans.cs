using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EnergyView;

internal readonly record struct PowerPlanOption(Guid Id, string Label)
{
    public override string ToString() => Label;
}

internal static partial class WindowsPowerPlans
{
    private static readonly Guid MaximumSavings = new("7ba84d69-e349-415a-8c41-c8ad2a867683");
    private static readonly Guid Saver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static IReadOnlyList<PowerPlanOption> Options { get; } =
    [
        new(MaximumSavings, "Máxima economia"),
        new(Saver, "Econômico"),
        new(new("381b4222-f694-41f0-9685-ff5bb260df2e"), "Médio"),
        new(new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), "Desempenho máximo")
    ];

    public static async Task<Guid> GetActiveAsync(CancellationToken cancellationToken)
    {
        string output = await RunAsync(cancellationToken, "/getactivescheme");
        return Guid.TryParse(SchemeId().Match(output).Value, out Guid scheme)
            ? scheme : throw new InvalidOperationException("O Windows não informou o plano de energia ativo.");
    }

    public static async Task ApplyAsync(Guid scheme, CancellationToken cancellationToken)
    {
        if (!Options.Any(option => option.Id == scheme))
            throw new ArgumentOutOfRangeException(nameof(scheme));

        string id = scheme.ToString("D");
        string available = await RunAsync(cancellationToken, "/list");
        bool exists = SchemeId().Matches(available).Any(match => Guid.Parse(match.Value) == scheme);
        if (!exists)
        {
            Guid template = scheme == MaximumSavings ? Saver : scheme;
            await RunAsync(cancellationToken, "/duplicatescheme", template.ToString("D"), id);
            if (scheme == MaximumSavings)
            {
                try
                {
                    await RunAsync(cancellationToken, "/changename", id, "Energy View · Máxima economia");
                    await RunAsync(cancellationToken, "/setacvalueindex", id, "SUB_PROCESSOR", "PROCTHROTTLEMIN", "5");
                    await RunAsync(cancellationToken, "/setdcvalueindex", id, "SUB_PROCESSOR", "PROCTHROTTLEMIN", "5");
                    await RunAsync(cancellationToken, "/setacvalueindex", id, "SUB_PROCESSOR", "PROCTHROTTLEMAX", "70");
                    await RunAsync(cancellationToken, "/setdcvalueindex", id, "SUB_PROCESSOR", "PROCTHROTTLEMAX", "70");
                }
                catch
                {
                    try { await RunAsync(CancellationToken.None, "/delete", id); }
                    catch (Exception error) when (error is InvalidOperationException or OperationCanceledException
                        or System.ComponentModel.Win32Exception) { Trace.TraceWarning("Power plan cleanup failed: {0}", error); }
                    throw;
                }
            }
        }
        await RunAsync(cancellationToken, "/setactive", id);
    }

    private static async Task<string> RunAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("O Windows não iniciou o powercfg.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        string output = await stdout;
        string error = await stderr;
        if (process.ExitCode != 0)
        {
            Trace.TraceWarning("powercfg {0} exited {1}: {2} {3}", arguments[0], process.ExitCode, output, error);
            throw new InvalidOperationException("O Windows não aceitou a operação com o plano de energia.");
        }
        return output;
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b")]
    private static partial Regex SchemeId();
}
