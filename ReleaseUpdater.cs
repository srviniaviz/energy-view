using System.Security.Cryptography;
using System.Text.Json;

namespace EnergyView;

internal readonly record struct AppRelease(Version Version, Uri Download, string Sha256, long Size);

internal sealed class ReleaseUpdater : IDisposable
{
    internal const string Repository = "srviniaviz/energy-view";
    internal const string ReleasesPage = $"https://github.com/{Repository}/releases/latest";
    private const long MaxInstallerBytes = 200 * 1024 * 1024;
    private readonly HttpClient client = new()
    {
        Timeout = TimeSpan.FromMinutes(3),
        MaxResponseContentBufferSize = 1024 * 1024
    };

    public ReleaseUpdater() => client.DefaultRequestHeaders.UserAgent.ParseAdd("EnergyView-Updater/1.0");

    public async Task<AppRelease?> FindAsync(CancellationToken cancellationToken)
    {
        byte[] body = await client.GetByteArrayAsync(
            $"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);
        Version current = typeof(ReleaseUpdater).Assembly.GetName().Version!;
        return ParseRelease(document.RootElement, new Version(current.Major, current.Minor, current.Build));
    }

    internal static AppRelease? ParseRelease(JsonElement release, Version current)
    {
        string? tag = release.GetProperty("tag_name").GetString();
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean() ||
            tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out Version? version) ||
            version.Build < 0 || version.Revision >= 0 || tag != $"v{version}" || version <= current)
            return null;

        string fileName = $"EnergyView-Setup-{tag}-x64.exe";
        string url = $"https://github.com/{Repository}/releases/download/{tag}/{fileName}";
        foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != fileName ||
                asset.GetProperty("state").GetString() != "uploaded" ||
                asset.GetProperty("browser_download_url").GetString() != url ||
                !asset.TryGetProperty("digest", out JsonElement assetDigest))
                continue;

            string? digest = assetDigest.GetString();
            long size = asset.GetProperty("size").GetInt64();
            if (digest is not null && digest.StartsWith("sha256:", StringComparison.Ordinal) &&
                digest.Length == 71 && digest.AsSpan(7).ContainsOnlyHex() && size is > 0 and <= MaxInstallerBytes)
                return new AppRelease(version, new Uri(url), digest[7..], size);
        }
        return null;
    }

    public async Task<string> DownloadAsync(AppRelease release, CancellationToken cancellationToken)
    {
        if (!AppInstallation.IsInstalled)
            throw new InvalidOperationException("Execute a versão instalada do Energy View para atualizar.");

        string directory = Path.Combine(AppInstallation.DirectoryPath, "updates");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Path.GetFileName(release.Download.AbsolutePath));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using HttpResponseMessage response = await client.GetAsync(release.Download,
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using Stream source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token)) != 0)
            {
                total += read;
                if (total > release.Size)
                    throw new InvalidDataException("O instalador recebido excede o tamanho publicado.");
                hash.AppendData(buffer.AsSpan(0, read));
                await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            if (total != release.Size || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()),
                release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O instalador recebido não corresponde à release do GitHub.");
            return path;
        }
        catch
        {
            try { File.Delete(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    public void Dispose() => client.Dispose();
}

internal static class ReleaseDigest
{
    public static bool ContainsOnlyHex(this ReadOnlySpan<char> digest)
    {
        foreach (char character in digest)
            if (!char.IsAsciiHexDigit(character))
                return false;
        return true;
    }
}
