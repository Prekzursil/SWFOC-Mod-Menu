using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Resolves operation capability state from fingerprint-mapped anchor requirements.
/// </summary>
public interface ICapabilityMapResolver
{
    Task<CapabilityResolutionResult> ResolveAsync(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        IReadOnlySet<string> resolvedAnchors);

    Task<CapabilityResolutionResult> ResolveAsync(
        BinaryFingerprint fingerprint,
        string requestedProfileId,
        string operationId,
        IReadOnlySet<string> resolvedAnchors,
        CancellationToken cancellationToken);

    Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint);

    Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken);
}
