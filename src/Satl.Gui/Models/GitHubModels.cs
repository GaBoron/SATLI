namespace Satl_Gui.Models;

public sealed record GitHubAccount(string Login, string AvatarUrl);

public sealed record GitHubDeviceChallenge(
    string DeviceCode,
    string UserCode,
    Uri VerificationUri,
    DateTimeOffset ExpiresAt,
    TimeSpan PollInterval);

public sealed record GitHubReportDraft(
    string GameName,
    string AppId,
    string StoreUrl,
    string ErrorType,
    string Reason,
    string Reference);
