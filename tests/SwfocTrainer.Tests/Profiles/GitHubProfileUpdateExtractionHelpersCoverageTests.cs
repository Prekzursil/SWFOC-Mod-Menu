using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class GitHubProfileUpdateExtractionHelpersCoverageTests
{
    [Fact]
    public void ProfileRepositoryOptions_ShouldExposeExpectedDefaults()
    {
        var options = new ProfileRepositoryOptions();

        options.ProfilesRootPath.Should().Contain("profiles");
        options.ManifestFileName.Should().Be("manifest.json");
        options.DownloadCachePath.Should().Contain("SwfocTrainer");
        options.RemoteManifestUrl.Should().BeNull();
    }

    [Fact]
    public void ExtractToDirectorySafely_ShouldExtractNestedFiles_AndDirectoryEntries()
    {
        var tempRoot = CreateTempDirectory();
        var zipPath = Path.Combine(tempRoot, "profiles.zip");
        var extractRoot = Path.Combine(tempRoot, "extract");

        try
        {
            CreateZip(
                zipPath,
                ("profiles/", null),
                ("profiles/base_swfoc.json", "{}"),
                ("manifest.json", "{\"profiles\":[]}"));

            InvokeExtract(zipPath, extractRoot);

            Directory.Exists(Path.Combine(extractRoot, "profiles")).Should().BeTrue();
            File.ReadAllText(Path.Combine(extractRoot, "profiles", "base_swfoc.json")).Should().Be("{}");
            File.ReadAllText(Path.Combine(extractRoot, "manifest.json")).Should().Contain("profiles");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData("C:/evil.txt", "drive-qualified")]
    [InlineData("/evil.txt", "rooted")]
    [InlineData("../evil.txt", "escapes extraction root")]
    public void ExtractToDirectorySafely_ShouldRejectUnsafePaths(string entryPath, string expectedMessage)
    {
        var tempRoot = CreateTempDirectory();
        var zipPath = Path.Combine(tempRoot, "unsafe.zip");
        var extractRoot = Path.Combine(tempRoot, "extract");

        try
        {
            CreateZip(zipPath, (entryPath, "owned"));

            var act = () => InvokeExtract(zipPath, extractRoot);

            act.Should().Throw<InvalidDataException>().WithMessage($"*{expectedMessage}*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static void InvokeExtract(string zipPath, string extractRoot)
    {
        var method = typeof(GitHubProfileUpdateService).Assembly
            .GetType("SwfocTrainer.Profiles.Services.GitHubProfileUpdateExtractionHelpers", throwOnError: true)!
            .GetMethod("ExtractToDirectorySafely", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        try
        {
            method.Invoke(null, [zipPath, extractRoot]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    private static void CreateZip(string zipPath, params (string Path, string? Content)[] entries)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (path, content) in entries)
        {
            var entry = archive.CreateEntry(path);
            if (content is null)
            {
                continue;
            }

            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "swfoctrainer-profile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
