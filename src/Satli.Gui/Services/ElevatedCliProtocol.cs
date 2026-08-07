using System.Buffers.Binary;
using System.Text.Json;
using Satli_Gui.Serialization;

namespace Satli_Gui.Services;

internal static class ElevatedCliProtocol
{
    private const int MaximumMessageBytes = 16 * 1024 * 1024;

    public static Task WriteRequestAsync(Stream stream, CliInvocation request) =>
        WriteAsync(
            stream,
            JsonSerializer.SerializeToUtf8Bytes(
                request,
                SatliJsonSerializerContext.Default.CliInvocation));

    public static async Task<CliInvocation> ReadRequestAsync(Stream stream)
    {
        var payload = await ReadAsync(stream);
        return JsonSerializer.Deserialize(
            payload,
            SatliJsonSerializerContext.Default.CliInvocation)
            ?? throw new InvalidDataException("管理员工作进程收到空请求。");
    }

    public static Task WriteResponseAsync(Stream stream, ElevatedCliResponse response) =>
        WriteAsync(
            stream,
            JsonSerializer.SerializeToUtf8Bytes(
                response,
                SatliJsonSerializerContext.Default.ElevatedCliResponse));

    public static async Task<ElevatedCliResponse> ReadResponseAsync(Stream stream)
    {
        var payload = await ReadAsync(stream);
        return JsonSerializer.Deserialize(
            payload,
            SatliJsonSerializerContext.Default.ElevatedCliResponse)
            ?? throw new InvalidDataException("管理员工作进程返回空响应。");
    }

    private static async Task WriteAsync(Stream stream, byte[] payload)
    {
        if (payload.Length > MaximumMessageBytes)
        {
            throw new InvalidDataException("管理员工作进程消息超过安全大小上限。");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }

    private static async Task<byte[]> ReadAsync(Stream stream)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumMessageBytes)
        {
            throw new InvalidDataException("管理员工作进程消息长度无效。");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload);
        return payload;
    }
}
