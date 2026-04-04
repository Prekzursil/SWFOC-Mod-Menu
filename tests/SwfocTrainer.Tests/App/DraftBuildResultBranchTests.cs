using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Branch coverage for DraftBuildResult factory methods.
/// </summary>
public sealed class DraftBuildResultBranchTests
{
    [Fact]
    public void Failed_ShouldThrow_WhenMessageIsNull()
    {
        var act = () => DraftBuildResult.Failed(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Failed_ShouldReturnFailedResult()
    {
        var result = DraftBuildResult.Failed("some error");
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("some error");
        result.Draft.Should().BeNull();
    }

    [Fact]
    public void FromDraft_ShouldThrow_WhenDraftIsNull()
    {
        var act = () => DraftBuildResult.FromDraft(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromDraft_ShouldReturnSuccessResult()
    {
        var draft = new SelectedUnitDraft(Hp: 100f);
        var result = DraftBuildResult.FromDraft(draft);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be("ok");
        result.Draft.Should().BeSameAs(draft);
    }
}
