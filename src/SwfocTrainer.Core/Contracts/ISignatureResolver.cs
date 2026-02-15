using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ISignatureResolver
{
    Task<SymbolMap> ResolveAsync(
        ProfileBuild profileBuild,
        IReadOnlyList<SignatureSet> signatureSets,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        CancellationToken cancellationToken = default);
}
