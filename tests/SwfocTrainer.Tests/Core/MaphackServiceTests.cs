using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class MaphackServiceTests
{
    private static readonly ILogger<MaphackService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<MaphackService>();

    [Fact]
    public void BuildRevealAllLuaCommand_ReturnsExpectedSnippet()
    {
        MaphackService.BuildRevealAllLuaCommand()
            .Should().Be("local p = Find_Player(\"local\"); if p and FOWManager then FOWManager.Reveal_All(p) end");
    }

    [Fact]
    public void BuildRevealAllLuaCommand_DoesNotUseSwfocPrefix()
    {
        // Sanity check: maphack is intentionally a non-SWFOC_* path because the
        // bridge ships no native fog-of-war helper.
        MaphackService.BuildRevealAllLuaCommand()
            .Should().NotContain("SWFOC_")
            .And.Contain("FOWManager.Reveal_All");
    }

    [Fact]
    public void BuildUndoRevealLuaCommand_ReturnsExpectedSnippet()
    {
        MaphackService.BuildUndoRevealLuaCommand()
            .Should().Be("local p = Find_Player(\"local\"); if p and FOWManager then FOWManager.Undo_Reveal_All(p) end");
    }

    [Fact]
    public void BuildUndoRevealLuaCommand_GuardsAgainstNilFOWManager()
    {
        MaphackService.BuildUndoRevealLuaCommand()
            .Should().Contain("if p and FOWManager then");
    }

    [Fact]
    public void Constructor_NullBridgeOverload_Accepted()
    {
        var act = () => new MaphackService(NullLogger);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task RevealAllAsync_Offline_ReturnsSuccess()
    {
        var service = new MaphackService(NullLogger);

        var result = await service.RevealAllAsync("p1", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.AddressSource.Should().Be(AddressSource.None);
        result.Diagnostics.Should().ContainKey("lua_call");
        result.Diagnostics!["lua_call"]!.ToString().Should().Contain("FOWManager.Reveal_All");
    }

    [Fact]
    public async Task UndoRevealAsync_Offline_ReturnsSuccess()
    {
        var service = new MaphackService(NullLogger);

        var result = await service.UndoRevealAsync("p1", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics!["lua_call"]!.ToString().Should().Contain("FOWManager.Undo_Reveal_All");
    }

    [Fact]
    public async Task RevealAllAsync_NullProfileId_Throws()
    {
        var service = new MaphackService(NullLogger);

        var act = () => service.RevealAllAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new MaphackService(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
