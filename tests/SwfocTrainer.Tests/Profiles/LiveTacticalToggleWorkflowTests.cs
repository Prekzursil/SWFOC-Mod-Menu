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

        var services = CreateServices(locator);
        var profileId = await ResolveAttachProfileAsync(services.ProfileRepository, running);
        await EnsureAttachedTacticalSessionAsync(services.Runtime, profileId);
        await EnsureTacticalToggleActionsAsync(services.ProfileRepository, profileId);

        var godEnable = await ReadEnableToggleAsync(services.Runtime, "tactical_god_mode");
        var oneHitEnable = await ReadEnableToggleAsync(services.Runtime, "tactical_one_hit_mode");
        var godToggle = await ExecuteToggleAsync(services.Orchestrator, profileId, "toggle_tactical_god_mode", "tactical_god_mode", godEnable);
        var godRevert = await ExecuteToggleAsync(services.Orchestrator, profileId, "toggle_tactical_god_mode", "tactical_god_mode", !godEnable);
        var oneHitToggle = await ExecuteToggleAsync(services.Orchestrator, profileId, "toggle_tactical_one_hit_mode", "tactical_one_hit_mode", oneHitEnable);
        var oneHitRevert = await ExecuteToggleAsync(services.Orchestrator, profileId, "toggle_tactical_one_hit_mode", "tactical_one_hit_mode", !oneHitEnable);

        _output.WriteLine($"god toggle={godToggle.Succeeded} revert={godRevert.Succeeded}");
        _output.WriteLine($"one_hit toggle={oneHitToggle.Succeeded} revert={oneHitRevert.Succeeded}");

        AssertToggleResult(godToggle, "god toggle");
        AssertToggleResult(godRevert, "god revert");
        AssertToggleResult(oneHitToggle, "one-hit toggle");
        AssertToggleResult(oneHitRevert, "one-hit revert");

        if (services.Runtime.IsAttached)
        {
            await services.Runtime.DetachAsync();
        }
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

    private ServiceDependencies CreateServices(ProcessLocator locator)
    {
        var repoRoot = TestPaths.FindRepoRoot();
        var profileRepo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(repoRoot, "profiles", "default")
        });
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance);
        var runtime = new RuntimeAdapter(locator, profileRepo, resolver, NullLogger<RuntimeAdapter>.Instance);
        var freezeService = new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance);
        var orchestrator = new TrainerOrchestrator(profileRepo, runtime, freezeService, NullAuditLogger.Instance);
        return new ServiceDependencies(profileRepo, runtime, orchestrator);
    }

    private async Task<string> ResolveAttachProfileAsync(FileSystemProfileRepository profileRepo, ProcessMetadata running)
    {
        var profiles = await ResolveProfilesAsync(profileRepo);
        var context = running.LaunchContext ?? new LaunchContextResolver().Resolve(running, profiles);
        return context.Recommendation.ProfileId ?? "base_swfoc";
    }

    private async Task<AttachSession> EnsureAttachedTacticalSessionAsync(RuntimeAdapter runtime, string profileId)
    {
        var session = await runtime.AttachAsync(profileId);
        if (session.Process.Mode is not (RuntimeMode.AnyTactical or RuntimeMode.TacticalLand or RuntimeMode.TacticalSpace))
        {
            throw LiveSkip.For(_output, $"runtime mode is {session.Process.Mode}, tactical checks require AnyTactical/TacticalLand/TacticalSpace.");
        }

        return session;
    }

    private async Task EnsureTacticalToggleActionsAsync(FileSystemProfileRepository profileRepo, string profileId)
    {
        var profile = await profileRepo.ResolveInheritedProfileAsync(profileId);
        if (!profile.Actions.ContainsKey("toggle_tactical_god_mode") ||
            !profile.Actions.ContainsKey("toggle_tactical_one_hit_mode"))
        {
            throw LiveSkip.For(_output, $"profile '{profileId}' does not expose tactical toggle actions.");
        }
    }

    private async Task<bool> ReadEnableToggleAsync(RuntimeAdapter runtime, string symbol)
    {
        try
        {
            return await runtime.ReadAsync<byte>(symbol) == 0;
        }
        catch (Exception ex)
        {
            throw LiveSkip.For(_output, $"tactical symbols not readable: {ex.Message}");
        }
    }

    private static Task<ActionExecutionResult> ExecuteToggleAsync(
        TrainerOrchestrator orchestrator,
        string profileId,
        string actionId,
        string symbol,
        bool enabled)
    {
        return orchestrator.ExecuteAsync(
            profileId,
            actionId,
            new JsonObject
            {
                ["symbol"] = symbol,
                ["boolValue"] = enabled
            },
            RuntimeMode.AnyTactical);
    }

    private static void AssertToggleResult(ActionExecutionResult result, string label)
    {
        result.Succeeded.Should().BeTrue($"{label} failed: {result.Message}");
    }

    private sealed class NullAuditLogger : SwfocTrainer.Core.Contracts.IAuditLogger
    {
        public static readonly NullAuditLogger Instance = new();

        public Task WriteAsync(SwfocTrainer.Core.Logging.ActionAuditRecord record, CancellationToken cancellationToken = default)
        {
            _ = record;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed record ServiceDependencies(
        FileSystemProfileRepository ProfileRepository,
        RuntimeAdapter Runtime,
        TrainerOrchestrator Orchestrator);
}
