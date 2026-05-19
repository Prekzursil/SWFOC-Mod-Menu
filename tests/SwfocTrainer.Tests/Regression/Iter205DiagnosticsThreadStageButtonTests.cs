using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 205) — pins the iter-181 SWFOC_ThreadGetCurrentStageLua
/// surfacing as a 4th probe button on the Diagnostics tab "Read engine
/// state" row. Sibling to the iter-190 Game mode / Local player / Time
/// scale buttons. Reuses the iter-190 ReadGlobalStateAsync pattern (no
/// V2UnitMutationDispatcher; direct SendRawAsync via SafeProbeAsync).
///
/// Validates that iter-181's namespace-agnostic finding (helper handles
/// dotted Thread.* names transparently) survives at the UX layer.
/// Diagnostics tab grows from 3 → 4 probe buttons.
/// </summary>
public sealed class Iter205DiagnosticsThreadStageButtonTests
{
    [Fact]
    public void CatalogEntry_RemainsLive()
    {
        // Pin: SWFOC name spelling pinned in iter-181; iter-205 surfacing
        // doesn't touch the name. Status must stay LIVE for the button
        // to be honest.
        CapabilityStatusCatalog.Entries["SWFOC_ThreadGetCurrentStageLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_DocumentsIter205Surfacing()
    {
        var note = CapabilityStatusCatalog.Entries["SWFOC_ThreadGetCurrentStageLua"].Note;
        note.Should().Contain("Iter 205");
        note.Should().Contain("Diagnostics");
        note.Should().Contain("Thread stage",
            "rationale must reference the operator-facing button label");
    }

    [Fact]
    public void CatalogRationale_PreservesIter181NamespaceAgnosticFinding()
    {
        // Pin: extending the rationale with iter-205 surfacing must NOT
        // lose the iter-181 architectural finding (namespace-agnostic
        // codegen for dotted names like Thread.Get_Current_Stage). That
        // finding is load-bearing for future iters that add more
        // namespaced wires (FOWManager / SFXManager / Thread / etc.).
        var note = CapabilityStatusCatalog.Entries["SWFOC_ThreadGetCurrentStageLua"].Note;
        note.Should().Contain("namespace-agnostic");
        note.Should().Contain("iter-180");
        note.Should().Contain("iter-178");
    }

    [Fact]
    public void Vm_ExposesReadThreadStageCommandAndAction()
    {
        // Pin: the new ICommand + capability action are both on the public
        // surface. Reflection walk so we don't depend on the VM constructor
        // (which has a real bridge dependency).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.DiagnosticsTabViewModel);
        var cmd = t.GetProperty("ReadThreadStageCommand");
        var action = t.GetProperty("ReadThreadStageAction");
        cmd.Should().NotBeNull("Diagnostics 'Thread stage' button binds to ReadThreadStageCommand");
        action.Should().NotBeNull("Diagnostics tab capability surface includes ReadThreadStageAction");
    }
}
