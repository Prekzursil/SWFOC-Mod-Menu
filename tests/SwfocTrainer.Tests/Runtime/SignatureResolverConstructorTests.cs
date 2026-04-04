using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage tests for SignatureResolver (the main partial class file).
/// Covers constructor guards, ResolveAsync null guards, and SelectBestGhidraPackPath delegation.
/// The ResolveInternal method requires a real running process so we test it indirectly
/// through the public API guards.
/// </summary>
public sealed class SignatureResolverConstructorTests
{
    // ──────────────── Constructor guards ────────────────

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new SignatureResolver(null!, "some-root");
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullGhidraRoot_ThrowsArgumentNullException()
    {
        var act = () => new SignatureResolver(NullLogger<SignatureResolver>.Instance, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("ghidraSymbolPackRoot");
    }

    [Fact]
    public void Constructor_ValidArgs_DoesNotThrow()
    {
        var act = () => new SignatureResolver(NullLogger<SignatureResolver>.Instance, "some-root");
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_DefaultRoot_DoesNotThrow()
    {
        // Uses the parameterless-style constructor (logger only) which calls ResolveDefaultGhidraSymbolPackRoot
        var act = () => new SignatureResolver(NullLogger<SignatureResolver>.Instance);
        act.Should().NotThrow();
    }

    // ──────────────── ResolveAsync (4-param) — null guards ────────────────

    [Fact]
    public async Task ResolveAsync_NullProfileBuild_ThrowsArgumentNullException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        var act = () => resolver.ResolveAsync(
            null!,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("profileBuild");
    }

    [Fact]
    public async Task ResolveAsync_NullSignatureSets_ThrowsArgumentNullException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        var build = new ProfileBuild("p", "1.0", "game.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(
            build,
            null!,
            new Dictionary<string, long>(),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("signatureSets");
    }

    [Fact]
    public async Task ResolveAsync_NullFallbackOffsets_ThrowsArgumentNullException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        var build = new ProfileBuild("p", "1.0", "game.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(
            build,
            Array.Empty<SignatureSet>(),
            null!,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("fallbackOffsets");
    }

    // ──────────────── ResolveAsync (3-param overload) — null guards ────────────────

    [Fact]
    public async Task ResolveAsync3_NullProfileBuild_ThrowsArgumentNullException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        var act = () => resolver.ResolveAsync(
            null!,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>());

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("profileBuild");
    }

    [Fact]
    public async Task ResolveAsync3_NullSignatureSets_ThrowsArgumentNullException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        var build = new ProfileBuild("p", "1.0", "game.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(
            build,
            null!,
            new Dictionary<string, long>());

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("signatureSets");
    }

    [Fact]
    public async Task ResolveAsync3_NullFallbackOffsets_ThrowsArgumentNullException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        var build = new ProfileBuild("p", "1.0", "game.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(
            build,
            Array.Empty<SignatureSet>(),
            null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("fallbackOffsets");
    }

    // ──────────────── ResolveAsync — process not found ────────────────

    [Fact]
    public async Task ResolveAsync_ProcessNotFound_ThrowsInvalidOperationException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        // Use a PID that doesn't exist and an exe path that doesn't match any running process
        var build = new ProfileBuild("test-profile", "1.0", "nonexistent_game_xyz_12345.exe", ExeTarget.Swfoc, ProcessId: 99999999);

        var act = () => resolver.ResolveAsync(
            build,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Could not find running process*");
    }

    [Fact]
    public async Task ResolveAsync_ProcessIdZero_ExePathEmpty_ThrowsInvalidOperationException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        // ProcessId = 0, empty exe path → name search returns null
        var build = new ProfileBuild("test-profile", "1.0", "", ExeTarget.Swfoc, ProcessId: 0);

        var act = () => resolver.ResolveAsync(
            build,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Could not find running process*");
    }

    [Fact]
    public async Task ResolveAsync_ProcessIdZero_ExePathWhitespace_ThrowsInvalidOperationException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        var build = new ProfileBuild("test-profile", "1.0", "   ", ExeTarget.Swfoc, ProcessId: 0);

        var act = () => resolver.ResolveAsync(
            build,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Could not find running process*");
    }

    [Fact]
    public async Task ResolveAsync_ProcessIdZero_NonExistentExeName_ThrowsInvalidOperationException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        var build = new ProfileBuild("test-profile", "1.0", "zzz_no_such_process_xyz.exe", ExeTarget.Swfoc, ProcessId: 0);

        var act = () => resolver.ResolveAsync(
            build,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Could not find running process*");
    }

    // ──────────────── SelectBestGhidraPackPath — delegation ────────────────

    [Fact]
    public void SelectBestGhidraPackPath_DelegatesToSymbolHydration()
    {
        // This just verifies the delegation works; actual logic tested in SymbolHydration tests
        var result = SignatureResolver.SelectBestGhidraPackPath("nonexistent-root", "some_fp");
        result.Should().BeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_NullRoot_ThrowsViaDelegate()
    {
        var act = () => SignatureResolver.SelectBestGhidraPackPath(null!, "fp");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SelectBestGhidraPackPath_NullFingerprint_ThrowsViaDelegate()
    {
        var act = () => SignatureResolver.SelectBestGhidraPackPath("root", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ──────────────── ResolveAsync — cancellation ────────────────

    [Fact]
    public async Task ResolveAsync_CancelledToken_ThrowsTaskCanceledException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, "root");
        var build = new ProfileBuild("test", "1.0", "nonexistent.exe", ExeTarget.Swfoc, ProcessId: 99999999);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => resolver.ResolveAsync(
            build,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            cts.Token);

        // Either TaskCanceledException or OperationCanceledException
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
