using System.Reflection;
using System.Runtime.Serialization;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelLaunchCoverageTests
{
    [Fact]
    public void BuildLaunchWorkshopIds_ShouldNormalizeAndDeduplicateTokens()
    {
        var viewModel = CreateUninitializedViewModel();
        viewModel.LaunchWorkshopId = "1397421866, 3447786229,1397421866,, 3287776766";

        var workshopIds = InvokeBuildLaunchWorkshopIds(viewModel);

        workshopIds.Should().Equal("1397421866", "3447786229", "3287776766");
    }

    [Fact]
    public async Task BuildLaunchRequestAsync_ShouldResolveProfileWorkshopChain_WhenManualListMissing()
    {
        var viewModel = CreateUninitializedViewModel();
        SetPrivateField(viewModel, "_profiles", new StubProfileRepository(BuildProfile(
            steamWorkshopId: "3447786229",
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1397421866,3447786229",
                ["parentDependencies"] = "1125571106"
            })));

        viewModel.LaunchTarget = "Sweaw";
        viewModel.LaunchMode = "SteamMod";
        viewModel.LaunchWorkshopId = string.Empty;
        viewModel.SelectedProfileId = "roe_3447786229_swfoc";
        viewModel.LaunchModPath = " Mods\\ROE ";
        viewModel.TerminateExistingBeforeLaunch = true;

        var request = await InvokeBuildLaunchRequestAsync(viewModel);

        request.Target.Should().Be(GameLaunchTarget.Sweaw);
        request.Mode.Should().Be(GameLaunchMode.SteamMod);
        request.WorkshopIds.Should().Equal("1125571106", "3447786229", "1397421866");
        viewModel.LaunchWorkshopId.Should().Be("1125571106,3447786229,1397421866");
        request.ModPath.Should().Be("Mods\\ROE");
        request.TerminateExistingTargets.Should().BeTrue();
    }

    [Fact]
    public async Task BuildLaunchRequestAsync_ShouldFallbackToManualInputs_WhenProfileLookupThrows()
    {
        var viewModel = CreateUninitializedViewModel();
        SetPrivateField(viewModel, "_profiles", new ThrowingProfileRepository());

        viewModel.LaunchTarget = "Swfoc";
        viewModel.LaunchMode = "SteamMod";
        viewModel.LaunchWorkshopId = string.Empty;
        viewModel.SelectedProfileId = "missing_profile";

        var request = await InvokeBuildLaunchRequestAsync(viewModel);

        request.Target.Should().Be(GameLaunchTarget.Swfoc);
        request.WorkshopIds.Should().BeEmpty();
        viewModel.LaunchWorkshopId.Should().BeEmpty();
    }

    [Fact]
    public async Task LaunchAndAttachAsync_ShouldSetFailureStatus_WhenLaunchFails()
    {
        var viewModel = CreateUninitializedViewModel();
        SetPrivateField(viewModel, "_gameLauncher", new StubGameLaunchService(
            new GameLaunchResult(
                Succeeded: false,
                Message: "no process",
                ProcessId: 0,
                ExecutablePath: string.Empty,
                Arguments: string.Empty)));

        viewModel.LaunchTarget = "Swfoc";
        viewModel.LaunchMode = "Vanilla";
        viewModel.SelectedProfileId = null;

        await InvokeLaunchAndAttachAsync(viewModel);

        viewModel.Status.Should().Contain("Launch failed: no process");
    }

    [Fact]
    public async Task LaunchAndAttachAsync_ShouldAdvanceToAttachPhase_WhenLaunchSucceeds()
    {
        var viewModel = CreateUninitializedViewModel();
        SetPrivateField(viewModel, "_gameLauncher", new StubGameLaunchService(
            new GameLaunchResult(
                Succeeded: true,
                Message: "started",
                ProcessId: 4242,
                ExecutablePath: @"C:\Games\swfoc.exe",
                Arguments: "STEAMMOD=1397421866")));

        viewModel.LaunchTarget = "Swfoc";
        viewModel.LaunchMode = "Vanilla";
        viewModel.SelectedProfileId = null;

        await InvokeLaunchAndAttachAsync(viewModel);

        viewModel.Status.Should().Contain("Launch started (pid=4242). Attaching...");
    }

    private static MainViewModel CreateUninitializedViewModel()
    {
#pragma warning disable SYSLIB0050
        return (MainViewModel)FormatterServices.GetUninitializedObject(typeof(MainViewModel));
#pragma warning restore SYSLIB0050
    }

    private static async Task<GameLaunchRequest> InvokeBuildLaunchRequestAsync(MainViewModel viewModel)
    {
        var method = typeof(MainViewModel).GetMethod("BuildLaunchRequestAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var task = method!.Invoke(viewModel, Array.Empty<object?>());
        task.Should().BeAssignableTo<Task<GameLaunchRequest>>();
        return await (Task<GameLaunchRequest>)task!;
    }

    private static IReadOnlyList<string> InvokeBuildLaunchWorkshopIds(MainViewModel viewModel)
    {
        var method = typeof(MainViewModel).GetMethod("BuildLaunchWorkshopIds", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var ids = method!.Invoke(viewModel, Array.Empty<object?>());
        ids.Should().BeAssignableTo<IReadOnlyList<string>>();
        return (IReadOnlyList<string>)ids!;
    }

    private static async Task InvokeLaunchAndAttachAsync(MainViewModel viewModel)
    {
        var method = typeof(MainViewModel).GetMethod("LaunchAndAttachAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var task = method!.Invoke(viewModel, Array.Empty<object?>());
        task.Should().BeAssignableTo<Task>();
        await (Task)task!;
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var type = instance.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        field.Should().NotBeNull($"field '{fieldName}' should exist in the MainViewModel inheritance graph.");
        field!.SetValue(instance, value);
    }

    private static TrainerProfile BuildProfile(string steamWorkshopId, IReadOnlyDictionary<string, string> metadata)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: steamWorkshopId,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    private sealed class StubGameLaunchService : IGameLaunchService
    {
        private readonly GameLaunchResult _result;

        public StubGameLaunchService(GameLaunchResult result)
        {
            _result = result;
        }

        public Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly TrainerProfile _profile;

        public StubProfileRepository(TrainerProfile profile)
        {
            _profile = profile;
        }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
            => Task.FromResult(_profile);

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
            => Task.FromResult(_profile);

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
    }

    private sealed class ThrowingProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("profile missing");

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("profile missing");

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
