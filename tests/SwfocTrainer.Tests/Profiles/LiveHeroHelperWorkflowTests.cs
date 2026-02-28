using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Profiles;

public sealed class LiveHeroHelperWorkflowTests
{
    private static readonly HashSet<string> ForcedHelperProfileIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "aotr_1397421866_swfoc",
        "roe_3447786229_swfoc"
    };

    private readonly ITestOutputHelper _output;

    public LiveHeroHelperWorkflowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public async Task Hero_Helper_Workflow_Should_Return_Action_Result_For_Aotr_Or_Roe()
    {
        var locator = new ProcessLocator();
        var supported = await locator.FindSupportedProcessesAsync();
        if (supported.Count == 0)
        {
            throw LiveSkip.For(_output, "no supported process detected.");
        }

        var repoRoot = TestPaths.FindRepoRoot();
        var profileRepo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(repoRoot, "profiles", "default")
        });
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance);
        var runtime = new RuntimeAdapter(locator, profileRepo, resolver, NullLogger<RuntimeAdapter>.Instance);

        var profiles = await ResolveProfilesAsync(profileRepo);
        var target = SelectHelperTargetContext(supported, profiles);
        WriteHelperContextDiagnostics(supported, target);

        if (target is null)
        {
            throw LiveSkip.For(_output, "no AOTR/ROE launch context detected.");
        }

        var profileId = target.Context.Recommendation.ProfileId!;
        var profile = await profileRepo.ResolveInheritedProfileAsync(profileId);
        var session = await runtime.AttachAsync(profileId);
        _output.WriteLine($"Attached for helper smoke: profile={profileId} pid={session.Process.ProcessId} mode={session.Process.Mode}");

        var actionSpec = ResolveHelperAction(profileId, profile);
        if (actionSpec is null)
        {
            throw LiveSkip.For(_output, $"profile '{profileId}' does not expose helper hero actions.");
        }

        var action = profile.Actions[actionSpec.Value.ActionId];
        var result = await runtime.ExecuteAsync(new ActionExecutionRequest(action, actionSpec.Value.Payload, profileId, session.Process.Mode));
        _output.WriteLine($"helper action={actionSpec.Value.ActionId} success={result.Succeeded} message={result.Message}");
        AssertHelperActionResult(result);
    }

    private static (string ActionId, JsonObject Payload)? ResolveHelperAction(string profileId, TrainerProfile profile)
    {
        if (profileId == "roe_3447786229_swfoc" && profile.Actions.ContainsKey("toggle_roe_respawn_helper"))
        {
            return (
                ActionId: "toggle_roe_respawn_helper",
                Payload: new JsonObject
                {
                    ["helperHookId"] = "roe_respawn_bridge",
                    ["boolValue"] = false
                });
        }

        if (profile.Actions.ContainsKey("set_hero_state_helper"))
        {
            return (
                ActionId: "set_hero_state_helper",
                Payload: new JsonObject
                {
                    ["helperHookId"] = "aotr_hero_state_bridge",
                    ["globalKey"] = "SWFOC_TRAINER_HERO_HELPER_SMOKE",
                    ["intValue"] = 0
                });
        }

        return null;
    }

    private static void AssertHelperActionResult(ActionExecutionResult result)
    {
        if (!result.Succeeded)
        {
            result.Message.Should().NotBeNullOrWhiteSpace();
            result.Message.ToLowerInvariant().Should().MatchRegex("helper|dependency|disabled|unsupported");
            return;
        }

        result.Succeeded.Should().BeTrue();
    }

    private static SupportedProcessContext? SelectHelperTargetContext(
        IReadOnlyList<ProcessMetadata> supported,
        IReadOnlyList<TrainerProfile> profiles)
    {
        var contexts = supported
            .Where(x => x.ExeTarget == ExeTarget.Swfoc)
            .Select(x => new SupportedProcessContext(
                x,
                x.LaunchContext ?? new LaunchContextResolver().Resolve(x, profiles)))
            .ToList();

        var selected = contexts
            .OrderByDescending(x => x.Context.Recommendation.ProfileId == "roe_3447786229_swfoc")
            .ThenByDescending(x => x.Context.Recommendation.ProfileId == "aotr_1397421866_swfoc")
            .FirstOrDefault(x =>
                x.Context.Recommendation.ProfileId == "roe_3447786229_swfoc" ||
                x.Context.Recommendation.ProfileId == "aotr_1397421866_swfoc");
        if (selected is not null)
        {
            return selected;
        }

        var forcedProfileId = NormalizeProfileId(Environment.GetEnvironmentVariable("SWFOC_FORCE_PROFILE_ID"));
        if (!IsSupportedForcedHelperProfile(forcedProfileId))
        {
            return null;
        }

        var forcedTarget = contexts
            .OrderByDescending(x => x.Context.Source.Equals("forced", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.Process.ProcessName.Equals("StarWarsG", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (forcedTarget is null)
        {
            return null;
        }

        var forcedContext = forcedTarget.Context with
        {
            Recommendation = new ProfileRecommendation(
                forcedProfileId,
                ReasonCode: "forced_profile_env_fallback",
                Confidence: 1.0),
            Source = "forced"
        };
        return forcedTarget with { Context = forcedContext };
    }

    private void WriteHelperContextDiagnostics(
        IReadOnlyList<ProcessMetadata> supported,
        SupportedProcessContext? selected)
    {
        var forcedProfileId = NormalizeProfileId(Environment.GetEnvironmentVariable("SWFOC_FORCE_PROFILE_ID"));
        var forcedWorkshopIds = Environment.GetEnvironmentVariable("SWFOC_FORCE_WORKSHOP_IDS") ?? string.Empty;
        _output.WriteLine($"helper forced profile env={forcedProfileId ?? "<empty>"}");
        _output.WriteLine($"helper forced workshop env={forcedWorkshopIds}");

        foreach (var process in supported.Where(x => x.ExeTarget == ExeTarget.Swfoc))
        {
            var launchContext = process.LaunchContext;
            var recommendation = launchContext?.Recommendation;
            var steamIds = launchContext is null
                ? "<none>"
                : string.Join(",", launchContext.SteamModIds);
            _output.WriteLine(
                $"helper candidate pid={process.ProcessId} name={process.ProcessName} path={process.ProcessPath} source={launchContext?.Source ?? "<none>"} profile={recommendation?.ProfileId ?? "<none>"} reason={recommendation?.ReasonCode ?? "<none>"} steammods={steamIds}");
        }

        if (selected is null)
        {
            _output.WriteLine("helper selection: <none>");
            return;
        }

        _output.WriteLine(
            $"helper selected pid={selected.Process.ProcessId} profile={selected.Context.Recommendation.ProfileId} reason={selected.Context.Recommendation.ReasonCode} source={selected.Context.Source}");
    }

    private static bool IsSupportedForcedHelperProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        return ForcedHelperProfileIds.Contains(profileId);
    }

    private static string? NormalizeProfileId(string? profileId)
    {
        return string.IsNullOrWhiteSpace(profileId) ? null : profileId.Trim();
    }

    private static async Task<IReadOnlyList<TrainerProfile>> ResolveProfilesAsync(FileSystemProfileRepository profileRepo)
    {
        var ids = await profileRepo.ListAvailableProfilesAsync();
        var profiles = new List<TrainerProfile>(ids.Count);
        foreach (var id in ids)
        {
            profiles.Add(await profileRepo.ResolveInheritedProfileAsync(id));
        }

        return profiles;
    }

    private sealed record SupportedProcessContext(ProcessMetadata Process, LaunchContext Context);
}
