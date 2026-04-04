using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Branch coverage for MainViewModelCreditsHelpers: TryParseCreditsValue,
/// ResolveCreditsStateTag, BuildCreditsSuccessStatus, and CreditsStatusResult.
/// </summary>
public sealed class MainViewModelCreditsHelpersBranchTests
{
    [Fact]
    public void TryParseCreditsValue_ShouldThrow_WhenNull()
    {
        var act = () => MainViewModelCreditsHelpers.TryParseCreditsValue(null!, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryParseCreditsValue_ShouldSucceed_ForValidPositiveInt()
    {
        var ok = MainViewModelCreditsHelpers.TryParseCreditsValue("1000000", out var value, out var error);
        ok.Should().BeTrue();
        value.Should().Be(1000000);
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseCreditsValue_ShouldSucceed_ForZero()
    {
        var ok = MainViewModelCreditsHelpers.TryParseCreditsValue("0", out var value, out var error);
        ok.Should().BeTrue();
        value.Should().Be(0);
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseCreditsValue_ShouldFail_ForNegative()
    {
        var ok = MainViewModelCreditsHelpers.TryParseCreditsValue("-5", out var value, out var error);
        ok.Should().BeFalse();
        value.Should().Be(0);
        error.Should().Contain("Invalid credits value");
    }

    [Fact]
    public void TryParseCreditsValue_ShouldFail_ForNonNumeric()
    {
        var ok = MainViewModelCreditsHelpers.TryParseCreditsValue("abc", out var value, out var error);
        ok.Should().BeFalse();
        value.Should().Be(0);
        error.Should().Contain("Invalid credits value");
    }

    [Fact]
    public void TryParseCreditsValue_ShouldFail_ForFloat()
    {
        var ok = MainViewModelCreditsHelpers.TryParseCreditsValue("1.5", out _, out var error);
        ok.Should().BeFalse();
        error.Should().Contain("Invalid credits value");
    }

    [Fact]
    public void ResolveCreditsStateTag_ShouldThrow_WhenResultIsNull()
    {
        var act = () => MainViewModelCreditsHelpers.ResolveCreditsStateTag(null!, false);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResolveCreditsStateTag_ShouldReturnDiagnosticTag_WhenPresent()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?> { ["creditsStateTag"] = "CUSTOM_TAG" });
        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, false);
        tag.Should().Be("CUSTOM_TAG");
    }

    [Fact]
    public void ResolveCreditsStateTag_ShouldReturnHookLock_WhenFreezeTrueAndNoDiagnostic()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature);
        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, true);
        tag.Should().Be("HOOK_LOCK");
    }

    [Fact]
    public void ResolveCreditsStateTag_ShouldReturnHookOneshot_WhenFreezeFalseAndNoDiagnostic()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature);
        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, false);
        tag.Should().Be("HOOK_ONESHOT");
    }

    [Fact]
    public void ResolveCreditsStateTag_ShouldReturnHookOneshot_WhenDiagnosticTagIsWhitespace()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?> { ["creditsStateTag"] = "  " });
        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, false);
        tag.Should().Be("HOOK_ONESHOT");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_ShouldThrow_WhenStateTagIsNull()
    {
        var act = () => MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 100, null!, "");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCreditsSuccessStatus_ShouldThrow_WhenDiagnosticsSuffixIsNull()
    {
        var act = () => MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 100, "tag", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCreditsSuccessStatus_Freeze_ShouldSucceed_WhenHookLock()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(true, 5000, "HOOK_LOCK", " [test]");
        result.IsValid.Should().BeTrue();
        result.ShouldFreeze.Should().BeTrue();
        result.StatusMessage.Should().Contain("HOOK_LOCK");
        result.StatusMessage.Should().Contain("5,000");
        result.StatusMessage.Should().Contain("[test]");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_Freeze_ShouldFail_WhenNotHookLock()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(true, 5000, "WRONG_TAG", "");
        result.IsValid.Should().BeFalse();
        result.ShouldFreeze.Should().BeFalse();
        result.StatusMessage.Should().Contain("unexpected state");
        result.StatusMessage.Should().Contain("WRONG_TAG");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_NoFreeze_ShouldSucceed_WhenHookOneshot()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 999, "HOOK_ONESHOT", "");
        result.IsValid.Should().BeTrue();
        result.ShouldFreeze.Should().BeFalse();
        result.StatusMessage.Should().Contain("HOOK_ONESHOT");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_NoFreeze_ShouldFail_WhenNotHookOneshot()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 999, "WRONG_TAG", "");
        result.IsValid.Should().BeFalse();
        result.StatusMessage.Should().Contain("unexpected state");
    }
}
