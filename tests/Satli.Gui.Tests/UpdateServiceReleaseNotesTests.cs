using System.Net;
using Satli_Gui.Services;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class UpdateServiceReleaseNotesTests
{
    private static readonly Uri LatestEndpoint = new("https://example.invalid/releases/latest");
    private static readonly Uri ApiEndpoint = new("https://api.example.invalid/releases/latest");
    private static readonly Uri FeedEndpoint = new("https://example.invalid/releases.atom");

    [Fact]
    public async Task PrefersApiMarkdownBeforeAtomFallback()
    {
        var apiRequests = 0;
        var feedRequests = 0;
        using var client = new HttpClient(new RoutingHttpHandler(request =>
        {
            if (request.RequestUri == LatestEndpoint)
            {
                return ReleaseRedirect();
            }
            if (request.RequestUri == ApiEndpoint)
            {
                apiRequests++;
                return JsonResponse(
                    """
                    {
                      "tag_name": "v1.1.0",
                      "html_url": "https://github.com/GaBoron/SATLI/releases/tag/v1.1.0",
                      "body": "## 修复\n\n- 正确渲染 **Markdown**",
                      "assets": []
                    }
                    """);
            }

            feedRequests++;
            return AtomResponse("订阅源不应覆盖 API Markdown");
        }));
        var service = CreateService(client);

        var result = await service.CheckAsync();

        Assert.Equal("## 修复\n\n- 正确渲染 **Markdown**", result.ReleaseNotes);
        Assert.Equal(1, apiRequests);
        Assert.Equal(0, feedRequests);
    }

    [Fact]
    public async Task FallsBackToAtomNotesWhenApiIsUnavailable()
    {
        var feedRequests = 0;
        using var client = new HttpClient(new RoutingHttpHandler(request =>
        {
            if (request.RequestUri == LatestEndpoint)
            {
                return ReleaseRedirect();
            }
            if (request.RequestUri == ApiEndpoint)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            feedRequests++;
            return AtomResponse("API 不可用时仍显示说明");
        }));
        var service = CreateService(client);

        var result = await service.CheckAsync();

        Assert.Contains("API 不可用时仍显示说明", result.ReleaseNotes);
        Assert.Equal(1, feedRequests);
    }

    [Fact]
    public async Task FallsBackFromRateLimitedPageToAtomFeed()
    {
        var latest = new Uri("https://example.invalid/releases/latest");
        var feed = new Uri("https://example.invalid/releases.atom");
        var api = new Uri("https://api.example.invalid/releases/latest");
        var atom = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <link rel="alternate" href="https://github.com/GaBoron/SATLI/releases/tag/v0.4.0" />
                <content type="html">&lt;h2&gt;修复&lt;/h2&gt;&lt;ul&gt;&lt;li&gt;改进更新检查回退&lt;/li&gt;&lt;/ul&gt;</content>
              </entry>
            </feed>
            """;
        using var client = new HttpClient(new RoutingHttpHandler(request =>
        {
            if (request.RequestUri == latest)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }
            if (request.RequestUri == feed)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(atom),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        var service = new UpdateService(
            client,
            new Version(0, 3, 0),
            latest,
            feedEndpoint: feed,
            apiEndpoint: api);

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.4.0", result.LatestVersion);
        Assert.Contains("改进更新检查回退", result.ReleaseNotes);
    }

    private static UpdateService CreateService(HttpClient client) => new(
        client,
        new Version(1, 0, 0),
        LatestEndpoint,
        feedEndpoint: FeedEndpoint,
        apiEndpoint: ApiEndpoint);

    private static HttpResponseMessage ReleaseRedirect() => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            "https://github.com/GaBoron/SATLI/releases/tag/v1.1.0"),
        Content = new StringContent(string.Empty),
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json),
    };

    private static HttpResponseMessage AtomResponse(string notes) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <link rel="alternate" href="https://github.com/GaBoron/SATLI/releases/tag/v1.1.0" />
                <content type="html">&lt;h2&gt;修复&lt;/h2&gt;&lt;p&gt;{{notes}}&lt;/p&gt;</content>
              </entry>
            </feed>
            """),
    };

    private sealed class RoutingHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = route(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
