using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit.Abstractions;
using Xunit;

namespace SwfocTrainer.Tests.Common;

internal static class LiveSkip
{
    public static Exception For(ITestOutputHelper output, string reason)
    {
        output.WriteLine($"SKIP — {reason}");
        return new SkipException(reason);
    }

    /// <summary>
    /// 2026-04-29 (iter 120/121): wraps <c>RuntimeAdapter.AttachAsync(string)</c>
    /// to convert the <c>ATTACH_NO_PROCESS</c> path into a clean SKIP rather
    /// than a FAIL. The locator's <c>FindBestMatchAsync</c> can return a
    /// non-null <c>ProcessMetadata</c> for sidecar processes (e.g.
    /// <c>SwfocExtender.Host</c>) that look SWFOC-related but aren't the
    /// actual game executable. The profile-aware <c>AttachAsync</c> then
    /// re-runs <c>SelectProcessForProfileAsync</c> with stricter matching
    /// and throws <c>ATTACH_NO_PROCESS</c> — that's a "no game" signal,
    /// NOT a test failure. Centralised here so every Live* test gets the
    /// same defensive behaviour.
    /// </summary>
    public static async Task<AttachSession> AttachOrSkipAsync(
        RuntimeAdapter runtime,
        string profileId,
        ITestOutputHelper output)
    {
        try
        {
            return await runtime.AttachAsync(profileId);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ATTACH_NO_PROCESS", StringComparison.Ordinal))
        {
            throw For(output,
                "locator matched a sidecar process but profile-aware AttachAsync " +
                $"could not locate the actual game executable: {ex.Message}");
        }
    }
}
