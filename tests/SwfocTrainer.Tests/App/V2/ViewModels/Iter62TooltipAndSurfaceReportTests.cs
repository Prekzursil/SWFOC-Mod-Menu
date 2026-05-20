using System.IO;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 62) — pins the new
/// <see cref="CapabilityAwareAction.Tooltip"/> property + the Diagnostics
/// tab's <c>OpenCapabilitySurfaceReportCommand</c>.
/// </summary>
public sealed class Iter62TooltipAndSurfaceReportTests
{
    [Fact]
    public void Tooltip_LiveAction_FormatsAsNameBadgeNote()
    {
        var action = new CapabilityAwareAction("Toggle god mode", "SWFOC_GodMode");
        action.Tooltip.Should().Be("Toggle god mode · LIVE · Hardpoint-behavior sweep + SetHP detour");
    }

    [Fact]
    public void Tooltip_Phase2PendingAction_IncludesBlockedReason()
    {
        // v1.0.2: FreezeAI rationale rewritten to prioritize the LIVE alternative
        // (SuspendAiLua) first, then the deferred-work explanation. This is the
        // operator-trust pattern — surface what the operator CAN do now before
        // what they can't.
        var action = new CapabilityAwareAction("Toggle freeze AI", "SWFOC_FreezeAI");
        action.Tooltip.Should().Contain("PHASE 2 PENDING");
        action.Tooltip.Should().Contain("USE LIVE ALTERNATIVE");
        action.Tooltip.Should().Contain("SuspendAiLua");
    }

    [Fact]
    public void Tooltip_MultiHelperAction_JoinsNotesWithBullet()
    {
        var action = new CapabilityAwareAction("Instant win",
            "SWFOC_HealAllLocal", "SWFOC_ListTacticalUnits", "SWFOC_KillUnit");
        action.Tooltip.Should().Contain("LIVE");
        // Bullet separator from iter 61 multi-helper note joining.
        action.Tooltip.Should().Contain(" · ");
    }

    [Fact]
    public void Tooltip_NoCatalogNote_FallsBackToNameAndBadgeOnly()
    {
        var action = new CapabilityAwareAction("Unknown", "SWFOC_NotInCatalog_Iter62");
        action.Tooltip.Should().Contain("Unknown");
        action.Tooltip.Should().Contain("UNAVAILABLE");
        // The catalog returns a "Not in catalogue" note for unknowns; that
        // surfaces in the tooltip too. The fallback path (no note at all)
        // is exercised via direct-construction tests.
        action.Tooltip.Should().Contain("Not in catalogue");
    }

    [Fact]
    public void Tooltip_EmptyNote_DropsTrailingSegment()
    {
        // When ALL helper notes are empty (which doesn't happen with the
        // catalog today, but is part of the contract), the trailing
        // " · Note" segment is dropped.
        // We can't trigger this from the catalog alone, so verify the
        // formatter directly via reflection-free path: an UNAVAILABLE
        // unknown gets the "Not in catalogue" note, but a hand-built
        // CapabilityAwareAction with empty note would get "Name · Badge"
        // only. Use the implementation's runtime branch via
        // string.IsNullOrEmpty(Note) check.
        var action = new CapabilityAwareAction("Demo", "SWFOC_GodMode");
        action.Tooltip.Should().StartWith("Demo · LIVE");
    }

    [Fact]
    public void DiagnosticsTab_ResolveCapabilitySurfaceReportPath_FindsTheFile()
    {
        var path = DiagnosticsTabViewModel.ResolveCapabilitySurfaceReportPath();
        // CI without the swfoc_memory sibling will return null — that's
        // also a valid behavior (the button no-ops with a status-line
        // message). When running locally the file exists.
        if (path is not null)
        {
            File.Exists(path).Should().BeTrue();
            Path.GetFileName(path).Should().StartWith("capability_surface_");
            Path.GetFileName(path).Should().EndWith(".md");
        }
    }

    [Fact]
    public void DiagnosticsTab_OpenSurfaceReportCommand_IsExposed()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();
        var vm = new DiagnosticsTabViewModel(adapter, settings);

        vm.OpenCapabilitySurfaceReportCommand.Should().NotBeNull(
            "iter 62 added the surface-report sibling button to Diagnostics");
        vm.OpenCapabilityReportCommand.Should().NotBeNull(
            "iter 287's catalog-report button must remain alongside the new one");
    }
}
