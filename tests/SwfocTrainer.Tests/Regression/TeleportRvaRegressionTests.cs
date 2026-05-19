using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// Guards the corrected ledger entry for RVA <c>0x5819E0</c>: this is
/// <c>GameObjectWrapper::Teleport</c>, NOT <c>Make_Invulnerable_Lua</c>.
/// </summary>
/// <remarks>
/// <para>
/// In session 2026-04-07, IDA Pro cross-validation found explicit class::method
/// strings in the decompile body of <c>sub_1405819E0</c>:
/// <c>"GameObjectWrapper::Teleport -- invalid number of parameters"</c> and
/// <c>"GameObjectWrapper:: Teleport -- unable to deduce a position from parameter 1"</c>.
/// This is the strongest possible single-tool evidence (compiler-emitted
/// strings) and supersedes the earlier Ghidra-derived label that called this
/// RVA <c>Make_Invulnerable_Lua</c>.
/// </para>
/// <para>
/// The fix in <c>knowledge-base/verified_facts.json</c>:
/// <list type="bullet">
///   <item>Added <c>rva_teleport_lua_wrapper</c> at <c>0x5819E0</c> with
///         <c>confidence: VERIFIED</c>.</item>
///   <item>Marked <c>rva_make_invulnerable_lua_ghidra</c> as
///         <c>confidence: DEPRECATED</c> with a note explaining the
///         supersession.</item>
///   <item>The real <c>Make_Invulnerable</c> Lua binding is at
///         <c>rva_make_invulnerable_lua_wrapper</c> = <c>0x57D550</c>.</item>
/// </list>
/// </para>
/// <para>
/// If this test fires, the ledger has been edited to revert the correction --
/// somebody has either re-claimed <c>0x5819E0</c> as <c>Make_Invulnerable</c>
/// or removed the <c>rva_teleport_lua_wrapper</c> entry. That would be a
/// data-integrity regression: hooks placed at <c>0x5819E0</c> with the wrong
/// identity will teleport the unit instead of making it invulnerable.
/// </para>
/// </remarks>
public sealed class TeleportRvaRegressionTests
{
    private const string LedgerEnvVar = "SWFOC_VERIFIED_FACTS";
    private const string DefaultLedgerPath =
        @"C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\verified_facts.json";
    private const string TeleportRva = "0x5819E0";

    private static string? ResolveLedgerPath()
    {
        var envOverride = System.Environment.GetEnvironmentVariable(LedgerEnvVar);
        if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
        {
            return envOverride;
        }
        return File.Exists(DefaultLedgerPath) ? DefaultLedgerPath : null;
    }

    [SkippableFact]
    public void Ledger_0x5819E0_IsTeleportNotMakeInvulnerable()
    {
        var ledgerPath = ResolveLedgerPath();
        Skip.If(
            ledgerPath is null,
            $"verified_facts.json not found at {DefaultLedgerPath} and ${LedgerEnvVar} not set");

        var json = File.ReadAllText(ledgerPath!);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 1. rva_teleport_lua_wrapper must exist with RVA 0x5819E0 and
        //    confidence VERIFIED.
        root.TryGetProperty("rva_teleport_lua_wrapper", out var teleportEntry)
            .Should().BeTrue(
                because: "session 2026-04-07 added rva_teleport_lua_wrapper " +
                         "to correct the Ghidra mislabel at 0x5819E0");

        teleportEntry.GetProperty("rva").GetString()
            .Should().Be(TeleportRva,
                because: "0x5819E0 is the verified Teleport wrapper address");

        teleportEntry.GetProperty("confidence").GetString()
            .Should().Be("VERIFIED",
                because: "IDA found explicit GameObjectWrapper::Teleport strings " +
                         "in the decompile body -- the strongest single-tool evidence");

        var teleportClaim = teleportEntry.GetProperty("claim").GetString() ?? string.Empty;
        teleportClaim.Should().Contain("Teleport",
            because: "the claim must explicitly identify this RVA as Teleport");
    }

    [SkippableFact]
    public void Ledger_DeprecatedGhidraEntry_IsStillMarkedDeprecated()
    {
        var ledgerPath = ResolveLedgerPath();
        Skip.If(
            ledgerPath is null,
            $"verified_facts.json not found at {DefaultLedgerPath} and ${LedgerEnvVar} not set");

        var json = File.ReadAllText(ledgerPath!);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 2. If rva_make_invulnerable_lua_ghidra still exists in the ledger,
        //    it must carry confidence DEPRECATED. (It is acceptable for this
        //    entry to be removed entirely; the test only fires if it exists
        //    with a non-deprecated grading.)
        if (!root.TryGetProperty("rva_make_invulnerable_lua_ghidra", out var ghidraEntry))
        {
            return; // entry was removed -- that's also acceptable
        }

        var confidence = ghidraEntry.GetProperty("confidence").GetString();
        confidence.Should().Be("DEPRECATED",
            because: "the Ghidra-mislabeled entry for 0x5819E0 was deprecated on " +
                     "2026-04-07 in favor of rva_teleport_lua_wrapper");
    }

    [SkippableFact]
    public void Ledger_NoActiveEntry_Claims_0x5819E0_IsMakeInvulnerable()
    {
        var ledgerPath = ResolveLedgerPath();
        Skip.If(
            ledgerPath is null,
            $"verified_facts.json not found at {DefaultLedgerPath} and ${LedgerEnvVar} not set");

        var json = File.ReadAllText(ledgerPath!);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 3. Walk every entry. If any non-DEPRECATED entry has rva == 0x5819E0
        //    AND its claim contains "Make_Invulnerable", flag it as a regression.
        //    The deprecated rva_make_invulnerable_lua_ghidra entry is allowed
        //    because its confidence is DEPRECATED.
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            if (!prop.Value.TryGetProperty("rva", out var rvaNode)) continue;
            if (rvaNode.ValueKind != JsonValueKind.String) continue;

            var entryRva = rvaNode.GetString();
            if (!string.Equals(entryRva, TeleportRva, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var confidence = prop.Value.TryGetProperty("confidence", out var confNode)
                ? confNode.GetString()
                : null;
            if (string.Equals(confidence, "DEPRECATED", System.StringComparison.OrdinalIgnoreCase))
            {
                continue; // tolerated
            }

            var claim = prop.Value.TryGetProperty("claim", out var claimNode)
                ? claimNode.GetString() ?? string.Empty
                : string.Empty;

            // Allowed: an active entry that claims this RVA is Teleport.
            // Disallowed: an active entry that claims this RVA is Make_Invulnerable.
            if (claim.Contains("Make_Invulnerable", System.StringComparison.Ordinal)
                && !claim.Contains("Teleport", System.StringComparison.Ordinal))
            {
                Assert.Fail(
                    $"Active ledger entry '{prop.Name}' claims RVA {TeleportRva} is " +
                    $"Make_Invulnerable, but session 2026-04-07 IDA evidence proved " +
                    $"this address is GameObjectWrapper::Teleport. " +
                    $"See knowledge-base/v5_service_fixes_applied.md and " +
                    $"rva_teleport_lua_wrapper for the correction. " +
                    $"Claim text: \"{claim}\"");
            }
        }
    }
}
