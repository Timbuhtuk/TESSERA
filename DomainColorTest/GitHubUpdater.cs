using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace DomainColorTest;

public sealed record TesseraRelease(Version Version, string Tag, Uri Page, Uri Download, string Sha256);

public static class GitHubUpdater
{
    private const string LatestRelease = "https://api.github.com/repos/Timbuhtuk/TESSERA/releases/latest";
    private const string AssetPrefix = "https://github.com/Timbuhtuk/TESSERA/releases/download/";
    private static readonly HttpClient Client = CreateClient();

    public static Version CurrentVersion => typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static bool CanInstall => string.Equals(Path.GetFileName(Environment.ProcessPath), "Tessera.exe", StringComparison.OrdinalIgnoreCase);

    public static async Task<TesseraRelease?> CheckAsync(HttpClient? client = null, Version? currentVersion = null)
    {
        var http = client ?? Client;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync(LatestRelease, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var root = json.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryVersion(tag, out var version) || version <= (currentVersion ?? CurrentVersion)) return null;
        var page = new Uri(root.GetProperty("html_url").GetString() ?? "");
        if (page.Scheme != Uri.UriSchemeHttps || page.Host != "github.com" ||
            !page.AbsolutePath.StartsWith("/Timbuhtuk/TESSERA/releases/tag/", StringComparison.Ordinal))
            throw new InvalidDataException("Некорректная ссылка на релиз GitHub.");

        JsonElement? exe = null;
        JsonElement? checksums = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            if (name == "Tessera.exe") exe = asset;
            if (name == "SHA256SUMS.txt") checksums = asset;
        }
        if (exe is null) throw new InvalidDataException("В релизе нет Tessera.exe.");
        var download = AssetUri(exe.Value, tag, "Tessera.exe");
        string? digest = exe.Value.TryGetProperty("digest", out var value) ? value.GetString() : null;
        if (digest is null && checksums is not null)
        {
            var checksumUri = AssetUri(checksums.Value, tag, "SHA256SUMS.txt");
            string content = await http.GetStringAsync(checksumUri, timeout.Token);
            if (content.Length > 65536) throw new InvalidDataException("Файл контрольных сумм слишком велик.");
            digest = content.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.EndsWith("  Tessera.exe", StringComparison.Ordinal))?.Split(' ', 2)[0];
        }
        if (digest is null) throw new InvalidDataException("В релизе нет SHA-256 для Tessera.exe.");
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) digest = digest[7..];
        if (!IsSha256(digest)) throw new InvalidDataException("Некорректная контрольная сумма релиза.");
        return new TesseraRelease(version, tag, page, download, digest.ToLowerInvariant());
    }

    public static async Task<string> DownloadAsync(TesseraRelease release, HttpClient? client = null, string? outputFolder = null)
    {
        var http = client ?? Client;
        string folder = outputFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tessera", "Updates");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"Tessera-{release.Tag}-{Guid.NewGuid():N}.exe");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            using var response = await http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 500_000_000)
                throw new InvalidDataException("Файл обновления слишком велик.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[131072];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > 500_000_000) throw new InvalidDataException("Файл обновления слишком велик.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }
            if (new FileInfo(path).Length is 0 or > 500_000_000 || !MatchesSha256(path, release.Sha256))
                throw new InvalidDataException("Контрольная сумма обновления не совпала.");
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public static void StartInstall(string downloadedPath, string digest)
    {
        if (!CanInstall) throw new InvalidOperationException("Автоустановка доступна только для Tessera.exe из релиза.");
        string target = Environment.ProcessPath!;
        string directory = Path.GetDirectoryName(target)!;
        string probe = Path.Combine(directory, $".Tessera-write-check-{Guid.NewGuid():N}");
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        var start = new ProcessStartInfo(downloadedPath) { UseShellExecute = false };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(target);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add(digest);
        if (Process.Start(start) is null) throw new IOException("Не удалось запустить установщик обновления.");
    }

    public static void ApplyUpdate(string target, int processId, string digest)
    {
        string source = Environment.ProcessPath ?? throw new IOException("Не найден файл обновления.");
        if (!IsSha256(digest) || !MatchesSha256(source, digest))
            throw new InvalidDataException("Контрольная сумма обновления не совпала.");
        if (!string.Equals(Path.GetFileName(target), "Tessera.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Некорректный путь приложения.");
        target = Path.GetFullPath(target);
        if (!File.Exists(target) || Path.GetFullPath(source).Equals(target, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Не найден установленный Tessera.exe.", target);

        try
        {
            using var oldProcess = Process.GetProcessById(processId);
            if (!oldProcess.WaitForExit(30000)) throw new TimeoutException("Приложение не закрылось за 30 секунд.");
        }
        catch (ArgumentException) { }

        string directory = Path.GetDirectoryName(target)!;
        string suffix = Guid.NewGuid().ToString("N");
        string staged = Path.Combine(directory, $".Tessera-update-{suffix}.exe");
        string backup = Path.Combine(directory, $".Tessera-backup-{suffix}.exe");
        try
        {
            File.Copy(source, staged);
            File.Replace(staged, target, backup);
            try
            {
                var launch = new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = directory };
                if (Process.Start(launch) is null) throw new IOException("Не удалось открыть обновлённую Tessera.");
            }
            catch
            {
                File.Replace(backup, target, null);
                throw;
            }
            try { File.Delete(backup); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }

    public static bool TryVersion(string tag, out Version version)
    {
        version = new Version(0, 0);
        return tag.StartsWith('v') && Version.TryParse(tag[1..], out version!) && version.Build >= 0 && version.Revision < 0;
    }

    private static Uri AssetUri(JsonElement asset, string tag, string name)
    {
        var uri = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "");
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com" ||
            uri.AbsoluteUri != AssetPrefix + Uri.EscapeDataString(tag) + "/" + name)
            throw new InvalidDataException("Некорректная ссылка на файл релиза.");
        return uri;
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool MatchesSha256(string path, string expected)
    {
        using var file = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(file));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Tessera-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
