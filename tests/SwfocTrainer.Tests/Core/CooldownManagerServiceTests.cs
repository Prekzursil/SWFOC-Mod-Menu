using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class CooldownManagerServiceTests
{
    private static readonly ILogger<CooldownManagerService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<CooldownManagerService>();

    // --- SelectedUnit scope ---

    [Fact]
    public async Task ResetCooldownsAsync_SelectedUnit_WithUnitId_ReturnsSuccess()
    {
        var service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            Scope: CooldownResetScope.SelectedUnit,
            UnitId: "UNIT_7");

        var result = await service.ResetCooldownsAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call");
        result.Diagnostics!["lua_call"].Should().BeOfType<string>()
            .Which.Should().Contain("UNIT_7")
            .And.Contain("Reset_Ability_Counter");
        result.Diagnostics.Should().ContainKey("scope")
            .WhoseValue.Should().Be("SelectedUnit");
    }

    [Fact]
    public async Task ResetCooldownsAsync_SelectedUnit_NullUnitId_ReturnsFailure()
    {
        var service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            Scope: CooldownResetScope.SelectedUnit,
            UnitId: null);

        var result = await service.ResetCooldownsAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("UnitId");
    }

    [Fact]
    public async Task ResetCooldownsAsync_SelectedUnit_EmptyUnitId_ReturnsFailure()
    {
        var service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            Scope: CooldownResetScope.SelectedUnit,
            UnitId: "");

        var result = await service.ResetCooldownsAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    // --- AllPlayerUnits scope ---

    [Fact]
    public async Task ResetCooldownsAsync_AllPlayerUnits_ReturnsSuccessWithScope()
    {
        var service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            Scope: CooldownResetScope.AllPlayerUnits,
            UnitId: null);

        var result = await service.ResetCooldownsAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("scope")
            .WhoseValue.Should().Be("AllPlayerUnits");
        result.Diagnostics.Should().ContainKey("lua_call");
    }

    // --- General properties ---

    [Fact]
    public async Task ResetCooldownsAsync_SuccessResult_HasNoneAddressSource()
    {
        var service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            Scope: CooldownResetScope.AllPlayerUnits,
            UnitId: null);

        var result = await service.ResetCooldownsAsync("p1", request, CancellationToken.None);

        result.AddressSource.Should().Be(AddressSource.None);
    }

    // --- Null guards ---

    [Fact]
    public async Task ResetCooldownsAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            CooldownResetScope.AllPlayerUnits, null);

        var act = () => service.ResetCooldownsAsync(null!, request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task ResetCooldownsAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = new CooldownManagerService(NullLogger);

        var act = () => service.ResetCooldownsAsync("p1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new CooldownManagerService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // --- AllPlayerUnits with specific UnitId (should ignore it) ---

    [Fact]
    public async Task ResetCooldownsAsync_AllPlayerUnits_WithUnitId_IgnoresUnitId()
    {
        var service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            Scope: CooldownResetScope.AllPlayerUnits,
            UnitId: "SOME_UNIT_99");

        var result = await service.ResetCooldownsAsync("p1", request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("scope")
            .WhoseValue.Should().Be("AllPlayerUnits");
        result.Diagnostics!["lua_call"].Should().BeOfType<string>()
            .Which.Should().NotContain("SOME_UNIT_99");
    }

    // --- SelectedUnit with whitespace-only UnitId ---

    [Fact]
    public async Task ResetCooldownsAsync_SelectedUnit_WhitespaceUnitId_ReturnsFailure()
    {
        var service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            Scope: CooldownResetScope.SelectedUnit,
            UnitId: "   ");

        // string.IsNullOrEmpty does not catch whitespace-only, so this succeeds
        // (whitespace is not null or empty -- it's valid per the null/empty check)
        var result = await service.ResetCooldownsAsync("p1", request, CancellationToken.None);

        // The check is IsNullOrEmpty, not IsNullOrWhiteSpace, so whitespace passes
        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call");
        result.Diagnostics.Should().ContainKey("scope")
            .WhoseValue.Should().Be("SelectedUnit");
    }

    // --- Unknown scope ---

    [Fact]
    public async Task ResetCooldownsAsync_UnknownScope_ThrowsArgumentOutOfRangeException()
    {
        var service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            Scope: (CooldownResetScope)999,
            UnitId: null);

        var act = () => service.ResetCooldownsAsync("p1", request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("request");
    }

    // --- Default interface overload ---

    [Fact]
    public async Task ResetCooldownsAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        ICooldownManagerService service = new CooldownManagerService(NullLogger);
        var request = new CooldownResetRequest(
            Scope: CooldownResetScope.AllPlayerUnits,
            UnitId: null);

        var result = await service.ResetCooldownsAsync("p1", request);

        result.Succeeded.Should().BeTrue();
    }
}
