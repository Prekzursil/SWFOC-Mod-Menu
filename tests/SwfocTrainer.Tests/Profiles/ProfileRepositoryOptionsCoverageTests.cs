using FluentAssertions;
using SwfocTrainer.Profiles.Config;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class ProfileRepositoryOptionsCoverageTests
{
    [Fact]
    public void ProfileRepositoryOptions_ShouldExposeStableDefaults()
    {
        var options = new ProfileRepositoryOptions();

        options.ProfilesRootPath.Should().NotBeNullOrWhiteSpace();
        options.ProfilesRootPath.Should().Contain("profiles");
        options.ManifestFileName.Should().Be("manifest.json");
        options.DownloadCachePath.Should().NotBeNullOrWhiteSpace();
        options.DownloadCachePath.Should().Contain("SwfocTrainer");
        options.RemoteManifestUrl.Should().BeNull();
    }
}
