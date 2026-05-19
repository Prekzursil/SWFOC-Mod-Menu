using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// Guards <c>SwfocTrainer.Runtime.Services.ValueFreezeService</c> against re-introducing
/// the system-wide-freeze pattern that took down a host PC during live-game testing on
/// 2026-04-26.
/// </summary>
/// <remarks>
/// <para>
/// Timeline:
/// <list type="number">
/// <item>Pre-2026-04-27: <c>AggressiveWriteLoop</c> used <c>winmm.dll!timeBeginPeriod(1)</c> +
/// a tight <c>Thread.Sleep(1)</c> loop running at <c>ThreadPriority.AboveNormal</c> with
/// <c>WriteAsync().GetAwaiter().GetResult()</c> sync-over-async IPC. The combination raised the
/// system-wide OS scheduler tick rate to 1ms (affects every process on the host), preempted the
/// desktop compositor, and could deadlock the bridge IPC. User reported the host PC becoming
/// unresponsive and required physical restart.</item>
/// <item>2026-04-27: replaced with a <see cref="System.Threading.PeriodicTimer"/>-based async
/// pump at the natural 16ms (60 fps) game-frame cadence, on the default thread pool, with
/// proper <c>await</c>. See <c>knowledge-base/freeze_audit_2026-04-27.md</c>.</item>
/// </list>
/// </para>
/// <para>
/// The regression pair below is red-on-old-shape / green-on-new-shape. A future simplification
/// that re-introduces winmm.dll, custom thread priorities, or sync-over-async on the freeze
/// path must fail here.
/// </para>
/// </remarks>
public sealed class ValueFreezeServiceFreezeRegressionTests
{
    private static string LoadServiceSource()
    {
        // The test runs from tests/SwfocTrainer.Tests/bin/Debug/net8.0-windows/.
        // Walk up to the editor solution root, then into the source file.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(
                dir,
                "src", "SwfocTrainer.Runtime", "Services", "ValueFreezeService.cs");
            if (File.Exists(candidate))
            {
                // Strip /// xmldoc, // line comments, /* block comments */, and "string literals"
                // so the regression checks match real API uses, not historical-context docs that
                // intentionally mention the banned identifiers.
                var raw = File.ReadAllText(candidate);
                return StripCommentsAndStrings(raw);
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "ValueFreezeService.cs not found by walking up from " + AppContext.BaseDirectory);
    }

    private static string StripCommentsAndStrings(string src)
    {
        // Block comments
        src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        // Line comments (incl. ///)
        src = Regex.Replace(src, @"//[^\n]*", string.Empty);
        // Verbatim @"...", interpolated $"...", and regular "..." string literals.
        // Conservative: drop everything between unescaped quotes on the same line.
        src = Regex.Replace(src, @"@""(?:""""|[^""])*""", string.Empty);
        src = Regex.Replace(src, @"\$""(?:\\.|[^""\\])*""", string.Empty);
        src = Regex.Replace(src, @"""(?:\\.|[^""\\])*""", string.Empty);
        return src;
    }

    [Fact]
    public void Regression_NoWinmmTimerImport()
    {
        // RED on the old shape: file imported winmm.dll's timeBeginPeriod / timeEndPeriod.
        // GREEN on the new shape: no winmm.dll references.
        var src = LoadServiceSource();

        src.Should().NotContain("winmm.dll",
            "ValueFreezeService must not raise the system-wide OS timer resolution; "
            + "see knowledge-base/freeze_audit_2026-04-27.md for why this freezes the host PC.");
        src.Should().NotContain("timeBeginPeriod",
            "Even an indirect timeBeginPeriod call would re-introduce the system-wide freeze.");
        src.Should().NotContain("TimeBeginPeriod",
            "P/Invoke alias for timeBeginPeriod is also disallowed.");
    }

    [Fact]
    public void Regression_NoSyncOverAsyncOnAggressiveFreezePath()
    {
        // RED on the old shape: AggressiveWriteLoop did `_runtime.WriteAsync(...).GetAwaiter().GetResult()`.
        // GREEN on the new shape: pure async/await, no .Result and no .GetAwaiter().GetResult().
        var src = LoadServiceSource();

        // The aggressive pump should never use sync-over-async. Allow these patterns
        // elsewhere in the file only if a future change introduces them outside the
        // aggressive section, but currently the simplest assertion is "absent everywhere".
        src.Should().NotContain("WriteAsync(entry.Symbol, entry.Value).GetAwaiter().GetResult()",
            "sync-over-async on the aggressive freeze path can deadlock the WPF UI thread.");
        src.Should().NotContain(".GetAwaiter().GetResult()",
            "ValueFreezeService is fully async — sync-over-async is forbidden anywhere in this file.");
    }

    [Fact]
    public void Regression_NoCustomThreadPriorityOnAggressivePump()
    {
        // RED on the old shape: aggressive thread used ThreadPriority.AboveNormal.
        // GREEN on the new shape: no ThreadPriority customisation in this file.
        var src = LoadServiceSource();

        src.Should().NotContain("ThreadPriority.AboveNormal",
            "Aggressive freeze must not preempt the desktop compositor or game render thread.");
        src.Should().NotContain("ThreadPriority.Highest",
            "Highest priority on a polling pump is even worse — disallowed.");
        src.Should().NotContain("Priority = ThreadPriority",
            "Any ThreadPriority customisation in ValueFreezeService is disallowed.");
    }

    [Fact]
    public void Regression_AggressivePumpUsesPeriodicTimerNotBusyLoop()
    {
        // GREEN on the new shape: pump uses PeriodicTimer at AggressivePulseIntervalMs.
        var src = LoadServiceSource();

        src.Should().Contain("PeriodicTimer",
            "Aggressive pump must use PeriodicTimer (not Thread.Sleep busy loop).");
        src.Should().Contain("AggressivePulseIntervalMs",
            "Aggressive pump cadence must be a named constant (currently 16ms = 60 fps).");
    }

    [Fact]
    public void Regression_AggressiveCadenceIsAtLeast10ms()
    {
        // The cadence must be slow enough not to thrash the host. 16ms (60fps) is correct;
        // anything below 10ms would be a regression toward the busy-loop pattern.
        var src = LoadServiceSource();

        // Find the constant declaration.
        var marker = "AggressivePulseIntervalMs";
        var idx = src.IndexOf(marker, StringComparison.Ordinal);
        idx.Should().BeGreaterThan(-1, "the cadence constant should be declared in this file.");

        // Find an `=` and the int literal immediately after.
        var eqIdx = src.IndexOf('=', idx);
        eqIdx.Should().BeGreaterThan(-1);
        var semiIdx = src.IndexOf(';', eqIdx);
        semiIdx.Should().BeGreaterThan(-1);
        var literal = src.Substring(eqIdx + 1, semiIdx - eqIdx - 1).Trim();
        var ok = int.TryParse(literal, out var ms);
        ok.Should().BeTrue($"cadence literal '{literal}' should be a plain int.");
        ms.Should().BeGreaterThanOrEqualTo(10,
            "cadence must be >=10ms to avoid host-system thrash.");
        ms.Should().BeLessThanOrEqualTo(33,
            "cadence must be <=33ms (~30fps) so the freeze still wins the race against "
            + "the game's per-frame float→int sync.");
    }
}
