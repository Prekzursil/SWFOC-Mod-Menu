using FluentAssertions;
using SwfocTrainer.Core.IO;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class TrustedPathPolicyTests
{
    [Fact]
    public void NormalizeAbsolute_ShouldThrow_WhenPathIsEmpty()
    {
        var action = () => TrustedPathPolicy.NormalizeAbsolute("   ");
        action.Should().Throw<InvalidOperationException>().WithMessage("*cannot be empty*");
    }

    [Fact]
    public void EnsureDirectory_ShouldCreateAndReturnNormalizedPath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"trusted-policy-{Guid.NewGuid():N}");
        try
        {
            var created = TrustedPathPolicy.EnsureDirectory(Path.Combine(tempRoot, "nested", "."));
            Directory.Exists(created).Should().BeTrue();
            Path.IsPathRooted(created).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void CombineUnderRoot_ShouldAllowSubpaths_AndRejectTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trusted-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var combined = TrustedPathPolicy.CombineUnderRoot(root, "mods", "", "AOTR");
            combined.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            TrustedPathPolicy.IsSubPath(root, combined).Should().BeTrue();

            var traversal = () => TrustedPathPolicy.CombineUnderRoot(root, "..", "outside");
            traversal.Should().Throw<InvalidOperationException>().WithMessage("*outside trusted root*");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IsSubPath_ShouldHandleRootAndSiblingCases()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"trusted-root-{Guid.NewGuid():N}"));
        var child = Path.Combine(root, "child");
        var sibling = root + "_sibling";

        TrustedPathPolicy.IsSubPath(root, root).Should().BeTrue();
        TrustedPathPolicy.IsSubPath(root, child).Should().BeTrue();
        TrustedPathPolicy.IsSubPath(root, sibling).Should().BeFalse();
    }

    [Fact]
    public void EnsureAllowedExtension_ShouldValidateSupportedExtensions()
    {
        var allowed = () => TrustedPathPolicy.EnsureAllowedExtension("bundle.json", ".json", ".md");
        allowed.Should().NotThrow();

        var missingExt = () => TrustedPathPolicy.EnsureAllowedExtension("bundle", ".json");
        missingExt.Should().Throw<InvalidOperationException>().WithMessage("*allowed file extension*");

        var unsupported = () => TrustedPathPolicy.EnsureAllowedExtension("bundle.txt", ".json");
        unsupported.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported extension*");

        var noRestrictions = () => TrustedPathPolicy.EnsureAllowedExtension("bundle", Array.Empty<string>());
        noRestrictions.Should().NotThrow();
    }

    [Fact]
    public void BuildSiblingFilePath_ShouldReturnSibling_AndRejectRootPath()
    {
        var file = Path.Combine(Path.GetTempPath(), $"trusted-file-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "{}");
        try
        {
            var sibling = TrustedPathPolicy.BuildSiblingFilePath(file, "-copy");
            sibling.EndsWith("-copy.json", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            Path.GetDirectoryName(sibling).Should().Be(Path.GetDirectoryName(Path.GetFullPath(file)));

            var rootPath = Path.GetPathRoot(file) ?? file;
            var action = () => TrustedPathPolicy.BuildSiblingFilePath(rootPath, "-bad");
            action.Should().Throw<InvalidOperationException>().WithMessage("*Cannot resolve directory*");
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
