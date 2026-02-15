using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Profiles;

public sealed class LiveTacticalToggleWorkflowTests
{
    private readonly ITestOutputHelper _output;

    public LiveTacticalToggleWorkflowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public async Task Tactical_Toggles_Should_Execute_And_Revert_When_Tactical_Mode()
    {
        var locator = new ProcessLocator();
        var running = await locator.FindBestMatchAsync(ExeTarget.Swfoc);
        if (running is null)
        {
            throw LiveSkip.For(_output, "no swfoc process detected.");
        }

        var repoRoot = TestPaths.FindRepoRoot();
        var profileRepo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(repoRoot, "profiles", "default")
        });
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance);
        var runtime = new RuntimeAdapter(locator, profileRepo, resolver, NullLogger<RuntimeAdapter>.Instance);
        var freezeService = new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance);
        var orchestrator = new TrainerOrchestrator(profileRepo, runtime, freezeService, NullAuditLogger.Instance);

        var profiles = await ResolveProfilesAsync(profileRepo);
        var context = running.LaunchContext ?? new LaunchContextResolver().Resolve(running, profiles);
        var profileId = context.Recommendation.ProfileId ?? "base_swfoc";
        var session = await runtime.AttachAsync(profileId);
        if (session.Process.Mode != RuntimeMode.Tactical)
        {
            throw LiveSkip.For(_output, $"runtime mode is {session.Process.Mode}, tactical checks require Tactical.");
        }

        var profile = await profileRepo.ResolveInheritedProfileAsync(profileId);
        if (!profile.Actions.ContainsKey("toggle_tactical_god_mode") ||
            !profile.Actions.ContainsKey("toggle_tactical_one_hit_mode"))
        {
            throw LiveSkip.For(_output, $"profile '{profileId}' does not expose tactical toggle actions.");
        }

        byte godCurrent;
        byte oneHitCurrent;
        try
        {
            godCurrent = await runtime.ReadAsync<byte>("tactical_god_mode");
            oneHitCurrent = await runtime.ReadAsync<byte>("tactical_one_hit_mode");
        }
        catch (Exception ex)
        {
            throw LiveSkip.For(_output, $"tactical symbols not readable: {ex.Message}");
        }

        var godEnable = godCurrent == 0;
        var oneHitEnable = oneHitCurrent == 0;

        var godToggle = await orchestrator.ExecuteAsync(
            profileId,
            "toggle_tactical_god_mode",
            new JsonObject
            {
                ["symbol"] = "tactical_god_mode",
                ["boolValue"] = godEnable
            },
            RuntimeMode.Tactical);
        var godRevert = await orchestrator.ExecuteAsync(
            profileId,
            "toggle_tactical_god_mode",
            new JsonObject
            {
                ["symbol"] = "tactical_god_mode",
                ["boolValue"] = !godEnable
            },
            RuntimeMode.Tactical);

        var oneHitToggle = await orchestrator.ExecuteAsync(
            profileId,
            "toggle_tactical_one_hit_mode",
            new JsonObject
            {
                ["symbol"] = "tactical_one_hit_mode",
                ["boolValue"] = oneHitEnable
            },
            RuntimeMode.Tactical);
        var oneHitRevert = await orchestrator.ExecuteAsync(
            profileId,
            "toggle_tactical_one_hit_mode",
            new JsonObject
            {
                ["symbol"] = "tactical_one_hit_mode",
                ["boolValue"] = !oneHitEnable
            },
            RuntimeMode.Tactical);

        _output.WriteLine($"god toggle={godToggle.Succeeded} revert={godRevert.Succeeded}");
        _output.WriteLine($"one_hit toggle={oneHitToggle.Succeeded} revert={oneHitRevert.Succeeded}");

        godToggle.Succeeded.Should().BeTrue($"god toggle failed: {godToggle.Message}");
        godRevert.Succeeded.Should().BeTrue($"god revert failed: {godRevert.Message}");
        oneHitToggle.Succeeded.Should().BeTrue($"one-hit toggle failed: {oneHitToggle.Message}");
        oneHitRevert.Succeeded.Should().BeTrue($"one-hit revert failed: {oneHitRevert.Message}");
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

    private sealed class NullAuditLogger : SwfocTrainer.Core.Contracts.IAuditLogger
    {
        public static readonly NullAuditLogger Instance = new();

        public Task WriteAsync(SwfocTrainer.Core.Logging.ActionAuditRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
