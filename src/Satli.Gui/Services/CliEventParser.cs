using System.Text.Json;
using Satli_Gui.Models;
using Satli_Gui.Serialization;

namespace Satli_Gui.Services;

internal static class CliEventParser
{
    public static SatliEvent Parse(string line)
    {
        SatliEvent parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(
                line,
                SatliJsonSerializerContext.Default.SatliEvent)
                ?? throw new InvalidDataException("SATLI 返回了空事件。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"SATLI 返回了无效事件：{line}", exception);
        }

        if (parsed.ProtocolVersion != 1)
        {
            throw new InvalidDataException($"不支持的 SATLI GUI 协议版本：{parsed.ProtocolVersion}");
        }
        return parsed;
    }
}
