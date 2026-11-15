using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EnergyView;

internal static class WindowsStartup
{
    private const int TaskMissing = unchecked((int)0x80070002);

    public static bool IsEnabled()
    {
        dynamic folder = Connect().GetFolder("\\");
        try
        {
            return (bool)folder.GetTask(TaskName(UserSid())).Enabled;
        }
        catch (Exception error) when (error.HResult == TaskMissing)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled, bool elevated)
    {
        if (!AppInstallation.IsInstalled)
            throw new InvalidOperationException("Instale o Energy View pelo instalador oficial para iniciar com o Windows.");

        string sid = UserSid();
        dynamic service = Connect();
        dynamic folder = service.GetFolder("\\");
        if (!enabled)
        {
            try { folder.DeleteTask(TaskName(sid), 0); }
            catch (Exception error) when (error.HResult == TaskMissing) { }
            return;
        }

        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Inicia o Energy View na bandeja ao entrar no Windows.";
        definition.Principal.UserId = sid;
        definition.Principal.LogonType = 3; // Interactive token
        definition.Principal.RunLevel = elevated ? 1 : 0;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT0S";

        dynamic trigger = definition.Triggers.Create(9); // Logon
        trigger.UserId = sid;
        dynamic action = definition.Actions.Create(0); // Executable
        action.Path = AppInstallation.ExecutablePath;
        action.WorkingDirectory = AppInstallation.DirectoryPath;
        action.Arguments = Program.StartupArgument;
        folder.RegisterTaskDefinition(TaskName(sid), definition, 6, sid, null, 3, null);
    }

    private static dynamic Connect()
    {
        Type type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    private static string UserSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }

    private static string TaskName(string sid) => $"EnergyView-{sid}";
}
