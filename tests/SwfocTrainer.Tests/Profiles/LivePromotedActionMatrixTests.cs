using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Profiles;

public sealed class LivePromotedActionMatrixTests
{
    private const string AotrWorkshopId = "1397421866";
    private const string RoeWorkshopId = "3447786229";

    private static readonly string[] TargetProfiles =
    [
        "base_swfoc",
        "aotr_1397421866_swfoc",
        "roe_3447786229_swfoc"
    ];

    private static readonly PromotedActionSpec[] PromotedActions =
    [
        new(
            "freeze_timer",
            () => new JsonObject
            {
                ["symbol"] = "game_timer_freeze",
                ["boolValue"] = false
            }),
        new(
            "toggle_fog_reveal",
            () => new JsonObject
            {
                ["symbol"] = "fog_reveal",
                ["boolValue"] = false
            }),
        new(
            "toggle_ai",
            () => new JsonObject
            {
                ["symbol"] = "ai_enabled",
                ["boolValue"] = true
            }),
        new(
            "set_unit_cap",
            () => new JsonObject
            {
                ["intValue"] = 300,
                ["enable"] = false
            }),
        new(
            "toggle_instant_build_patch",
            () => new JsonObject
            {
                ["enable"] = false
            })
    ];

    private readonly ITestOutputHelper _output;

    public LivePromotedActionMatrixTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Promoted_Actions_Should_Route_Via_Extender_Without_Hybrid_Fallback()
    {
        var matrixEntries = new List<ActionStatusEntry>(TargetProfiles.Length * PromotedActions.Length);
        try
        {
            var locator = new ProcessLocator();
            var supportedProcesses = await FindSupportedSwfocProcessesAsync(locator);

            if (supportedProcesses.Length == 0)
            {
                throw LiveSkip.For(_output, "no live swfoc process detected.");
            }

            var hasAotrContext = supportedProcesses.Any(x => ProcessContainsWorkshopId(x, AotrWorkshopId));
            var hasRoeContext = supportedProcesses.Any(x => ProcessContainsWorkshopId(x, RoeWorkshopId));
            _output.WriteLine($"live process contexts: swfoc={supportedProcesses.Length} aotr={hasAotrContext} roe={hasRoeContext}");
            var dependencies = BuildRuntimeDependencies(locator);
            foreach (var profileId in TargetProfiles)
            {
                await ExecuteProfileMatrixAsync(
                    dependencies,
                    matrixEntries,
                    profileId,
                    hasAotrContext,
                    hasRoeContext);
            }

            matrixEntries
                .Where(x => !x.Outcome.Equals("Skipped", StringComparison.OrdinalIgnoreCase))
                .Should()
                .NotBeEmpty("at least one profile context must execute promoted action checks during live validation.");
        }
        finally
        {
            TryWriteActionStatusDiagnostics(matrixEntries);
        }
    }

    private static async Task<ProcessMetadata[]> FindSupportedSwfocProcessesAsync(ProcessLocator locator)
    {
        return (await locator.FindSupportedProcessesAsync())
            .Where(x => x.ExeTarget == ExeTarget.Swfoc)
            .ToArray();
    }

