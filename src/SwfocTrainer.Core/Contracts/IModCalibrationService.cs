using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Exports calibration artifacts and computes compatibility reports for custom-mod promotion.
/// </summary>
public interface IModCalibrationService
{
    Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request, CancellationToken cancellationToken = default);

    Task<ModCompatibilityReport> BuildCompatibilityReportAsync(
        TrainerProfile profile,
        AttachSession? session,
        DependencyValidationResult? dependencyValidation = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog = null,
        CancellationToken cancellationToken = default);
}
