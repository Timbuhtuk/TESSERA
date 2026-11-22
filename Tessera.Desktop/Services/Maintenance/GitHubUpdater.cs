using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tessera.Desktop;

public sealed record TesseraRelease(Version Version, string Tag, Uri Page, Uri Download, string Sha256);

public static class GitHubUpdater
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Timbuhtuk/TESSERA/releases/latest";
    private const string ReleasesUrl = "https://github.com/Timbuhtuk/TESSERA/releases/";
    private const string ReleaseTagPath = "/Timbuhtuk/TESSERA/releases/tag/";
    private const string AssetPrefix = ReleasesUrl + "download/";
    private static readonly HttpClient Client = CreateClient();
    private static long _apiRetryAfterUtcTicks;

    public static Version CurrentVersion => typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static bool CanInstall
    {
        get
        {
            string? path = Environment.ProcessPath;
            return path is not null && CanInstallFrom(path, typeof(App).Assembly.Location,
                FileVersionInfo.GetVersionInfo(path).ProductName);
        }
    }

    // The application assembly has no Location when embedded in the released single-file executable.
    internal static bool CanInstallFrom(string? path, string assemblyLocation, string? productName) =>
        string.IsNullOrEmpty(assemblyLocation) && IsTesseraExecutable(path, productName);

    private static bool IsTesseraExecutable(string? path, string? productName) =>
        !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(productName, "Tessera", StringComparison.OrdinalIgnoreCase);

    public static async Task<TesseraRelease?> CheckAsync(HttpClient? client = null, Version? currentVersion = null)
    {
        var http = client ?? Client;
        Version installed = currentVersion ?? CurrentVersion;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        if (client is null && DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref _apiRetryAfterUtcTicks))
            return await CheckReleasePageAsync(http, installed, timeout.Token);

        using var response = await http.GetAsync(LatestReleaseApi, timeout.Token);
        if (await IsApiRateLimitedAsync(response, timeout.Token))
        {
            if (client is null) DeferApiChecks(response);
            return await CheckReleasePageAsync(http, installed, timeout.Token);
        }
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var root = json.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryVersion(tag, out var version) || version <= installed) return null;
        var page = new Uri(root.GetProperty("html_url").GetString() ?? "");
        if (page.Scheme != Uri.UriSchemeHttps || page.Host != "github.com" ||
            !page.AbsolutePath.StartsWith(ReleaseTagPath, StringComparison.Ordinal))
            throw new InvalidDataException(Localization.Text("InvalidGithubReleaseURL"));

        JsonElement? exe = null;
        JsonElement? checksums = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            if (name == "Tessera.exe") exe = asset;
            if (name == "SHA256SUMS.txt") checksums = asset;
        }
        if (exe is null) throw new InvalidDataException(Localization.Text("TheReleaseHasNoTesseraExe"));
        var download = AssetUri(exe.Value, tag, "Tessera.exe");
        string? digest = exe.Value.TryGetProperty("digest", out var value) ? value.GetString() : null;
        // TESSERA_LEGACY Verify releases without a GitHub asset digest using the published SHA256SUMS.txt.
        if (digest is null && checksums is not null)
        {
            var checksumUri = AssetUri(checksums.Value, tag, "SHA256SUMS.txt");
            digest = await ReadExecutableChecksumAsync(http, checksumUri, timeout.Token);
        }
        return new TesseraRelease(version, tag, page, download, ValidateChecksum(digest));
    }

    private static async Task<bool> IsApiRateLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests) return true;
        if (response.StatusCode != HttpStatusCode.Forbidden) return false;
        if (response.Headers.RetryAfter is not null ||
            (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.Contains("0")) ||
            response.ReasonPhrase?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true) return true;

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeferApiChecks(HttpResponseMessage response)
    {
        var now = DateTimeOffset.UtcNow;
        var retryAfter = response.Headers.RetryAfter;
        var nextCheck = retryAfter?.Date ?? now.Add(retryAfter?.Delta ?? TimeSpan.FromMinutes(1));
        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.Contains("0") &&
            response.Headers.TryGetValues("x-ratelimit-reset", out var reset) &&
            long.TryParse(reset.FirstOrDefault(), out long resetSeconds) && resetSeconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds() &&
            resetSeconds > nextCheck.ToUnixTimeSeconds())
            nextCheck = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);

        Interlocked.Exchange(ref _apiRetryAfterUtcTicks, nextCheck.UtcTicks);
    }

    private static async Task<TesseraRelease?> CheckReleasePageAsync(HttpClient http, Version installed, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl + "latest");
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = response.RequestMessage?.RequestUri;
        if (page is null || page.Scheme != Uri.UriSchemeHttps || page.Host != "github.com" || !page.IsDefaultPort ||
            !page.AbsolutePath.StartsWith(ReleaseTagPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Localization.Text("InvalidGithubReleaseURL"));

        string tag = page.AbsolutePath[ReleaseTagPath.Length..];
        if (!TryVersion(tag, out var version) || version <= installed) return null;

        string assets = AssetPrefix + Uri.EscapeDataString(tag) + "/";
        var checksumUri = new Uri(assets + "SHA256SUMS.txt");
        string? digest = await ReadExecutableChecksumAsync(http, checksumUri, cancellationToken);
        return new TesseraRelease(version, tag, page, new Uri(assets + "Tessera.exe"), ValidateChecksum(digest));
    }

    private static async Task<string?> ReadExecutableChecksumAsync(HttpClient http, Uri uri, CancellationToken cancellationToken)
    {
        string content = await http.GetStringAsync(uri, cancellationToken);
        if (content.Length > 65536) throw new InvalidDataException(Localization.Text("ChecksumFileIsTooLarge"));
        return content.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.EndsWith("  Tessera.exe", StringComparison.Ordinal))?.Split(' ', 2)[0];
    }

    private static string ValidateChecksum(string? digest)
    {
        if (digest is null) throw new InvalidDataException(Localization.Text("ReleaseHasNoSHA256Tessera"));
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) digest = digest[7..];
        if (!IsSha256(digest)) throw new InvalidDataException(Localization.Text("InvalidReleaseChecksum"));
        return digest.ToLowerInvariant();
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
                throw new InvalidDataException(Localization.Text("UpdateFileIsTooLarge"));
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[131072];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > 500_000_000) throw new InvalidDataException(Localization.Text("UpdateFileIsTooLarge"));
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }
            if (new FileInfo(path).Length is 0 or > 500_000_000 || !MatchesSha256(path, release.Sha256))
                throw new InvalidDataException(Localization.Text("UpdateChecksumMismatch"));
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
        if (!CanInstall) throw new InvalidOperationException(Localization.Text("AutomaticInstallationRequiresSingleFileTessera"));
        string target = Environment.ProcessPath!;
        string directory = Path.GetDirectoryName(target)!;
        string probe = Path.Combine(directory, $".Tessera-write-check-{Guid.NewGuid():N}");
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        var start = new ProcessStartInfo(downloadedPath) { UseShellExecute = false };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(target);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add(digest);
        if (Process.Start(start) is null) throw new IOException(Localization.Text("CouldNotStartTheUpdateInstaller"));
    }

    public static void ApplyUpdate(string target, int processId, string digest)
    {
        string source = Environment.ProcessPath ?? throw new IOException(Localization.Text("UpdateFileNotFound"));
        if (!IsSha256(digest) || !MatchesSha256(source, digest))
            throw new InvalidDataException(Localization.Text("UpdateChecksumMismatch"));
        target = Path.GetFullPath(target);
        if (!File.Exists(target) || Path.GetFullPath(source).Equals(target, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException(Localization.Text("InstalledTesseraExecutableNotFound"), target);
        if (!IsTesseraExecutable(target, FileVersionInfo.GetVersionInfo(target).ProductName))
            throw new InvalidDataException(Localization.Text("InvalidApplicationPath"));

        try
        {
            using var oldProcess = Process.GetProcessById(processId);
            if (!oldProcess.WaitForExit(30000)) throw new TimeoutException(Localization.Text("ApplicationDidNotCloseWithin30"));
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
                if (Process.Start(launch) is null) throw new IOException(Localization.Text("CouldNotOpenTheUpdatedTessera"));
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
            throw new InvalidDataException(Localization.Text("InvalidReleaseFileURL"));
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
