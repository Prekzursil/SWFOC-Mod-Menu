using FluentAssertions;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class ProfileInheritanceTests
{
    [Fact]
    public async Task RoeProfile_Should_Inherit_Actions_From_Base_And_Aotr()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repository = new FileSystemProfileRepository(options);
        var profile = await repository.ResolveInheritedProfileAsync("roe_3447786229_swfoc");

        profile.Actions.Should().ContainKey("set_credits");
        profile.Actions.Should().ContainKey("set_hero_state_helper");
        profile.Actions.Should().ContainKey("toggle_roe_respawn_helper");
        profile.HelperModHooks.Should().NotBeEmpty();
        profile.FeatureFlags.Should().ContainKey("roe_profile");
        profile.HostPreference.Should().Be("starwarsg_preferred");
        profile.BackendPreference.Should().Be("auto");
        profile.RequiredCapabilities.Should().Contain("set_credits");
    }

    [Fact]
    public async Task Manifest_Should_List_All_Target_Profiles()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repository = new FileSystemProfileRepository(options);
        var manifest = await repository.LoadManifestAsync();

        manifest.Profiles.Select(x => x.Id).Should().BeEquivalentTo([
            "base_sweaw",
            "base_swfoc",
            "aotr_1397421866_swfoc",
            "roe_3447786229_swfoc",
            "universal_auto"
        ]);
    }
}
