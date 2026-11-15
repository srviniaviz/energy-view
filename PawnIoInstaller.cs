using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace EnergyView;

internal static class PawnIoInstaller
{
    private const string InstallerUrl =
        "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    private const string InstallerSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public static bool IsInstalled
    {
        get
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? product = machine.OpenSubKey(RegistryPath);
            return Version.TryParse(product?.GetValue("DisplayVersion") as string, out _);
        }
    }

    internal static async Task<byte[]> DownloadVerifiedAsync()
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
            MaxResponseContentBufferSize = 16 * 1024 * 1024
        };
        byte[] payload = await client.GetByteArrayAsync(InstallerUrl).ConfigureAwait(false);
        if (Convert.ToHexString(SHA256.HashData(payload)) != InstallerSha256)
            throw new InvalidDataException("O arquivo recebido não corresponde ao instalador oficial do PawnIO.");
        return payload;
    }

    public static async Task<bool> InstallAsync()
    {
        byte[] payload = await DownloadVerifiedAsync();
        DirectoryInfo temporary = Directory.CreateTempSubdirectory("EnergyView-PawnIO-");
        string path = Path.Combine(temporary.FullName, "PawnIO_setup.exe");
        try
        {
            await File.WriteAllBytesAsync(path, payload);
            using Process installer = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Verb = "runas"
            }) ?? throw new InvalidOperationException("Não foi possível abrir o instalador do PawnIO.");
            await installer.WaitForExitAsync();
            return IsInstalled;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(path);
                temporary.Delete();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("PawnIO installer cleanup failed: {0}", error);
            }
        }
    }
}
