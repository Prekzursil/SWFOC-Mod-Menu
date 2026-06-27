using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 158) — pins global-method LIVE batch via new
/// Lua_DispatchGlobalArgMethod helper. LIVE flips #53-55. Master
/// loop now at 55 LIVE wires.
/// </summary>
public sealed class Iter158GlobalMethodBatchTests
{
    [Theory]
    [InlineData("SWFOC_DisableBombingRunLua")]
    [InlineData("SWFOC_FlashGuiObjectLua")]
    [InlineData("SWFOC_HideGuiObjectLua")]
    public void GlobalBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void DisableBombingRun_NoteFlagsReversedParam()
    {
        // docs/lua-api.md flags this — pass false to disable. Operator
        // confusion magnet, so the catalog note carries the warning.
        CapabilityStatusCatalog.Entries["SWFOC_DisableBombingRunLua"].Note
            .Should().Contain("reversed");
    }
}
