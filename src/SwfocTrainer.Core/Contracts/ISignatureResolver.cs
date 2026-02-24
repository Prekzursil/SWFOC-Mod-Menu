using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ISignatureResolver
{
    Task<SymbolMap> ResolveAsync(
        ProfileBuild profileBuild,
        IReadOnlyList<SignatureSet> signatureSets,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        CancellationToken cancellationToken);

    Task<SymbolMap> ResolveAsync(
        ProfileBuild profileBuild,
        IReadOnlyList<SignatureSet> signatureSets,
        IReadOnlyDictionary<string, long> fallbackOffsets)
    {
        return ResolveAsync(profileBuild, signatureSets, fallbackOffsets, CancellationToken.None);
    }
}
