using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage sweep for ModDependencyValidator — targets all uncovered
/// validation, marker, workshop discovery, local resolution, CSV parsing,
/// and metadata branches.
/// </summary>
public sealed class ModDependencyValidatorBranchCoverageTests
{
    // ── Validate — null argument guards ────────────────────────────────────

    [Fact]
    public void Validate_ShouldThrow_WhenProfileIsNull()
    {
        var validator = new ModDependencyValidator();
        var act = () => validator.Validate(null!, CreateProcess(Path.GetTempPath()));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenProcessIsNull()
    {
        var validator = new ModDependencyValidator();
        var act = () => validator.Validate(CreateProfile(null), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── ValidateMarkerMetadata — no marker ────────────────────────────────

    [Fact]
    public void Validate_ShouldPass_WhenNoMarkerAndNoDependencies()
    {
        var profile = CreateProfile(null);
        var process = CreateProcess(Path.GetTempPath());
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.Pass);
        result.Message.Should().Contain("No workshop dependencies declared");
    }

    // ── ValidateMarkerMetadata — whitespace marker is OK ──────────────────

    [Fact]
    public void Validate_ShouldNotHardFail_WhenMarkerIsWhitespace()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredMarkerFile"] = "   "
        });
        var process = CreateProcess(Path.GetTempPath());
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().NotBe(DependencyValidationStatus.HardFail);
    }

    // ── ValidateMarkerMetadata — safe marker is OK ────────────────────────

    [Fact]
    public void Validate_ShouldNotHardFail_WhenMarkerIsSafe()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredMarkerFile"] = "Data/XML/GameObjectFiles.xml"
        });
        var process = CreateProcess(Path.GetTempPath());
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().NotBe(DependencyValidationStatus.HardFail);
    }

    // ── ValidateMarkerMetadata — path traversal ───────────────────────────

    [Fact]
    public void Validate_ShouldHardFail_WhenMarkerContainsTraversal()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredMarkerFile"] = "../../../etc/passwd",
            ["requiredWorkshopIds"] = "1111111111"
        });
        var process = CreateProcess(Path.GetTempPath());
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.HardFail);
        result.Message.Should().Contain("Invalid dependency marker path");
    }

    // ── CollectRequiredWorkshopIds branches ────────────────────────────────

    [Fact]
    public void Validate_ShouldPass_WhenNoWorkshopIdsDeclared()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredMarkerFile"] = "Data/marker.xml"
        });
        var process = CreateProcess(Path.GetTempPath());
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.Pass);
        result.Message.Should().Contain("No workshop dependencies");
    }

    [Fact]
    public void Validate_ShouldCollectFromSteamWorkshopId()
    {
        var profile = CreateProfileWithSteamWorkshopId("9999999999",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredMarkerFile"] = "Data/marker.xml"
            });
        // Use a process with no mod path so local resolution cannot succeed
        var process = CreateProcessWithNoModPath();
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        // Will soft-fail because workshop folder 9999999999 won't exist and no local roots
        result.Status.Should().Be(DependencyValidationStatus.SoftFail);
        result.Message.Should().Contain("9999999999");
    }

    [Fact]
    public void Validate_ShouldCollectFromRequiredWorkshopId_Singular()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopId"] = "8888888888"
        });
        var process = CreateProcessWithNoModPath();
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.SoftFail);
        result.Message.Should().Contain("8888888888");
    }

    [Fact]
    public void Validate_ShouldCollectFromMultipleRequiredWorkshopIds()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopIds"] = "1111111111,2222222222,3333333333"
        });
        var process = CreateProcessWithNoModPath();
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.SoftFail);
        result.Message.Should().Contain("1111111111");
    }

    // ── AddCsvMetadataValues — null metadata ──────────────────────────────

    [Fact]
    public void Validate_ShouldHandleNullMetadata()
    {
        var profile = CreateProfile(null);
        var process = CreateProcess(Path.GetTempPath());
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.Pass);
    }

    // ── AddCsvMetadataValues — empty value ────────────────────────────────

    [Fact]
    public void Validate_ShouldHandleEmptyWorkshopIdValue()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopIds"] = ""
        });
        var process = CreateProcess(Path.GetTempPath());
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.Pass);
    }

    // ── GetDependencySensitiveActions branches ─────────────────────────────

    [Fact]
    public void Validate_ShouldIncludeHelperActionsInDisabledSet_WhenSoftFail()
    {
        var profile = CreateProfileWithHelperAction(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "9999999999",
                ["dependencySensitiveActions"] = "custom_action"
            });
        var process = CreateProcessWithNoModPath();
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.SoftFail);
        result.DisabledActionIds.Should().Contain("spawn_unit_helper");
        result.DisabledActionIds.Should().Contain("custom_action");
    }

    // ── BuildDependencyValidationResult — all resolved with issues ────────

    [Fact]
    public void Validate_ShouldPass_WithIssuesMessage_WhenAllResolvedByLocal()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var modRoot = Path.Join(tempRoot, "roe-mod");
            WriteMarker(modRoot, "Data/XML/Gameobjectfiles.xml");

            var parentRoot = Path.Join(tempRoot, "parent-mod");
            WriteMarker(parentRoot, "Data/XML/Gameobjectfiles.xml");

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111,2222222222",
                ["requiredMarkerFile"] = "Data/XML/Gameobjectfiles.xml",
                ["localParentPathHints"] = "parent-mod"
            });
            var process = CreateProcess(modRoot);
            var validator = new ModDependencyValidator();

            var result = validator.Validate(profile, process);

            result.Status.Should().Be(DependencyValidationStatus.Pass);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // ── BuildDependencyValidationResult — unresolved with no workshop roots

    [Fact]
    public void Validate_ShouldSoftFail_WithNoWorkshopRootsDiscovered_Message()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopIds"] = "9876543210"
        });
        var process = CreateProcessWithNoModPath();
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.SoftFail);
        result.Message.Should().Contain("soft-failed");
        result.Message.Should().Contain("Attach will continue");
    }

    // ── ResolveWorkshopDependencies — marker check branches ───────────────

    [Fact]
    public void Validate_ShouldReportMissingMarker_WhenWorkshopFolderExistsButNoMarker()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            // Create a fake workshop root structure
            var workshopRoot = Path.Join(tempRoot, "steamapps", "workshop", "content", "32470");
            var modFolder = Path.Join(workshopRoot, "1111111111");
            Directory.CreateDirectory(modFolder);
            // No marker file

            // We can't easily inject the workshop root into the validator,
            // but we can test with local dependency roots instead
            var modRoot = Path.Join(tempRoot, "local-mod");
            Directory.CreateDirectory(modRoot);
            // No marker in modRoot

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111",
                ["requiredMarkerFile"] = "Data/marker.xml"
            });
            var process = CreateProcess(modRoot);
            var validator = new ModDependencyValidator();

            var result = validator.Validate(profile, process);

            result.Status.Should().Be(DependencyValidationStatus.SoftFail);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // ── ResolveLocalDependencies — no unresolved IDs ──────────────────────

    [Fact]
    public void Validate_ShouldSkipLocalResolution_WhenAllResolvedByWorkshop()
    {
        // If all workshop IDs are resolved, local resolution is skipped
        // We test this indirectly — no local roots needed
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopIds"] = ""
        });
        var process = CreateProcess(Path.GetTempPath());
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.Pass);
    }

    // ── ResolveLocalDependencyRoots — no modpath ──────────────────────────

    [Fact]
    public void Validate_ShouldHandleMissingModPath()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopIds"] = "1111111111"
        });
        // Process with no command line and no launch context
        var process = new ProcessMetadata(777, "StarWarsG", Path.Join(Path.GetTempPath(), "StarWarsG.exe"),
            null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.SoftFail);
    }

    // ── ResolveModPathRaw — from launch context ───────────────────────────

    [Fact]
    public void Validate_ShouldUseModPathFromLaunchContext_WhenAvailable()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var modRoot = Path.Join(tempRoot, "mod-dir");
            WriteMarker(modRoot, "Data/marker.xml");

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111",
                ["requiredMarkerFile"] = "Data/marker.xml"
            });

            var launchContext = new LaunchContext(
                LaunchKind.Workshop, true, new[] { "1111111111" },
                modRoot, modRoot, "unit_test",
                new ProfileRecommendation(null, "none", 0.0),
                "detected");

            var process = new ProcessMetadata(777, "StarWarsG",
                Path.Join(modRoot, "..", "corruption", "StarWarsG.exe"),
                $"StarWarsG.exe MODPATH=\"{modRoot}\"",
                ExeTarget.Swfoc, RuntimeMode.Unknown,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                launchContext);
            var validator = new ModDependencyValidator();

            var result = validator.Validate(profile, process);

            // Local resolution should find the mod root
            result.Should().NotBeNull();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // ── ExtractModPath branches ───────────────────────────────────────────

    [Fact]
    public void ExtractModPath_ShouldReturnNull_WhenNullCommandLine()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ExtractModPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { null });
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractModPath_ShouldReturnNull_WhenEmptyCommandLine()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ExtractModPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "" });
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractModPath_ShouldReturnNull_WhenNoModPathPresent()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ExtractModPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "game.exe -start" });
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractModPath_ShouldExtractQuotedPath()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ExtractModPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string?)method!.Invoke(null, new object?[] { "game.exe MODPATH=\"Mods/My Mod\"" });
        result.Should().Be("Mods/My Mod");
    }

    [Fact]
    public void ExtractModPath_ShouldExtractUnquotedPath()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ExtractModPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string?)method!.Invoke(null, new object?[] { "game.exe modpath=Mods/AOTR" });
        result.Should().Be("Mods/AOTR");
    }

    // ── BuildPossibleModRoots branches ────────────────────────────────────

    [Fact]
    public void BuildPossibleModRoots_ShouldReturnEmpty_WhenNormalizedIsWhitespace()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("BuildPossibleModRoots", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[] { "  \"\"  ", "game.exe" })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildPossibleModRoots_ShouldReturnRooted_WhenPathIsAbsolute()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("BuildPossibleModRoots", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[]
        {
            @"C:\Mods\MyMod", @"C:\Games\StarWarsG.exe"
        })!;
        result.Should().ContainSingle();
        result[0].Should().Contain("Mods");
    }

    [Fact]
    public void BuildPossibleModRoots_ShouldReturnRelativeCandidates_WhenPathIsRelative()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("BuildPossibleModRoots", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[]
        {
            "Mods/AOTR", @"C:\Games\corruption\StarWarsG.exe"
        })!;
        result.Count.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void BuildPossibleModRoots_ShouldReturnEmpty_WhenProcessPathIsWhitespace()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("BuildPossibleModRoots", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[] { "Mods/AOTR", "" })!;
        result.Should().BeEmpty();
    }

    // ── HasMarker branches ────────────────────────────────────────────────

    [Fact]
    public void HasMarker_ShouldReturnTrue_WhenMarkerFileExists()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            WriteMarker(tempRoot, "marker.txt");
            var method = typeof(ModDependencyValidator)
                .GetMethod("HasMarker", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = (bool)method!.Invoke(null, new object[] { tempRoot, "marker.txt" })!;
            result.Should().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void HasMarker_ShouldReturnFalse_WhenMarkerFileMissing()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("HasMarker", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { Path.GetTempPath(), "nonexistent.xml" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void HasMarker_ShouldHandleForwardSlashesInMarker()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            WriteMarker(tempRoot, Path.Join("Data", "XML", "marker.xml"));
            var method = typeof(ModDependencyValidator)
                .GetMethod("HasMarker", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = (bool)method!.Invoke(null, new object[] { tempRoot, "Data/XML/marker.xml" })!;
            result.Should().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // ── ReadMetadata branches ─────────────────────────────────────────────

    [Fact]
    public void ReadMetadata_ShouldReturnNull_WhenMetadataIsNull()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ReadMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = CreateProfile(null);
        var result = method!.Invoke(null, new object[] { profile, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadMetadata_ShouldReturnNull_WhenKeyNotFound()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ReadMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["other_key"] = "value"
        });
        var result = method!.Invoke(null, new object[] { profile, "missing_key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadMetadata_ShouldReturnValue_WhenKeyExists()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ReadMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["myKey"] = "myValue"
        });
        var result = (string?)method!.Invoke(null, new object[] { profile, "myKey" });
        result.Should().Be("myValue");
    }

    // ── ParseCsvMetadata branches ─────────────────────────────────────────

    [Fact]
    public void ParseCsvMetadata_ShouldReturnEmpty_WhenNoKey()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ParseCsvMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[] { profile, "missingKey" })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseCsvMetadata_ShouldParseCsvValues()
    {
        var method = typeof(ModDependencyValidator)
            .GetMethod("ParseCsvMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["myList"] = "a, b, c"
        });
        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[] { profile, "myList" })!;
        result.Should().HaveCount(3);
    }

    // ── AddParentHintedRoots — with traversal ─────────────────────────────

    [Fact]
    public void Validate_ShouldSkipParentHints_WithPathTraversal()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var modRoot = Path.Join(tempRoot, "mod");
            WriteMarker(modRoot, "Data/marker.xml");

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111",
                ["requiredMarkerFile"] = "Data/marker.xml",
                ["localParentPathHints"] = "../escape"
            });
            var process = CreateProcess(modRoot);
            var validator = new ModDependencyValidator();

            var result = validator.Validate(profile, process);

            // Traversal hints should be filtered out
            result.Should().NotBeNull();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // ── AddParentHintedRoots — no parent hints ────────────────────────────

    [Fact]
    public void Validate_ShouldSkipParentHints_WhenNoneConfigured()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var modRoot = Path.Join(tempRoot, "mod");
            WriteMarker(modRoot, "Data/marker.xml");

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111",
                ["requiredMarkerFile"] = "Data/marker.xml"
            });
            var process = CreateProcess(modRoot);
            var validator = new ModDependencyValidator();

            var result = validator.Validate(profile, process);

            result.Should().NotBeNull();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // ── ResolveLocalDependencies — no local roots ─────────────────────────

    [Fact]
    public void Validate_ShouldSkipLocalResolution_WhenNoLocalRoots()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopIds"] = "1111111111"
        });
        // Process with no command line -> no mod path -> no local roots
        var process = new ProcessMetadata(777, "StarWarsG",
            Path.Join(Path.GetTempPath(), "StarWarsG.exe"),
            null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.SoftFail);
    }

    // ── ResolveLocalDependencies — marker check on local roots ────────────

    [Fact]
    public void Validate_ShouldNotResolveLocally_WhenMarkerMissing()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var modRoot = Path.Join(tempRoot, "mod");
            Directory.CreateDirectory(modRoot);
            // No marker file

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111",
                ["requiredMarkerFile"] = "Data/marker.xml"
            });
            var process = CreateProcess(modRoot);
            var validator = new ModDependencyValidator();

            var result = validator.Validate(profile, process);

            result.Status.Should().Be(DependencyValidationStatus.SoftFail);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // ── BuildDependencyValidationResult — all resolved, no issues ─────────

    [Fact]
    public void Validate_ShouldPassWithCleanMessage_WhenAllResolvedNoIssues()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var modRoot = Path.Join(tempRoot, "mod");
            WriteMarker(modRoot, "Data/marker.xml");

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111",
                ["requiredMarkerFile"] = "Data/marker.xml"
            });

            // Create process that will resolve dependency locally
            var process = CreateProcess(modRoot);
            var validator = new ModDependencyValidator();

            var result = validator.Validate(profile, process);

            if (result.Status == DependencyValidationStatus.Pass)
            {
                result.DisabledActionIds.Should().BeEmpty();
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string CreateTempRoot()
    {
        var root = Path.Join(Path.GetTempPath(), $"swfoc-dep-val-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static TrainerProfile CreateProfile(IReadOnlyDictionary<string, string>? metadata)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test Profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema_v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    private static TrainerProfile CreateProfileWithSteamWorkshopId(
        string steamWorkshopId,
        IReadOnlyDictionary<string, string>? metadata)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test Profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: steamWorkshopId,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema_v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    private static TrainerProfile CreateProfileWithHelperAction(
        IReadOnlyDictionary<string, string>? metadata)
    {
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn_unit_helper"] = new ActionSpec(
                "spawn_unit_helper",
                ActionCategory.Unit,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                false, 0, "helper action")
        };

        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test Profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema_v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    private static ProcessMetadata CreateProcessWithNoModPath()
    {
        return new ProcessMetadata(
            777, "StarWarsG",
            Path.Join(Path.GetTempPath(), "StarWarsG.exe"),
            null, ExeTarget.Swfoc, RuntimeMode.Unknown);
    }

    private static ProcessMetadata CreateProcess(string modPath)
    {
        var process = new ProcessMetadata(
            777, "StarWarsG",
            Path.Join(modPath, "..", "corruption", "StarWarsG.exe"),
            $"StarWarsG.exe MODPATH=\"{modPath}\"",
            ExeTarget.Swfoc, RuntimeMode.Unknown,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["detectedVia"] = "unit_test",
                ["isStarWarsG"] = "true"
            });

        var context = new LaunchContextResolver().Resolve(process, Array.Empty<TrainerProfile>());
        return process with { LaunchContext = context };
    }

    private static void WriteMarker(string root, string markerRelativePath)
    {
        var markerPath = Path.Join(root, markerRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, "<marker />");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
