using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ITransplantCompatibilityService
{
    Task<TransplantValidationReport> ValidateAsync(
        string targetProfileId,
        IReadOnlyList<string> activeWorkshopIds,
        IReadOnlyList<RosterEntityRecord> entities,
        CancellationToken cancellationToken);

    Task<TransplantValidationReport> ValidateAsync(
        string targetProfileId,
        IReadOnlyList<string> activeWorkshopIds,
        IReadOnlyList<RosterEntityRecord> entities)
    {
        return ValidateAsync(targetProfileId, activeWorkshopIds, entities, CancellationToken.None);
    }
}
