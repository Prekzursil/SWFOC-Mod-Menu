using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class ModConflictDetectorServiceTests
{
    private static readonly ILogger<ModConflictDetectorService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<ModConflictDetectorService>();

    // --- DetectConflictsAsync with real filesystem ---

    [Fact]
    public async Task DetectConflictsAsync_ConflictingMods_ReturnsConflictEntries()
    {
        var root = CreateTempRoot();
        try
        {
            var modA = CreateModWithXml(root, "ModA", "<Root><Unit Name=\"AT_AT\" /></Root>");
            var modB = CreateModWithXml(root, "ModB", "<Root><Unit Name=\"AT_AT\" /></Root>");

            var service = new ModConflictDetectorService(NullLogger);

            var result = await service.DetectConflictsAsync(
                new[] { modA, modB }, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].EntityId.Should().Be("AT_AT");
            result[0].ModSource1.Should().Be(modA);
            result[0].ModSource2.Should().Be(modB);
            result[0].ConflictType.Should().Be("duplicate_entity");
            result[0].Details.Should().Contain("AT_AT");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DetectConflictsAsync_NoConflicts_ReturnsEmptyList()
    {
        var root = CreateTempRoot();
        try
        {
            var modA = CreateModWithXml(root, "ModA", "<Root><Unit Name=\"AT_AT\" /></Root>");
            var modB = CreateModWithXml(root, "ModB", "<Root><Unit Name=\"X_WING\" /></Root>");

            var service = new ModConflictDetectorService(NullLogger);

            var result = await service.DetectConflictsAsync(
                new[] { modA, modB }, CancellationToken.None);

            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DetectConflictsAsync_SingleModPath_ReturnsEmptyList()
    {
        var service = new ModConflictDetectorService(NullLogger);

        var result = await service.DetectConflictsAsync(
            new[] { "C:\\fake\\mod" }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_EmptyModPaths_ReturnsEmptyList()
    {
        var service = new ModConflictDetectorService(NullLogger);

        var result = await service.DetectConflictsAsync(
            Array.Empty<string>(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_NonExistentPaths_ReturnsEmptyList()
    {
        var service = new ModConflictDetectorService(NullLogger);

        var result = await service.DetectConflictsAsync(
            new[] { "C:\\nonexistent\\modA", "C:\\nonexistent\\modB" },
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_WhitespacePathsSkipped_ReturnsEmptyList()
    {
        var service = new ModConflictDetectorService(NullLogger);

        var result = await service.DetectConflictsAsync(
            new[] { "", "  ", null! }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectConflictsAsync_MalformedXml_SkipsFileGracefully()
    {
        var root = CreateTempRoot();
        try
        {
            var modA = Path.Combine(root, "ModA");
            Directory.CreateDirectory(modA);
            File.WriteAllText(
                Path.Combine(modA, "units.xml"),
                "<<<NOT VALID XML>>>");

            var modB = CreateModWithXml(root, "ModB", "<Root><Unit Name=\"AT_AT\" /></Root>");

            var service = new ModConflictDetectorService(NullLogger);

            var result = await service.DetectConflictsAsync(
                new[] { modA, modB }, CancellationToken.None);

            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DetectConflictsAsync_ThreeModsWithOverlap_FindsAllPairConflicts()
    {
        var root = CreateTempRoot();
        try
        {
            var modA = CreateModWithXml(root, "ModA", "<Root><Unit Name=\"SHARED\" /></Root>");
            var modB = CreateModWithXml(root, "ModB", "<Root><Unit Name=\"SHARED\" /></Root>");
            var modC = CreateModWithXml(root, "ModC", "<Root><Unit Name=\"SHARED\" /></Root>");

            var service = new ModConflictDetectorService(NullLogger);

            var result = await service.DetectConflictsAsync(
                new[] { modA, modB, modC }, CancellationToken.None);

            result.Should().HaveCount(3);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- DetectDuplicateEntities unit tests ---

    [Fact]
    public void DetectDuplicateEntities_WithOverlap_ReturnsConflicts()
    {
        var dict1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AT_AT"] = "units.xml",
            ["X_WING"] = "units.xml"
        };
        var dict2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AT_AT"] = "custom_units.xml",
            ["TIE_FIGHTER"] = "fighters.xml"
        };

        var result = ModConflictDetectorService.DetectDuplicateEntities(
            dict1, dict2, "ModA", "ModB");

        result.Should().HaveCount(1);
        result[0].EntityId.Should().Be("AT_AT");
        result[0].ModSource1.Should().Be("ModA");
        result[0].ModSource2.Should().Be("ModB");
        result[0].Details.Should().Contain("units.xml");
        result[0].Details.Should().Contain("custom_units.xml");
    }

    [Fact]
    public void DetectDuplicateEntities_NoOverlap_ReturnsEmpty()
    {
        var dict1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AT_AT"] = "units.xml"
        };
        var dict2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X_WING"] = "units.xml"
        };

        var result = ModConflictDetectorService.DetectDuplicateEntities(
            dict1, dict2, "ModA", "ModB");

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectDuplicateEntities_EmptyDictionaries_ReturnsEmpty()
    {
        var result = ModConflictDetectorService.DetectDuplicateEntities(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            "ModA",
            "ModB");

        result.Should().BeEmpty();
    }

    // --- Null guards ---

    [Fact]
    public async Task DetectConflictsAsync_NullModPaths_ThrowsArgumentNullException()
    {
        var service = new ModConflictDetectorService(NullLogger);

        var act = () => service.DetectConflictsAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("modPaths");
    }

    [Fact]
    public void DetectDuplicateEntities_NullEntities1_ThrowsArgumentNullException()
    {
        var act = () => ModConflictDetectorService.DetectDuplicateEntities(
            null!,
            new Dictionary<string, string>(),
            "A",
            "B");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("entities1");
    }

    [Fact]
    public void DetectDuplicateEntities_NullEntities2_ThrowsArgumentNullException()
    {
        var act = () => ModConflictDetectorService.DetectDuplicateEntities(
            new Dictionary<string, string>(),
            null!,
            "A",
            "B");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("entities2");
    }

    [Fact]
    public void DetectDuplicateEntities_NullSource1_ThrowsArgumentNullException()
    {
        var act = () => ModConflictDetectorService.DetectDuplicateEntities(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            null!,
            "B");

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("source1");
    }

    [Fact]
    public void DetectDuplicateEntities_NullSource2_ThrowsArgumentNullException()
    {
        var act = () => ModConflictDetectorService.DetectDuplicateEntities(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            "A",
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("source2");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new ModConflictDetectorService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // --- Default overload ---

    [Fact]
    public async Task DetectConflictsAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IModConflictDetectorService service = new ModConflictDetectorService(NullLogger);

        var result = await service.DetectConflictsAsync(Array.Empty<string>());

        result.Should().BeEmpty();
    }

    // --- Cancellation token mid-scan ---

    [Fact]
    public async Task DetectConflictsAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var root = CreateTempRoot();
        try
        {
            var modA = CreateModWithXml(root, "ModA", "<Root><Unit Name=\"SHARED\" /></Root>");
            var modB = CreateModWithXml(root, "ModB", "<Root><Unit Name=\"SHARED\" /></Root>");

            var service = new ModConflictDetectorService(NullLogger);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => service.DetectConflictsAsync(
                new[] { modA, modB }, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- Directory with no XML files ---

    [Fact]
    public async Task DetectConflictsAsync_DirectoryWithNoXmlFiles_ReturnsEmptyList()
    {
        var root = CreateTempRoot();
        try
        {
            var modA = Path.Combine(root, "ModA");
            Directory.CreateDirectory(modA);
            File.WriteAllText(Path.Combine(modA, "readme.txt"), "no xml here");

            var modB = Path.Combine(root, "ModB");
            Directory.CreateDirectory(modB);
            File.WriteAllText(Path.Combine(modB, "notes.txt"), "also no xml");

            var service = new ModConflictDetectorService(NullLogger);

            var result = await service.DetectConflictsAsync(
                new[] { modA, modB }, CancellationToken.None);

            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- XML with no Name attributes ---

    [Fact]
    public async Task DetectConflictsAsync_XmlWithNoNameAttributes_ReturnsEmptyList()
    {
        var root = CreateTempRoot();
        try
        {
            var modA = CreateModWithXml(root, "ModA", "<Root><Unit Id=\"AT_AT\" /></Root>");
            var modB = CreateModWithXml(root, "ModB", "<Root><Unit Id=\"AT_AT\" /></Root>");

            var service = new ModConflictDetectorService(NullLogger);

            var result = await service.DetectConflictsAsync(
                new[] { modA, modB }, CancellationToken.None);

            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- XML with whitespace-only Name attribute ---

    [Fact]
    public async Task DetectConflictsAsync_XmlWithWhitespaceNameAttribute_SkipsEntry()
    {
        var root = CreateTempRoot();
        try
        {
            var modA = CreateModWithXml(root, "ModA", "<Root><Unit Name=\"   \" /></Root>");
            var modB = CreateModWithXml(root, "ModB", "<Root><Unit Name=\"   \" /></Root>");

            var service = new ModConflictDetectorService(NullLogger);

            var result = await service.DetectConflictsAsync(
                new[] { modA, modB }, CancellationToken.None);

            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- XML with lowercase "name" attribute (alternate casing) ---

    [Fact]
    public async Task DetectConflictsAsync_LowercaseNameAttribute_DetectsConflicts()
    {
        var root = CreateTempRoot();
        try
        {
            var modA = CreateModWithXml(root, "ModA", "<Root><Unit name=\"AT_AT\" /></Root>");
            var modB = CreateModWithXml(root, "ModB", "<Root><Unit name=\"AT_AT\" /></Root>");

            var service = new ModConflictDetectorService(NullLogger);

            var result = await service.DetectConflictsAsync(
                new[] { modA, modB }, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].EntityId.Should().Be("AT_AT");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- XML with null root element ---

    [Fact]
    public async Task DetectConflictsAsync_EmptyXmlDocument_HandledGracefully()
    {
        var root = CreateTempRoot();
        try
        {
            // An XML doc with only a declaration and no root element is malformed
            // and will trigger the XmlException catch branch
            var modA = Path.Combine(root, "ModA");
            Directory.CreateDirectory(modA);
            File.WriteAllText(Path.Combine(modA, "units.xml"), "<?xml version=\"1.0\"?>");

            var modB = CreateModWithXml(root, "ModB", "<Root><Unit Name=\"AT_AT\" /></Root>");

            var service = new ModConflictDetectorService(NullLogger);

            var result = await service.DetectConflictsAsync(
                new[] { modA, modB }, CancellationToken.None);

            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- IOException from GetXmlFiles (ScanModPath catch block) ---

    [Fact]
    public async Task DetectConflictsAsync_GetXmlFilesThrowsIOException_SkipsModGracefully()
    {
        var fs = new StubModFileSystem();
        fs.DirectoryExistsResult = true;
        fs.GetXmlFilesAction = _ => throw new IOException("disk read error");

        var service = new ModConflictDetectorService(NullLogger, fs);

        var result = await service.DetectConflictsAsync(
            new[] { "C:\\modA", "C:\\modB" }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    // --- XDocument with null root element ---

    [Fact]
    public async Task DetectConflictsAsync_XmlDocWithNullRoot_SkipsFileGracefully()
    {
        var fs = new StubModFileSystem();
        fs.DirectoryExistsResult = true;
        fs.GetXmlFilesResult = new[] { "units.xml" };
        // XDocument with no root element: new XDocument() produces doc.Root == null
        fs.LoadXmlResult = new XDocument();

        var service = new ModConflictDetectorService(NullLogger, fs);

        var result = await service.DetectConflictsAsync(
            new[] { "C:\\modA", "C:\\modB" }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    // --- Internal constructor null fileSystem guard ---

    [Fact]
    public void Constructor_NullFileSystem_ThrowsArgumentNullException()
    {
        var act = () => new ModConflictDetectorService(NullLogger, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fileSystem");
    }

    // --- Helpers ---

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateModWithXml(string root, string modName, string xmlContent)
    {
        var modDir = Path.Combine(root, modName);
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "units.xml"), xmlContent);
        return modDir;
    }

    /// <summary>
    /// Stub implementation of IModFileSystem for unit-testing paths that
    /// cannot be reached through the real filesystem easily (IOException
    /// from GetXmlFiles, XDocument with null root, etc.).
    /// </summary>
    private sealed class StubModFileSystem : IModFileSystem
    {
        public bool DirectoryExistsResult { get; set; }
        public string[]? GetXmlFilesResult { get; set; }
        public Func<string, string[]>? GetXmlFilesAction { get; set; }
        public XDocument? LoadXmlResult { get; set; }
        public Func<string, XDocument>? LoadXmlAction { get; set; }

        public bool DirectoryExists(string path) => DirectoryExistsResult;

        public string[] GetXmlFiles(string directoryPath)
        {
            if (GetXmlFilesAction is not null)
                return GetXmlFilesAction(directoryPath);
            return GetXmlFilesResult ?? Array.Empty<string>();
        }

        public XDocument LoadXml(string filePath)
        {
            if (LoadXmlAction is not null)
                return LoadXmlAction(filePath);
            return LoadXmlResult ?? new XDocument();
        }
    }
}
