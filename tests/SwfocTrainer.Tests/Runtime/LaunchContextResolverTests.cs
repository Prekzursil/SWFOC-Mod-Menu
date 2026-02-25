using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class LaunchContextResolverTests
{
    [Fact]
    public async Task Resolve_Should_Map_SteamMod_Roe_To_Roe_Profile()
    {
        var resolver = new LaunchContextResolver();
        var profiles = await LoadProfilesAsync();
        var process = CreateProcess(
            "StarWarsG",
            @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=3447786229");

        var context = resolver.Resolve(process, profiles);

        context.Recommendation.ProfileId.Should().Be("roe_3447786229_swfoc");
        context.Recommendation.ReasonCode.Should().Be("steammod_exact_roe");
        context.LaunchKind.Should().Be(LaunchKind.Workshop);
    }

    [Fact]
    public async Task Resolve_Should_Map_SteamMod_Aotr_To_Aotr_Profile()
    {
        var resolver = new LaunchContextResolver();
        var profiles = await LoadProfilesAsync();
        var process = CreateProcess(
            "StarWarsG",
            @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=1397421866");

        var context = resolver.Resolve(process, profiles);

        context.Recommendation.ProfileId.Should().Be("aotr_1397421866_swfoc");
        context.Recommendation.ReasonCode.Should().Be("steammod_exact_aotr");
        context.LaunchKind.Should().Be(LaunchKind.Workshop);
    }

    [Fact]
    public async Task Resolve_Should_Map_ModPath_Roe_Hints_To_Roe_Profile()
    {
        var resolver = new LaunchContextResolver();
        var profiles = await LoadProfilesAsync();
        var process = CreateProcess(
            "StarWarsG",
            @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\StarWarsG.exe",
            "StarWarsG.exe MODPATH=\"D:/mods/3447786229(submod)\"");

        var context = resolver.Resolve(process, profiles);

        context.Recommendation.ProfileId.Should().Be("roe_3447786229_swfoc");
        context.Recommendation.ReasonCode.Should().Be("modpath_hint_roe");
        context.LaunchKind.Should().Be(LaunchKind.LocalModPath);
        context.ModPathNormalized.Should().Contain("3447786229");
    }

    [Fact]
    public async Task Resolve_Should_Map_ModPath_Aotr_Hints_To_Aotr_Profile()
    {
        var resolver = new LaunchContextResolver();
        var profiles = await LoadProfilesAsync();
        var process = CreateProcess(
            "StarWarsG",
            @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\StarWarsG.exe",
            "StarWarsG.exe MODPATH=Mods\\AOTR");

        var context = resolver.Resolve(process, profiles);

        context.Recommendation.ProfileId.Should().Be("aotr_1397421866_swfoc");
        context.Recommendation.ReasonCode.Should().Be("modpath_hint_aotr");
        context.LaunchKind.Should().Be(LaunchKind.LocalModPath);
    }

    [Fact]
    public async Task Resolve_Should_Map_Sweaw_To_BaseSweaw()
    {
        var resolver = new LaunchContextResolver();
        var profiles = await LoadProfilesAsync();
        var process = new ProcessMetadata(
            42,
            "sweaw",
            @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\GameData\sweaw.exe",
            "sweaw.exe",
            ExeTarget.Sweaw,
            RuntimeMode.Unknown,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["detectedVia"] = "unit_test"
            });

        var context = resolver.Resolve(process, profiles);

        context.Recommendation.ProfileId.Should().Be("base_sweaw");
        context.Recommendation.ReasonCode.Should().Be("exe_target_sweaw");
    }

    [Fact]
    public async Task Resolve_Should_Fallback_To_BaseSwfoc_For_Ambiguous_StarWarsG()
    {
        var resolver = new LaunchContextResolver();
        var profiles = await LoadProfilesAsync();
        var process = CreateProcess(
            "StarWarsG",
            @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\StarWarsG.exe",
            null);

        var context = resolver.Resolve(process, profiles);

        context.Recommendation.ProfileId.Should().Be("base_swfoc");
        context.Recommendation.ReasonCode.Should().Be("foc_safe_starwarsg_fallback");
        context.Recommendation.Confidence.Should().BeLessThan(0.70);
    }

    [Fact]
    public async Task Resolve_Should_Prefer_AutoDiscovery_Profile_When_Exact_Workshop_Id_Is_Present()
    {
        var resolver = new LaunchContextResolver();
        var profiles = (await LoadProfilesAsync()).ToList();
        profiles.Add(BuildAutoDiscoveryProfile(
            id: "custom_enhanced_conflict_aotr_3664004146_swfoc",
            workshopId: "3664004146",
            localPathHints: "enhanced_conflict_aotr",
            requiredWorkshopIds: "3664004146,1397421866"));

        var process = CreateProcess(
            "StarWarsG",
            @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=3664004146 STEAMMOD=1397421866");

        var context = resolver.Resolve(process, profiles);

        context.Recommendation.ProfileId.Should().Be("custom_enhanced_conflict_aotr_3664004146_swfoc");
        context.Recommendation.ReasonCode.Should().Be("steammod_profile_exact");
        context.Recommendation.Confidence.Should().BeGreaterThan(0.95d);
    }

    [Fact]
    public async Task Resolve_Should_Match_AutoDiscovery_Profile_From_ModPath_Hints()
    {
        var resolver = new LaunchContextResolver();
        var profiles = (await LoadProfilesAsync()).ToList();
        profiles.Add(BuildAutoDiscoveryProfile(
            id: "custom_enhanced_conflict_aotr_3664004146_swfoc",
            workshopId: "3664004146",
            localPathHints: "enhanced_conflict_aotr,aotr_submods",
            requiredWorkshopIds: "3664004146,1397421866"));

        var process = CreateProcess(
            "StarWarsG",
            @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\StarWarsG.exe",
            "StarWarsG.exe MODPATH=Mods\\Enhanced_Conflict_AOTR");

        var context = resolver.Resolve(process, profiles);

        context.Recommendation.ProfileId.Should().Be("custom_enhanced_conflict_aotr_3664004146_swfoc");
        context.Recommendation.ReasonCode.Should().Be("modpath_profile_auto_discovery_hint");
        context.Recommendation.Confidence.Should().Be(0.90d);
    }

    private static ProcessMetadata CreateProcess(string name, string path, string? commandLine)
    {
        return new ProcessMetadata(
            9001,
            name,
            path,
            commandLine,
            ExeTarget.Swfoc,
            RuntimeMode.Unknown,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["detectedVia"] = "unit_test",
                ["isStarWarsG"] = "true",
            });
    }

    private static async Task<IReadOnlyList<TrainerProfile>> LoadProfilesAsync()
    {
        var root = TestPaths.FindRepoRoot();
        var repo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        });

        var ids = await repo.ListAvailableProfilesAsync();
        var list = new List<TrainerProfile>(ids.Count);
        foreach (var id in ids)
        {
            list.Add(await repo.ResolveInheritedProfileAsync(id));
        }

        return list;
    }

    private static TrainerProfile BuildAutoDiscoveryProfile(
        string id,
        string workshopId,
        string localPathHints,
        string requiredWorkshopIds)
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: "Auto discovery profile",
            Inherits: "base_swfoc",
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: workshopId,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "swfoc.save.v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["origin"] = "auto_discovery",
                ["confidence"] = "0.93",
                ["localPathHints"] = localPathHints,
                ["profileAliases"] = "enhanced_conflict_aotr",
                ["requiredWorkshopIds"] = requiredWorkshopIds
            },
            RequiredCapabilities: new[]
            {
                "set_credits",
                "freeze_timer",
                "toggle_fog_reveal",
                "toggle_ai",
                "set_unit_cap",
                "toggle_instant_build_patch"
            });
    }
}
