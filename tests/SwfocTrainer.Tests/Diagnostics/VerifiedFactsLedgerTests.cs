using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Diagnostics;

/// <summary>
/// Task #120 — unit + integration tests for the typed wrapper over
/// knowledge-base/verified_facts.json. Verifies schema invariants are
/// enforced loudly at load time so drift can't sneak past.
/// </summary>
public sealed class VerifiedFactsLedgerTests
{
    private const string HappyJson = """
    {
      "api_lua_checkstack": {
        "category": "lua_api",
        "claim": "lua_checkstack Lua 5.0.2 C API entry.",
        "confidence": "VERIFIED",
        "first_documented": "2025-06-15",
        "last_verified": "2026-04-07",
        "rva": "0x7B8BC0",
        "tool_findings": { "ghidra": {}, "ida_pro": {} },
        "tools_consensus": ["ghidra", "ida_pro"]
      },
      "api_lua_close_legacy": {
        "category": "lua_api",
        "claim": "DEPRECATED: superseded by api_lua_close.",
        "confidence": "DEPRECATED",
        "first_documented": "2025-06-15",
        "last_verified": "2026-04-04",
        "rva": "0x7B8A70",
        "tool_findings": {},
        "tools_consensus": []
      },
      "bhvr_focus_drain_timer": {
        "category": "behavior_finding",
        "claim": "SetTimer WM_TIMER drives Lua VM when SWFOC loses focus.",
        "confidence": "LIVE_OBSERVED",
        "first_documented": "2026-04-23",
        "last_verified": "2026-04-23",
        "tool_findings": { "frida_runtime": {} },
        "tools_consensus": ["frida_runtime"]
      }
    }
    """;

    // ─── Happy path ─────────────────────────────────────────────

    [Fact]
    public void LoadFromJson_HappyPath_ParsesAllEntries()
    {
        var ledger = VerifiedFactsLoader.LoadFromJson(HappyJson);
        ledger.Count.Should().Be(3);
        ledger.Entries.Keys.Should().Contain(new[] {
            "api_lua_checkstack", "api_lua_close_legacy", "bhvr_focus_drain_timer" });
    }

    [Fact]
    public void LoadFromJson_ConfidenceMapping_ReflectsJsonValue()
    {
        var ledger = VerifiedFactsLoader.LoadFromJson(HappyJson);
        ledger.TryGet("api_lua_checkstack")!.Confidence.Should().Be(VerifiedFactConfidence.Verified);
        ledger.TryGet("api_lua_close_legacy")!.Confidence.Should().Be(VerifiedFactConfidence.Deprecated);
        ledger.TryGet("bhvr_focus_drain_timer")!.Confidence.Should().Be(VerifiedFactConfidence.LiveObserved);
    }

    [Fact]
    public void GetRva_ParsesHexString_OrReturnsNull()
    {
        var ledger = VerifiedFactsLoader.LoadFromJson(HappyJson);
        ledger.GetRva("api_lua_checkstack").Should().Be(0x7B8BC0);
        ledger.GetRva("api_lua_close_legacy").Should().Be(0x7B8A70);
        ledger.GetRva("bhvr_focus_drain_timer").Should().BeNull("behavior finding has no RVA");
        ledger.GetRva("missing").Should().BeNull("missing entry returns null");
    }

