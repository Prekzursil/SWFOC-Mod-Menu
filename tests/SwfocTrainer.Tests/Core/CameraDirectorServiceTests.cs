using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class CameraDirectorServiceTests
{
    private static readonly ILogger<CameraDirectorService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<CameraDirectorService>();

    // --- Happy-path per command ---

    [Fact]
    public async Task ExecuteCameraCommandAsync_PointAt_ReturnsSuccessWithLuaCall()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "point_at", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Point_Camera_At(selectedUnit)");
        result.Diagnostics.Should().ContainKey("command")
            .WhoseValue.Should().Be("point_at");
    }

    [Fact]
    public async Task ExecuteCameraCommandAsync_ScrollTo_ReturnsScrollLuaCall()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "scroll_to", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Scroll_Camera_To(0,0,0)");
    }

    [Fact]
    public async Task ExecuteCameraCommandAsync_Zoom_ReturnsZoomLuaCall()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "zoom", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Zoom_Camera(1.0)");
    }

    [Fact]
    public async Task ExecuteCameraCommandAsync_Rotate_ReturnsRotateLuaCall()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "rotate", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Rotate_Camera_By(0)");
    }

    [Fact]
    public async Task ExecuteCameraCommandAsync_LetterboxOn_ReturnsLetterboxOnLuaCall()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "letterbox_on", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Letter_Box_On()");
    }

    [Fact]
    public async Task ExecuteCameraCommandAsync_LetterboxOff_ReturnsLetterboxOffLuaCall()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "letterbox_off", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Letter_Box_Off()");
    }

    [Fact]
    public async Task ExecuteCameraCommandAsync_Freeze_SetsGameSpeedToZero()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "freeze", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Game_Set_Speed(0)");
    }

    [Fact]
    public async Task ExecuteCameraCommandAsync_Unfreeze_RestoresGameSpeed()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "unfreeze", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Game_Set_Speed(1)");
    }

    // --- Unknown command ---

    [Fact]
    public async Task ExecuteCameraCommandAsync_UnknownCommand_ReturnsFailure()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "explode_camera", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("explode_camera");
        result.AddressSource.Should().Be(AddressSource.None);
    }

    // --- Case insensitivity ---

    [Fact]
    public async Task ExecuteCameraCommandAsync_CaseInsensitive_ReturnsSuccess()
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", "ZOOM", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("Zoom_Camera(1.0)");
    }

    // --- Theory: all known commands ---

    [Theory]
    [InlineData("point_at", "Point_Camera_At(selectedUnit)")]
    [InlineData("scroll_to", "Scroll_Camera_To(0,0,0)")]
    [InlineData("zoom", "Zoom_Camera(1.0)")]
    [InlineData("rotate", "Rotate_Camera_By(0)")]
    [InlineData("letterbox_on", "Letter_Box_On()")]
    [InlineData("letterbox_off", "Letter_Box_Off()")]
    [InlineData("freeze", "Game_Set_Speed(0)")]
    [InlineData("unfreeze", "Game_Set_Speed(1)")]
    public async Task ExecuteCameraCommandAsync_AllCommands_ReturnExpectedLuaCall(
        string command, string expectedLua)
    {
        var service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync(
            "p1", command, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().NotBeNullOrWhiteSpace();
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be(expectedLua);
    }

    // --- ResolveCommand mapping ---

    [Theory]
    [InlineData("point_at", "Point_Camera_At(unit)")]
    [InlineData("scroll_to", "Scroll_Camera_To(position)")]
    [InlineData("zoom", "Zoom_Camera(level)")]
    [InlineData("rotate", "Rotate_Camera_By(degrees)")]
    [InlineData("letterbox_on", "Letter_Box_On()")]
    [InlineData("letterbox_off", "Letter_Box_Off()")]
    [InlineData("freeze", "Game_Set_Speed(0)")]
    [InlineData("unfreeze", "Game_Set_Speed(1)")]
    public void ResolveCommand_KnownCommand_ReturnsLuaCall(string command, string expectedLua)
    {
        CameraDirectorService.ResolveCommand(command).Should().Be(expectedLua);
    }

    [Fact]
    public void ResolveCommand_UnknownCommand_ReturnsNull()
    {
        CameraDirectorService.ResolveCommand("destroy_universe").Should().BeNull();
    }

    [Fact]
    public void ResolveCommand_CaseInsensitive_ReturnsLuaCall()
    {
        CameraDirectorService.ResolveCommand("ZOOM").Should().Be("Zoom_Camera(level)");
    }

    [Fact]
    public void ResolveCommand_TrimmedInput_ReturnsLuaCall()
    {
        CameraDirectorService.ResolveCommand("  zoom  ").Should().Be("Zoom_Camera(level)");
    }

    [Fact]
    public void ResolveCommand_NullCommand_ThrowsArgumentNullException()
    {
        var act = () => CameraDirectorService.ResolveCommand(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("command");
    }

    // --- Null guards ---

    [Fact]
    public async Task ExecuteCameraCommandAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new CameraDirectorService(NullLogger);

        var act = () => service.ExecuteCameraCommandAsync(
            null!, "zoom", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task ExecuteCameraCommandAsync_NullCommand_ThrowsArgumentNullException()
    {
        var service = new CameraDirectorService(NullLogger);

        var act = () => service.ExecuteCameraCommandAsync(
            "p1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("command");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new CameraDirectorService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // --- Default overload ---

    [Fact]
    public async Task ExecuteCameraCommandAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        ICameraDirectorService service = new CameraDirectorService(NullLogger);

        var result = await service.ExecuteCameraCommandAsync("p1", "zoom");

        result.Succeeded.Should().BeTrue();
    }
}
