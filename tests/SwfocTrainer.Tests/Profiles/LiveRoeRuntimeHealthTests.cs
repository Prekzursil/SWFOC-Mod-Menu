using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using System.Text.Json;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Profiles;

/// <summary>
/// Live ROE health checks that exercise the exact runtime attach + credits path used by the app.
/// Skips quietly when ROE process isn't running.
/// </summary>
public sealed class LiveRoeRuntimeHealthTests
{
    private const string RoeWorkshopId = "3447786229";
    private readonly ITestOutputHelper _output;

    public LiveRoeRuntimeHealthTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Roe_Attach_And_Credits_Action_Should_Succeed_On_Live_Process()
    {
        var locator = new ProcessLocator();
        var roeCandidates = await FindRoeCandidatesAsync(locator);
        EnsureRoeCandidatesOrSkip(roeCandidates);

        var hasSwfoc = roeCandidates.Any(x => x.ProcessName.Equals("swfoc", StringComparison.OrdinalIgnoreCase) ||
                                              x.ProcessName.Equals("swfoc.exe", StringComparison.OrdinalIgnoreCase));
        var hasStarWarsG = roeCandidates.Any(IsStarWarsGProcess);
        _output.WriteLine($"Live ROE candidates: {roeCandidates.Length} (swfoc={hasSwfoc}, StarWarsG={hasStarWarsG})");

        var profileId = "roe_3447786229_swfoc";
        var runtime = BuildRuntimeAdapter(locator, out var profileRepo);
        var profile = await profileRepo.ResolveInheritedProfileAsync(profileId);
        var session = await runtime.AttachAsync(profileId);
        _output.WriteLine($"Attached PID={session.Process.ProcessId} Name={session.Process.ProcessName} Symbols={session.Symbols.Symbols.Count}");

        if (hasSwfoc && hasStarWarsG)
        {
            IsStarWarsGProcess(session.Process).Should().BeTrue("when both swfoc.exe and StarWarsG.exe exist, runtime must bind to the real game host");
        }

        session.Symbols.TryGetValue("credits", out var creditsSymbol).Should().BeTrue("credits must resolve for ROE profile");

        var currentCredits = await runtime.ReadAsync<int>("credits");
        var requestedCredits = currentCredits < 0 ? 0 : currentCredits;
        _output.WriteLine($"Live credits readback={currentCredits}; normalized request value={requestedCredits}");
        var action = profile.Actions["set_credits"];
        var payload = BuildSetCreditsPayload(requestedCredits);
        var result = await runtime.ExecuteAsync(
            new ActionExecutionRequest(action, payload, profileId, session.Process.Mode));

        WriteSetCreditsResult(result);

        TryWriteRuntimeEvidence(
            session.Process,
            profileId,
            result);

        if (!result.Succeeded &&
            !string.IsNullOrWhiteSpace(result.Message) &&
            result.Message.Contains("hook did not observe a sync tick", StringComparison.OrdinalIgnoreCase))
        {
            throw LiveSkip.For(_output, "set_credits precondition unmet: hook sync tick not observed. Enter galactic/campaign context and retry.");
        }

        result.Succeeded.Should().BeTrue($"set_credits should succeed on live ROE process. Message: {result.Message}");
        await runtime.DetachAsync();
    }

    private async Task<ProcessMetadata[]> FindRoeCandidatesAsync(ProcessLocator locator)
    {
        var supported = await locator.FindSupportedProcessesAsync();
        return supported
            .Where(x => x.ExeTarget == ExeTarget.Swfoc && ProcessContainsWorkshopId(x, RoeWorkshopId))
            .ToArray();
    }

    private void EnsureRoeCandidatesOrSkip(IReadOnlyList<ProcessMetadata> roeCandidates)
    {
        if (roeCandidates.Count == 0)
        {
            throw LiveSkip.For(_output, "no live FoC process with STEAMMOD=3447786229 found.");
        }
    }

    private static RuntimeAdapter BuildRuntimeAdapter(ProcessLocator locator, out FileSystemProfileRepository profileRepo)
    {
        var repoRoot = TestPaths.FindRepoRoot();
        profileRepo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(repoRoot, "profiles", "default")
        });
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance);
        return new RuntimeAdapter(locator, profileRepo, resolver, NullLogger<RuntimeAdapter>.Instance);
    }

    private static JsonObject BuildSetCreditsPayload(int requestedCredits)
    {
        return new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = requestedCredits,
            ["lockCredits"] = false
        };
    }

    private void WriteSetCreditsResult(ActionExecutionResult result)
    {
        _output.WriteLine($"set_credits result: success={result.Succeeded} source={result.AddressSource} message={result.Message}");
        if (result.Diagnostics is null)
        {
            return;
        }

        foreach (var kv in result.Diagnostics)
        {
            _output.WriteLine($"diag[{kv.Key}]={kv.Value}");
        }
    }

    private static bool IsStarWarsGProcess(ProcessMetadata process)
    {
        if (process.ProcessName.Equals("StarWarsG", StringComparison.OrdinalIgnoreCase) ||
            process.ProcessName.Equals("StarWarsG.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (process.Metadata is not null &&
            process.Metadata.TryGetValue("isStarWarsG", out var raw) &&
            bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return process.ProcessPath.Contains("StarWarsG.exe", StringComparison.OrdinalIgnoreCase);
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

    private static void TryWriteRuntimeEvidence(
        ProcessMetadata process,
        string profileId,
        ActionExecutionResult result)
    {
        if (!TryGetLiveOutputDirectory(out var outputDir))
        {
            return;
        }

        var payload = BuildRuntimeEvidencePayload(process, profileId, result);
        var path = Path.Combine(outputDir, "live-roe-runtime-evidence.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
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

    private static object BuildRuntimeEvidencePayload(
        ProcessMetadata process,
        string profileId,
        ActionExecutionResult result)
    {
        return new
        {
            testName = nameof(LiveRoeRuntimeHealthTests),
            profileId,
            process = new
            {
                process.ProcessId,
                process.ProcessName,
                process.HostRole,
                process.SelectionScore,
                process.WorkshopMatchCount
            },
            result = new
            {
                result.Succeeded,
                result.Message,
                backendRoute = ReadDiagnosticString(result.Diagnostics, "backendRoute"),
                routeReasonCode = ReadDiagnosticString(result.Diagnostics, "routeReasonCode"),
                capabilityProbeReasonCode = ReadDiagnosticString(result.Diagnostics, "capabilityProbeReasonCode"),
                hookState = ReadDiagnosticString(result.Diagnostics, "hookState"),
                diagnostics = result.Diagnostics
            },
            capturedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string? ReadDiagnosticString(IReadOnlyDictionary<string, object?>? diagnostics, string key)
    {
        if (diagnostics is null || !diagnostics.TryGetValue(key, out var rawValue))
        {
            return null;
        }

        return rawValue?.ToString();
    }
}
