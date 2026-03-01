using System.Text.RegularExpressions;
using FluentAssertions;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class ProfileMetadataPortabilityTests
{
    private static readonly Regex UserScopedWindowsSaveRootPattern = new(
        "\"saveRootDefault\"\\s*:\\s*\"[A-Za-z]:\\\\\\\\Users\\\\\\\\",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void DefaultProfiles_ShouldNotContain_UserSpecific_SaveRootDefaults()
    {
        var root = TestPaths.FindRepoRoot();
        var profileDirectory = Path.Combine(root, "profiles", "default", "profiles");
        var profileFiles = Directory.GetFiles(profileDirectory, "*.json", SearchOption.TopDirectoryOnly);

        profileFiles.Should().NotBeEmpty();

        foreach (var profileFile in profileFiles)
        {
            var content = File.ReadAllText(profileFile);
            UserScopedWindowsSaveRootPattern.IsMatch(content).Should().BeFalse(
                because: $"profile metadata in '{Path.GetFileName(profileFile)}' should stay portable across user accounts");
        }
    }
}
