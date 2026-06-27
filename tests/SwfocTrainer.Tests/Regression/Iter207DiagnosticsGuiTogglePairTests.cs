using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 207) — pins the iter-158 Hide_GUI_Object + iter-166
/// Show_GUI_Object symmetric pair surfacing on the Diagnostics tab.
/// Single shared element-name input field; hardcoded Lua wire format
/// (no V2UnitMutationDispatcher dependency, follows iter-190 Diagnostics
/// pattern).
///
/// Operator workflow this iter unlocks: hide an HUD element pre-cinematic
/// (e.g. "Tactical_HUD"), record the cutscene, click Show to restore.
/// Pairs with iter-150 Letter_Box_On/Off and iter-145 cinematic camera
/// primitives for full filming control.
/// </summary>
public sealed class Iter207DiagnosticsGuiTogglePairTests
{
    [Fact]
    public void CatalogEntries_BothRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_HideGuiObjectLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_ShowGuiObjectLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_BothEntriesDocumentIter207Surfacing()
    {
        var hide = CapabilityStatusCatalog.Entries["SWFOC_HideGuiObjectLua"].Note;
        var show = CapabilityStatusCatalog.Entries["SWFOC_ShowGuiObjectLua"].Note;

        hide.Should().Contain("Iter 207");
        show.Should().Contain("Iter 207");
    }

    [Fact]
    public void CatalogRationale_HideReferencesShowAndShared_FieldName()
    {
        // Pin: the symmetric-pair framing must be explicit. If a future
        // edit drops the cross-reference between the two entries, an
        // operator reading just one rationale won't know there's a paired
        // surface. Both entries call out GuiObjectElementName as the
        // shared input field name.
        var hide = CapabilityStatusCatalog.Entries["SWFOC_HideGuiObjectLua"].Note;
        hide.Should().Contain("Show");
        hide.Should().Contain("GuiObjectElementName");
    }

    [Fact]
    public void CatalogRationale_ShowReferencesHideAndPairFraming()
    {
        var show = CapabilityStatusCatalog.Entries["SWFOC_ShowGuiObjectLua"].Note;
        show.Should().Contain("symmetric pair");
        show.Should().Contain("GuiObjectElementName");
    }

    [Fact]
    public void Vm_ExposesHideShowCommandsAndCapabilityActionsAndSharedField()
    {
        // Pin: the new ICommand + capability action pairs are on the
        // public surface, plus the shared GuiObjectElementName property.
        // Reflection walk so we don't depend on the VM constructor (which
        // has a real bridge dependency).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.DiagnosticsTabViewModel);
        t.GetProperty("HideGuiObjectCommand").Should().NotBeNull();
        t.GetProperty("ShowGuiObjectCommand").Should().NotBeNull();
        t.GetProperty("HideGuiObjectAction").Should().NotBeNull();
        t.GetProperty("ShowGuiObjectAction").Should().NotBeNull();
        t.GetProperty("GuiObjectElementName")
            .Should().NotBeNull("Shared input field for both Hide and Show buttons");
    }
}
