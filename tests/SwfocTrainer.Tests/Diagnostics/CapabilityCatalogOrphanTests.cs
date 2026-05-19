using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Diagnostics;

/// <summary>
/// 2026-04-27 (iter 40) — orphan guard: every <c>SWFOC_*</c> function the
/// editor source actually CALLS must be catalogued in
/// <see cref="CapabilityStatusCatalog"/>. Without this, a new bridge
/// helper added to the editor would silently render with "UNAVAILABLE"
/// in the operator UI badge — exactly the mis-signal the original
/// directive warned against.
/// </summary>
/// <remarks>
/// <para>
/// Scans <c>src/SwfocTrainer.App/V2/</c> + <c>src/SwfocTrainer.Core/Services/</c>
/// for tokens matching <c>SWFOC_[A-Z][A-Za-z_0-9]+\s*\(</c> (i.e. callsite
/// shapes, not arbitrary mentions in comments). Compares the set against
/// the catalog dictionary. Any helper called but not catalogued fails
/// the test with a list of names + a hint to add them to
/// <c>CapabilityStatusCatalog.cs</c>.
/// </para>
/// <para>
/// The reverse direction (catalogued but never called) is NOT enforced
/// — historical entries for retired helpers are fine to keep around as
/// a record of intent.
/// </para>
/// </remarks>
public sealed class CapabilityCatalogOrphanTests
{
    private readonly ITestOutputHelper _output;

    public CapabilityCatalogOrphanTests(ITestOutputHelper output) => _output = output;

    /// <summary>Resolve the editor source root from the test bin dir.</summary>
    private static string ResolveSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Couldn't locate src/ root.");
    }

    private static IEnumerable<string> EnumerateCsFiles(string root, params string[] subDirs)
    {
        foreach (var sub in subDirs)
        {
            var path = Path.Combine(root, sub);
            if (!Directory.Exists(path)) continue;
            foreach (var f in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                yield return f;
            }
        }
    }

    [Fact]
    public void EveryEditorCalledSwfocFunction_HasCatalogEntry()
    {
        // Match SWFOC_<Name>( — open paren is the call-site signal that
        // distinguishes from comments / doc references / string literals
        // that just happen to mention the name.
        var rx = new Regex(@"\bSWFOC_([A-Z][A-Za-z_0-9]*)\s*\(", RegexOptions.Compiled);
        var root = ResolveSourceRoot();
        var called = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var file in EnumerateCsFiles(root,
                     Path.Combine("SwfocTrainer.App", "V2"),
                     Path.Combine("SwfocTrainer.Core", "Services")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in rx.Matches(text))
            {
                called.Add("SWFOC_" + m.Groups[1].Value);
            }
        }
        // SWFOC_X is a doc-comment placeholder in V2UnitMutationDispatcher.cs;
        // not a real call site. Same exclusion list lives nowhere else, so
        // hard-code here.
        called.Remove("SWFOC_X");

        var catalogue = CapabilityStatusCatalog.Entries.Keys.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var orphans = called.Where(name => !catalogue.Contains(name)).OrderBy(n => n, System.StringComparer.Ordinal).ToList();

        if (orphans.Count > 0)
        {
            _output.WriteLine("Orphan SWFOC_* functions (called by editor but NOT in CapabilityStatusCatalog):");
            foreach (var o in orphans) _output.WriteLine("  " + o);
            _output.WriteLine($"Add entries to {Path.Combine(root, "SwfocTrainer.Core", "Diagnostics", "CapabilityStatusCatalog.cs")}");
        }

        orphans.Should().BeEmpty(
            "every editor-called SWFOC_* must be catalogued so the operator badge isn't misleading");
    }
}