    [Fact]
    public void IsMultiToolVerified_GatesOnBothConfidenceAndConsensusLength()
    {
        var ledger = VerifiedFactsLoader.LoadFromJson(HappyJson);
        ledger.IsMultiToolVerified("api_lua_checkstack").Should().BeTrue(
            "VERIFIED + tools_consensus length 2 meets the hook-install bar");
        ledger.IsMultiToolVerified("api_lua_close_legacy").Should().BeFalse(
            "DEPRECATED entries never pass the hook-install bar");
        ledger.IsMultiToolVerified("bhvr_focus_drain_timer").Should().BeFalse(
            "LIVE_OBSERVED is not the strictest tier");
        ledger.IsMultiToolVerified("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void ByCategory_FiltersCaseInsensitively()
    {
        var ledger = VerifiedFactsLoader.LoadFromJson(HappyJson);
        var lua = ledger.ByCategory("lua_api").ToList();
        lua.Should().HaveCount(2);

        var lua2 = ledger.ByCategory("LUA_API").ToList();
        lua2.Should().HaveCount(2, "category filter is case-insensitive");

        ledger.ByCategory("unknown_cat").Should().BeEmpty();
    }

    [Fact]
    public void ByConfidence_FiltersByEnum()
    {
        var ledger = VerifiedFactsLoader.LoadFromJson(HappyJson);
        ledger.ByConfidence(VerifiedFactConfidence.Verified).Should().HaveCount(1);
        ledger.ByConfidence(VerifiedFactConfidence.Deprecated).Should().HaveCount(1);
        ledger.ByConfidence(VerifiedFactConfidence.LiveObserved).Should().HaveCount(1);
        ledger.ByConfidence(VerifiedFactConfidence.Unverified).Should().BeEmpty();
    }

    // ─── Schema failures ────────────────────────────────────────

    [Fact]
    public void LoadFromJson_MissingRequiredField_Throws()
    {
        const string bad = """
        { "oops": { "category": "lua_api", "confidence": "VERIFIED",
          "tools_consensus": ["ghidra","ida_pro"], "tool_findings": {} } }
        """;
        Action load = () => VerifiedFactsLoader.LoadFromJson(bad);
        load.Should().Throw<LedgerDriftException>()
            .WithMessage("*missing required field 'claim'*");
    }

    [Fact]
    public void LoadFromJson_UnknownConfidence_Throws()
    {
        const string bad = """
        { "oops": {
            "claim": "c", "category": "lua_api",
            "confidence": "MAYBE_OK",
            "tool_findings": {}, "tools_consensus": []
          } }
        """;
        Action load = () => VerifiedFactsLoader.LoadFromJson(bad);
        load.Should().Throw<LedgerDriftException>()
            .WithMessage("*unknown confidence 'MAYBE_OK'*");
    }

    [Fact]
    public void LoadFromJson_VerifiedWithSingleTool_Throws()
    {
        // Single-tool VERIFIED is the exact sneak-through shape Task
        // #120 was filed to prevent — a rushed claim getting labelled
        // VERIFIED with only ghidra's word. The loader must reject it.
        const string bad = """
        { "oops": {
            "claim": "c", "category": "lua_api",
            "confidence": "VERIFIED",
            "rva": "0xABC",
            "tool_findings": { "ghidra": {} },
            "tools_consensus": ["ghidra"]
          } }
        """;
        Action load = () => VerifiedFactsLoader.LoadFromJson(bad);
        load.Should().Throw<LedgerDriftException>()
            .WithMessage("*VERIFIED requires tools_consensus length >= 2*");
    }

    [Fact]
    public void LoadFromJson_VerifiedWithZeroTools_Throws()
    {
        const string bad = """
        { "oops": {
            "claim": "c", "category": "lua_api",
            "confidence": "VERIFIED",
            "tool_findings": {},
            "tools_consensus": []
          } }
        """;
        Action load = () => VerifiedFactsLoader.LoadFromJson(bad);
        load.Should().Throw<LedgerDriftException>()
            .WithMessage("*VERIFIED requires tools_consensus length >= 2*");
    }

    [Fact]
    public void LoadFromJson_DeprecatedWithZeroTools_Succeeds()
    {
        // DEPRECATED entries often drop their tools_consensus because
        // the superseding entry carries the evidence — the loader must
        // not reject that shape.
        const string ok = """
        { "old_thing": {
            "claim": "superseded", "category": "lua_api",
            "confidence": "DEPRECATED",
            "tool_findings": {}, "tools_consensus": []
          } }
        """;
        var ledger = VerifiedFactsLoader.LoadFromJson(ok);
        ledger.Count.Should().Be(1);
    }

    [Fact]
    public void LoadFromJson_InvalidRvaString_Throws()
    {
        const string bad = """
        { "oops": {
            "claim": "c", "category": "lua_api",
            "confidence": "UNVERIFIED",
            "rva": "not-hex",
            "tool_findings": {}, "tools_consensus": []
          } }
        """;
        Action load = () => VerifiedFactsLoader.LoadFromJson(bad);
        load.Should().Throw<LedgerDriftException>()
            .WithMessage("*rva='not-hex' is not a hex integer*");
    }

    [Fact]
    public void LoadFromJson_MalformedJson_Throws()
    {
        Action load = () => VerifiedFactsLoader.LoadFromJson("{ not valid json");
        load.Should().Throw<LedgerDriftException>()
            .WithMessage("*Ledger JSON is malformed*");
    }

    [Fact]
    public void LoadFromJson_NonObjectRoot_Throws()
    {
        Action load = () => VerifiedFactsLoader.LoadFromJson("[\"a\", \"b\"]");
        load.Should().Throw<LedgerDriftException>()
            .WithMessage("*root must be a JSON object*");
    }

    [Fact]
    public void LoadFromJson_AggregatesMultipleErrors()
    {
        // The loader shouldn't bail on the first error — the operator
        // wants to see the whole list so they can fix them in one pass.
        const string bad = """
        {
          "bad1": { "confidence": "VERIFIED", "category": "x",
            "tool_findings": {}, "tools_consensus": [] },
          "bad2": { "claim": "c", "category": "x", "confidence": "WAT",
            "tool_findings": {}, "tools_consensus": [] }
        }
        """;
        Action load = () => VerifiedFactsLoader.LoadFromJson(bad);
        var ex = load.Should().Throw<LedgerDriftException>().Which;
        ex.Message.Should().Contain("bad1");
        ex.Message.Should().Contain("bad2");
    }

    // ─── Integration with the real file ─────────────────────────

    [Fact]
    public void LoadFromPath_OnTheRealLedger_Succeeds()
    {
        // This is the drift-guard for the ACTUAL ledger — if someone
        // hand-edits knowledge-base/verified_facts.json and breaks the
        // schema, this test fails with the precise offending entries.
        var path = ResolveRealLedgerPath();
        var ledger = VerifiedFactsLoader.LoadFromPath(path);

        ledger.Count.Should().BeGreaterThan(300, "the ledger has 310+ entries as of 2026-04-24");
        ledger.ByConfidence(VerifiedFactConfidence.Verified).Should().NotBeEmpty();
    }

    private static string ResolveRealLedgerPath()
    {
        // Walk up from the test output dir to find the repo root
        // (knowledge-base/verified_facts.json sibling). Supports both
        // the editor repo layout and local dev runs.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var sibling = Path.Combine(dir, "knowledge-base", "verified_facts.json");
            if (File.Exists(sibling))
            {
                return sibling;
            }
            // The editor lives at "C:\Users\...\Downloads\SWFOC editor"
            // and the ledger lives at "C:\Users\...\Downloads\swfoc_memory\knowledge-base\verified_facts.json".
            // Try the known side-by-side layout explicitly.
            var siblingRepo = Path.Combine(dir, "..", "swfoc_memory", "knowledge-base", "verified_facts.json");
            if (File.Exists(siblingRepo))
            {
                return Path.GetFullPath(siblingRepo);
            }
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: the known absolute path from CLAUDE.md conventions.
        const string hardcoded = @"C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\verified_facts.json";
        if (File.Exists(hardcoded))
        {
            return hardcoded;
        }
        throw new FileNotFoundException(
            "Could not locate knowledge-base/verified_facts.json from test output dir.");
    }
}
