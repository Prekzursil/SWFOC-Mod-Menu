using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class CrashAnalyzerServiceTests
{
    private static readonly ILogger<CrashAnalyzerService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<CrashAnalyzerService>();

    // --- BuildCaptureSnapshotLuaCommand ---

    [Fact]
    public void BuildCaptureSnapshotLuaCommand_SimplePath_ReturnsExpectedLua()
    {
        CrashAnalyzerService.BuildCaptureSnapshotLuaCommand("C:/dumps/snap.txt")
            .Should().Be("return SWFOC_DumpState(\"C:/dumps/snap.txt\")");
    }

    [Fact]
    public void BuildCaptureSnapshotLuaCommand_RelativePath_ReturnsExpectedLua()
    {
        CrashAnalyzerService.BuildCaptureSnapshotLuaCommand("snap.bin")
            .Should().Be("return SWFOC_DumpState(\"snap.bin\")");
    }

    // --- ValidatePath ---

    [Fact]
    public void ValidatePath_NormalAbsolutePath_Accepted()
    {
        CrashAnalyzerService.ValidatePath("C:/dumps/snap.txt").Should().BeTrue();
    }

    [Fact]
    public void ValidatePath_RelativeNoTraversal_Accepted()
    {
        CrashAnalyzerService.ValidatePath("dumps/snap.txt").Should().BeTrue();
    }

    [Fact]
    public void ValidatePath_DotDotTraversal_Rejected()
    {
        CrashAnalyzerService.ValidatePath("C:/dumps/../etc/passwd").Should().BeFalse();
    }

    [Fact]
    public void ValidatePath_BackslashQuoteEscape_Rejected()
    {
        CrashAnalyzerService.ValidatePath("foo\\\"bar.txt").Should().BeFalse();
    }

    [Fact]
    public void ValidatePath_RawDoubleQuote_Rejected()
    {
        CrashAnalyzerService.ValidatePath("foo\"bar.txt").Should().BeFalse();
    }

    [Fact]
    public void ValidatePath_NullOrEmpty_Rejected()
    {
        CrashAnalyzerService.ValidatePath(null).Should().BeFalse();
        CrashAnalyzerService.ValidatePath("").Should().BeFalse();
        CrashAnalyzerService.ValidatePath("   ").Should().BeFalse();
    }

    // --- Constructor / offline mode ---

    [Fact]
    public void Constructor_NullBridgeOverload_Accepted()
    {
        var act = () => new CrashAnalyzerService(NullLogger);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task CaptureSnapshotAsync_Offline_ReturnsSuccess()
    {
        var service = new CrashAnalyzerService(NullLogger);

        var result = await service.CaptureSnapshotAsync("p1", "C:/dumps/snap.txt", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be("return SWFOC_DumpState(\"C:/dumps/snap.txt\")");
        result.Diagnostics.Should().ContainKey("path");
    }

    [Fact]
    public async Task CaptureSnapshotAsync_BadPath_ReturnsValidationFailure()
    {
        var service = new CrashAnalyzerService(NullLogger);

        var result = await service.CaptureSnapshotAsync("p1", "../escape.txt", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("validation");
        result.Diagnostics.Should().ContainKey("rejected_path");
    }

    [Fact]
    public async Task CaptureSnapshotAsync_NullProfileId_Throws()
    {
        var service = new CrashAnalyzerService(NullLogger);

        var act = () => service.CaptureSnapshotAsync(null!, "snap.txt", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task CaptureSnapshotAsync_NullPath_Throws()
    {
        var service = new CrashAnalyzerService(NullLogger);

        var act = () => service.CaptureSnapshotAsync("p1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("path");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new CrashAnalyzerService(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
