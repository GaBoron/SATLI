using System.Net;
using Satli_Gui.Services;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class UpdateServiceCheckTests
{
    [Fact]
    public async Task MapsLatestReleaseRedirectWithoutGithubApi()
    {
        using var client = new HttpClient(new StubHttpHandler(
            new Uri("https://github.com/GaBoron/SATLI/releases/tag/v0.3.0")));
        var service = new UpdateService(
            client,
            new Version(0, 2, 0),
            new Uri("https://example.invalid/latest"));

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.3.0", result.LatestVersion);
        Assert.Equal(
            "https://github.com/GaBoron/SATLI/releases/download/v0.3.0/SATLI-Setup-v0.3.0.exe",
            result.InstallerDownload?.AbsoluteUri);
        Assert.Contains("发现新版本", result.Message);
    }

    [Fact]
    public async Task ReportsCurrentVersion()
    {
        using var client = new HttpClient(new StubHttpHandler(
            new Uri("https://github.com/GaBoron/SATLI/releases/tag/v0.2.0")));
        var service = new UpdateService(
            client,
            new Version(0, 2, 0),
            new Uri("https://example.invalid/latest"));

        var result = await service.CheckAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("0.2.0", result.CurrentVersion);
        Assert.Contains("最新版本", result.Message);
    }

    [Fact]
    public async Task MapsForbiddenResponseToReadableMessage()
    {
        using var client = new HttpClient(new StubHttpHandler(
            new Uri("https://example.invalid/latest"),
            HttpStatusCode.Forbidden));
        var service = new UpdateService(
            client,
            new Version(0, 2, 0),
            new Uri("https://example.invalid/latest"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckAsync());

        Assert.Contains("请稍后重试", exception.Message);
    }

    private sealed class StubHttpHandler(
        Uri responseUri,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage
        {
            StatusCode = statusCode,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, responseUri),
            Content = new StringContent(string.Empty),
        });
    }
}
