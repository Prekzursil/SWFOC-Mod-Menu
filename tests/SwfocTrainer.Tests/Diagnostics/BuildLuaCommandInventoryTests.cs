using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Diagnostics;

/// <summary>
/// Drift-guard regression for Task #119. Cross-references the hand-
/// curated <see cref="BuildLuaCommandInventory"/> table against the
/// actual <c>Build*LuaCommand</c> static methods defined in
/// <c>SwfocTrainer.Core.Services</c> via reflection.
///
/// When a service is renamed / added / deleted and the developer forgets
/// to update the inventory file, this test fails with a precise list of
/// "in inventory but missing from source" and "in source but missing
/// from inventory" diffs — so the audit stays honest.
/// </summary>
public sealed class BuildLuaCommandInventoryTests
{
    // Anchor a Core type so we can locate the assembly without a hard
    // reference to a specific service (which would create a circular
    // mental dependency between "inventory is authoritative" and "which
    // type it's anchored on").
    private static readonly Assembly _coreAssembly =
        typeof(BuildLuaCommandInventory).Assembly;

    private const string ServicesNamespace = "SwfocTrainer.Core.Services";

    [Fact]
    public void Inventory_MatchesActualBuildLuaCommandMethods()
    {
        var discovered = DiscoverBuildLuaCommandMethods();
        var inventoryKeys = BuildLuaCommandInventory.Entries.Keys.ToHashSet(StringComparer.Ordinal);

        var missingFromInventory = discovered
            .Where(k => !inventoryKeys.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        var staleInInventory = inventoryKeys
            .Where(k => !discovered.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        missingFromInventory.Should().BeEmpty(
            "discovered Build*LuaCommand methods in SwfocTrainer.Core.Services " +
            "must all be catalogued in BuildLuaCommandInventory — add entries for " +
            "[" + string.Join(", ", missingFromInventory) + "]");

        staleInInventory.Should().BeEmpty(
            "BuildLuaCommandInventory entries must correspond to real methods — " +
            "these inventory keys no longer match any source method (rename or " +
            "delete them): [" + string.Join(", ", staleInInventory) + "]");
    }

    [Fact]
    public void Every_Entry_Declares_At_Least_One_LuaEntryPoint()
    {
        // Every classified method should cite at least one Lua function
        // it dispatches — this is the evidence trail that makes the audit
        // auditable. A zero-entry count means "we don't know what this
        // method calls" which defeats the purpose of the inventory.
        foreach (var entry in BuildLuaCommandInventory.Entries.Values)
        {
            entry.LuaEntryPoints.Should().NotBeEmpty(
                $"{entry.ServiceTypeName}.{entry.MethodName} must cite the Lua " +
                "function(s) it dispatches");
        }
    }

    [Fact]
    public void RealBridge_Entries_Only_Cite_SWFOC_Prefix_Functions()
    {
        // RealBridge means "hits a registered SWFOC_* helper in the bridge".
        // If an entry is labelled RealBridge but cites a non-SWFOC function,
        // the label is wrong — likely it should be RealEngine.
        foreach (var entry in BuildLuaCommandInventory.RealBridgeEntries())
        {
            foreach (var luaFunc in entry.LuaEntryPoints)
            {
                luaFunc.Should().StartWith("SWFOC_",
                    $"{entry.ServiceTypeName}.{entry.MethodName} is labelled RealBridge " +
                    $"but cites '{luaFunc}' which is not a SWFOC_* helper. " +
                    "Re-label as RealEngine or fix the LuaEntryPoint entry.");
            }
        }
    }

    [Fact]
    public void Inventory_CountsMatchExpectations_AsSnapshotOfThisAudit()
    {
        // Snapshot the current audit totals. If this fails, look at the
        // inventory carefully and update the snapshot — don't just bump
        // the numbers. The whole point is that a path-category shift
        // should be noticed by a human reviewer.
        var byPath = BuildLuaCommandInventory.Entries.Values
            .GroupBy(e => e.Path)
            .ToDictionary(g => g.Key, g => g.Count());

        byPath.GetValueOrDefault(BuildLuaCommandPath.RealBridge).Should().BeGreaterThanOrEqualTo(17,
            "at least 17 RealBridge methods should exist (economy 7 + combat 3 + hero 2 + " +
            "inspector 1 + damage-log 1 + crash 1 + faction-switch 2 = 17 minimum)");
        byPath.GetValueOrDefault(BuildLuaCommandPath.RealEngine).Should().BeGreaterThanOrEqualTo(10,
            "at least 10 RealEngine methods should exist (maphack 2 + corruption 2 + " +
            "roster 1 + faction-dashboard 1 + fleet 1 + ownership 1 + planet 1 + story 1 + spawn 1 = 11)");
        byPath.GetValueOrDefault(BuildLuaCommandPath.PartialStub).Should().BeGreaterThanOrEqualTo(2,
            "at least 2 PartialStub methods (AiControl + Cooldown)");
        byPath.GetValueOrDefault(BuildLuaCommandPath.Unknown).Should().Be(0,
            "Unknown is a defensive default — no entry should ship with it set");
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static HashSet<string> DiscoverBuildLuaCommandMethods()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        foreach (var type in _coreAssembly.GetTypes())
        {
            if (type.Namespace != ServicesNamespace)
            {
                continue;
            }
            foreach (var method in type.GetMethods(flags))
            {
                if (!method.Name.StartsWith("Build", StringComparison.Ordinal) ||
                    !method.Name.EndsWith("LuaCommand", StringComparison.Ordinal))
                {
                    continue;
                }
                if (method.ReturnType != typeof(string))
                {
                    continue;
                }
                keys.Add($"{type.Name}.{method.Name}");
            }
        }
        return keys;
    }
}
