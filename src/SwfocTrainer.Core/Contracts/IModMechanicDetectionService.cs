using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IModMechanicDetectionService
{
    Task<ModMechanicReport> DetectAsync(
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        CancellationToken cancellationToken);

    Task<ModMechanicReport> DetectAsync(
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
    {
        return DetectAsync(profile, session, catalog, CancellationToken.None);
    }
}
