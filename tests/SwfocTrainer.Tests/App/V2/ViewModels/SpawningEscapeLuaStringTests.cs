using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// Pins the 2026-04-27 (iter 15) <see cref="SpawningTabViewModel.EscapeLuaString"/>
/// helper that defends against backslash / double-quote injection in
/// type-name payloads sent through <c>SWFOC_BatchTypeExists</c>.
/// </summary>
/// <remarks>
/// Lua 5.0 string-literal escapes that matter for our payload shape:
/// <c>\\</c> → backslash, <c>\"</c> → double-quote. Anything else passes
/// through unchanged. Mod authors who add weird type names with embedded
/// quotes / backslashes still flow through cleanly.
/// </remarks>
public sealed class SpawningEscapeLuaStringTests
{
    [Fact]
    public void Empty_String_PassesThrough()
    {
        SpawningTabViewModel.EscapeLuaString(string.Empty).Should().BeEmpty();
    }

    [Theory]
    [InlineData("REBEL_INFANTRY", "REBEL_INFANTRY")]
    [InlineData("AOTR_REBEL_TANK", "AOTR_REBEL_TANK")]
    [InlineData("a|b|c", "a|b|c")]
    public void Plain_Names_PassUnchanged(string input, string expected)
    {
        SpawningTabViewModel.EscapeLuaString(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("path\\with\\backslash", "path\\\\with\\\\backslash")]
    [InlineData("\\", "\\\\")]
    [InlineData("\\\\", "\\\\\\\\")]
    public void Backslash_Doubled(string input, string expected)
    {
        SpawningTabViewModel.EscapeLuaString(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("\"quoted\"", "\\\"quoted\\\"")]
    [InlineData("a\"b", "a\\\"b")]
    public void Double_Quote_Backslashed(string input, string expected)
    {
        SpawningTabViewModel.EscapeLuaString(input).Should().Be(expected);
    }

    [Fact]
    public void Mixed_Escapes_BothApplied()
    {
        // "C:\path\\with\"quotes" — both backslash and quote need escaping.
        // Note that test-source string uses C# escape: actual content is:
        //   C:\path\\with"quotes
        // Lua-escape result:
        //   C:\\path\\\\with\"quotes
        var input = "C:\\path\\\\with\"quotes";
        var expected = "C:\\\\path\\\\\\\\with\\\"quotes";
        SpawningTabViewModel.EscapeLuaString(input).Should().Be(expected);
    }

    [Fact]
    public void Order_Backslash_Before_Quote_PreventsDoubleEscape()
    {
        // If we did Replace("\"") first then Replace("\\"), the inserted
        // backslashes from the quote step would be re-doubled. Backslash
        // must run FIRST. Pin that here so a future "swap the order"
        // refactor breaks the test.
        var input = "\\\"";          // literal: backslash + quote
        var expected = "\\\\\\\"";   // literal: \\\\ + \" → 4 chars in C#
        SpawningTabViewModel.EscapeLuaString(input).Should().Be(expected);
    }
}
