using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class UpdateServiceDownloadTests
{
    [Fact]
    public async Task DownloadsInstallerWithoutGithubAssetDigest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-update-download-{Guid.NewGuid():N}");
        var installerBytes = "installer"u8.ToArray();
        using var client = ClientFor(ReleaseJson(), installerBytes);
        try
        {
            var service = Service(client, root);

            var update = await service.CheckAsync();
            var installer = await service.DownloadInstallerAsync(update);

            Assert.Contains("修复刷新问题", update.ReleaseNotes);
            Assert.Equal(installerBytes, await File.ReadAllBytesAsync(installer));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task IgnoresGithubAssetDigest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satli-update-digest-ignored-{Guid.NewGuid():N}");
        var installerBytes = "installer"u8.ToArray();
        using var client = ClientFor(ReleaseJson(new string('0', 64)), installerBytes);
        try
        {
            var service = Service(client, root);
            var update = await service.CheckAsync();
            var installer = await service.DownloadInstallerAsync(update);

            Assert.Equal(installerBytes, await File.ReadAllBytesAsync(installer));
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

    private static string ReleaseJson(string? hash = null)
    {
        var digest = hash is null ? string.Empty : $",\"digest\":\"sha256:{hash}\"";
        return $$"""
            {
              "tag_name": "v0.3.0",
              "html_url": "https://github.com/GaBoron/SATLI/releases/tag/v0.3.0",
              "body": "## 修复\n- 修复刷新问题",
              "assets": [
                {"name":"SATLI-Setup-v0.3.0.exe","browser_download_url":"https://example.invalid/SATLI-Setup-v0.3.0.exe"{{digest}}}
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
