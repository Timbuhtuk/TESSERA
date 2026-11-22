using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Tessera.Desktop;

namespace Tessera.RegressionTests;

public static class GitHubUpdaterChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Renamed single-file Tessera can update while development builds cannot", () =>
        {
            Require(GitHubUpdater.CanInstallFrom(@"C:\Apps\Tessera (1).exe", "", "Tessera"), "Renamed release was rejected");
            Require(GitHubUpdater.CanInstallFrom(@"C:\Apps\custom-name.exe", "", "Tessera"), "Custom release name was rejected");
            Require(!GitHubUpdater.CanInstallFrom(@"C:\Apps\Tessera.Desktop.exe", @"C:\Apps\Tessera.Desktop.dll", "Tessera"), "Development build was accepted");
            Require(!GitHubUpdater.CanInstallFrom(@"C:\Apps\Other.exe", "", "Other"), "Unrelated executable was accepted");
            Require(!GitHubUpdater.CanInstallFrom(@"C:\Apps\Tessera.dll", "", "Tessera"), "DLL launch was accepted");
        });
        check("GitHub updater crosses major versions and skips installed or newer builds", () =>
        {
            foreach (var (current, tag, expected) in new[] {
                (new Version(1, 0, 57, 0), "v2.0.0", true),
                (new Version(2, 0, 0, 0), "v2.0.0", false),
                (new Version(2, 0, 1, 0), "v2.0.0", false),
                (new Version(2, 0, 0, 0), "v2.0.1", true) })
            {
                using var http = new HttpClient(new ReplyHandler(_ => Json($$"""{"tag_name":"{{tag}}","html_url":"https://github.com/Timbuhtuk/TESSERA/releases/tag/{{tag}}","assets":[{"name":"Tessera.exe","browser_download_url":"https://github.com/Timbuhtuk/TESSERA/releases/download/{{tag}}/Tessera.exe","digest":"sha256:{{new string('a', 64)}}"}]}""")));
                var release = GitHubUpdater.CheckAsync(http, current).GetAwaiter().GetResult();
                Require((release is not null) == expected, $"Incorrect update decision: {current} -> {tag}");
            }
        });
        check("GitHub release version and verified download", () =>
        {
            byte[] executable = Encoding.UTF8.GetBytes("standalone test executable");
            string digest = Convert.ToHexString(SHA256.HashData(executable)).ToLowerInvariant();
            using var http = new HttpClient(new ReplyHandler(uri => uri.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal)
                ? Json($$"""{"tag_name":"v1.0.42","html_url":"https://github.com/Timbuhtuk/TESSERA/releases/tag/v1.0.42","assets":[{"name":"Tessera.exe","browser_download_url":"https://github.com/Timbuhtuk/TESSERA/releases/download/v1.0.42/Tessera.exe","digest":"sha256:{{digest}}"}]}""")
                : Bytes(executable)));
            var release = GitHubUpdater.CheckAsync(http, new Version(1, 0, 41, 0)).GetAwaiter().GetResult();
            if (release is null || release.Tag != "v1.0.42") throw new Exception("New release was not found");
            string folder = Path.Combine(Path.GetTempPath(), "TesseraUpdaterChecks", Guid.NewGuid().ToString("N"));
            try
            {
                string path = GitHubUpdater.DownloadAsync(release, http, folder).GetAwaiter().GetResult();
                Require(File.ReadAllBytes(path).SequenceEqual(executable), "Verified download changed content");
                var invalid = release with { Sha256 = new string('0', 64) };
                try
                {
                    GitHubUpdater.DownloadAsync(invalid, http, folder).GetAwaiter().GetResult();
                    throw new Exception("Wrong digest was accepted");
                }
                catch (InvalidDataException) { }
                Require(Directory.GetFiles(folder).Length == 1, "Rejected download was not removed");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });
        check("GitHub release skips installed version", () =>
        {
            using var http = new HttpClient(new ReplyHandler(_ => Json("""{"tag_name":"v1.0.42"}""")));
            var release = GitHubUpdater.CheckAsync(http, new Version(1, 0, 42, 0)).GetAwaiter().GetResult();
            Require(release is null, "Installed version was offered again");
        });
        check("GitHub release requires the asset digest in the API response", () =>
        {
            using var http = new HttpClient(new ReplyHandler(uri => uri.Host == "api.github.com"
                ? Json("""{"tag_name":"v1.0.42","html_url":"https://github.com/Timbuhtuk/TESSERA/releases/tag/v1.0.42","assets":[{"name":"Tessera.exe","browser_download_url":"https://github.com/Timbuhtuk/TESSERA/releases/download/v1.0.42/Tessera.exe","digest":null},{"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/Timbuhtuk/TESSERA/releases/download/v1.0.42/SHA256SUMS.txt"}]}""")
                : throw new Exception("Unexpected checksum request")));
            try
            {
                GitHubUpdater.CheckAsync(http, new Version(1, 0, 41, 0)).GetAwaiter().GetResult();
                throw new Exception("A release without an asset digest was accepted.");
            }
            catch (InvalidDataException) { }
        });
        check("GitHub API rate limits fall back to the public release and its versioned checksum", () =>
        {
            const string tag = "v2.0.42";
            string digest = new string('a', 64);
            var rateLimits = new[]
            {
                new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = "rate limit exceeded" },
                new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("You have exceeded a secondary rate limit.") },
                new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            };
            rateLimits[2].Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            foreach (var rateLimit in rateLimits)
            {
                int apiCalls = 0, pageCalls = 0, checksumCalls = 0;
                using var http = new HttpClient(new ReplyHandler(uri =>
                {
                    if (uri.Host == "api.github.com") { apiCalls++; return rateLimit; }
                    if (uri.AbsoluteUri == "https://github.com/Timbuhtuk/TESSERA/releases/latest")
                    { pageCalls++; return ReleasePage(tag); }
                    if (uri.AbsoluteUri == $"https://github.com/Timbuhtuk/TESSERA/releases/download/{tag}/SHA256SUMS.txt")
                    { checksumCalls++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(digest + "  Tessera.exe\n") }; }
                    throw new Exception($"Unexpected request: {uri}");
                }));
                var release = GitHubUpdater.CheckAsync(http, new Version(2, 0, 41, 0)).GetAwaiter().GetResult();
                Require(release?.Tag == tag && release.Sha256 == digest &&
                    release.Download.AbsoluteUri == $"https://github.com/Timbuhtuk/TESSERA/releases/download/{tag}/Tessera.exe", "Fallback release details are incorrect");
                Require(apiCalls == 1 && pageCalls == 1 && checksumCalls == 1, "Fallback made unexpected requests");
            }
        });
        check("The public release fallback skips installed versions and rejects invalid checksums", () =>
        {
            using var http = new HttpClient(new ReplyHandler(uri =>
            {
                if (uri.Host == "api.github.com")
                {
                    var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
                    response.Headers.Add("x-ratelimit-remaining", "0");
                    return response;
                }
                if (uri.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal)) return ReleasePage("v2.0.42");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid  Tessera.exe\n") };
            }));
            Require(GitHubUpdater.CheckAsync(http, new Version(2, 0, 42, 0)).GetAwaiter().GetResult() is null, "Installed version was offered again");
            try
            {
                GitHubUpdater.CheckAsync(http, new Version(2, 0, 41, 0)).GetAwaiter().GetResult();
                throw new Exception("Invalid public release checksum was accepted");
            }
            catch (InvalidDataException) { }
        });
        check("GitHub release rejects download links outside the expected repository", () =>
        {
            using var http = new HttpClient(new ReplyHandler(_ =>
                Json("""{"tag_name":"v1.0.42","html_url":"https://github.com/Timbuhtuk/TESSERA/releases/tag/v1.0.42","assets":[{"name":"Tessera.exe","browser_download_url":"https://github.com/other/repository/releases/download/v1.0.42/Tessera.exe"}]}""")));
            try
            {
                GitHubUpdater.CheckAsync(http, new Version(1, 0, 41, 0)).GetAwaiter().GetResult();
                throw new Exception("Unexpected download location was accepted");
            }
            catch (InvalidDataException) { }
        });
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
    private static HttpResponseMessage ReleasePage(string tag) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Get, $"https://github.com/Timbuhtuk/TESSERA/releases/tag/{tag}")
    };
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class ReplyHandler(Func<Uri, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request.RequestUri!));
    }
}
