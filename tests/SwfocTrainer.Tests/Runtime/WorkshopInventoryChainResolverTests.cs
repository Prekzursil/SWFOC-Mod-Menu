using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class WorkshopInventoryChainResolverTests
{
    [Fact]
    public void ResolveChains_ShouldReturnEmpty_WhenNoItemsExist()
    {
        WorkshopInventoryChainResolver.ResolveChains(Array.Empty<WorkshopInventoryItem>())
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void ResolveChains_ShouldCreateSingleIndependentChain_WhenItemHasNoParents()
    {
        var item = new WorkshopInventoryItem(
            WorkshopId: "100",
            Title: "Independent",
            ItemType: WorkshopItemType.Mod,
            ParentWorkshopIds: Array.Empty<string>(),
            Tags: Array.Empty<string>(),
            ClassificationReason: "independent_mod");

        var chains = WorkshopInventoryChainResolver.ResolveChains(new[] { item });

        chains.Should().ContainSingle();
        chains[0].OrderedWorkshopIds.Should().Equal("100");
        chains[0].ClassificationReason.Should().Be("independent_mod");
        chains[0].MissingParentIds.Should().BeEmpty();
    }

    [Fact]
    public void ResolveChains_ShouldRecordMissingParents_WhenNoResolvedParentExists()
    {
        var item = new WorkshopInventoryItem(
            WorkshopId: "200",
            Title: "Child",
            ItemType: WorkshopItemType.Submod,
            ParentWorkshopIds: new[] { "999" },
            Tags: new[] { "Submod" },
            ClassificationReason: "parent_dependency");

        var chains = WorkshopInventoryChainResolver.ResolveChains(new[] { item });

        chains.Should().ContainSingle();
        chains[0].OrderedWorkshopIds.Should().Equal("200");
        chains[0].ClassificationReason.Should().Be("parent_dependency_missing");
        chains[0].MissingParentIds.Should().Equal("999");
    }

    [Fact]
    public void ResolveChains_ShouldBuildParentFirstChain_ForResolvedAncestors()
    {
        var parent = new WorkshopInventoryItem(
            WorkshopId: "100",
            Title: "Parent",
            ItemType: WorkshopItemType.Mod,
            ParentWorkshopIds: Array.Empty<string>(),
            Tags: Array.Empty<string>(),
            ClassificationReason: "independent_mod");
        var child = new WorkshopInventoryItem(
            WorkshopId: "200",
            Title: "Child",
            ItemType: WorkshopItemType.Submod,
            ParentWorkshopIds: new[] { "100" },
            Tags: new[] { "Submod" },
            ClassificationReason: "parent_dependency");
        var grandchild = new WorkshopInventoryItem(
            WorkshopId: "300",
            Title: "Grandchild",
            ItemType: WorkshopItemType.Submod,
            ParentWorkshopIds: new[] { "200" },
            Tags: new[] { "Submod" },
            ClassificationReason: "parent_dependency");

        var chains = WorkshopInventoryChainResolver.ResolveChains(new[] { grandchild, child, parent });

        chains.Should().Contain(x => x.OrderedWorkshopIds.SequenceEqual(new[] { "100", "200" }));
        chains.Should().Contain(x => x.OrderedWorkshopIds.SequenceEqual(new[] { "100", "200", "300" }));
    }

    [Fact]
    public void ResolveChains_ShouldMarkPartialMissing_WhenSomeParentsResolve()
    {
        var parent = new WorkshopInventoryItem(
            WorkshopId: "100",
            Title: "Parent",
            ItemType: WorkshopItemType.Mod,
            ParentWorkshopIds: Array.Empty<string>(),
            Tags: Array.Empty<string>(),
            ClassificationReason: "independent_mod");
        var child = new WorkshopInventoryItem(
            WorkshopId: "400",
            Title: "Child",
            ItemType: WorkshopItemType.Submod,
            ParentWorkshopIds: new[] { "100", "999" },
            Tags: new[] { "Submod" },
            ClassificationReason: "parent_dependency");

        var chains = WorkshopInventoryChainResolver.ResolveChains(new[] { parent, child });

        chains.Should().ContainSingle(x => x.OrderedWorkshopIds.SequenceEqual(new[] { "100", "400" }));
        var chain = chains.Single(x => x.OrderedWorkshopIds.SequenceEqual(new[] { "100", "400" }));
        chain.ClassificationReason.Should().Be("parent_dependency_partial_missing");
        chain.MissingParentIds.Should().Equal("999");
    }
}
