using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Exports calibration artifacts and computes compatibility reports for custom-mod promotion.
/// </summary>
public interface IModCalibrationService
{
    Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request, CancellationToken cancellationToken);

    Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request)
    {
        return ExportCalibrationArtifactAsync(request, CancellationToken.None);
    }

    Task<ModCompatibilityReport> BuildCompatibilityReportAsync(
        TrainerProfile profile,
        AttachSession? session,
        DependencyValidationResult? dependencyValidation,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        CancellationToken cancellationToken);

    Task<ModCompatibilityReport> BuildCompatibilityReportAsync(
        TrainerProfile profile,
        AttachSession? session)
    {
        return BuildCompatibilityReportAsync(profile, session, null, null, CancellationToken.None);
    }

    Task<ModCompatibilityReport> BuildCompatibilityReportAsync(
        TrainerProfile profile,
        AttachSession? session,
        DependencyValidationResult? dependencyValidation,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
    {
        return BuildCompatibilityReportAsync(profile, session, dependencyValidation, catalog, CancellationToken.None);
    }
}
