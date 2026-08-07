using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed record NetworkProbeResult(bool IsSuccess, string Message);

public sealed class NetworkProbeService
{
    private static readonly Uri UpdateEndpoint = new(
        "https://github.com/GaBoron/SATLI/releases/latest");

    public async Task<NetworkProbeResult> TestAsync(
        NetworkSettings settings,
        DownloadSourceSettings? downloadSources = null,
        CancellationToken cancellationToken = default)
    {
        using var client = NetworkHttpClientFactory.Create(settings);
        var catalogResult = await TestCatalogSourcesAsync(
            client,
            downloadSources,
            cancellationToken);
        if (!catalogResult.IsSuccess)
        {
            return catalogResult;
        }

        try
        {
            await ProbeAsync(client, UpdateEndpoint, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            return new NetworkProbeResult(
                false,
                NetworkErrorMessage.Describe(exception, "连接软件更新服务"));
        }
        return new NetworkProbeResult(
            true,
            $"网络连接正常：翻译目录（{catalogResult.Message}）和软件更新服务均可访问。");
    }

    private static async Task<NetworkProbeResult> TestCatalogSourcesAsync(
        HttpClient client,
        DownloadSourceSettings? downloadSources,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var endpoint in DownloadSourceCatalog.CatalogEndpoints(downloadSources))
        {
            try
            {
                await ProbeAsync(client, endpoint, cancellationToken);
                return new NetworkProbeResult(true, endpoint.Host);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                lastError = exception;
            }
        }
        return new NetworkProbeResult(
            false,
            NetworkErrorMessage.Describe(
                lastError ?? new HttpRequestException("没有可用的索引下载源。"),
                "连接翻译目录"));
    }

    private static async Task ProbeAsync(
        HttpClient client,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.ParseAdd("SATLInstaller/NetworkTest");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();
    }
}
