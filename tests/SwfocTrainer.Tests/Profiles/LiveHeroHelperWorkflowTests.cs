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
        var contexts = supported
            .Where(x => x.ExeTarget == ExeTarget.Swfoc)
            .Select(x => new
            {
                Process = x,
                Context = x.LaunchContext ?? new LaunchContextResolver().Resolve(x, profiles)
            })
            .ToList();

        var target = contexts
            .OrderByDescending(x => x.Context.Recommendation.ProfileId == "roe_3447786229_swfoc")
            .ThenByDescending(x => x.Context.Recommendation.ProfileId == "aotr_1397421866_swfoc")
            .FirstOrDefault(x =>
                x.Context.Recommendation.ProfileId == "roe_3447786229_swfoc" ||
                x.Context.Recommendation.ProfileId == "aotr_1397421866_swfoc");

        if (target is null)
        {
            throw LiveSkip.For(_output, "no AOTR/ROE launch context detected.");
        }

        var profileId = target.Context.Recommendation.ProfileId!;
        var profile = await profileRepo.ResolveInheritedProfileAsync(profileId);
        var session = await runtime.AttachAsync(profileId);
        _output.WriteLine($"Attached for helper smoke: profile={profileId} pid={session.Process.ProcessId} mode={session.Process.Mode}");

        string actionId;
        JsonObject payload;
        if (profileId == "roe_3447786229_swfoc" && profile.Actions.ContainsKey("toggle_roe_respawn_helper"))
        {
            actionId = "toggle_roe_respawn_helper";
            payload = new JsonObject
            {
                ["helperHookId"] = "roe_respawn_bridge",
                ["boolValue"] = false
            };
        }
        else if (profile.Actions.ContainsKey("set_hero_state_helper"))
        {
            actionId = "set_hero_state_helper";
            payload = new JsonObject
            {
                ["helperHookId"] = "aotr_hero_state_bridge",
                ["globalKey"] = "SWFOC_TRAINER_HERO_HELPER_SMOKE",
                ["intValue"] = 0
            };
        }
        else
        {
            throw LiveSkip.For(_output, $"profile '{profileId}' does not expose helper hero actions.");
        }

        var action = profile.Actions[actionId];
        var result = await runtime.ExecuteAsync(new ActionExecutionRequest(action, payload, profileId, session.Process.Mode));
        _output.WriteLine($"helper action={actionId} success={result.Succeeded} message={result.Message}");

        if (!result.Succeeded)
        {
            result.Message.Should().NotBeNullOrWhiteSpace();
            result.Message.ToLowerInvariant().Should().MatchRegex("helper|dependency|disabled|unsupported");
            return;
        }

        result.Succeeded.Should().BeTrue();
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
}
