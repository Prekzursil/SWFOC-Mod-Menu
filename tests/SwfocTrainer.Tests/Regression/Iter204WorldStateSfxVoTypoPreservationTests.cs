using System.Linq;
using System.Reflection;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 204) — pins the iter-181 SFX VO toggle native-UX
/// surface in the WorldState tab. The signature feature of iter-181 is
/// the engine TYPO "Reponse" (not "Response") — docs/lua-api.md section
/// 6 confirms the misspelling and the iter-181 catalog rationale + tests
/// pin it. Iter 204 surfaces the wire through dispatcher → VM → XAML;
/// these tests assert the typo SURVIVES at every layer (catalog,
/// dispatcher method name, VM property name, CapabilityAwareAction name,
/// command name).
///
/// Why this matters: a future operator-friendly "fix the typo" edit
/// would silently break the bridge dispatch (SWFOC_* names map directly
/// to bridge handler registrations). The reverse-orphan check would
/// catch it, but only after the broken edit; this test fails BEFORE the
/// build, in C# compile time effectively (since the assertions reference
/// the actual symbols).
///
/// 2 wires from one engine API: VO on (bool=1) + VO off (bool=0).
/// Hardcoded bool-string args via per-command lambdas — no input field.
/// </summary>
public sealed class Iter204WorldStateSfxVoTypoPreservationTests
{
    [Fact]
    public void DispatcherMethod_BindsToCorrectSwfocNameWithTypo()
    {
        var t = typeof(V2UnitMutationDispatcher);
        var method = t.GetMethod(nameof(V2UnitMutationDispatcher.SfxAllowUnitReponseVoLuaAsync));
        method.Should().NotBeNull("WorldState SFX VO toggle binds to SfxAllowUnitReponseVoLuaAsync");
        method!.Name.Should().Contain("Reponse", "engine TYPO 'Reponse' MUST survive in dispatcher method name");
        method.Name.Should().NotContain("Response", "method must NOT spell 'Response' correctly — that would break the bridge");
    }

    [Fact]
    public void CatalogEntry_NameAndStatusPreserveTypo()
    {
        // Pin: catalog SWFOC name spells "Reponse". This is the bridge handler
        // registration key — if the catalog "fixes" the typo, every editor
        // call to this wire fails with "unknown SWFOC_* function".
        CapabilityStatusCatalog.Entries["SWFOC_SFXAllowUnitReponseVoLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries.Keys
            .Should().NotContain("SWFOC_SFXAllowUnitResponseVoLua",
                "the typo-corrected version MUST NOT exist in the catalog — would mean the typo was 'fixed' and bridge broken");
    }

    [Fact]
    public void CatalogRationale_PinsTypoExplicitlyWithIter204Reference()
    {
        // Pin: rationale calls out the typo with the literal misspelling.
        // The English word "responses" (lower-case, plural) DOES legitimately
        // appear in the rationale ("Toggles whether units play VO responses
        // to player commands.") so we don't assert NotContain("Response") —
        // we instead assert the typo-CORRECTED engine-API name does NOT
        // appear, since that would mean someone "fixed" the SWFOC_* name.
        var note = CapabilityStatusCatalog.Entries["SWFOC_SFXAllowUnitReponseVoLua"].Note;
        note.Should().Contain("typo");
        note.Should().Contain("Reponse");
        note.Should().NotContain("SFXAllowUnitResponseVo",
            "rationale must NOT contain the typo-corrected SWFOC name — would mean the typo was 'fixed'");
        note.Should().NotContain("Allow_Unit_Response_VO",
            "rationale must NOT contain the typo-corrected engine method name");
        note.Should().Contain("Iter 204");
    }

    [Fact]
    public void VmCapabilityActions_ExposeBothOnAndOffWithTypoPreserved()
    {
        // Pin: WorldStateTabViewModel publishes BOTH a VO-on and a VO-off
        // capability action, both naming the engine typo verbatim. Reflection
        // walk so we don't depend on the VM constructor (which has 8 deps).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.WorldStateTabViewModel);
        var onProp = t.GetProperty("SfxAllowUnitReponseVoOn");
        var offProp = t.GetProperty("SfxAllowUnitReponseVoOff");
        onProp.Should().NotBeNull("VO-on capability action property must exist on the VM");
        offProp.Should().NotBeNull("VO-off capability action property must exist on the VM");
        onProp!.Name.Should().Contain("Reponse");
        offProp!.Name.Should().Contain("Reponse");
    }

    [Fact]
    public void VmCommands_ExposeOnAndOffWithTypoPreserved()
    {
        // Pin: the two ICommand properties also preserve the typo. These
        // back the XAML buttons via Command="{Binding ...}" — if a future
        // refactor renames them with "Response", the buttons silently
        // bind to nothing (WPF binding failure is a runtime warning, not
        // a compile error).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.WorldStateTabViewModel);
        var onCmd = t.GetProperty("SfxAllowUnitReponseVoOnCommand");
        var offCmd = t.GetProperty("SfxAllowUnitReponseVoOffCommand");
        onCmd.Should().NotBeNull();
        offCmd.Should().NotBeNull();

        var typoProps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Reponse")).ToList();
        typoProps.Should().HaveCountGreaterThanOrEqualTo(4,
            "VM should expose at least 4 typo-bearing public members "
            + "(SfxAllowUnitReponseVoOn capability + Off capability + OnCommand + OffCommand)");
    }
}
