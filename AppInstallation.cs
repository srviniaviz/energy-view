namespace EnergyView;

internal static class AppInstallation
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Energy View");

    public static string ExecutablePath => Path.Combine(DirectoryPath, "EnergyView.exe");

    public static bool IsInstalled => string.Equals(
        Environment.ProcessPath, ExecutablePath, StringComparison.OrdinalIgnoreCase);
}
