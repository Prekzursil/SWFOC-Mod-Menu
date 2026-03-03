using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class GameLaunchServiceTests
{
    [Fact]
    public void BuildArguments_ShouldEmitChainedSteamModArguments_WhenMultipleWorkshopIdsProvided()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.SteamMod,
            WorkshopIds: new[] { "1397421866", "3447786229" });

        var args = InvokeBuildArguments(request);

        args.Should().Be("STEAMMOD=1397421866 STEAMMOD=3447786229");
    }

    [Fact]
    public void BuildArguments_ShouldNormalizeCsvAndPreserveInputOrder()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.SteamMod,
            WorkshopIds: new[] { "1397421866,3447786229", "3447786229", "3287776766" });

        var args = InvokeBuildArguments(request);

        args.Should().Be("STEAMMOD=1397421866 STEAMMOD=3447786229 STEAMMOD=3287776766");
    }

    [Fact]
    public void BuildArguments_ShouldReturnEmpty_ForVanillaMode()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.Vanilla,
            WorkshopIds: new[] { "1397421866" });

        var args = InvokeBuildArguments(request);

        args.Should().BeEmpty();
    }

    private static string InvokeBuildArguments(GameLaunchRequest request)
    {
        var method = typeof(GameLaunchService).GetMethod(
            "BuildArguments",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var result = method!.Invoke(null, new object?[] { request });
        result.Should().BeOfType<string>();
        return (string)result!;
    }
}
