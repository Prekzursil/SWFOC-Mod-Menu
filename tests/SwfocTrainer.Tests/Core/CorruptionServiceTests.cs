using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class CorruptionServiceTests
{
    private static readonly ILogger<CorruptionService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<CorruptionService>();

    // --- SetCorruptionAsync: each CorruptionType ---

    [Theory]
    [InlineData(CorruptionType.Racketeering)]
    [InlineData(CorruptionType.Bribery)]
    [InlineData(CorruptionType.Piracy)]
    [InlineData(CorruptionType.Kidnapping)]
    [InlineData(CorruptionType.Sabotage)]
    public async Task SetCorruptionAsync_ValidType_ReturnsSuccess(CorruptionType type)
    {
        var service = new CorruptionService(NullLogger);
        var entry = new CorruptionEntry("CORUSCANT", type, 1);

        var result = await service.SetCorruptionAsync("p1", entry, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("corruption_type")
            .WhoseValue.Should().Be(type.ToString());
        result.Diagnostics.Should().ContainKey("planet_id")
            .WhoseValue.Should().Be("CORUSCANT");
        result.Diagnostics.Should().ContainKey("lua_call");
        result.Diagnostics.Should().ContainKey("foc_only");
    }

    // --- SetCorruptionAsync: None type ---

    [Fact]
    public async Task SetCorruptionAsync_NoneType_ReturnsFailureWithMessage()
    {
        var service = new CorruptionService(NullLogger);
        var entry = new CorruptionEntry("CORUSCANT", CorruptionType.None, 0);

        var result = await service.SetCorruptionAsync("p1", entry, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("corruption type");
    }

    // --- SetCorruptionAsync: empty PlanetId ---

    [Fact]
    public async Task SetCorruptionAsync_EmptyPlanetId_ReturnsFailure()
    {
        var service = new CorruptionService(NullLogger);
        var entry = new CorruptionEntry("", CorruptionType.Bribery, 1);

        var result = await service.SetCorruptionAsync("p1", entry, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("PlanetId");
    }

    [Fact]
    public async Task SetCorruptionAsync_WhitespacePlanetId_ReturnsFailure()
    {
        var service = new CorruptionService(NullLogger);
        var entry = new CorruptionEntry("   ", CorruptionType.Bribery, 1);

        var result = await service.SetCorruptionAsync("p1", entry, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    // --- SetCorruptionAsync: default overload ---

    [Fact]
    public async Task SetCorruptionAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        ICorruptionService service = new CorruptionService(NullLogger);
        var entry = new CorruptionEntry("KUAT", CorruptionType.Piracy, 2);

        var result = await service.SetCorruptionAsync("p1", entry);

        result.Succeeded.Should().BeTrue();
    }

    // --- RemoveCorruptionAsync: valid ---

    [Fact]
    public async Task RemoveCorruptionAsync_ValidPlanetId_ReturnsSuccess()
    {
        var service = new CorruptionService(NullLogger);

        var result = await service.RemoveCorruptionAsync("p1", "CORUSCANT", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("CORUSCANT");
        result.Diagnostics.Should().ContainKey("lua_call");
        result.Diagnostics.Should().ContainKey("foc_only");
    }

    // --- RemoveCorruptionAsync: empty ---

    [Fact]
    public async Task RemoveCorruptionAsync_EmptyPlanetId_ReturnsFailure()
    {
        var service = new CorruptionService(NullLogger);

        var result = await service.RemoveCorruptionAsync("p1", "", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("PlanetId");
    }

    [Fact]
    public async Task RemoveCorruptionAsync_WhitespacePlanetId_ReturnsFailure()
    {
        var service = new CorruptionService(NullLogger);

        var result = await service.RemoveCorruptionAsync("p1", "   ", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    // --- RemoveCorruptionAsync: default overload ---

    [Fact]
    public async Task RemoveCorruptionAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        ICorruptionService service = new CorruptionService(NullLogger);

        var result = await service.RemoveCorruptionAsync("p1", "KUAT");

        result.Succeeded.Should().BeTrue();
    }

    // --- AddressSource ---

    [Fact]
    public async Task SetCorruptionAsync_SuccessResult_HasNoneAddressSource()
    {
        var service = new CorruptionService(NullLogger);
        var entry = new CorruptionEntry("MON_CALAMARI", CorruptionType.Sabotage, 3);

        var result = await service.SetCorruptionAsync("p1", entry, CancellationToken.None);

        result.AddressSource.Should().Be(AddressSource.None);
    }

    [Fact]
    public async Task RemoveCorruptionAsync_SuccessResult_HasNoneAddressSource()
    {
        var service = new CorruptionService(NullLogger);

        var result = await service.RemoveCorruptionAsync("p1", "MON_CALAMARI", CancellationToken.None);

        result.AddressSource.Should().Be(AddressSource.None);
    }

    // --- ValidateCorruptionType theory ---

    [Theory]
    [InlineData(CorruptionType.Racketeering, true)]
    [InlineData(CorruptionType.Bribery, true)]
    [InlineData(CorruptionType.Piracy, true)]
    [InlineData(CorruptionType.Kidnapping, true)]
    [InlineData(CorruptionType.Sabotage, true)]
    [InlineData(CorruptionType.None, false)]
    [InlineData((CorruptionType)99, false)]
    public void ValidateCorruptionType_ReturnsExpected(CorruptionType type, bool expected)
    {
        CorruptionService.ValidateCorruptionType(type).Should().Be(expected);
    }

    // --- Null guards ---

    [Fact]
    public async Task SetCorruptionAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new CorruptionService(NullLogger);
        var entry = new CorruptionEntry("CORUSCANT", CorruptionType.Bribery, 1);

        var act = () => service.SetCorruptionAsync(null!, entry, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task SetCorruptionAsync_NullEntry_ThrowsArgumentNullException()
    {
        var service = new CorruptionService(NullLogger);

        var act = () => service.SetCorruptionAsync("p1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("entry");
    }

    [Fact]
    public async Task RemoveCorruptionAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new CorruptionService(NullLogger);

        var act = () => service.RemoveCorruptionAsync(null!, "CORUSCANT", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task RemoveCorruptionAsync_NullPlanetId_ThrowsArgumentNullException()
    {
        var service = new CorruptionService(NullLogger);

        var act = () => service.RemoveCorruptionAsync("p1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("planetId");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new CorruptionService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
