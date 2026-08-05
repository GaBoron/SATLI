using System.Security.Cryptography;
using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class UpdateServiceDigestTests
{
    [Fact]
    public async Task DownloadsInstallerVerifiedByGithubAssetDigest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-update-digest-{Guid.NewGuid():N}");
        var installerBytes = "verified-installer"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(installerBytes)).ToLowerInvariant();
        using var client = ClientFor(ReleaseJson(hash), installerBytes);
        try
        {
            var service = Service(client, root);

            var update = await service.CheckAsync();
            var installer = await service.DownloadInstallerAsync(update);

            Assert.Contains("修复刷新问题", update.ReleaseNotes);
            Assert.Equal(hash, update.InstallerSha256);
            Assert.Equal(installerBytes, await File.ReadAllBytesAsync(installer));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RefusesInstallerWhenGithubDigestIsMissing()
    {
        using var client = ClientFor(ReleaseJson(null), "installer"u8.ToArray());
        var service = Service(client, Path.GetTempPath());

        var update = await service.CheckAsync();

        Assert.Null(update.InstallerSha256);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadInstallerAsync(update));
    }

    [Fact]
    public async Task DeletesPartialInstallerWhenGithubDigestDoesNotMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-update-mismatch-{Guid.NewGuid():N}");
        var installerBytes = "tampered-installer"u8.ToArray();
        using var client = ClientFor(ReleaseJson(new string('0', 64)), installerBytes);
        try
        {
            var service = Service(client, root);
            var update = await service.CheckAsync();

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.DownloadInstallerAsync(update));

            Assert.Empty(Directory.GetFiles(root));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static UpdateService Service(HttpClient client, string updateDirectory) => new(
        client,
        new Version(0, 2, 0),
        new Uri("https://example.invalid/latest"),
        updateDirectory);

    private static HttpClient ClientFor(string releaseJson, byte[] installerBytes) => new(
        new RoutingHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".exe", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(installerBytes),
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson),
            };
        }));

    private static string ReleaseJson(string? hash)
    {
        var digest = hash is null ? string.Empty : $",\"digest\":\"sha256:{hash}\"";
        return $$"""
            {
              "tag_name": "v0.3.0",
              "html_url": "https://github.com/GaBoron/steam-achievement-translation-installer/releases/tag/v0.3.0",
              "body": "## 修复\n- 修复刷新问题",
              "assets": [
                {"name":"SATLInstaller-Setup-v0.3.0.exe","browser_download_url":"https://example.invalid/SATLInstaller-Setup-v0.3.0.exe"{{digest}}}
              ]
            }
            """;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class RoutingHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = route(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
