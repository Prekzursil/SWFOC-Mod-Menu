using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ITelemetryLogTailService
{
    TelemetryModeResolution ResolveLatestMode(
        string? processPath,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow);

    HelperOperationVerification VerifyOperationToken(
        string? processPath,
        string operationToken,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow)
    {
        return HelperOperationVerification.Unavailable("helper_operation_verification_not_supported");
    }

    HelperAutoloadVerification VerifyAutoloadProfile(
        string? processPath,
        string? profileId,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow)
    {
        return HelperAutoloadVerification.Unavailable("helper_autoload_verification_not_supported");
    }
}
