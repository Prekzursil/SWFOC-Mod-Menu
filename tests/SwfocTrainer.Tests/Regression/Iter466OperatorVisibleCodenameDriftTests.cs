using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 466): wider iter-388 drift audit regression guard.
/// Scans MainWindowV2.xaml operator-visible attribute values
/// (ToolTip, Text, Header, Content) for internal "iter N" / "iter-N"
/// codename leaks. XAML comments are excluded by the attribute-only
/// regex per iter-388 codified rule "How to apply" (comments OK,
/// operator-visible text not OK).
///
/// Red form: 5 hits existed pre-iter-466 (4 Inspector tooltips + 1
/// Save Monitor banner). Green form: 0 hits post-iter-466 edits.
/// 3rd recursive self-validation of iter-388 codified rule.
/// </summary>
public sealed class Iter466OperatorVisibleCodenameDriftTests
{
    private static string LoadXamlSource()
    {
        var asmDir = Path.GetDirectoryName(typeof(Iter466OperatorVisibleCodenameDriftTests).Assembly.Location)!;
        var dir = new DirectoryInfo(asmDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("test must locate MainWindowV2.xaml");
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml"));
    }

    [Fact]
    public void OperatorVisibleAttributes_HaveNoIterCodenameLeaks()
    {
        var xaml = LoadXamlSource();
        var pattern = new Regex(
            @"(?<attr>ToolTip|Text|Header|Content)\s*=\s*""(?<value>[^""]*iter[ -]?\d+[^""]*)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var hits = new List<string>();
        foreach (Match m in pattern.Matches(xaml))
        {
            var lineNo = 1;
            for (var i = 0; i < m.Index; i++)
            {
                if (xaml[i] == '\n') lineNo++;
            }
            var attr = m.Groups["attr"].Value;
            var value = m.Groups["value"].Value;
            if (value.Length > 140) value = value.Substring(0, 140) + "...";
            hits.Add($"L{lineNo}: {attr}=\"{value}\"");
        }

        hits.Should().BeEmpty(
            "iter-388 codified rule forbids 'iter N' / 'iter-N' internal codenames in operator-visible XAML attribute values. " +
            "Capability badges already surface LIVE/PHASE2 status. " +
            "Demote codenames to functional descriptions; XAML <!-- comments --> are exempt and may keep iter-N refs. " +
            "Hits: " + string.Join(" | ", hits));
    }
}
