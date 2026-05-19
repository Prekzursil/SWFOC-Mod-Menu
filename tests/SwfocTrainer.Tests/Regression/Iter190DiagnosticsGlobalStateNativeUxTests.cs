using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 190) — pins the Diagnostics tab native UX for the
/// iter-178 global no-arg getter wires (Get_Game_Mode, Get_Local_Player,
/// Get_Seconds_Per_Game_Minute). Third tab in the iter-188/189 surfacing arc.
///
/// These wires are 0-arg globals so the Diagnostics-tab UX needs no input
/// field — operator just clicks one of the 3 buttons in the Refresh row.
/// Result lands in the diagnostic log via AppendLog.
/// </summary>
public sealed class Iter190DiagnosticsGlobalStateNativeUxTests
{
    [Fact]
    public void CatalogAction_PointsToLiveCatalogEntries()
    {
        var swfocNames = new[]
        {
            "SWFOC_GetGameModeLua",
            "SWFOC_GetLocalPlayerLua",
            "SWFOC_GetSecondsPerGameMinuteLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-190 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_ReferencesIter178Helper()
    {
        // Pin: iter-178 wires use the new 9th helper (Lua_DispatchGlobalGetterNoArg).
        // The catalog rationale must reference iter-178 so future readers see the
        // helper-introduction provenance.
        var gameMode = CapabilityStatusCatalog.Entries["SWFOC_GetGameModeLua"].Note;
        gameMode.Should().Contain("iter-178");
    }

    [Fact]
    public void CatalogRationale_DocumentsCompositionWithOperatorWorkflows()
    {
        // Pin: the rationale should show how the wire composes into operator
        // workflows. iter-178 GetLocalPlayer pairs with iter-155 PlayerGiveMoney
        // for "give MY player credits" — that pairing should be in the catalog
        // so the iter-190 button's operator-facing tooltip mirrors the rationale.
        var localPlayer = CapabilityStatusCatalog.Entries["SWFOC_GetLocalPlayerLua"].Note;
        localPlayer.Should().Contain("Give_Money");
    }
}
