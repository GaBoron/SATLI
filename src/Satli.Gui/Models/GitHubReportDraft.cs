namespace Satli_Gui.Models;

public sealed record GitHubReportDraft(
    string GameName,
    string AppId,
    string ErrorType,
    string Reason,
    string Reference);
