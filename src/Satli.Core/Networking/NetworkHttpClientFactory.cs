using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Satli.Core.Networking;

public sealed record DnsServerEndpoint(IPAddress Address, int Port);

public static class NetworkHttpClientFactory
{
    public static HttpClient Create(IReadOnlyDictionary<string, string> environment)
    {
        var proxyMode = environment.GetValueOrDefault("SATLI_PROXY_MODE") ?? "system";
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseProxy = proxyMode != "direct",
        };
        if (proxyMode == "manual")
        {
            var address = environment.GetValueOrDefault("SATLI_PROXY_ADDRESS");
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
                throw new UsageException("代理地址必须是完整的 http:// 或 https:// 地址");
            var proxy = new WebProxy(uri);
            if (environment.GetValueOrDefault("SATLI_PROXY_USERNAME") is { Length: > 0 } user)
                proxy.Credentials = new NetworkCredential(
                    user,
                    environment.GetValueOrDefault("SATLI_PROXY_PASSWORD"));
            handler.Proxy = proxy;
        }
        if (environment.GetValueOrDefault("SATLI_DNS_MODE") == "custom")
        {
            var resolver = new CustomDnsResolver(
                ParseDnsServers(environment.GetValueOrDefault("SATLI_DNS_SERVERS") ?? ""),
                TimeSpan.FromSeconds(5));
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                Exception? lastError = null;
                foreach (var address in await resolver.ResolveAsync(
                    context.DnsEndPoint.Host,
                    cancellationToken))
                {
                    var socket = new Socket(
                        address.AddressFamily,
                        SocketType.Stream,
                        ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(
                            new IPEndPoint(address, context.DnsEndPoint.Port),
                            cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception exception) when (
                        exception is SocketException or OperationCanceledException)
                    {
                        lastError = exception;
                        socket.Dispose();
                        if (exception is OperationCanceledException) throw;
                    }
                }
                throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
            };
        }
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
    }

    private static IReadOnlyList<DnsServerEndpoint> ParseDnsServers(string value)
    {
        var result = new List<DnsServerEndpoint>();
        foreach (var item in value.Split(
            [';', ',', '\r', '\n'],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (IPAddress.TryParse(item, out var address))
            {
                result.Add(new DnsServerEndpoint(address, 53));
                continue;
            }
            if (Uri.TryCreate($"dns://{item}", UriKind.Absolute, out var endpoint)
                && IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out address)
                && endpoint.Port is >= 1 and <= 65535)
            {
                result.Add(new DnsServerEndpoint(address, endpoint.Port));
                continue;
            }
            throw new UsageException($"DNS 服务器“{item}”无效");
        }
        return result.Count > 0
            ? result
            : throw new UsageException("自定义 DNS 至少需要填写一个服务器地址");
    }
}

internal sealed class CustomDnsResolver(
    IReadOnlyList<DnsServerEndpoint> servers,
    TimeSpan timeout)
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal)) return [literal];
        foreach (var server in servers)
        {
            var addresses = new List<IPAddress>();
            foreach (var type in new ushort[] { 1, 28 })
            {
                try
                {
                    addresses.AddRange(
                        await QueryAsync(host, type, server, cancellationToken));
                }
                catch (Exception exception) when (
                    exception is SocketException or TimeoutException or InvalidDataException)
                {
                }
            }
            if (addresses.Count > 0) return addresses.Distinct().ToArray();
        }
        throw new SocketException((int)SocketError.HostNotFound);
    }

    private async Task<IReadOnlyList<IPAddress>> QueryAsync(
        string host,
        ushort recordType,
        DnsServerEndpoint server,
        CancellationToken cancellationToken)
    {
        var id = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        var query = BuildQuery(host, recordType, id);
        using var socket = new Socket(
            server.Address.AddressFamily,
            SocketType.Dgram,
            ProtocolType.Udp);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            await socket.SendToAsync(
                query,
                SocketFlags.None,
                new IPEndPoint(server.Address, server.Port),
                linked.Token);
            var buffer = new byte[4096];
            EndPoint source = server.Address.AddressFamily == AddressFamily.InterNetwork
                ? new IPEndPoint(IPAddress.Any, 0)
                : new IPEndPoint(IPAddress.IPv6Any, 0);
            var received = await socket.ReceiveFromAsync(
                buffer,
                SocketFlags.None,
                source,
                linked.Token);
            return Parse(buffer.AsSpan(0, received.ReceivedBytes), id);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"DNS 服务器 {server.Address} 响应超时");
        }
    }

    private static byte[] BuildQuery(string host, ushort recordType, ushort id)
    {
        using var stream = new MemoryStream();
        foreach (var value in new ushort[] { id, 0x0100, 1, 0, 0, 0 })
            WriteUInt16(stream, value);
        foreach (var label in new IdnMapping().GetAscii(host.TrimEnd('.')).Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new InvalidDataException("DNS 主机名无效");
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        stream.WriteByte(0);
        WriteUInt16(stream, recordType);
        WriteUInt16(stream, 1);
        return stream.ToArray();
    }

    private static IReadOnlyList<IPAddress> Parse(ReadOnlySpan<byte> data, ushort id)
    {
        if (data.Length < 12 || ReadUInt16(data, 0) != id)
            throw new InvalidDataException("DNS 响应无效");
        if ((ReadUInt16(data, 2) & 0x000F) != 0)
            throw new SocketException((int)SocketError.HostNotFound);
        var questions = ReadUInt16(data, 4);
        var answers = ReadUInt16(data, 6);
        var offset = 12;
        for (var index = 0; index < questions; index++)
        {
            SkipName(data, ref offset);
            Require(data, offset, 4);
            offset += 4;
        }
        var result = new List<IPAddress>();
        for (var index = 0; index < answers; index++)
        {
            SkipName(data, ref offset);
            Require(data, offset, 10);
            var type = ReadUInt16(data, offset);
            var recordClass = ReadUInt16(data, offset + 2);
            var length = ReadUInt16(data, offset + 8);
            offset += 10;
            Require(data, offset, length);
            if (recordClass == 1 && ((type == 1 && length == 4) || (type == 28 && length == 16)))
                result.Add(new IPAddress(data.Slice(offset, length)));
            offset += length;
        }
        return result;
    }

    private static void SkipName(ReadOnlySpan<byte> data, ref int offset)
    {
        while (true)
        {
            Require(data, offset, 1);
            var length = data[offset++];
            if (length == 0) return;
            if ((length & 0xC0) == 0xC0)
            {
                Require(data, offset, 1);
                offset++;
                return;
            }
            if ((length & 0xC0) != 0) throw new InvalidDataException("DNS 名称无效");
            Require(data, offset, length);
            offset += length;
        }
    }
    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        Require(data, offset, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    }
    private static void Require(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException("DNS 响应不完整");
    }
    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
