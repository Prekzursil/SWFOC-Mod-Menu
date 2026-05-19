using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class AiControlServiceTests
{
    private static readonly ILogger<AiControlService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<AiControlService>();

    // --- Happy-path per action ---

    [Fact]
    public async Task ExecuteAiControlAsync_SuspendAll_ReturnsSuccessWithSuspendDiagnostics()
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: AiControlAction.SuspendAll,
            SuspendSeconds: 60,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = await service.ExecuteAiControlAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Suspend_AI(60)");
        result.Diagnostics.Should().ContainKey("action_id")
            .WhoseValue.Should().Be("ai_suspend_all");
    }

    [Fact]
    public async Task ExecuteAiControlAsync_SuspendAll_NullSeconds_UsesDefault()
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: AiControlAction.SuspendAll,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = await service.ExecuteAiControlAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Suspend_AI(9999)");
    }

    [Fact]
    public async Task ExecuteAiControlAsync_ResumeAll_ReturnsSuccessWithResumeCall()
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: AiControlAction.ResumeAll,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = await service.ExecuteAiControlAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Suspend_AI(0)");
    }

    [Fact]
    public async Task ExecuteAiControlAsync_PreventUsage_IncludesCrashWarningInDiagnostics()
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: AiControlAction.PreventUsage,
            SuspendSeconds: null,
            TargetUnitId: "UNIT_42",
            FactionId: null,
            Difficulty: null);

        var result = await service.ExecuteAiControlAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call");
        result.Diagnostics!["lua_call"].Should().BeOfType<string>()
            .Which.Should().Contain("WARNING");
        result.Diagnostics.Should().ContainKey("action_id")
            .WhoseValue.Should().Be("ai_prevent_usage");
    }

    [Fact]
    public async Task ExecuteAiControlAsync_SetDifficulty_ReturnsDifficultyDiagnostics()
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: AiControlAction.SetDifficulty,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: "EMPIRE",
            Difficulty: 3);

        var result = await service.ExecuteAiControlAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call");
        result.Diagnostics!["lua_call"].Should().BeOfType<string>()
            .Which.Should().Contain("EMPIRE");
        result.Diagnostics.Should().ContainKey("action_id")
            .WhoseValue.Should().Be("ai_set_difficulty");
    }

    // --- Theory: all actions produce a valid result ---

    [Theory]
    [InlineData(AiControlAction.SuspendAll)]
    [InlineData(AiControlAction.ResumeAll)]
    [InlineData(AiControlAction.PreventUsage)]
    [InlineData(AiControlAction.SetDifficulty)]
    public async Task ExecuteAiControlAsync_AllActions_ReturnSucceeded(AiControlAction action)
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: action,
            SuspendSeconds: 10,
            TargetUnitId: "U1",
            FactionId: "REBELS",
            Difficulty: 2);

        var result = await service.ExecuteAiControlAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().NotBeNullOrWhiteSpace();
        result.AddressSource.Should().Be(AddressSource.None);
    }

    // --- ResolveAiAction mapping ---

    [Fact]
    public void ResolveAiAction_SuspendAll_ReturnsSuspendId()
    {
        AiControlService.ResolveAiAction(AiControlAction.SuspendAll)
            .Should().Be("ai_suspend_all");
    }

    [Fact]
    public void ResolveAiAction_ResumeAll_ReturnsResumeId()
    {
        AiControlService.ResolveAiAction(AiControlAction.ResumeAll)
            .Should().Be("ai_resume_all");
    }

    [Fact]
    public void ResolveAiAction_PreventUsage_ReturnsPreventId()
    {
        AiControlService.ResolveAiAction(AiControlAction.PreventUsage)
            .Should().Be("ai_prevent_usage");
    }

    [Fact]
    public void ResolveAiAction_SetDifficulty_ReturnsDifficultyId()
    {
        AiControlService.ResolveAiAction(AiControlAction.SetDifficulty)
            .Should().Be("ai_set_difficulty");
    }

    [Fact]
    public void ResolveAiAction_UnknownAction_ThrowsArgumentOutOfRangeException()
    {
        var act = () => AiControlService.ResolveAiAction((AiControlAction)999);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("action");
    }

    // --- Null guards ---

    [Fact]
    public async Task ExecuteAiControlAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            AiControlAction.ResumeAll, null, null, null, null);

        var act = () => service.ExecuteAiControlAsync(null!, request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task ExecuteAiControlAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = new AiControlService(NullLogger);

        var act = () => service.ExecuteAiControlAsync("p1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new AiControlService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // --- Unknown action in ExecuteAiControlAsync ---

    [Fact]
    public async Task ExecuteAiControlAsync_UnknownAction_ThrowsArgumentOutOfRangeException()
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: (AiControlAction)999,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var act = () => service.ExecuteAiControlAsync("p1", request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // --- Edge cases: null optional fields ---

    [Fact]
    public async Task ExecuteAiControlAsync_PreventUsage_NullUnitId_StillSucceeds()
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: AiControlAction.PreventUsage,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = await service.ExecuteAiControlAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call");
        result.Diagnostics.Should().ContainKey("action_id")
            .WhoseValue.Should().Be("ai_prevent_usage");
    }

    [Fact]
    public async Task ExecuteAiControlAsync_SetDifficulty_NullFactionId_StillSucceeds()
    {
        var service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: AiControlAction.SetDifficulty,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = await service.ExecuteAiControlAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call");
        result.Diagnostics!["lua_call"].Should().BeOfType<string>()
            .Which.Should().Contain("unknown");
        result.Diagnostics.Should().ContainKey("action_id")
            .WhoseValue.Should().Be("ai_set_difficulty");
    }

    // --- Default interface overload ---

    [Fact]
    public async Task ExecuteAiControlAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IAiControlService service = new AiControlService(NullLogger);
        var request = new AiControlRequest(
            Action: AiControlAction.ResumeAll,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = await service.ExecuteAiControlAsync("p1", request);

        result.Succeeded.Should().BeTrue();
    }

    // --- BuildAiLuaCommand ---

    [Fact]
    public void BuildAiLuaCommand_SuspendAll_WithSeconds_ReturnsSuspendCommand()
    {
        var request = new AiControlRequest(
            Action: AiControlAction.SuspendAll,
            SuspendSeconds: 60,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = AiControlService.BuildAiLuaCommand(request);

        result.Should().Be("Suspend_AI(60)");
    }

    [Fact]
    public void BuildAiLuaCommand_SuspendAll_NullSeconds_UsesDefault()
    {
        var request = new AiControlRequest(
            Action: AiControlAction.SuspendAll,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = AiControlService.BuildAiLuaCommand(request);

        result.Should().Be("Suspend_AI(9999)");
    }

    [Fact]
    public void BuildAiLuaCommand_ResumeAll_ReturnsSuspendZero()
    {
        var request = new AiControlRequest(
            Action: AiControlAction.ResumeAll,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = AiControlService.BuildAiLuaCommand(request);

        result.Should().Be("Suspend_AI(0)");
    }

    [Fact]
    public void BuildAiLuaCommand_PreventUsage_ReturnsPreventCommand()
    {
        var request = new AiControlRequest(
            Action: AiControlAction.PreventUsage,
            SuspendSeconds: null,
            TargetUnitId: "UNIT_42",
            FactionId: null,
            Difficulty: null);

        var result = AiControlService.BuildAiLuaCommand(request);

        result.Should().Contain("Prevent_AI_Usage");
        result.Should().Contain("WARNING");
    }

    [Fact]
    public void BuildAiLuaCommand_SetDifficulty_WithFaction_ReturnsDifficultyCommand()
    {
        var request = new AiControlRequest(
            Action: AiControlAction.SetDifficulty,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: "EMPIRE",
            Difficulty: 3);

        var result = AiControlService.BuildAiLuaCommand(request);

        result.Should().Contain("EMPIRE");
    }

    [Fact]
    public void BuildAiLuaCommand_SetDifficulty_NullFaction_UsesUnknown()
    {
        var request = new AiControlRequest(
            Action: AiControlAction.SetDifficulty,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var result = AiControlService.BuildAiLuaCommand(request);

        result.Should().Contain("unknown");
    }

    [Fact]
    public void BuildAiLuaCommand_UnknownAction_ThrowsArgumentOutOfRangeException()
    {
        var request = new AiControlRequest(
            Action: (AiControlAction)999,
            SuspendSeconds: null,
            TargetUnitId: null,
            FactionId: null,
            Difficulty: null);

        var act = () => AiControlService.BuildAiLuaCommand(request);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("request");
    }

    [Fact]
    public void BuildAiLuaCommand_NullRequest_ThrowsArgumentNullException()
    {
        var act = () => AiControlService.BuildAiLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("request");
    }
}
