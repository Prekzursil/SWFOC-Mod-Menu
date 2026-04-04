using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for DraftBuildResult:
/// Failed and FromDraft factory methods, null guards.
/// </summary>
public sealed class MainViewModelDraftBuildResultWave5Tests
{
    [Fact]
    public void Failed_ShouldSetSucceededFalseAndNullDraft()
    {
        var result = DraftBuildResult.Failed("test error");
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("test error");
        result.Draft.Should().BeNull();
    }

    [Fact]
    public void Failed_NullMessage_ShouldThrow()
    {
        var act = () => DraftBuildResult.Failed(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromDraft_ShouldSetSucceededTrueAndDraft()
    {
        var draft = new SelectedUnitDraft(100f, 50f, 2f, 1.5f, 1f, 3, 1);
        var result = DraftBuildResult.FromDraft(draft);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be("ok");
        result.Draft.Should().BeSameAs(draft);
    }

    [Fact]
    public void FromDraft_NullDraft_ShouldThrow()
    {
        var act = () => DraftBuildResult.FromDraft(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
