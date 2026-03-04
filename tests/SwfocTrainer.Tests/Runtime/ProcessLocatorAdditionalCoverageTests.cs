#pragma warning disable CA1014
using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ProcessLocatorAdditionalCoverageTests
{
    [Fact]
    public void GetProcessDetection_ShouldUseCmdlineModMarkersHeuristic()
    {
        var detection = InvokePrivateStatic("GetProcessDetection", "custom.exe", @"C:\Games\custom.exe", "custom.exe MODPATH=Mods/AOTR");

        detection.Should().NotBeNull();
        ReadProperty<ExeTarget>(detection!, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection!, "DetectedVia").Should().Be("cmdline_mod_markers");
    }

    [Fact]
    public void GetProcessDetection_ShouldReturnUnknown_WhenNoHintsPresent()
    {
        var detection = InvokePrivateStatic("GetProcessDetection", "custom.exe", @"C:\Games\custom.exe", "");

        detection.Should().NotBeNull();
        ReadProperty<ExeTarget>(detection!, "ExeTarget").Should().Be(ExeTarget.Unknown);
        ReadProperty<string>(detection!, "DetectedVia").Should().Be("unknown");
    }

    [Theory]
    [InlineData("starwarsg.exe sweaw.exe", ExeTarget.Sweaw, "starwarsg_cmdline_sweaw_hint")]
    [InlineData("starwarsg.exe steammod=123", ExeTarget.Swfoc, "starwarsg_cmdline_foc_hint")]
    [InlineData("starwarsg.exe modpath=Mods/abc", ExeTarget.Swfoc, "starwarsg_cmdline_foc_hint")]
    [InlineData("starwarsg.exe corruption", ExeTarget.Swfoc, "starwarsg_cmdline_foc_hint")]
    public void TryDetectStarWarsGFromCommandLine_ShouldMapHints(string commandLine, ExeTarget expectedTarget, string expectedVia)
    {
        var detection = InvokePrivateStatic("TryDetectStarWarsGFromCommandLine", commandLine);

        detection.Should().NotBeNull();
        ReadProperty<ExeTarget>(detection!, "ExeTarget").Should().Be(expectedTarget);
        ReadProperty<string>(detection!, "DetectedVia").Should().Be(expectedVia);
    }

    [Fact]
    public void TryDetectStarWarsGFromCommandLine_ShouldReturnNull_WhenNoMatchingHints()
    {
        var detection = InvokePrivateStatic("TryDetectStarWarsGFromCommandLine", "starwarsg.exe --windowed");

        detection.Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\Games\Corruption\StarWarsG.exe", "", "starwarsg_path_corruption")]
    [InlineData(@"C:\Games\GameData\StarWarsG.exe", "", "starwarsg_path_gamedata_foc_safe")]
    [InlineData(@"C:\Games\Unknown\StarWarsG.exe", "", "starwarsg_default_foc_safe")]
    public void TryDetectStarWarsG_ShouldUsePathFallbacks(string processPath, string commandLine, string expectedVia)
    {
        var detection = InvokePrivateStatic("TryDetectStarWarsG", "StarWarsG.exe", processPath, commandLine);

        detection.Should().NotBeNull();
        ReadProperty<ExeTarget>(detection!, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection!, "DetectedVia").Should().Be(expectedVia);
    }

    [Fact]
    public void TryDetectStarWarsG_ShouldReturnNull_WhenProcessIsNotStarWarsG()
    {
        var detection = InvokePrivateStatic("TryDetectStarWarsG", "other.exe", @"C:\Games\other.exe", "");

        detection.Should().BeNull();
    }

    [Theory]
    [InlineData("sweaw.exe", @"C:\Game\sweaw.exe", null, true)]
    [InlineData("swfoc", @"C:\Game\swfoc.exe", null, true)]
    [InlineData("unknown", @"C:\Game\abc.exe", "contains swfoc.exe", true)]
    [InlineData("unknown", @"C:\Game\abc.exe", null, false)]
    public void TryDetectDirectTarget_ShouldMatchKnownTargets(string processName, string processPath, string? commandLine, bool expectedFound)
    {
        var method = typeof(ProcessLocator).GetMethod("TryDetectDirectTarget", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var invokeArgs = new object?[] { processName, processPath, commandLine, null };
        var found = (bool)method!.Invoke(null, invokeArgs)!;

        found.Should().Be(expectedFound);
    }

    [Fact]
    public void UtilityMethods_ShouldCoverNameAndTokenBranches()
    {
        ((bool)InvokePrivateStatic("ContainsToken", "abc DEF", "def")!).Should().BeTrue();
        ((bool)InvokePrivateStatic("ContainsToken", null, "def")!).Should().BeFalse();

        ((bool)InvokePrivateStatic("IsProcessName", "swfoc.exe", "swfoc")!).Should().BeTrue();
        ((bool)InvokePrivateStatic("IsProcessName", " SWFOC ", "swfoc")!).Should().BeFalse();
        ((bool)InvokePrivateStatic("IsProcessName", null, "swfoc")!).Should().BeFalse();
    }

    [Fact]
    public void NormalizeWorkshopIds_ShouldDeduplicateSortAndIgnoreWhitespace()
    {
        var ids = (IReadOnlyList<string>)InvokePrivateStatic(
            "NormalizeWorkshopIds",
            (IReadOnlyList<string>)new[] { " 3447786229,1397421866 ", "", "1397421866", "  " })!;

        ids.Should().Equal("1397421866", "3447786229");
    }

    [Fact]
    public void DetermineHostRole_ShouldMapStarWarsGAndLauncherCases()
    {
        var starWarsGDetection = InvokePrivateStatic("GetProcessDetection", "StarWarsG.exe", @"C:\Games\Corruption\StarWarsG.exe", "");
        var sweawDetection = InvokePrivateStatic("GetProcessDetection", "sweaw.exe", @"C:\Games\sweaw.exe", "");
        var unknownDetection = InvokePrivateStatic("GetProcessDetection", "random.exe", @"C:\Games\random.exe", "");

        ((ProcessHostRole)InvokePrivateStatic("DetermineHostRole", starWarsGDetection)!).Should().Be(ProcessHostRole.GameHost);
        ((ProcessHostRole)InvokePrivateStatic("DetermineHostRole", sweawDetection)!).Should().Be(ProcessHostRole.Launcher);
        ((ProcessHostRole)InvokePrivateStatic("DetermineHostRole", unknownDetection)!).Should().Be(ProcessHostRole.Unknown);
    }

    [Fact]
    public void ResolveForcedContext_ShouldReturnDetected_WhenNoForcedHints()
    {
        var resolution = InvokePrivateStatic(
            "ResolveForcedContext",
            "StarWarsG.exe",
            null,
            new[] { "1397421866" },
            ProcessLocatorOptions.None);

        resolution.Should().NotBeNull();
        ReadProperty<string>(resolution!, "Source").Should().Be("detected");
        ReadStringSequenceProperty(resolution!, "EffectiveSteamModIds").Should().Equal("1397421866");
    }

    [Fact]
    public async Task FindSupportedProcessesAsync_ShouldEnumerateProcesses_WithForcedOptionsWithoutThrowing()
    {
        var repository = new CountingProfileRepository();
        var locator = new ProcessLocator(new LaunchContextResolver(), repository);

        var options = new ProcessLocatorOptions(
            ForcedWorkshopIds: new[] { "3447786229", "1397421866" },
            ForcedProfileId: "roe_3447786229_swfoc");

        var processes = await locator.FindSupportedProcessesAsync(options, CancellationToken.None);

        processes.Should().NotBeNull();
        repository.ListCalls.Should().BeGreaterOrEqualTo(1);

        if (processes.Count > 0)
        {
            processes[0].Metadata.Should().NotBeNull();
            processes[0].Metadata!.Should().ContainKey("launchContextSource");
        }
    }

    [Fact]
    public async Task LoadProfilesForLaunchContextAsync_ShouldCacheProfilesWithinTtlWindow()
    {
        var repository = new CountingProfileRepository();
        var locator = new ProcessLocator(new LaunchContextResolver(), repository);

        var first = await InvokeLoadProfiles(locator);
        var second = await InvokeLoadProfiles(locator);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        repository.ListCalls.Should().Be(1);
        repository.ResolveCalls.Should().Be(1);
    }

    [Fact]
    public async Task LoadProfilesForLaunchContextAsync_ShouldReturnEmpty_WhenRepositoryThrows()
    {
        var locator = new ProcessLocator(new LaunchContextResolver(), new ThrowingProfileRepository());

        var profiles = await InvokeLoadProfiles(locator);

        profiles.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" base_swfoc ", "base_swfoc")]
    public void NormalizeForcedProfileId_ShouldTrimOrReturnNull(string? raw, string? expected)
    {
        var normalized = (string?)InvokePrivateStatic("NormalizeForcedProfileId", raw);
        normalized.Should().Be(expected);
    }

    private static async Task<IReadOnlyList<TrainerProfile>> InvokeLoadProfiles(ProcessLocator locator)
    {
        var method = typeof(ProcessLocator).GetMethod("LoadProfilesForLaunchContextAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var task = method!.Invoke(locator, new object?[] { CancellationToken.None });
        task.Should().BeAssignableTo<Task<IReadOnlyList<TrainerProfile>>>();

        return await (Task<IReadOnlyList<TrainerProfile>>)task!;
    }

    private static object? InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(ProcessLocator).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected private static method '{methodName}'");
        return method!.Invoke(null, args);
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull();
        return (T)property!.GetValue(instance)!;
    }

    private static IReadOnlyList<string> ReadStringSequenceProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull();
        return ((IEnumerable<string>)property!.GetValue(instance)!).ToArray();
    }

    private static TrainerProfile BuildProfile(string id)
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: id,
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets:
            [
                new SignatureSet(
                    Name: "default",
                    GameBuild: "build",
                    Signatures:
                    [
                        new SignatureSpec("credits", "AA BB", 0)
                    ])
            ],
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class CountingProfileRepository : IProfileRepository
    {
        private readonly TrainerProfile _profile = BuildProfile("base_swfoc");

        public int ListCalls { get; private set; }
        public int ResolveCalls { get; private set; }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_profile);
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            ResolveCalls++;
            return Task.FromResult(_profile);
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            _ = profile;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            ListCalls++;
            return Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
        }
    }

    private sealed class ThrowingProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new InvalidOperationException("manifest unavailable");
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new InvalidOperationException("profile unavailable");
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new InvalidOperationException("resolve unavailable");
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            _ = profile;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new InvalidOperationException("list unavailable");
        }
    }
}

#pragma warning restore CA1014
