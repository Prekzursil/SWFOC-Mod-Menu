using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Catalog.Parsing;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

public sealed class CatalogWave11CoverageTests
{
    // ── CatalogService.LoadPrebuiltCatalogAsync L177: DeserializeAsync returns null ──
    // The partial branch at L177 is the ?? coalescing on the deserialized result.
    // When the JSON file contains "null", DeserializeAsync returns null, exercising
    // the right-hand side of the ?? operator.
    [Fact]
    public async Task LoadPrebuiltCatalogAsync_NullJsonContent_ShouldReturnEmptyDictionary()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"catwave11_{Guid.NewGuid():N}");
        var profileDir = Path.Join(tempDir, "testprofile");
        Directory.CreateDirectory(profileDir);
        var catalogPath = Path.Join(profileDir, "catalog.json");
        try
        {
            // Write "null" so DeserializeAsync returns null
            await File.WriteAllTextAsync(catalogPath, "null");

            var method = typeof(CatalogService).GetMethod(
                "LoadPrebuiltCatalogAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull("LoadPrebuiltCatalogAsync must exist");

            var options = new CatalogOptions { CatalogRootPath = tempDir };
            var profileRepo = new StubProfileRepository();
            var service = new CatalogService(options, profileRepo, NullLogger<CatalogService>.Instance);

            var task = (Task<Dictionary<string, IReadOnlyList<string>>>)method!.Invoke(
                service,
                new object[] { "testprofile", CancellationToken.None })!;
            var result = await task;

            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // ── XmlObjectExtractor.cs L20: attr whose Value is non-null (already covered)
    // but the branch where attr IS null (element lacks the attribute).
    // We need elements that do NOT have any of the interesting attributes at all.
    [Fact]
    public void ExtractObjectNames_NoInterestingAttributes_ShouldReturnEmpty()
    {
        var tempPath = Path.Join(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xml");
        try
        {
            // Elements with no Name/ID/Id/Object_Name/Type attributes
            File.WriteAllText(tempPath, "<Root><Unit Foo=\"bar\" /><Ship Health=\"100\" /></Root>");
            var result = XmlObjectExtractor.ExtractObjectNames(tempPath);
            result.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // ── XmlObjectExtractor.cs L20: attr.Value?.Trim() ──
    // The ?. check for null Value - XAttribute.Value is never actually null,
    // but we can ensure the branch where the value IS present is exercised
    // with various trimming scenarios.
    [Fact]
    public void ExtractObjectNames_AttributeWithLeadingTrailingSpaces_ShouldTrim()
    {
        var tempPath = Path.Join(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tempPath, "<Root><Unit Name=\"  TROOPER  \" ID=\"  unit_2  \" /></Root>");
            var result = XmlObjectExtractor.ExtractObjectNames(tempPath);
            result.Should().Contain("TROOPER");
            result.Should().Contain("unit_2");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// Stub IProfileRepository for constructing CatalogService in tests.
    private sealed class StubProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(CreateDummyProfile(profileId));

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(CreateDummyProfile(profileId));

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        private static TrainerProfile CreateDummyProfile(string id) =>
            new(
                Id: id,
                DisplayName: "Test",
                Inherits: null,
                ExeTarget: ExeTarget.Swfoc,
                SteamWorkshopId: null,
                SignatureSets: Array.Empty<SignatureSet>(),
                FallbackOffsets: new Dictionary<string, long>(),
                Actions: new Dictionary<string, ActionSpec>(),
                FeatureFlags: new Dictionary<string, bool>(),
                CatalogSources: Array.Empty<CatalogSource>(),
                SaveSchemaId: "default",
                HelperModHooks: Array.Empty<HelperHookSpec>());
    }
}
