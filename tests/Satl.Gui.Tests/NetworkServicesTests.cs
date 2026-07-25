using System.Net;
using System.Net.Sockets;
using System.Text;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class NetworkServicesTests
{
    [Fact]
    public void ValidatorAcceptsGeneralDesktopNetworkSettings()
    {
        var normalized = NetworkSettingsValidator.Normalize(new NetworkSettings
        {
            DnsMode = "custom",
            DnsServers = "1.1.1.1; [2606:4700:4700::1111]:53",
            ProxyMode = "manual",
            ProxyAddress = "http://127.0.0.1:7890",
            ProxyUsername = "user",
            ProxyPassword = "secret",
        });

        Assert.Equal("custom", normalized.DnsMode);
        Assert.Equal("manual", normalized.ProxyMode);
        Assert.Equal(2, NetworkSettingsValidator.ParseDnsServers(normalized.DnsServers).Count);
        Assert.Equal("secret", normalized.ProxyPassword);
    }

    [Fact]
    public void ValidatorRejectsProxyAddressWithoutScheme()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            NetworkSettingsValidator.Normalize(new NetworkSettings
            {
                ProxyMode = "manual",
                ProxyAddress = "127.0.0.1:7890",
            }));

        Assert.Contains("http://", exception.Message);
    }

    [Theory]
    [InlineData(SocketError.HostNotFound, "DNS")]
    [InlineData(SocketError.ConnectionRefused, "代理")]
    [InlineData(SocketError.TimedOut, "超时")]
    public void SocketErrorsUseUserFacingChinese(SocketError socketError, string expected)
    {
        var message = NetworkErrorMessage.Describe(
            new HttpRequestException("internal code error", new SocketException((int)socketError)),
            "测试连接");

        Assert.Contains(expected, message);
        Assert.DoesNotContain("internal code error", message);
    }

    [Fact]
    public void ProxyAuthenticationErrorExplainsWhatTheUserShouldCheck()
    {
        var message = NetworkErrorMessage.Describe(
            new HttpRequestException(
                "internal",
                null,
                HttpStatusCode.ProxyAuthenticationRequired),
            "下载");

        Assert.Contains("代理服务器需要身份验证", message);
        Assert.Contains("用户名和密码", message);
        Assert.DoesNotContain("internal", message);
    }

    [Fact]
    public async Task SettingsServiceEncryptsProxyPasswordAtRest()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"satl-network-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new GuiSettings
            {
                Network = new NetworkSettings
                {
                    ProxyMode = "manual",
                    ProxyAddress = "http://127.0.0.1:7890",
                    ProxyUsername = "user",
                    ProxyPassword = "top-secret",
                },
            });

            var serialized = await File.ReadAllTextAsync(path);
            var loaded = await service.LoadAsync();

            Assert.DoesNotContain("top-secret", serialized);
            Assert.DoesNotContain("ProxyBypass", serialized);
            Assert.DoesNotContain("ConnectTimeout", serialized);
            Assert.DoesNotContain("DnsTimeout", serialized);
            Assert.Equal("top-secret", loaded.Network.ProxyPassword);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SettingsServiceEncryptsSteamWebApiKeyAtRest()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"satl-steam-api-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        const string apiKey = "0123456789abcdef0123456789abcdef";
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new GuiSettings
            {
                SteamLibrary = new SteamLibrarySettings
                {
                    Enabled = true,
                    SteamId = "76561198000000000",
                    ApiKey = apiKey,
                },
            });

            var serialized = await File.ReadAllTextAsync(path);
            var loaded = await service.LoadAsync();

            Assert.DoesNotContain(apiKey, serialized);
            Assert.Contains("ProtectedApiKey", serialized);
            Assert.True(loaded.SteamLibrary.Enabled);
            Assert.Equal("76561198000000000", loaded.SteamLibrary.SteamId);
            Assert.Equal(apiKey, loaded.SteamLibrary.ApiKey);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SteamLibraryValidatorRequiresCompleteCredentialsForUse()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            SteamLibrarySettingsValidator.RequireConfigured(new SteamLibrarySettings
            {
                Enabled = true,
                SteamId = "not-a-steam-id",
                ApiKey = "short",
            }));

        Assert.Contains("SteamID64", exception.Message);
        Assert.False(SteamLibrarySettingsValidator.IsConfigured(new SteamLibrarySettings()));
        Assert.True(SteamLibrarySettingsValidator.IsConfigured(new SteamLibrarySettings
        {
            SteamId = "76561198000000000",
            ApiKey = "0123456789abcdef0123456789abcdef",
        }));
    }

    [Fact]
    public async Task SteamWebApiProbeReadsGameCount()
    {
        Uri? requestedUri = null;
        var service = new SteamWebApiProbeService(_ =>
            new HttpClient(new StubHandler(request =>
            {
                requestedUri = request.RequestUri;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"response":{"game_count":42,"games":[]}}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            })));

        var result = await service.TestAsync(
            new SteamLibrarySettings
            {
                Enabled = true,
                SteamId = "76561198000000000",
                ApiKey = "0123456789abcdef0123456789abcdef",
            },
            new NetworkSettings());

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.GameCount);
        Assert.Contains("42", result.Message);
        Assert.Contains("include_appinfo=false", requestedUri?.Query);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
