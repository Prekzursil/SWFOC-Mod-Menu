using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

/// <summary>
/// Verifies FoC action execution-kind and capability contracts survive inheritance resolution.
/// </summary>
public sealed class ProfileActionCatalogTests
{
    [Fact]
    public async Task BaseSwfoc_Should_Contain_Freeze_Actions()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repo = new FileSystemProfileRepository(options);
        var profile = await repo.ResolveInheritedProfileAsync("base_swfoc");

        profile.Actions.Should().ContainKey("freeze_symbol");
        profile.Actions.Should().ContainKey("unfreeze_symbol");

        var freeze = profile.Actions["freeze_symbol"];
        freeze.ExecutionKind.Should().Be(ExecutionKind.Freeze);
        freeze.PayloadSchema["required"].Should().NotBeNull();

        var unfreeze = profile.Actions["unfreeze_symbol"];
        unfreeze.ExecutionKind.Should().Be(ExecutionKind.Freeze);
    }

    [Fact]
    public async Task BaseSwfoc_Should_Route_Promoted_Actions_Via_Sdk()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repo = new FileSystemProfileRepository(options);
        var profile = await repo.ResolveInheritedProfileAsync("base_swfoc");

        profile.Actions.Should().ContainKey("freeze_timer");
        profile.Actions.Should().ContainKey("toggle_fog_reveal");
        profile.Actions.Should().ContainKey("toggle_ai");
        profile.Actions.Should().ContainKey("set_unit_cap");
        profile.Actions.Should().ContainKey("toggle_instant_build_patch");

        profile.Actions["freeze_timer"].ExecutionKind.Should().Be(ExecutionKind.Sdk);
        profile.Actions["toggle_fog_reveal"].ExecutionKind.Should().Be(ExecutionKind.Sdk);
        profile.Actions["toggle_ai"].ExecutionKind.Should().Be(ExecutionKind.Sdk);

        var cap = profile.Actions["set_unit_cap"];
        cap.ExecutionKind.Should().Be(ExecutionKind.Sdk);
        var capRequired = cap.PayloadSchema["required"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        capRequired.Should().Contain("intValue");

        var instantBuild = profile.Actions["toggle_instant_build_patch"];
        instantBuild.ExecutionKind.Should().Be(ExecutionKind.Sdk);
        var instantRequired = instantBuild.PayloadSchema["required"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        instantRequired.Should().Contain("enable");

        profile.Actions.Should().ContainKey("set_unit_cap_patch_fallback");
        profile.Actions.Should().ContainKey("toggle_fog_reveal_patch_fallback");
        profile.Actions["set_unit_cap_patch_fallback"].ExecutionKind.Should().Be(ExecutionKind.CodePatch);
        profile.Actions["toggle_fog_reveal_patch_fallback"].ExecutionKind.Should().Be(ExecutionKind.CodePatch);
        profile.Actions.Should().ContainKey("set_credits_extender_experimental");
        profile.Actions["set_credits_extender_experimental"].ExecutionKind.Should().Be(ExecutionKind.Sdk);
        profile.FeatureFlags.Should().ContainKey("allow_extender_credits");
        profile.FeatureFlags["allow_extender_credits"].Should().BeFalse();
        profile.FeatureFlags.Should().ContainKey("allow_fog_patch_fallback");
        profile.FeatureFlags["allow_fog_patch_fallback"].Should().BeFalse();
        profile.FeatureFlags.Should().ContainKey("allow_unit_cap_patch_fallback");
        profile.FeatureFlags["allow_unit_cap_patch_fallback"].Should().BeFalse();
    }

    [Fact]
    public async Task BaseSweaw_Should_Contain_Freeze_Actions()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repo = new FileSystemProfileRepository(options);
        var profile = await repo.ResolveInheritedProfileAsync("base_sweaw");

        profile.Actions.Should().ContainKey("freeze_symbol");
        profile.Actions.Should().ContainKey("unfreeze_symbol");

        profile.Actions["freeze_symbol"].ExecutionKind.Should().Be(ExecutionKind.Freeze);
        profile.Actions["unfreeze_symbol"].ExecutionKind.Should().Be(ExecutionKind.Freeze);
        profile.Actions.Should().ContainKey("set_unit_cap_patch_fallback");
        profile.Actions.Should().ContainKey("toggle_fog_reveal_patch_fallback");
        profile.Actions["set_unit_cap_patch_fallback"].ExecutionKind.Should().Be(ExecutionKind.CodePatch);
        profile.Actions["toggle_fog_reveal_patch_fallback"].ExecutionKind.Should().Be(ExecutionKind.CodePatch);
        profile.FeatureFlags["allow_fog_patch_fallback"].Should().BeFalse();
        profile.FeatureFlags["allow_unit_cap_patch_fallback"].Should().BeFalse();
    }

    [Fact]
    public async Task RoeProfile_Should_Inherit_Promoted_Sdk_Actions_From_Base()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repo = new FileSystemProfileRepository(options);
        var profile = await repo.ResolveInheritedProfileAsync("roe_3447786229_swfoc");

        // ROE inherits from AOTR which inherits from base_swfoc
        profile.Actions.Should().ContainKey("freeze_symbol");
        profile.Actions.Should().ContainKey("unfreeze_symbol");
        profile.Actions["freeze_timer"].ExecutionKind.Should().Be(ExecutionKind.Sdk);
        profile.Actions["toggle_fog_reveal"].ExecutionKind.Should().Be(ExecutionKind.Sdk);
        profile.Actions["toggle_ai"].ExecutionKind.Should().Be(ExecutionKind.Sdk);

        // Also verify promoted former CodePatch actions survive inheritance as SDK-routed actions.
        profile.Actions.Should().ContainKey("set_unit_cap");
        profile.Actions["set_unit_cap"].ExecutionKind.Should().Be(ExecutionKind.Sdk);
        profile.Actions.Should().ContainKey("toggle_instant_build_patch");
        profile.Actions["toggle_instant_build_patch"].ExecutionKind.Should().Be(ExecutionKind.Sdk);
    }

    [Fact]
    public async Task AllProfiles_Should_Have_Set_Credits_Action()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repo = new FileSystemProfileRepository(options);
        var profiles = new[] { "base_swfoc", "base_sweaw", "aotr_1397421866_swfoc", "roe_3447786229_swfoc" };

        foreach (var pid in profiles)
        {
            var profile = await repo.ResolveInheritedProfileAsync(pid);
            profile.Actions.Should().ContainKey("set_credits",
                because: $"profile '{pid}' should have set_credits for the trainer to work");
        }
    }

    [Fact]
    public async Task SwfocProfiles_Should_Route_SetCredits_ViaMemory_ByDefault()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repo = new FileSystemProfileRepository(options);
        var swfocProfiles = new[] { "base_swfoc", "aotr_1397421866_swfoc", "roe_3447786229_swfoc" };

        foreach (var pid in swfocProfiles)
        {
            var profile = await repo.ResolveInheritedProfileAsync(pid);
            profile.Actions["set_credits"].ExecutionKind.Should().Be(ExecutionKind.Memory,
                because: $"profile '{pid}' should keep managed memory as authoritative credits route");
        }
    }

    [Fact]
    public async Task SwfocProfiles_Should_Require_Promoted_Sdk_Action_Capabilities()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repo = new FileSystemProfileRepository(options);
        var swfocProfiles = new[] { "base_swfoc", "aotr_1397421866_swfoc", "roe_3447786229_swfoc" };
        var promotedSdkActions = new[]
        {
            "freeze_timer",
            "toggle_fog_reveal",
            "toggle_ai",
            "set_unit_cap",
            "toggle_instant_build_patch"
        };

        foreach (var pid in swfocProfiles)
        {
            var profile = await repo.ResolveInheritedProfileAsync(pid);
            profile.RequiredCapabilities.Should().Contain(promotedSdkActions,
                because: $"profile '{pid}' should gate promoted FoC actions behind capability contract");
        }
    }

    [Fact]
    public async Task FreezeAction_Schema_Should_Require_Symbol()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repo = new FileSystemProfileRepository(options);
        var profile = await repo.ResolveInheritedProfileAsync("base_swfoc");

        var freezeSchema = profile.Actions["freeze_symbol"].PayloadSchema;
        var required = freezeSchema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

        required.Should().Contain("symbol");
        required.Should().Contain("freeze");
    }
}
