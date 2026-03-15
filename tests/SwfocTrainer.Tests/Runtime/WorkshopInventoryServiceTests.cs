using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class WorkshopInventoryServiceTests
{
    [Fact]
    public async Task DiscoverInstalledAsync_ShouldCollectInstalledIds_FromManifestAndWorkshopRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = Path.Combine(tempRoot, "appworkshop_32470.acf");
            await File.WriteAllTextAsync(
                manifestPath,
                "\"AppWorkshop\"\n{\n  \"WorkshopItemsInstalled\"\n  {\n    \"1397421866\" { }\n    \"3447786229\" { }\n  }\n}\n");

            var workshopRoot = Path.Combine(tempRoot, "content", "32470");
            Directory.CreateDirectory(workshopRoot);
            Directory.CreateDirectory(Path.Combine(workshopRoot, "3287776766"));

            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance);
            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: false),
                CancellationToken.None);

            result.Items.Select(x => x.WorkshopId)
                .Should()
                .BeEquivalentTo(new[] { "1397421866", "3447786229", "3287776766" });
            result.Items.Should().OnlyContain(x => x.ItemType == WorkshopItemType.Unknown);
            result.Diagnostics.Should().Contain(x => x.Contains("manifest="));
            result.Chains.Should().NotBeNull();
            result.Chains!.Should().Contain(x => x.OrderedWorkshopIds.SequenceEqual(new[] { "1397421866" }));
            result.Chains.Should().Contain(x => x.OrderedWorkshopIds.SequenceEqual(new[] { "3287776766" }));
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
    public async Task DiscoverInstalledAsync_ShouldClassifySubmod_WhenChildrenDeclareParentDependency()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = await WriteSingleItemManifestAsync(tempRoot, "3447786229");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "3447786229");
            using var httpClient = CreateStaticJsonClient(DetailsPayloadWithParentInChildren);
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);

            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            var item = result.Items.Should().ContainSingle().Subject;
            item.ItemType.Should().Be(WorkshopItemType.Submod);
            item.ClassificationReason.Should().Be("parent_dependency");
            item.ParentWorkshopIds.Should().ContainSingle().Which.Should().Be("1397421866");
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
    public async Task DiscoverInstalledAsync_ShouldUseDescriptionFallback_AndTagSubmodClassification()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = await WriteSingleItemManifestAsync(tempRoot, "5555555555");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "5555555555");
            using var httpClient = CreateStaticJsonClient(DetailsPayloadWithDescriptionFallbackAndTags);
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);

            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            var item = result.Items.Should().ContainSingle().Subject;
            item.Description.Should().Be("Uses description fallback");
            item.Tags.Should().Contain("Submod");
            item.ItemType.Should().Be(WorkshopItemType.Submod);
            item.ClassificationReason.Should().Be("tag_submod_unknown_parent");
            item.ParentWorkshopIds.Should().BeEmpty();
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
    public async Task DiscoverInstalledAsync_ShouldResolveParentFirstChain_WhenParentAndChildInstalled()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = Path.Combine(tempRoot, "appworkshop_32470.acf");
            await File.WriteAllTextAsync(
                manifestPath,
                "\"AppWorkshop\"\n{\n  \"WorkshopItemsInstalled\"\n  {\n    \"1397421866\" { }\n    \"3447786229\" { }\n  }\n}\n");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "1397421866", "3447786229");
            using var httpClient = CreateStaticJsonClient(DetailsPayloadWithParentAndChildInstalled);
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);

            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            result.Chains.Should().Contain(x => x.OrderedWorkshopIds.SequenceEqual(new[] { "1397421866", "3447786229" }));
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
    public async Task DiscoverInstalledAsync_ShouldMarkPartialMissingReason_WhenOnlySomeParentsResolve()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = Path.Combine(tempRoot, "appworkshop_32470.acf");
            await File.WriteAllTextAsync(
                manifestPath,
                "\"AppWorkshop\"\n{\n  \"WorkshopItemsInstalled\"\n  {\n    \"1397421866\" { }\n    \"3661482670\" { }\n  }\n}\n");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "1397421866", "3661482670");
            using var httpClient = CreateStaticJsonClient(DetailsPayloadWithPartialMissingParents);
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);

            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            var chain = result.Chains.Should()
                .ContainSingle(x => x.OrderedWorkshopIds.SequenceEqual(new[] { "1397421866", "3661482670" }))
                .Subject;
            chain.ClassificationReason.Should().Be("parent_dependency_partial_missing");
            chain.MissingParentIds.Should().Contain("9999999999");
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
    public async Task DiscoverInstalledAsync_ShouldReturnEmpty_WhenNoManifestOrWorkshopContentFound()
    {
        var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance);
        var result = await service.DiscoverInstalledAsync(
            new WorkshopInventoryRequest(
                AppId: "32470",
                ManifestPath: Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.acf"),
                WorkshopContentRootPath: Path.Combine(Path.GetTempPath(), $"missing-root-{Guid.NewGuid():N}"),
                FetchRemoteMetadata: false),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.Diagnostics.Should().Contain("No installed workshop IDs were discovered from manifest/content roots.");
        result.Chains.Should().NotBeNull();
        result.Chains.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverInstalledAsync_ShouldIgnoreNonNumericWorkshopDirectories()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var workshopRoot = Path.Combine(tempRoot, "content", "32470");
            Directory.CreateDirectory(workshopRoot);
            Directory.CreateDirectory(Path.Combine(workshopRoot, "1234567890"));
            Directory.CreateDirectory(Path.Combine(workshopRoot, "abc123"));
            Directory.CreateDirectory(Path.Combine(workshopRoot, "9876x"));

            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance);
            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: Path.Combine(tempRoot, "missing.acf"),
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: false),
                CancellationToken.None);

            result.Items.Select(x => x.WorkshopId).Should().Equal("1234567890");
            result.Diagnostics.Should().Contain("manifest_missing");
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
    public async Task DiscoverInstalledAsync_ShouldNotIncludeUnknownParentIds_InResolvedChains()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = await WriteSingleItemManifestAsync(tempRoot, "2313576303");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "2313576303");
            using var httpClient = CreateStaticJsonClient(DetailsPayloadWithMissingParent);
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);
            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            AssertMissingParentChain(result);
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
    public async Task DiscoverInstalledAsync_ShouldUseEnvironmentManifestAndWorkshopRoot_WhenRequestPathsAreOmitted()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var previousManifest = Environment.GetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH");
        var previousWorkshopRoot = Environment.GetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT");
        try
        {
            var manifestPath = await WriteSingleItemManifestAsync(tempRoot, "7777777777");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "8888888888");
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH", manifestPath);
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT", workshopRoot);

            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance);
            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: " ",
                    ManifestPath: null,
                    WorkshopContentRootPath: null,
                    FetchRemoteMetadata: false),
                CancellationToken.None);

            result.AppId.Should().Be("32470");
            result.Items.Select(x => x.WorkshopId)
                .Should()
                .BeEquivalentTo(new[] { "7777777777", "8888888888" });
            result.Diagnostics.Should().Contain($"manifest={manifestPath}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH", previousManifest);
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT", previousWorkshopRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DiscoverInstalledAsync_ShouldRecordHttpFailure_WhenMetadataFetchReturnsNonSuccessStatus()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = await WriteSingleItemManifestAsync(tempRoot, "1234123412");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "1234123412");
            using var httpClient = CreateStaticJsonClient("{}", HttpStatusCode.ServiceUnavailable);
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);

            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            result.Diagnostics.Should().Contain("details_fetch_http_503 batch_start=0");
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
    public async Task DiscoverInstalledAsync_ShouldRecordFetchFailure_WhenMetadataFetchThrows()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = await WriteSingleItemManifestAsync(tempRoot, "1234000000");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "1234000000");
            using var httpClient = CreateThrowingJsonClient(new HttpRequestException("network down"));
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);

            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            result.Diagnostics.Should().Contain(x => x.StartsWith("details_fetch_failed batch_start=0 message=network down", StringComparison.Ordinal));
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
    public async Task DiscoverInstalledAsync_ShouldRecordParseFailure_WhenMetadataPayloadIsInvalidJson()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = await WriteSingleItemManifestAsync(tempRoot, "1000000001");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "1000000001");
            using var httpClient = CreateStaticJsonClient("{\"response\":");
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);

            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            result.Diagnostics.Should().Contain(x => x.StartsWith("details_parse_failed batch_start=0", StringComparison.Ordinal));
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
    public async Task DiscoverInstalledAsync_ShouldRecordMissingPayload_WhenDetailsArrayIsAbsent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = await WriteSingleItemManifestAsync(tempRoot, "1000000002");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "1000000002");
            using var httpClient = CreateStaticJsonClient("{\"response\":{}}");
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);

            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            result.Diagnostics.Should().Contain("details_missing_payload batch_start=0");
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
    public async Task DiscoverInstalledAsync_ShouldClassifySubmod_WhenSubmodKeywordIsPresent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-workshop-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manifestPath = await WriteSingleItemManifestAsync(tempRoot, "6666666666");
            var workshopRoot = CreateWorkshopRoot(tempRoot, "6666666666");
            using var httpClient = CreateStaticJsonClient("""
{
  "response": {
    "publishedfiledetails": [
      {
        "publishedfileid": "6666666666",
        "title": "Submod package",
        "file_description": "",
        "tags": []
      }
    ]
  }
}
""");
            var service = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance, httpClient);

            var result = await service.DiscoverInstalledAsync(
                new WorkshopInventoryRequest(
                    AppId: "32470",
                    ManifestPath: manifestPath,
                    WorkshopContentRootPath: workshopRoot,
                    FetchRemoteMetadata: true),
                CancellationToken.None);

            var item = result.Items.Should().ContainSingle().Subject;
            item.ItemType.Should().Be(WorkshopItemType.Submod);
            item.ClassificationReason.Should().Be("keyword_submod_unknown_parent");
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
    public void BuildManifestCandidates_ShouldPreferEnvironmentOverride_WhenRequestPathMissing()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH", @"C:\tmp\manifest.acf");
            var request = new WorkshopInventoryRequest(AppId: "32470", ManifestPath: null, WorkshopContentRootPath: null);

            var candidates = InvokePrivateStatic<IEnumerable<string>>("BuildManifestCandidates", request, "32470").ToArray();

            candidates.Should().ContainSingle().Which.Should().Be(@"C:\tmp\manifest.acf");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH", previous);
        }
    }

    [Fact]
    public void BuildWorkshopContentCandidates_ShouldPreferEnvironmentOverride_WhenRequestRootMissing()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT", @"C:\tmp\workshop\content\32470");
            var request = new WorkshopInventoryRequest(AppId: "32470", ManifestPath: null, WorkshopContentRootPath: null);

            var candidates = InvokePrivateStatic<IEnumerable<string>>("BuildWorkshopContentCandidates", request, "32470").ToArray();

            candidates.Should().ContainSingle().Which.Should().Be(@"C:\tmp\workshop\content\32470");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT", previous);
        }
    }

    [Fact]
    public void BuildManifestCandidates_ShouldUseDefaultSteamRootFallback_WhenNoOverridesProvided()
    {
        var previousManifest = Environment.GetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH");
        var previousSteamRoot = Environment.GetEnvironmentVariable("STEAM_INSTALL_PATH");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH", null);
            Environment.SetEnvironmentVariable("STEAM_INSTALL_PATH", @"D:\SteamLibrary");
            var request = new WorkshopInventoryRequest(AppId: "32470", ManifestPath: null, WorkshopContentRootPath: null);

            var candidates = InvokePrivateStatic<IEnumerable<string>>("BuildManifestCandidates", request, "32470").ToArray();

            candidates.Should().Contain(path =>
                path.StartsWith(@"D:\SteamLibrary", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(@"steamapps\workshop\appworkshop_32470.acf", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_MANIFEST_PATH", previousManifest);
            Environment.SetEnvironmentVariable("STEAM_INSTALL_PATH", previousSteamRoot);
        }
    }

    [Fact]
    public void BuildWorkshopContentCandidates_ShouldUseDefaultSteamRootFallback_WhenNoOverridesProvided()
    {
        var previousContent = Environment.GetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT");
        var previousSteamRoot = Environment.GetEnvironmentVariable("STEAM_INSTALL_PATH");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT", null);
            Environment.SetEnvironmentVariable("STEAM_INSTALL_PATH", @"E:\SteamRoot");
            var request = new WorkshopInventoryRequest(AppId: "32470", ManifestPath: null, WorkshopContentRootPath: null);

            var candidates = InvokePrivateStatic<IEnumerable<string>>("BuildWorkshopContentCandidates", request, "32470").ToArray();

            candidates.Should().Contain(path =>
                path.StartsWith(@"E:\SteamRoot", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(@"steamapps\workshop\content\32470", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_WORKSHOP_CONTENT_ROOT", previousContent);
            Environment.SetEnvironmentVariable("STEAM_INSTALL_PATH", previousSteamRoot);
        }
    }

    [Fact]
    public void EnumerateDefaultSteamRoots_ShouldIncludeEnvironmentVariablePath()
    {
        var previousSteamRoot = Environment.GetEnvironmentVariable("STEAM_INSTALL_PATH");
        try
        {
            Environment.SetEnvironmentVariable("STEAM_INSTALL_PATH", @"  F:\SteamRoot  ");

            var roots = InvokePrivateStatic<IEnumerable<string>>("EnumerateDefaultSteamRoots").ToArray();

            roots.Should().Contain(@"F:\SteamRoot");
            roots.Should().OnlyHaveUniqueItems();
        }
        finally
        {
            Environment.SetEnvironmentVariable("STEAM_INSTALL_PATH", previousSteamRoot);
        }
    }

    [Fact]
    public void ResolveDescription_And_ParseTags_ShouldReturnEmptyValues_WhenFieldsAreMissing()
    {
        using var detailDoc = JsonDocument.Parse("""{"publishedfileid":"777"}""");
        var detail = detailDoc.RootElement;

        var description = InvokePrivateStatic<string>("ResolveDescription", detail);
        var tags = InvokePrivateStatic<IReadOnlyList<string>>("ParseTags", detail);

        description.Should().BeEmpty();
        tags.Should().BeEmpty();
    }

    [Fact]
    public void TryGetDetailsArray_ShouldReturnFalse_WhenResponsePayloadMissing()
    {
        using var payload = JsonDocument.Parse("""{"root":{}}""");
        var method = typeof(WorkshopInventoryService).GetMethod("TryGetDetailsArray", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { payload, null };
        var parsed = (bool)method!.Invoke(null, args)!;

        parsed.Should().BeFalse();
    }

    [Fact]
    public void TryGetDetailsArray_ShouldReturnFalse_WhenPublishedFileDetailsNodeIsNotArray()
    {
        using var payload = JsonDocument.Parse("""{"response":{"publishedfiledetails":{}}}""");
        var method = typeof(WorkshopInventoryService).GetMethod("TryGetDetailsArray", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { payload, null };
        var parsed = (bool)method!.Invoke(null, args)!;

        parsed.Should().BeFalse();
    }

    [Fact]
    public void BuildPostForm_ShouldEmitIndexedIdsAndItemCount()
    {
        var method = typeof(WorkshopInventoryService).GetMethod("BuildPostForm", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var form = (Dictionary<string, string>)method!.Invoke(null, new object?[] { new[] { "1397421866", "3447786229" } })!;

        form.Should().Contain("itemcount", "2");
        form.Should().Contain("publishedfileids[0]", "1397421866");
        form.Should().Contain("publishedfileids[1]", "3447786229");
    }

    [Fact]
    public void TryMapItem_ShouldReturnFalse_WhenPublishedFileIdIsMissingOrWhitespace()
    {
        var method = typeof(WorkshopInventoryService).GetMethod("TryMapItem", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        using var missingIdDoc = JsonDocument.Parse("""{"title":"mod"}""");
        var argsMissing = new object?[] { missingIdDoc.RootElement, null };
        var missingResult = (bool)method!.Invoke(null, argsMissing)!;

        using var whitespaceIdDoc = JsonDocument.Parse("""{"publishedfileid":"   ","title":"mod"}""");
        var argsWhitespace = new object?[] { whitespaceIdDoc.RootElement, null };
        var whitespaceResult = (bool)method.Invoke(null, argsWhitespace)!;

        missingResult.Should().BeFalse();
        whitespaceResult.Should().BeFalse();
    }

    [Fact]
    public void ResolveChains_ShouldReturnEmpty_ForEmptyItemCollection()
    {
        var resolverType = typeof(WorkshopInventoryService).Assembly.GetType("SwfocTrainer.Runtime.Services.WorkshopInventoryChainResolver");
        resolverType.Should().NotBeNull();
        var resolveMethod = resolverType!.GetMethod("ResolveChains", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        resolveMethod.Should().NotBeNull();

        var chains = (IReadOnlyList<WorkshopInventoryChain>)resolveMethod!.Invoke(null, new object?[] { Array.Empty<WorkshopInventoryItem>() })!;

        chains.Should().BeEmpty();
    }

    [Fact]
    public void AddChainIfUnique_ShouldSkipEmptyAndDuplicateChains()
    {
        var resolverType = typeof(WorkshopInventoryService).Assembly.GetType("SwfocTrainer.Runtime.Services.WorkshopInventoryChainResolver");
        resolverType.Should().NotBeNull();
        var addMethod = resolverType!.GetMethod("AddChainIfUnique", BindingFlags.NonPublic | BindingFlags.Static);
        addMethod.Should().NotBeNull();

        var chains = new List<WorkshopInventoryChain>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingParents = Array.Empty<string>();

        addMethod!.Invoke(null, new object?[] { Array.Empty<string>(), "none", missingParents, chains, seen });
        addMethod.Invoke(null, new object?[] { new[] { "1397421866" }, "independent_mod", missingParents, chains, seen });
        addMethod.Invoke(null, new object?[] { new[] { "1397421866" }, "independent_mod", missingParents, chains, seen });

        chains.Should().ContainSingle();
        chains[0].ChainId.Should().Be("1397421866");
    }

    private static T InvokePrivateStatic<T>(string methodName, params object?[] arguments)
    {
        var method = typeof(WorkshopInventoryService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"private static method '{methodName}' should exist.");
        return (T)method!.Invoke(null, arguments)!;
    }

    private static async Task<string> WriteSingleItemManifestAsync(string tempRoot, string workshopId)
    {
        var manifestPath = Path.Combine(tempRoot, "appworkshop_32470.acf");
        await File.WriteAllTextAsync(
            manifestPath,
            "\"AppWorkshop\"\n{\n  \"WorkshopItemsInstalled\"\n  {\n    \"" + workshopId + "\" { }\n  }\n}\n");
        return manifestPath;
    }

    private static string CreateWorkshopRoot(string tempRoot, string workshopId)
    {
        var workshopRoot = Path.Combine(tempRoot, "content", "32470");
        Directory.CreateDirectory(workshopRoot);
        Directory.CreateDirectory(Path.Combine(workshopRoot, workshopId));
        return workshopRoot;
    }

    private static string CreateWorkshopRoot(string tempRoot, params string[] workshopIds)
    {
        var workshopRoot = Path.Combine(tempRoot, "content", "32470");
        Directory.CreateDirectory(workshopRoot);
        foreach (var workshopId in workshopIds)
        {
            Directory.CreateDirectory(Path.Combine(workshopRoot, workshopId));
        }

        return workshopRoot;
    }

    private static HttpClient CreateStaticJsonClient(string payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpClient(new StaticJsonHttpHandler(payload, statusCode))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    private static HttpClient CreateThrowingJsonClient(Exception exception)
    {
        return new HttpClient(new ThrowingHttpHandler(exception))
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    private static void AssertMissingParentChain(WorkshopInventoryGraph result)
    {
        var chain = result.Chains.Should()
            .ContainSingle(x => x.OrderedWorkshopIds.SequenceEqual(new[] { "2313576303" }))
            .Subject;
        result.Chains.Should().NotContain(x => x.OrderedWorkshopIds.Contains("2486018498"));
        chain.ClassificationReason.Should().Be("parent_dependency_missing");
        chain.MissingParentIds.Should().BeEquivalentTo(new[] { "2486018498" });
    }

    private const string DetailsPayloadWithMissingParent = """
{
  "response": {
    "publishedfiledetails": [
      {
        "publishedfileid": "2313576303",
        "title": "Submod Child",
        "file_description": "Requires parent STEAMMOD=2486018498",
        "tags": []
      }
    ]
  }
}
""";

    private const string DetailsPayloadWithParentInChildren = """
{
  "response": {
    "publishedfiledetails": [
      {
        "publishedfileid": "3447786229",
        "title": "ROE submod",
        "file_description": "",
        "children": [
          { "publishedfileid": "1397421866" }
        ],
        "tags": []
      }
    ]
  }
}
""";

    private const string DetailsPayloadWithDescriptionFallbackAndTags = """
{
  "response": {
    "publishedfiledetails": [
      {
        "publishedfileid": "5555555555",
        "title": "Tag-only submod",
        "description": "Uses description fallback",
        "tags": [
          { "tag": "Submod" }
        ]
      }
    ]
  }
}
""";

    private const string DetailsPayloadWithParentAndChildInstalled = """
{
  "response": {
    "publishedfiledetails": [
      {
        "publishedfileid": "1397421866",
        "title": "AOTR",
        "file_description": "",
        "tags": []
      },
      {
        "publishedfileid": "3447786229",
        "title": "ROE submod",
        "file_description": "",
        "children": [
          { "publishedfileid": "1397421866" }
        ],
        "tags": []
      }
    ]
  }
}
""";

    private const string DetailsPayloadWithPartialMissingParents = """
{
  "response": {
    "publishedfiledetails": [
      {
        "publishedfileid": "1397421866",
        "title": "AOTR",
        "file_description": "",
        "tags": []
      },
      {
        "publishedfileid": "3661482670",
        "title": "Complex submod",
        "file_description": "Requires STEAMMOD=9999999999",
        "children": [
          { "publishedfileid": "1397421866" }
        ],
        "tags": []
      }
    ]
  }
}
""";

    private sealed class StaticJsonHttpHandler(string payload, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            throw exception;
        }
    }
}