    private static RuntimeDependencies BuildRuntimeDependencies(ProcessLocator locator)
    {
        var repoRoot = TestPaths.FindRepoRoot();
        var profileRepo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(repoRoot, "profiles", "default")
        });
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance);
        var runtime = new RuntimeAdapter(locator, profileRepo, resolver, NullLogger<RuntimeAdapter>.Instance);
        return new RuntimeDependencies(profileRepo, runtime);
    }

    private async Task ExecuteProfileMatrixAsync(
        RuntimeDependencies dependencies,
        ICollection<ActionStatusEntry> matrixEntries,
        string profileId,
        bool hasAotrContext,
        bool hasRoeContext)
    {
        if (TrySkipUnavailableProfileContext(matrixEntries, profileId, hasAotrContext, hasRoeContext))
        {
            return;
        }

        var attachResult = await TryAttachProfileAsync(dependencies.Runtime, profileId);
        if (attachResult.Session is null)
        {
            AddSkippedProfileEntries(
                matrixEntries,
                profileId,
                skipReasonCode: "attach_profile_mismatch",
                message: attachResult.FailureMessage ?? $"Attach profile mismatch for '{profileId}'.");
            return;
        }

        try
        {
            var session = attachResult.Session;
            _output.WriteLine($"attached profile={profileId} pid={session.Process.ProcessId} mode={session.Process.Mode}");
            var profile = await dependencies.ProfileRepository.ResolveInheritedProfileAsync(profileId);
            foreach (var actionSpec in PromotedActions)
            {
                await ExecuteAndAssertActionAsync(
                    dependencies.Runtime,
                    profile,
                    matrixEntries,
                    profileId,
                    session.Process.Mode,
                    actionSpec);
            }
        }
        finally
        {
            if (dependencies.Runtime.IsAttached)
            {
                await dependencies.Runtime.DetachAsync();
            }
        }
    }

    private static async Task<AttachAttempt> TryAttachProfileAsync(RuntimeAdapter runtime, string profileId)
    {
        try
        {
            return new AttachAttempt(await runtime.AttachAsync(profileId), null);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(RuntimeReasonCode.ATTACH_PROFILE_MISMATCH.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return new AttachAttempt(null, ex.Message);
        }
    }

    private async Task ExecuteAndAssertActionAsync(
        RuntimeAdapter runtime,
        TrainerProfile profile,
        ICollection<ActionStatusEntry> matrixEntries,
        string profileId,
        RuntimeMode mode,
        PromotedActionSpec actionSpec)
    {
        profile.Actions.Should().ContainKey(
            actionSpec.ActionId,
            because: $"profile '{profileId}' must expose promoted action '{actionSpec.ActionId}'");

        var action = profile.Actions[actionSpec.ActionId];
        var request = new ActionExecutionRequest(action, actionSpec.BuildPayload(), profileId, mode);
        var result = await runtime.ExecuteAsync(request);
        var backendRoute = ReadDiagnosticString(result.Diagnostics, "backendRoute");
        var routeReasonCode = ReadDiagnosticString(result.Diagnostics, "routeReasonCode");
        var capabilityProbeReasonCode = ReadDiagnosticString(result.Diagnostics, "capabilityProbeReasonCode");
        var hybridExecution = ReadDiagnosticBool(result.Diagnostics, "hybridExecution");
        var hasFallbackMarker = HasHybridFallbackMarker(result, backendRoute, routeReasonCode, result.Diagnostics);

        matrixEntries.Add(new ActionStatusEntry(
            ProfileId: profileId,
            ActionId: actionSpec.ActionId,
            Outcome: result.Succeeded ? "Passed" : "Failed",
            Message: result.Message,
            BackendRoute: backendRoute,
            RouteReasonCode: routeReasonCode,
            CapabilityProbeReasonCode: capabilityProbeReasonCode,
            HybridExecution: hybridExecution,
            HasFallbackMarker: hasFallbackMarker,
            SkipReasonCode: null));

        _output.WriteLine(
            $"matrix profile={profileId} action={actionSpec.ActionId} success={result.Succeeded} backend={backendRoute} route={routeReasonCode} cap={capabilityProbeReasonCode} hybrid={hybridExecution} fallbackMarker={hasFallbackMarker} msg={result.Message}");

        AssertPromotedActionExecution(
            result,
            profileId,
            actionSpec.ActionId,
            backendRoute,
            routeReasonCode,
            capabilityProbeReasonCode,
            hybridExecution,
            hasFallbackMarker,
            action.ExecutionKind);
    }

    private bool TrySkipUnavailableProfileContext(
        ICollection<ActionStatusEntry> matrixEntries,
        string profileId,
        bool hasAotrContext,
        bool hasRoeContext)
    {
        if (IsProfileContextAvailable(profileId, hasAotrContext, hasRoeContext))
        {
            return false;
        }

        AddSkippedProfileEntries(
            matrixEntries,
            profileId,
            skipReasonCode: "profile_context_not_detected",
            message: $"Launch context for profile '{profileId}' was not detected in running processes.");
        return true;
    }

    private static void AssertPromotedActionExecution(
        ActionExecutionResult result,
        string profileId,
        string actionId,
        string? backendRoute,
        string? routeReasonCode,
        string? capabilityProbeReasonCode,
        bool? hybridExecution,
        bool hasFallbackMarker,
        ExecutionKind executionKind)
    {
        result.Succeeded.Should().BeTrue(
            $"promoted action '{actionId}' should execute successfully for profile '{profileId}'. message={result.Message}");
        var expectedBackend = executionKind == ExecutionKind.Sdk
            ? ExecutionBackendKind.Extender
            : ExecutionBackendKind.Memory;
        backendRoute.Should().Be(
            expectedBackend.ToString(),
            because: $"promoted action '{actionId}' should respect execution kind '{executionKind}' for profile '{profileId}'.");
        routeReasonCode.Should().Be(
            RuntimeReasonCode.CAPABILITY_PROBE_PASS.ToString(),
            because: $"promoted action '{actionId}' should pass route capability gate for profile '{profileId}'.");
        capabilityProbeReasonCode.Should().Be(
            RuntimeReasonCode.CAPABILITY_PROBE_PASS.ToString(),
            because: $"promoted action '{actionId}' should report capability probe pass for profile '{profileId}'.");
        hybridExecution.HasValue.Should().BeTrue(
            because: $"promoted action '{actionId}' should emit hybrid execution diagnostics for profile '{profileId}'.");
        hybridExecution.Should().BeFalse(
            because: $"promoted action '{actionId}' should execute as native-authoritative extender flow for profile '{profileId}'.");
        hasFallbackMarker.Should().BeFalse(
            because: $"promoted action '{actionId}' should not include fallback markers for profile '{profileId}'.");
    }

    private void AddSkippedProfileEntries(
        ICollection<ActionStatusEntry> matrixEntries,
        string profileId,
        string skipReasonCode,
        string message)
    {
        foreach (var actionSpec in PromotedActions)
        {
            matrixEntries.Add(new ActionStatusEntry(
                ProfileId: profileId,
                ActionId: actionSpec.ActionId,
                Outcome: "Skipped",
                Message: message,
                BackendRoute: null,
                RouteReasonCode: null,
                CapabilityProbeReasonCode: null,
                HybridExecution: null,
                HasFallbackMarker: false,
                SkipReasonCode: skipReasonCode));
        }

        _output.WriteLine($"matrix profile={profileId} skipped reason={skipReasonCode} message={message}");
    }

    private static bool IsProfileContextAvailable(string profileId, bool hasAotrContext, bool hasRoeContext)
    {
        return profileId switch
        {
            "base_swfoc" => true,
            "aotr_1397421866_swfoc" => hasAotrContext,
            "roe_3447786229_swfoc" => hasRoeContext,
            _ => false
        };
    }

    private static bool ProcessContainsWorkshopId(ProcessMetadata process, string workshopId)
    {
        if (process.LaunchContext is not null &&
            process.LaunchContext.SteamModIds.Any(x => x.Equals(workshopId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (process.CommandLine?.Contains(workshopId, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (process.Metadata is not null &&
            process.Metadata.TryGetValue("steamModIdsDetected", out var ids) &&
            !string.IsNullOrWhiteSpace(ids))
        {
            return ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(x => x.Equals(workshopId, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static string? ReadDiagnosticString(IReadOnlyDictionary<string, object?>? diagnostics, string key)
    {
        if (diagnostics is null || !diagnostics.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        return rawValue?.ToString();
    }

    private static bool? ReadDiagnosticBool(IReadOnlyDictionary<string, object?>? diagnostics, string key)
    {
        if (diagnostics is null || !diagnostics.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return null;
        }

        if (rawValue is bool boolValue)
        {
            return boolValue;
        }

        return bool.TryParse(rawValue.ToString(), out var parsed) ? parsed : null;
    }

    private static bool HasHybridFallbackMarker(
        ActionExecutionResult result,
        string? backendRoute,
        string? routeReasonCode,
        IReadOnlyDictionary<string, object?>? diagnostics)
    {
        if (diagnostics is not null && diagnostics.ContainsKey("fallbackBackend"))
        {
            return true;
        }

        if (string.Equals(backendRoute, ExecutionBackendKind.Memory.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(routeReasonCode, RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return result.Message?.Contains("fallback", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void TryWriteActionStatusDiagnostics(IReadOnlyCollection<ActionStatusEntry> entries)
    {
        if (!TryGetLiveOutputDirectory(out var outputDir))
        {
            return;
        }

        var path = Path.Combine(outputDir, "live-promoted-action-matrix.json");
        var payload = new
        {
            testName = nameof(LivePromotedActionMatrixTests),
            capturedAtUtc = DateTimeOffset.UtcNow,
            actionStatusDiagnostics = new
            {
                status = "captured",
                source = "live-promoted-action-matrix.json",
                summary = new
                {
                    total = entries.Count,
                    passed = entries.Count(x => x.Outcome.Equals("Passed", StringComparison.OrdinalIgnoreCase)),
                    failed = entries.Count(x => x.Outcome.Equals("Failed", StringComparison.OrdinalIgnoreCase)),
                    skipped = entries.Count(x => x.Outcome.Equals("Skipped", StringComparison.OrdinalIgnoreCase))
                },
                entries
            }
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static bool TryGetLiveOutputDirectory(out string outputDir)
    {
        outputDir = Environment.GetEnvironmentVariable("SWFOC_LIVE_OUTPUT_DIR") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            return false;
        }

        Directory.CreateDirectory(outputDir);
        return true;
    }

    private sealed record PromotedActionSpec(string ActionId, Func<JsonObject> BuildPayload);

    private sealed record RuntimeDependencies(
        FileSystemProfileRepository ProfileRepository,
        RuntimeAdapter Runtime);

    private sealed record AttachAttempt(AttachSession? Session, string? FailureMessage);

    private sealed record ActionStatusEntry(
        string ProfileId,
        string ActionId,
        string Outcome,
        string Message,
        string? BackendRoute,
        string? RouteReasonCode,
        string? CapabilityProbeReasonCode,
        bool? HybridExecution,
        bool HasFallbackMarker,
        string? SkipReasonCode);
}
