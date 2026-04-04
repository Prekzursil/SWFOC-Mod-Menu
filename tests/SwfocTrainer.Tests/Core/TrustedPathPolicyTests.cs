using FluentAssertions;
using SwfocTrainer.Core.IO;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class TrustedPathPolicyTests
{
    [Fact]
    public void GetOrCreateAppDataRoot_ShouldReturnNonEmptyPath()
    {
        var root = TrustedPathPolicy.GetOrCreateAppDataRoot();
        root.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(root).Should().BeTrue();
    }

    [Fact]
    public void EnsureDirectory_ShouldThrow_WhenPathIsNull()
    {
        var act = () => TrustedPathPolicy.EnsureDirectory(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EnsureDirectory_ShouldCreateAndReturnNormalizedPath()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"tpp-test-{Guid.NewGuid():N}");
        try
        {
            var result = TrustedPathPolicy.EnsureDirectory(tempDir);
            result.Should().NotBeNullOrWhiteSpace();
            Directory.Exists(result).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void NormalizeAbsolute_ShouldThrow_WhenPathIsEmpty()
    {
        var act = () => TrustedPathPolicy.NormalizeAbsolute("");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NormalizeAbsolute_ShouldThrow_WhenPathIsWhitespace()
    {
        var act = () => TrustedPathPolicy.NormalizeAbsolute("   ");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NormalizeAbsolute_ShouldReturnFullPath()
    {
        var result = TrustedPathPolicy.NormalizeAbsolute(Path.GetTempPath());
        result.Should().NotBeNullOrWhiteSpace();
        Path.IsPathFullyQualified(result).Should().BeTrue();
    }

    [Fact]
    public void CombineUnderRoot_ShouldThrow_WhenRootPathIsNull()
    {
        var act = () => TrustedPathPolicy.CombineUnderRoot(null!, "segment");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CombineUnderRoot_ShouldThrow_WhenSegmentsIsNull()
    {
        var act = () => TrustedPathPolicy.CombineUnderRoot(Path.GetTempPath(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CombineUnderRoot_ShouldSkipEmptySegments()
    {
        var root = Path.GetTempPath();
        var result = TrustedPathPolicy.CombineUnderRoot(root, "", "sub", "  ", "child");
        result.Should().Contain("sub");
        result.Should().Contain("child");
    }

    [Fact]
    public void CombineUnderRoot_ShouldThrow_WhenResultEscapesRoot()
    {
        var root = Path.Join(Path.GetTempPath(), "testroot");
        var act = () => TrustedPathPolicy.CombineUnderRoot(root, "..", "..", "..", "etc");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureSubPath_ShouldThrow_WhenRootPathIsNull()
    {
        var act = () => TrustedPathPolicy.EnsureSubPath(null!, Path.GetTempPath());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EnsureSubPath_ShouldThrow_WhenCandidatePathIsNull()
    {
        var act = () => TrustedPathPolicy.EnsureSubPath(Path.GetTempPath(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EnsureSubPath_ShouldThrow_WhenCandidateIsOutsideRoot()
    {
        var root = Path.Join(Path.GetTempPath(), "root-a");
        var candidate = Path.Join(Path.GetTempPath(), "root-b", "child");
        var act = () => TrustedPathPolicy.EnsureSubPath(root, candidate);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureSubPath_ShouldReturnNormalized_WhenCandidateIsUnderRoot()
    {
        var root = Path.GetTempPath();
        var candidate = Path.Join(root, "child", "nested");
        var result = TrustedPathPolicy.EnsureSubPath(root, candidate);
        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void IsSubPath_ShouldThrow_WhenRootIsNull()
    {
        var act = () => TrustedPathPolicy.IsSubPath(null!, Path.GetTempPath());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsSubPath_ShouldThrow_WhenCandidateIsNull()
    {
        var act = () => TrustedPathPolicy.IsSubPath(Path.GetTempPath(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsSubPath_ShouldReturnTrue_WhenPathsAreEqual()
    {
        var path = Path.GetTempPath();
        TrustedPathPolicy.IsSubPath(path, path).Should().BeTrue();
    }

    [Fact]
    public void IsSubPath_ShouldReturnTrue_WhenCandidateIsUnderRoot()
    {
        var root = Path.GetTempPath();
        var candidate = Path.Join(root, "child");
        TrustedPathPolicy.IsSubPath(root, candidate).Should().BeTrue();
    }

    [Fact]
    public void IsSubPath_ShouldReturnFalse_WhenCandidateIsOutsideRoot()
    {
        var root = Path.Join(Path.GetTempPath(), "rootA");
        var candidate = Path.Join(Path.GetTempPath(), "rootB");
        TrustedPathPolicy.IsSubPath(root, candidate).Should().BeFalse();
    }

    [Fact]
    public void EnsureAllowedExtension_ShouldThrow_WhenPathIsNull()
    {
        var act = () => TrustedPathPolicy.EnsureAllowedExtension(null!, ".txt");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EnsureAllowedExtension_ShouldThrow_WhenExtensionsArrayIsNull()
    {
        var act = () => TrustedPathPolicy.EnsureAllowedExtension("file.txt", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EnsureAllowedExtension_ShouldNotThrow_WhenNoExtensionsSpecified()
    {
        var act = () => TrustedPathPolicy.EnsureAllowedExtension("file.txt");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureAllowedExtension_ShouldNotThrow_WhenExtensionMatches()
    {
        var act = () => TrustedPathPolicy.EnsureAllowedExtension("file.sav", ".sav", ".json");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureAllowedExtension_ShouldThrow_WhenExtensionDoesNotMatch()
    {
        var act = () => TrustedPathPolicy.EnsureAllowedExtension("file.exe", ".sav", ".json");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureAllowedExtension_ShouldThrow_WhenPathHasNoExtension()
    {
        var act = () => TrustedPathPolicy.EnsureAllowedExtension("filewithnoext", ".sav");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildSiblingFilePath_ShouldThrow_WhenSourcePathIsNull()
    {
        var act = () => TrustedPathPolicy.BuildSiblingFilePath(null!, "_backup");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildSiblingFilePath_ShouldThrow_WhenSuffixIsNull()
    {
        var act = () => TrustedPathPolicy.BuildSiblingFilePath(Path.Join(Path.GetTempPath(), "file.sav"), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildSiblingFilePath_ShouldReturnSiblingWithSuffix()
    {
        var source = Path.Join(Path.GetTempPath(), "campaign.sav");
        var result = TrustedPathPolicy.BuildSiblingFilePath(source, ".edited");
        result.Should().Contain("campaign.edited.sav");
    }
}
