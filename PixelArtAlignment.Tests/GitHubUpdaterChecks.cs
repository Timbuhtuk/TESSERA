using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using DomainColorTest;

namespace PixelArtAlignment.Tests;

public static class GitHubUpdaterChecks
{
    public static void Run(Action<string, Action> check)
    {
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
        check("GitHub release uses checksum file when asset digest is unavailable", () =>
        {
            string digest = new string('a', 64);
            using var http = new HttpClient(new ReplyHandler(uri => uri.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal)
                ? Json("""{"tag_name":"v1.0.42","html_url":"https://github.com/Timbuhtuk/TESSERA/releases/tag/v1.0.42","assets":[{"name":"Tessera.exe","browser_download_url":"https://github.com/Timbuhtuk/TESSERA/releases/download/v1.0.42/Tessera.exe","digest":null},{"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/Timbuhtuk/TESSERA/releases/download/v1.0.42/SHA256SUMS.txt"}]}""")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(digest + "  Tessera.exe\n") }));
            var release = GitHubUpdater.CheckAsync(http, new Version(1, 0, 41, 0)).GetAwaiter().GetResult();
            Require(release?.Sha256 == digest, "Fallback checksum was not read");
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
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class ReplyHandler(Func<Uri, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request.RequestUri!));
    }
}
