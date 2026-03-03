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
