using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 309, Thread D arc post-finale): pin tests for
/// <see cref="MainViewModelV2.ResolveIconsRoot"/> precedence:
///   1. settings.IconsRoot (operator-explicit, persisted to v2_settings.json)
///   2. SWFOC_EXTRACTED_DDS_ROOT env var (operator-explicit, session-only)
///   3. null = no icons (graceful — null IconPath hides the Image control)
///
/// Whitespace-only settings value falls through to the env var.
///
/// Pinned to a dedicated xUnit collection so the SWFOC_EXTRACTED_DDS_ROOT
/// env-var manipulation here doesn't race with iter-307 / iter-308 tests
/// (different env var, different collection — they're orthogonal).
/// </summary>
[Collection("IconsRootEnv")]
public sealed class Iter309IconsRootResolutionTests : IDisposable
{
    private const string EnvVarName = "SWFOC_EXTRACTED_DDS_ROOT";
    private readonly string? _origEnv;

    public Iter309IconsRootResolutionTests()
    {
        _origEnv = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVarName, _origEnv);
    }

    [Fact]
    public void Settings_HasExplicitIconsRoot_TakesPrecedence_OverEnvVar()
    {
        Environment.SetEnvironmentVariable(EnvVarName, @"C:\env\icons");
        var settings = new V2Settings { IconsRoot = @"C:\settings\icons" };

        MainViewModelV2.ResolveIconsRoot(settings).Should().Be(@"C:\settings\icons",
            because: "operator-explicit settings value wins over env-var fallback");
    }

    [Fact]
    public void Settings_NullIconsRoot_FallsThroughToEnvVar()
    {
        Environment.SetEnvironmentVariable(EnvVarName, @"C:\env\icons");
        var settings = new V2Settings { IconsRoot = null };

        MainViewModelV2.ResolveIconsRoot(settings).Should().Be(@"C:\env\icons",
            because: "null settings value falls through to env var (operator-explicit, session-only)");
    }

    [Fact]
    public void Settings_EmptyIconsRoot_FallsThroughToEnvVar()
    {
        Environment.SetEnvironmentVariable(EnvVarName, @"C:\env\icons");
        var settings = new V2Settings { IconsRoot = string.Empty };

        MainViewModelV2.ResolveIconsRoot(settings).Should().Be(@"C:\env\icons",
            because: "empty string is treated as 'unset' and falls through to env var");
    }

    [Fact]
    public void Settings_WhitespaceIconsRoot_FallsThroughToEnvVar()
    {
        Environment.SetEnvironmentVariable(EnvVarName, @"C:\env\icons");
        var settings = new V2Settings { IconsRoot = "   " };

        MainViewModelV2.ResolveIconsRoot(settings).Should().Be(@"C:\env\icons",
            because: "whitespace-only is treated as 'unset' (operator probably typo'd)");
    }

    [Fact]
    public void Both_NullSettings_AndNullEnv_ReturnsNull()
    {
        Environment.SetEnvironmentVariable(EnvVarName, null);
        var settings = new V2Settings { IconsRoot = null };

        MainViewModelV2.ResolveIconsRoot(settings).Should().BeNull(
            because: "no operator config = no icons; UnitIconResolver gracefully returns null IconPath, which hides the Image control");
    }

    [Fact]
    public void Both_EmptySettings_AndEmptyEnv_ReturnsNull()
    {
        Environment.SetEnvironmentVariable(EnvVarName, string.Empty);
        var settings = new V2Settings { IconsRoot = string.Empty };

        MainViewModelV2.ResolveIconsRoot(settings).Should().BeNull(
            because: "empty values at every level should normalize to null, not propagate empty strings into the resolver");
    }

    [Fact]
    public void NullSettings_Throws()
    {
        var act = () => MainViewModelV2.ResolveIconsRoot(null!);
        act.Should().Throw<ArgumentNullException>(
            because: "null settings is a programmer bug, not an operator state — fail fast at the boundary");
    }
}
