using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ModDependencyValidatorTests
{
    [Fact]
    public void Validate_Should_Pass_When_Local_Mod_And_Parent_Hints_Satisfy_Dependencies()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var roeRoot = Path.Combine(tempRoot, "roe-submod");
            var aotrRoot = Path.Combine(tempRoot, "aotr-parent");
            WriteMarker(roeRoot);
            WriteMarker(aotrRoot);

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111,2222222222",
                ["requiredMarkerFile"] = "Data/XML/Gameobjectfiles.xml",
                ["dependencySensitiveActions"] = "spawn_unit_helper",
                ["localParentPathHints"] = "aotr-parent"
            });

            var process = CreateProcess(roeRoot);
            var validator = new ModDependencyValidator();

            var result = validator.Validate(profile, process);

            result.Status.Should().Be(DependencyValidationStatus.Pass);
            result.DisabledActionIds.Should().BeEmpty();
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Validate_Should_SoftFail_When_Dependencies_Are_Missing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var roeRoot = Path.Combine(tempRoot, "roe-submod");
            WriteMarker(roeRoot);

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111,2222222222",
                ["requiredMarkerFile"] = "Data/XML/Gameobjectfiles.xml",
                ["dependencySensitiveActions"] = "spawn_unit_helper"
            });

            var process = CreateProcess(roeRoot);
            var validator = new ModDependencyValidator();

            var result = validator.Validate(profile, process);

            result.Status.Should().Be(DependencyValidationStatus.SoftFail);
            result.DisabledActionIds.Should().Contain("spawn_unit_helper");
            result.Message.Should().ContainEquivalentOf("missing dependencies");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Validate_Should_HardFail_On_Unsafe_Marker_Path()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopIds"] = "1111111111",
            ["requiredMarkerFile"] = "../escape/path.xml"
        });

        var process = CreateProcess(Path.Combine(Path.GetTempPath(), "dummy-mod"));
        var validator = new ModDependencyValidator();

        var result = validator.Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.HardFail);
        result.Message.Should().ContainEquivalentOf("Invalid dependency marker path");
    }

    private static TrainerProfile CreateProfile(IReadOnlyDictionary<string, string> metadata)
    {
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn_unit_helper"] = new ActionSpec(
                "spawn_unit_helper",
                ActionCategory.Unit,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0,
                Description: "helper action")
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

    private static ProcessMetadata CreateProcess(string modPath)
    {
        var process = new ProcessMetadata(
            777,
            "StarWarsG",
            Path.Combine(modPath, "..", "corruption", "StarWarsG.exe"),
            $"StarWarsG.exe MODPATH=\"{modPath}\"",
            ExeTarget.Swfoc,
            RuntimeMode.Unknown,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["detectedVia"] = "unit_test",
                ["isStarWarsG"] = "true"
            });

        var context = new LaunchContextResolver().Resolve(process, Array.Empty<TrainerProfile>());
        return process with { LaunchContext = context };
    }

    private static void WriteMarker(string root)
    {
        var markerPath = Path.Combine(root, "Data", "XML", "Gameobjectfiles.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, "<GameObjectFiles />");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // no-op in tests
        }
    }
}
