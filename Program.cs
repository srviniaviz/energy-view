using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace EnergyView;

internal static class Program
{
    private const string WaitForInstanceArgument = "--wait-for-instance";
    internal const string StartupArgument = "--startup";

    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = new Mutex(false, @"Local\EnergyView");
        try
        {
            int waitMilliseconds = args.Contains(WaitForInstanceArgument) ? 15000 : 0;
            if (!instance.WaitOne(waitMilliseconds))
                return;
        }
        catch (AbandonedMutexException)
        {
            // The previous process exited while holding the mutex. This process now owns it.
        }

        try
        {
            ApplicationConfiguration.Initialize();
            bool driverInstalled = PawnIoInstaller.IsInstalled;
            bool elevated = IsElevated();
            bool startHidden = args.Contains(StartupArgument);
            if ((driverInstalled || AppInstallation.IsInstalled) && !elevated && TryElevate(startHidden))
                return;

            using var context = new TrayContext(driverInstalled, startHidden, elevated);
            Application.Run(context);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    internal static void RelaunchForSensors(bool startHidden = false)
    {
        using Process next = Process.Start(new ProcessStartInfo(
            Environment.ProcessPath ?? Application.ExecutablePath,
            startHidden ? $"{WaitForInstanceArgument} {StartupArgument}" : WaitForInstanceArgument)
        {
            UseShellExecute = true,
            Verb = IsElevated() ? "open" : "runas"
        }) ?? throw new InvalidOperationException("Não foi possível reabrir o Energy View.");
    }

    private static bool TryElevate(bool startHidden)
    {
        try
        {
            RelaunchForSensors(startHidden);
            return true;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            return false;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Trace.TraceWarning("Automatic sensor elevation failed: {0}", error);
            return false;
        }
    }

    private static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
