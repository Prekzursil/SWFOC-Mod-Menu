using System.Net;
using System.Net.Http;
using System.Text;
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

    private static HttpClient CreateStaticJsonClient(string payload)
    {
        return new HttpClient(new StaticJsonHttpHandler(payload))
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

    private sealed class StaticJsonHttpHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
