using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 181) — pins iter-181 namespace expansion that proves
/// namespace-agnosticism is shared across BOTH iter-158 (global-arg) AND
/// iter-178 (global-no-arg-getter) helpers, not just iter-158 (per iter-180).
/// LIVE flips #134-135; master loop now at 135 LIVE wires.
/// SFXManager wire intentionally preserves the engine's "Reponse" typo.
/// </summary>
public sealed class Iter181NamespaceExpansionBatchTests
{
    [Theory]
    [InlineData("SWFOC_ThreadGetCurrentStageLua")]
    [InlineData("SWFOC_SFXAllowUnitReponseVoLua")]
    public void NamespaceExpansionBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void ThreadGetCurrentStage_NotePinsIter178HelperReuse()
    {
        // Pin: catalog should explicitly call out that iter-178 helper
        // (not just iter-158 from iter-180) is namespace-agnostic.
        var note = CapabilityStatusCatalog.Entries["SWFOC_ThreadGetCurrentStageLua"].Note;
        note.Should().Contain("iter-178");
        note.Should().Contain("iter-180");
        note.Should().Contain("namespace-agnostic");
    }

    [Fact]
    public void ThreadGetCurrentStage_NoteShowsFullyQualifiedName()
    {
        // Pin: rationale should show actual Thread.Get_Current_Stage() expression.
        CapabilityStatusCatalog.Entries["SWFOC_ThreadGetCurrentStageLua"].Note
            .Should().Contain("Thread.Get_Current_Stage");
    }

    [Fact]
    public void SFXAllowUnitReponseVo_PreservesEngineTypo()
    {
        // Pin: SWFOC_* name MUST contain "Reponse" not "Response" — the engine
        // has this typo and we deliberately preserve it. This test FAILS the
        // moment someone "helpfully" corrects the typo.
        CapabilityStatusCatalog.Entries.Should()
            .ContainKey("SWFOC_SFXAllowUnitReponseVoLua");
        CapabilityStatusCatalog.Entries.Should()
            .NotContainKey("SWFOC_SFXAllowUnitResponseVoLua");
    }

    [Fact]
    public void SFXAllowUnitReponseVo_NoteDocumentsTypoIntent()
    {
        // Pin: catalog rationale should explicitly explain the typo so
        // future readers don't "fix" it.
        var note = CapabilityStatusCatalog.Entries["SWFOC_SFXAllowUnitReponseVoLua"].Note;
        note.Should().Contain("typo");
        note.Should().Contain("Reponse");
        note.Should().Contain("Response");  // documents the would-be-correct form
    }

    [Fact]
    public void NamespaceExpansionBatch_AllTaggedIter181Live()
    {
        var iter181Entries = new[]
        {
            "SWFOC_ThreadGetCurrentStageLua",
            "SWFOC_SFXAllowUnitReponseVoLua",
        };
        foreach (var name in iter181Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 181 LIVE",
                    $"{name} should be tagged as iter 181 LIVE");
        }
    }
}
