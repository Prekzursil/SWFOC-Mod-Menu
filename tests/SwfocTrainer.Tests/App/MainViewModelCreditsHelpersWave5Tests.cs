using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for CreditsStatusResult record:
/// Success/Failure factory methods, null guards,
/// and ResolveCreditsStateTag with diagnostics containing non-string values.
/// </summary>
public sealed class MainViewModelCreditsHelpersWave5Tests
{
    [Fact]
    public void CreditsStatusResult_Success_ShouldSetAllFields()
    {
        var result = CreditsStatusResult.Success(true, "locked");
        result.IsValid.Should().BeTrue();
        result.ShouldFreeze.Should().BeTrue();
        result.StatusMessage.Should().Be("locked");
    }

    [Fact]
    public void CreditsStatusResult_Success_NullMessage_ShouldThrow()
    {
        var act = () => CreditsStatusResult.Success(false, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreditsStatusResult_Failure_ShouldSetIsValidFalse()
    {
        var result = CreditsStatusResult.Failure("error");
        result.IsValid.Should().BeFalse();
        result.ShouldFreeze.Should().BeFalse();
        result.StatusMessage.Should().Be("error");
    }

    [Fact]
    public void CreditsStatusResult_Failure_NullMessage_ShouldThrow()
    {
        var act = () => CreditsStatusResult.Failure(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResolveCreditsStateTag_DiagnosticsWithNonStringTag_ShouldFallback()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?> { ["creditsStateTag"] = 42 });
        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, true);
        // 42.ToString() is "42", which is non-whitespace, so it should be returned
        tag.Should().Be("42");
    }

    [Fact]
    public void ResolveCreditsStateTag_DiagnosticsWithNullTag_ShouldReturnFreezeBasedDefault()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?> { ["creditsStateTag"] = null });
        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, true);
        tag.Should().Be("HOOK_LOCK");
    }

    [Fact]
    public void ResolveCreditsStateTag_NoDiagnosticsAtAll_ShouldReturnFreezeBasedDefault()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature, Diagnostics: null);
        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, false);
        tag.Should().Be("HOOK_ONESHOT");
    }

    [Fact]
    public void ResolveCreditsStateTag_EmptyDiagnostics_ShouldReturnFreezeBasedDefault()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?>());
        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, true);
        tag.Should().Be("HOOK_LOCK");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_FreezeFalse_HookOneshot_ShouldSucceed()
    {
        var status = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 500, "HOOK_ONESHOT", "");
        status.IsValid.Should().BeTrue();
        status.ShouldFreeze.Should().BeFalse();
        status.StatusMessage.Should().Contain("HOOK_ONESHOT");
        status.StatusMessage.Should().Contain("500");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_FreezeTrue_HookLock_ShouldSucceed()
    {
        var status = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(true, 9999, "HOOK_LOCK", " [diag]");
        status.IsValid.Should().BeTrue();
        status.ShouldFreeze.Should().BeTrue();
        status.StatusMessage.Should().Contain("HOOK_LOCK");
        status.StatusMessage.Should().Contain("9,999");
        status.StatusMessage.Should().Contain("[diag]");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_FreezeTrue_WrongTag_ShouldReturnFailure()
    {
        var status = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(true, 100, "HOOK_ONESHOT", "");
        status.IsValid.Should().BeFalse();
        status.ShouldFreeze.Should().BeFalse();
        status.StatusMessage.Should().Contain("unexpected state");
        status.StatusMessage.Should().Contain("HOOK_ONESHOT");
        status.StatusMessage.Should().Contain("lock mode");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_FreezeFalse_WrongTag_ShouldReturnFailure()
    {
        var status = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 100, "HOOK_LOCK", "");
        status.IsValid.Should().BeFalse();
        status.StatusMessage.Should().Contain("unexpected state");
        status.StatusMessage.Should().Contain("HOOK_LOCK");
        status.StatusMessage.Should().Contain("one-shot mode");
    }
}
