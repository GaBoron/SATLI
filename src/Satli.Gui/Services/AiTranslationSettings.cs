using System.Text;
using Satli.Core.TranslationProjects;
using Satli_Gui.Models;

namespace Satli_Gui.Services;

internal static class AiTranslationSettings
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SATLI.AiApiKey.v1");
    public static string Protect(string key) => ProtectedDataMigration.Protect(key, Entropy);
    public static string Key(AiSettings settings) => ProtectedDataMigration.Unprotect(settings.ProtectedApiKey, Entropy).Value;
    public static AiTranslationOptions Options(AiSettings settings) => new(settings.Protocol,
        settings.Endpoint.Trim(), settings.Model.Trim(), settings.StructuredOutput, settings.WebSearch);
}
