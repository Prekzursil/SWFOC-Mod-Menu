using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

public sealed class CatalogServiceVisualCoverageTests
{
    [Fact]
    public async Task LoadTypedCatalogAsync_ShouldRespectMaxParsedXmlFiles_AndFallbackFactionAffiliation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var dataRoot = Path.Combine(root, "Data");
            Directory.CreateDirectory(dataRoot);

            var factionsPath = Path.Combine(dataRoot, "Factions.xml");
            var unitsPath = Path.Combine(dataRoot, "Units.xml");
            await File.WriteAllTextAsync(
                factionsPath,
                """
                <Root>
                  <Faction Name="EMPIRE">
                    <Text_ID>TEXT_EMPIRE</Text_ID>
                  </Faction>
                </Root>
                """);
            await File.WriteAllTextAsync(
                unitsPath,
                """
                <Root>
                  <LandUnit Name="EMPIRE_STORMTROOPER_SQUAD">
                    <Text_ID>TEXT_STORMTROOPER</Text_ID>
                    <Affiliation>EMPIRE</Affiliation>
                  </LandUnit>
                </Root>
                """);

            var service = new CatalogService(
                new CatalogOptions
                {
                    CatalogRootPath = root,
                    MaxParsedXmlFiles = 1
                },
                new StubProfileRepository(CreateProfile("limit_profile", [new CatalogSource("xml", factionsPath), new CatalogSource("xml", unitsPath)])),
                NullLogger<CatalogService>.Instance);

            var snapshot = await service.LoadTypedCatalogAsync("limit_profile", CancellationToken.None);

            snapshot.Entities.Should().ContainSingle();
            var faction = snapshot.Entities.Single();
            faction.EntityId.Should().Be("EMPIRE");
            faction.Kind.Should().Be(CatalogEntityKind.Faction);
            faction.Affiliations.Should().ContainSingle("EMPIRE");
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
    public void ResolveVisualReference_ShouldHandleRootedPaths_AndParentSearchRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-visual-roots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var xmlDirectory = Path.Combine(root, "Data", "XML");
            Directory.CreateDirectory(xmlDirectory);
            var sourcePath = Path.Combine(xmlDirectory, "Objects.xml");
            File.WriteAllText(sourcePath, "<Root />");

            var relativeIcon = Path.Combine(root, "Art", "Textures", "UI", "i_relative.tga");
            Directory.CreateDirectory(Path.GetDirectoryName(relativeIcon)!);
            File.WriteAllText(relativeIcon, "relative");

            var rootedIcon = Path.Combine(root, "rooted.tga");
            File.WriteAllText(rootedIcon, "rooted");

            InvokePrivateStatic<string?>("ResolveVisualReference", sourcePath, "i_relative.tga").Should().Be(relativeIcon);
            InvokePrivateStatic<string?>("ResolveVisualReference", sourcePath, rootedIcon).Should().Be(rootedIcon);
            InvokePrivateStatic<string?>("ResolveVisualReference", sourcePath, Path.Combine(root, "missing.tga")).Should().BeNull();
            InvokePrivateStatic<string?>("ResolveVisualReference", "Objects.xml", "i_relative.tga").Should().BeNull();
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
    public void CatalogHelperSelectionMethods_ShouldEscalateVisualAndCompatibilityState()
    {
        InvokePrivateStatic<CatalogEntityVisualState>("ResolveVisualState", null, null)
            .Should().Be(CatalogEntityVisualState.Unknown);
        InvokePrivateStatic<CatalogEntityVisualState>("ResolveVisualState", "i_missing.tga", null)
            .Should().Be(CatalogEntityVisualState.Missing);
        InvokePrivateStatic<CatalogEntityVisualState>("ResolveVisualState", "i_ok.tga", @"C:\Art\i_ok.tga")
            .Should().Be(CatalogEntityVisualState.Resolved);

        InvokePrivateStatic<CatalogEntityVisualState>(
            "SelectVisualState",
            CatalogEntityVisualState.Unknown,
            CatalogEntityVisualState.Resolved).Should().Be(CatalogEntityVisualState.Resolved);

        InvokePrivateStatic<CatalogEntityCompatibilityState>(
            "SelectCompatibilityState",
            CatalogEntityCompatibilityState.Unknown,
            CatalogEntityCompatibilityState.Blocked).Should().Be(CatalogEntityCompatibilityState.Blocked);

        InvokePrivateStatic<string?>("ChooseValue", "EMPIRE", "REBEL", "EMPIRE").Should().Be("REBEL");
    }

    private static TrainerProfile CreateProfile(string profileId, IReadOnlyList<CatalogSource> catalogSources)
    {
        return new TrainerProfile(
            Id: profileId,
            DisplayName: "catalog visual test profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: catalogSources,
            SaveSchemaId: "schema_v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static T InvokePrivateStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(CatalogService).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull($"Expected helper '{methodName}'");
        return (T)method!.Invoke(null, args)!;
    }

    private sealed class StubProfileRepository(TrainerProfile profile) : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(profile);
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(profile);
        }

        public Task ValidateProfileAsync(TrainerProfile profileToValidate, CancellationToken cancellationToken)
        {
            _ = profileToValidate;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult((IReadOnlyList<string>)new[] { profile.Id });
        }
    }
}
