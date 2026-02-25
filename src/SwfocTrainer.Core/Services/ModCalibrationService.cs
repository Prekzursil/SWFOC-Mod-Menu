#pragma warning disable S4136
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class ModCalibrationService : IModCalibrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IActionReliabilityService _actionReliability;

    public ModCalibrationService(IActionReliabilityService actionReliability)
    {
        _actionReliability = actionReliability;
    }

    public async Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request, CancellationToken cancellationToken)
    {
        ValidateArtifactRequest(request);

        Directory.CreateDirectory(request.OutputDirectory);

        var warnings = new List<string>();
        var candidates = CollectCalibrationCandidates(request.Session);
        var moduleFingerprint = ResolveModuleFingerprint(request.Session, warnings, candidates.Count);
        var payload = BuildCalibrationPayload(request, moduleFingerprint, candidates);

        var safeProfileId = SanitizeFileToken(request.ProfileId);
        var artifactPath = Path.Combine(
            request.OutputDirectory,
            $"calibration-{safeProfileId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");

        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);

        return new ModCalibrationArtifactResult(
            Succeeded: true,
            ArtifactPath: artifactPath,
            ModuleFingerprint: moduleFingerprint,
            Candidates: candidates,
            Warnings: warnings);
    }

    public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(
        TrainerProfile profile,
        AttachSession? session,
        DependencyValidationResult? dependencyValidation,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        CancellationToken cancellationToken)
    {
        var runtimeMode = session?.Process.Mode ?? RuntimeMode.Unknown;
        var dependencyStatus = dependencyValidation?.Status ?? InferDependencyStatus(session);
        var actionRows = BuildActionCompatibilityRows(profile, session, catalog);
        var unresolvedCritical = CountUnresolvedCriticalSymbols(profile, session);
        var promotionReady = IsPromotionReady(dependencyStatus, unresolvedCritical, actionRows);
        var notes = BuildCompatibilityNotes(session, dependencyStatus, unresolvedCritical);

        return Task.FromResult(new ModCompatibilityReport(
            ProfileId: profile.Id,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RuntimeMode: runtimeMode,
            DependencyStatus: dependencyStatus,
            UnresolvedCriticalSymbols: unresolvedCritical,
            PromotionReady: promotionReady,
            Actions: actionRows,
            Notes: notes));
    }

    private static void ValidateArtifactRequest(ModCalibrationArtifactRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProfileId))
        {
            throw new InvalidDataException("ProfileId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new InvalidDataException("OutputDirectory is required.");
        }
    }

    private static IReadOnlyList<CalibrationCandidate> CollectCalibrationCandidates(AttachSession? session)
    {
        if (session is null)
        {
            return Array.Empty<CalibrationCandidate>();
        }

        return session.Symbols.Symbols.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(symbol => new CalibrationCandidate(
                Symbol: symbol.Name,
                Source: symbol.Source.ToString(),
                HealthStatus: symbol.HealthStatus.ToString(),
                Confidence: symbol.Confidence,
                Notes: symbol.Diagnostics))
            .ToArray();
    }

    private static string ResolveModuleFingerprint(
        AttachSession? session,
        ICollection<string> warnings,
        int candidateCount)
    {
        if (session is null)
        {
            warnings.Add("No attach session was provided; artifact contains no symbol candidates.");
            return "session_unavailable";
        }

        if (candidateCount == 0)
        {
            warnings.Add("Attach session does not contain resolved symbols.");
        }

        return ComputeModuleFingerprint(session);
    }

    private static object BuildCalibrationPayload(
        ModCalibrationArtifactRequest request,
        string moduleFingerprint,
        IReadOnlyList<CalibrationCandidate> candidates)
    {
        return new
        {
            schemaVersion = "1.0",
            generatedAtUtc = DateTimeOffset.UtcNow,
            profileId = request.ProfileId,
            moduleFingerprint,
            operatorNotes = request.OperatorNotes,
            process = request.Session is null ? null : BuildProcessPayload(request.Session),
            candidates
        };
    }

    private static object BuildProcessPayload(AttachSession session)
    {
        return new
        {
            pid = session.Process.ProcessId,
            name = session.Process.ProcessName,
            path = session.Process.ProcessPath,
            commandLineAvailable = session.Process.LaunchContext?.CommandLineAvailable ?? false,
            launchKind = session.Process.LaunchContext?.LaunchKind.ToString() ?? "Unknown",
            launchReasonCode = session.Process.LaunchContext?.Recommendation.ReasonCode ?? "unknown"
        };
    }

    private IReadOnlyList<ModActionCompatibility> BuildActionCompatibilityRows(
        TrainerProfile profile,
        AttachSession? session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
    {
        var reliability = session is null
            ? BuildSessionUnavailableReliability(profile)
            : _actionReliability.Evaluate(profile, session, catalog);

        return reliability
            .OrderBy(x => x.ActionId, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ModActionCompatibility(x.ActionId, x.State, x.ReasonCode, x.Confidence))
            .ToArray();
    }

    private static IReadOnlyList<ActionReliabilityInfo> BuildSessionUnavailableReliability(TrainerProfile profile)
    {
        return profile.Actions.Keys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(actionId => new ActionReliabilityInfo(actionId, ActionReliabilityState.Unavailable, "session_unavailable", 0.00d))
            .ToArray();
    }

    private static int CountUnresolvedCriticalSymbols(TrainerProfile profile, AttachSession? session)
    {
        var criticalSymbols = ParseCriticalSymbols(profile.Metadata);
        if (session is null)
        {
            return criticalSymbols.Count;
        }

        return session.Symbols.Symbols
            .Where(x => criticalSymbols.Contains(x.Key))
            .Count(x => x.Value.HealthStatus == SymbolHealthStatus.Unresolved);
    }

    private static bool IsPromotionReady(
        DependencyValidationStatus dependencyStatus,
        int unresolvedCritical,
        IReadOnlyList<ModActionCompatibility> actionRows)
    {
        var hasUnavailableActions = actionRows.Any(x => x.State == ActionReliabilityState.Unavailable);
        return dependencyStatus != DependencyValidationStatus.HardFail &&
               unresolvedCritical == 0 &&
               !hasUnavailableActions;
    }

    private static IReadOnlyList<string> BuildCompatibilityNotes(
        AttachSession? session,
        DependencyValidationStatus dependencyStatus,
        int unresolvedCritical)
    {
        var notes = new List<string>();
        if (session is null)
        {
            notes.Add("No attach session was provided. Report reflects static profile analysis only.");
        }

        AddDependencyNote(dependencyStatus, notes);
        if (unresolvedCritical > 0)
        {
            notes.Add($"{unresolvedCritical} critical symbol(s) unresolved.");
        }

        return notes;
    }

    private static void AddDependencyNote(DependencyValidationStatus dependencyStatus, ICollection<string> notes)
    {
        if (dependencyStatus == DependencyValidationStatus.SoftFail)
        {
            notes.Add("Dependency validation returned SoftFail; helper-dependent actions may be blocked.");
            return;
        }

        if (dependencyStatus == DependencyValidationStatus.HardFail)
        {
            notes.Add("Dependency validation returned HardFail; promotion gate is blocked.");
        }
    }

    public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request)
    {
        return ExportCalibrationArtifactAsync(request, CancellationToken.None);
    }

    public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(
        TrainerProfile profile,
        AttachSession? session)
    {
        return BuildCompatibilityReportAsync(profile, session, null, null, CancellationToken.None);
    }

    public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(
        TrainerProfile profile,
        AttachSession? session,
        DependencyValidationResult? dependencyValidation,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
    {
        return BuildCompatibilityReportAsync(profile, session, dependencyValidation, catalog, CancellationToken.None);
    }

    private static DependencyValidationStatus InferDependencyStatus(AttachSession? session)
    {
        if (session?.Process.Metadata is null)
        {
            return DependencyValidationStatus.Pass;
        }

        if (!session.Process.Metadata.TryGetValue("dependencyValidation", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return DependencyValidationStatus.Pass;
        }

        return Enum.TryParse<DependencyValidationStatus>(raw, true, out var status)
            ? status
            : DependencyValidationStatus.Pass;
    }

    private static HashSet<string> ParseCriticalSymbols(IReadOnlyDictionary<string, string>? metadata)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (metadata is null)
        {
            return set;
        }

        if (!metadata.TryGetValue("criticalSymbols", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return set;
        }

        foreach (var symbol in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            set.Add(symbol);
        }

        return set;
    }

    private static string ComputeModuleFingerprint(AttachSession session)
    {
        var seed = string.Join('|',
            session.Process.ProcessName,
            session.Process.ProcessPath,
            session.Build.GameBuild,
            session.Process.CommandLine ?? string.Empty,
            session.Symbols.Symbols.Count.ToString());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
    }

    private static string SanitizeFileToken(string value)
    {
        return new string(value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');
    }
}
