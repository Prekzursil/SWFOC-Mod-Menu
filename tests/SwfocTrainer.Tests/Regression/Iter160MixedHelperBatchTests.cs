using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 160) — pins mixed-helper LIVE batch demonstrating
/// dispatcher-set reusability across receiver shapes:
///   - Lock_Controls(bool)             via iter-158 global-arg
///   - (player):Disable_Orbital_Bombardment(bool) via iter-111 obj-bool
///   - Story_Event_Trigger(name)       via iter-158 global-arg
/// LIVE flips #60-62. Master loop now at 62 LIVE wires.
/// Key insight pinned by this test: the iter-111 obj-bool helper works
/// for ANY obj receiver including PlayerWrappers, not just units. The
/// helper name is misleading; semantics are obj:bool_method.
/// </summary>
public sealed class Iter160MixedHelperBatchTests
{
    [Theory]
    [InlineData("SWFOC_LockControlsLua")]
    [InlineData("SWFOC_DisableOrbitalBombardmentLua")]
    [InlineData("SWFOC_StoryEventTriggerLua")]
    public void MixedHelperBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void DisableOrbitalBombardment_NoteFlagsPlayerReceiver()
    {
        // Pin: the catalog rationale must call out that this is a
        // PlayerWrapper method (not unit). Future readers should know
        // the iter-111 helper is shape-agnostic before reusing it.
        CapabilityStatusCatalog.Entries["SWFOC_DisableOrbitalBombardmentLua"].Note
            .Should().Contain("PlayerWrapper");
    }

    [Fact]
    public void StoryEventTrigger_NoteContrastsWithIter159StoryEvent()
    {
        // Pin: catalog should clarify the Trigger variant is an
        // alternative to iter-159's Story_Event, not a duplicate.
        CapabilityStatusCatalog.Entries["SWFOC_StoryEventTriggerLua"].Note
            .Should().Contain("Story_Event");
    }

    [Fact]
    public void MixedHelperBatch_AllTaggedIter160()
    {
        var iter160Entries = new[]
        {
            "SWFOC_LockControlsLua",
            "SWFOC_DisableOrbitalBombardmentLua",
            "SWFOC_StoryEventTriggerLua",
        };
        foreach (var name in iter160Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 160 LIVE",
                    $"{name} should be tagged as iter 160 LIVE in catalog rationale");
        }
    }
}
