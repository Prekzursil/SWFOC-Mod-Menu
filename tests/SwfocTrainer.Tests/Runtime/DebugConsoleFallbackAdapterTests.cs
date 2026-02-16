using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class DebugConsoleFallbackAdapterTests
{
    [Fact]
    public void Prepare_Should_Return_SwitchControl_For_SetOwner()
    {
        var adapter = new DebugConsoleFallbackAdapter();
        var payload = new Dictionary<string, object?>
        {
            ["intValue"] = 3
        };

        var result = adapter.Prepare(SdkOperationId.SetOwner, "aotr_1397421866_swfoc", RuntimeMode.Tactical, payload);

        result.Supported.Should().BeTrue();
        result.Mode.Should().Be("debug_console_prepared");
        result.ReasonCode.Should().Be("switchcontrol_template");
        result.PreparedCommand.Should().Be("SwitchControl 3");
    }

    [Fact]
    public void Prepare_Should_Return_Unsupported_For_Unmapped_Operation()
    {
        var adapter = new DebugConsoleFallbackAdapter();

        var result = adapter.Prepare(SdkOperationId.SetHp, "base_swfoc", RuntimeMode.Tactical);

        result.Supported.Should().BeFalse();
        result.Mode.Should().Be("none");
        result.ReasonCode.Should().Be("fallback_not_supported");
        result.PreparedCommand.Should().BeNull();
    }
}
