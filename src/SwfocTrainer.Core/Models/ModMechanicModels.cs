namespace SwfocTrainer.Core.Models;

public sealed record ModMechanicSupport(
    string ActionId,
    bool Supported,
    RuntimeReasonCode ReasonCode,
    string Message,
    double Confidence = 0.50d);

public sealed record ModMechanicReport(
    string ProfileId,
    DateTimeOffset GeneratedAtUtc,
    bool DependenciesSatisfied,
    bool HelperBridgeReady,
    IReadOnlyList<ModMechanicSupport> ActionSupport,
    IReadOnlyDictionary<string, object?> Diagnostics)
{
    public static ModMechanicReport Empty(string profileId) =>
        new(
            ProfileId: profileId,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: false,
            HelperBridgeReady: false,
            ActionSupport: Array.Empty<ModMechanicSupport>(),
            Diagnostics: new Dictionary<string, object?>());
}
