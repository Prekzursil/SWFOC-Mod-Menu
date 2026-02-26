using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Scanning;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Profiles;

/// <summary>
/// Live integration tests for credits read/write against the running game process.
/// These tests require swfoc to be running and will be skipped if no process is found.
/// </summary>
public sealed class LiveCreditsTests
{
    private readonly ITestOutputHelper _output;
    private sealed record CreditsSyncInstructionCandidate(nint Address, byte[] OriginalBytes, string PatternName);

    private static readonly (string Name, string Aob)[] CreditsSyncPatterns =
    {
        ("exact_hook", "F3 0F 2C 50 70 89 57"),
        ("cvttss2si_edx_rax_70", "F3 0F 2C 50 70"),
        ("cvttss2si_ecx_rax_70", "F3 0F 2C 48 70"),
        ("cvttss2si_eax_rax_70", "F3 0F 2C 40 70"),
        ("cvttss2si_edx_rcx_70", "F3 0F 2C 51 70"),
        ("cvttss2si_edx_rdx_70", "F3 0F 2C 52 70"),
        ("cvttss2si_edx_rbx_70", "F3 0F 2C 53 70"),
        ("cvttss2si_edx_rax_78", "F3 0F 2C 50 78"),
        ("cvttss2si_edx_rax_68", "F3 0F 2C 50 68"),
        ("cvttss2si_any_70", "F3 0F 2C ?? 70"),
        ("cvttss2si_any_any", "F3 0F 2C"),
    };

    public LiveCreditsTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Credits_LiveDiagnostic_Should_Identify_Working_Strategy()
    {
        // ── 1. ATTACH ────────────────────────────────────────────────────────
        var locator = new ProcessLocator();
        var running = await locator.FindBestMatchAsync(ExeTarget.Swfoc);
        if (running is null)
        {
            throw LiveSkip.For(_output, "no swfoc process found.");
        }

        var repoRoot = TestPaths.FindRepoRoot();
        var profileRepo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(repoRoot, "profiles", "default")
        });
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance);
        var runtime = new RuntimeAdapter(locator, profileRepo, resolver, NullLogger<RuntimeAdapter>.Instance);

        var profiles = await ResolveProfilesAsync(profileRepo);
        var context = running.LaunchContext ?? new LaunchContextResolver().Resolve(running, profiles);
        var profileId = context.Recommendation.ProfileId ?? "base_swfoc";

        _output.WriteLine(
            $"Profile: {profileId} (reason={context.Recommendation.ReasonCode}, confidence={context.Recommendation.Confidence:0.00}) PID: {running.ProcessId}");
        var session = await runtime.AttachAsync(profileId);
        runtime.IsAttached.Should().BeTrue("runtime should report attached after a successful attach");
        var completed = await ExecuteCreditsDiagnosticsAsync(runtime, session);
        await runtime.DetachAsync();
        if (!completed)
        {
            throw LiveSkip.For(_output, "credits diagnostics did not complete; live prerequisites were not satisfied.");
        }

        _output.WriteLine("\nDone. Detached.");
    }

    private async Task<bool> ExecuteCreditsDiagnosticsAsync(RuntimeAdapter runtime, AttachSession session)
    {
        var symbolRead = await TryResolveCreditsSymbolAsync(runtime, session);
        if (symbolRead is null)
        {
            return false;
        }

        await RunDirectIntWriteReadbackAsync(runtime, symbolRead.Value.CurrentValue);

        using var process = Process.GetProcessById(session.Process.ProcessId);
        var mainModule = process.MainModule!;
        var baseAddress = mainModule.BaseAddress;
        var moduleSize = mainModule.ModuleMemorySize;
        using var accessor = new ProcessMemoryAccessor(process.Id);
        var moduleBytes = accessor.ReadBytes(baseAddress, moduleSize);
        _output.WriteLine($"Module: base=0x{baseAddress.ToInt64():X}  size=0x{moduleSize:X}");
        var creditsRva = symbolRead.Value.Symbol.Address.ToInt64() - baseAddress.ToInt64();
        _output.WriteLine($"Credits int RVA: 0x{creditsRva:X}");
        var bestCandidate = FindBestCreditsSyncInstruction(moduleBytes, baseAddress, creditsRva);
        await RunNopPatchExperimentAsync(runtime, accessor, bestCandidate);
        await RunFreezeComparisonAsync(runtime);
        DumpRipRelativeStoreHits(moduleBytes, baseAddress, creditsRva);
        return true;
    }

    private async Task<(SymbolInfo Symbol, int CurrentValue)?> TryResolveCreditsSymbolAsync(
        RuntimeAdapter runtime,
        AttachSession session)
    {
        try
        {
            if (!session.Symbols.TryGetValue("credits", out var symbol) || symbol is null)
            {
                _output.WriteLine("FAIL — 'credits' symbol not resolved. Symbols available: " +
                    string.Join(", ", session.Symbols.Symbols.Keys));
                return null;
            }

            var originalCredits = await runtime.ReadAsync<int>("credits");
            _output.WriteLine($"Credits symbol: addr=0x{symbol.Address.ToInt64():X}  source={symbol.Source}  currentValue={originalCredits}");
            return (symbol, originalCredits);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"FAIL — Cannot read credits: {ex.Message}");
            return null;
        }
    }

    private async Task RunDirectIntWriteReadbackAsync(RuntimeAdapter runtime, int originalCredits)
    {
        var testValue = originalCredits + 12345;
        _output.WriteLine("\n=== TEST 1: Direct int write ===");
        _output.WriteLine($"Writing {testValue} to credits int address...");
        await runtime.WriteAsync("credits", testValue);
        var immediateRead = await runtime.ReadAsync<int>("credits");
        _output.WriteLine($"Immediate readback: {immediateRead}  (expected {testValue}, match={immediateRead == testValue})");

        await Task.Delay(200);
        var delayedRead = await runtime.ReadAsync<int>("credits");
        _output.WriteLine($"After 200ms: {delayedRead}  (expected {testValue}, match={delayedRead == testValue})");
        _output.WriteLine($"Raw int write persists after 200ms: {delayedRead == testValue}");
        await runtime.WriteAsync("credits", originalCredits);
    }

    private CreditsSyncInstructionCandidate? FindBestCreditsSyncInstruction(
        byte[] moduleBytes,
        nint baseAddress,
        long creditsRva)
    {
        _output.WriteLine("\n=== SCANNING for credits float→int sync instruction ===");
        CreditsSyncInstructionCandidate? bestCandidate = null;

        foreach (var (name, aob) in CreditsSyncPatterns)
        {
            var pattern = AobPattern.Parse(aob);
            var allHits = FindAllPatternOffsets(moduleBytes, pattern, maxHits: 50);
            _output.WriteLine($"  Pattern '{name}' ({aob}): {allHits.Count} hit(s)");
            TryCaptureBestCandidate(name, allHits, moduleBytes, baseAddress, creditsRva, ref bestCandidate);
        }

        return bestCandidate;
    }

    private void TryCaptureBestCandidate(
        string patternName,
        IReadOnlyList<int> allHits,
        byte[] moduleBytes,
        nint baseAddress,
        long creditsRva,
        ref CreditsSyncInstructionCandidate? bestCandidate)
    {
        foreach (var hitOffset in allHits)
        {
            var hitAddress = baseAddress + hitOffset;
            var contextBytes = ReadContextBytes(moduleBytes, hitOffset, count: 20);
            var contextHex = BitConverter.ToString(contextBytes).Replace("-", " ");
            var storeInfo = FindNearbyRipRelativeStore(moduleBytes, hitOffset, 20, creditsRva);
            _output.WriteLine($"    hit RVA=0x{hitOffset:X}  addr=0x{hitAddress.ToInt64():X}  bytes={contextHex}  storeToCredits={storeInfo}");

            if (bestCandidate is not null || !storeInfo.StartsWith("YES") || patternName == "cvttss2si_any_any")
            {
                continue;
            }

            bestCandidate = new CreditsSyncInstructionCandidate(hitAddress, contextBytes[..5], patternName);
            _output.WriteLine("    *** IDENTIFIED as credits sync instruction! ***");
        }
    }

    private static byte[] ReadContextBytes(byte[] moduleBytes, int offset, int count)
    {
        var end = Math.Min(offset + count, moduleBytes.Length);
        return moduleBytes.AsSpan(offset, end - offset).ToArray();
    }

    private async Task RunNopPatchExperimentAsync(
        RuntimeAdapter runtime,
        ProcessMemoryAccessor accessor,
        CreditsSyncInstructionCandidate? bestCandidate)
    {
        if (bestCandidate is null)
        {
            _output.WriteLine("\n=== No credits sync instruction identified via store correlation. ===");
            return;
        }

        _output.WriteLine("\n=== TEST 2: NOP-patch credits sync ===");
        _output.WriteLine($"Patching 5 bytes at 0x{bestCandidate.Address.ToInt64():X} (pattern: {bestCandidate.PatternName})");

        var currentBeforeNop = await runtime.ReadAsync<int>("credits");
        _output.WriteLine($"Credits before NOP: {currentBeforeNop}");
        accessor.WriteBytes(bestCandidate.Address, new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 }, executablePatch: true);

        var testValue = currentBeforeNop + 99999;
        await runtime.WriteAsync("credits", testValue);
        var afterNopImmediate = await runtime.ReadAsync<int>("credits");
        _output.WriteLine($"After NOP + write, immediate readback: {afterNopImmediate} (expected {testValue}, match={afterNopImmediate == testValue})");

        await Task.Delay(500);
        var afterNopDelayed = await runtime.ReadAsync<int>("credits");
        _output.WriteLine($"After NOP + write, 500ms later: {afterNopDelayed} (expected {testValue}, match={afterNopDelayed == testValue})");

        await Task.Delay(2000);
        var afterNop2s = await runtime.ReadAsync<int>("credits");
        _output.WriteLine($"After NOP + write, 2500ms later: {afterNop2s} (expected {testValue}, match={afterNop2s == testValue})");

        var nopPatchWorks = afterNop2s == testValue;
        _output.WriteLine($"NOP patch makes credits persist: {nopPatchWorks}");
        accessor.WriteBytes(bestCandidate.Address, bestCandidate.OriginalBytes, executablePatch: true);
        _output.WriteLine("Restored original bytes.");
        await runtime.WriteAsync("credits", currentBeforeNop);

        if (!nopPatchWorks)
        {
            return;
        }

        _output.WriteLine("\n=== RESULT: NOP PATCH WORKS ===");
        _output.WriteLine($"Address: 0x{bestCandidate.Address.ToInt64():X}");
        _output.WriteLine($"Pattern: {bestCandidate.PatternName}");
        _output.WriteLine($"Original bytes: {BitConverter.ToString(bestCandidate.OriginalBytes)}");
        _output.WriteLine("This should be added as a CodePatch action in the profile.");
    }

    private async Task RunFreezeComparisonAsync(RuntimeAdapter runtime)
    {
        _output.WriteLine("\n=== TEST 3a: Regular freeze (50ms) ===");
        var regularResult = await RunFreezeSampleAsync(
            runtime,
            freezeFactory: () => new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance, pulseIntervalMs: 50),
            startFreeze: (freeze, target) => freeze.FreezeInt("credits", target),
            targetDelta: 77777,
            sampleLabel: "Froze credits",
            resultLabel: "Regular freeze");

        _output.WriteLine("\n=== TEST 3b: Aggressive freeze (~1ms) ===");
        var aggressiveResult = await RunFreezeSampleAsync(
            runtime,
            freezeFactory: () => new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance),
            startFreeze: (freeze, target) => freeze.FreezeIntAggressive("credits", target),
            targetDelta: 88888,
            sampleLabel: "Aggressive-froze credits",
            resultLabel: "Aggressive freeze");

        _output.WriteLine("\n=== FREEZE COMPARISON ===");
        _output.WriteLine($"Regular 50ms: {regularResult.MatchRate:F1}% match rate");
        _output.WriteLine($"Aggressive ~1ms: {aggressiveResult.MatchRate:F1}% match rate");
    }

    private async Task<(double MatchRate, IReadOnlyList<int> Samples)> RunFreezeSampleAsync(
        RuntimeAdapter runtime,
        Func<ValueFreezeService> freezeFactory,
        Action<ValueFreezeService, int> startFreeze,
        int targetDelta,
        string sampleLabel,
        string resultLabel)
    {
        using var freezeService = freezeFactory();
        var currentValue = await runtime.ReadAsync<int>("credits");
        var freezeTarget = currentValue + targetDelta;
        startFreeze(freezeService, freezeTarget);
        _output.WriteLine($"{sampleLabel} to {freezeTarget}. Sampling over 2 seconds...");

        var samples = await SampleCreditsAsync(runtime, sampleCount: 40, delayMs: 50);
        freezeService.Unfreeze("credits");

        var matchCount = samples.Count(value => value == freezeTarget);
        var matchRate = 100.0 * matchCount / samples.Count;
        _output.WriteLine($"{resultLabel} samples: {matchCount}/{samples.Count} matched target ({matchRate:F1}%)");
        _output.WriteLine($"Sample values: {string.Join(", ", samples.Take(20))}");
        await runtime.WriteAsync("credits", currentValue);
        return (matchRate, samples);
    }

    private static async Task<List<int>> SampleCreditsAsync(RuntimeAdapter runtime, int sampleCount, int delayMs)
    {
        var samples = new List<int>(sampleCount);
        for (var i = 0; i < sampleCount; i++)
        {
            await Task.Delay(delayMs);
            samples.Add(await runtime.ReadAsync<int>("credits"));
        }

        return samples;
    }

    private void DumpRipRelativeStoreHits(byte[] moduleBytes, nint baseAddress, long creditsRva)
    {
        _output.WriteLine($"\n=== Scanning for ALL stores to credits int address (RVA 0x{creditsRva:X}) ===");
        var storeHits = FindAllRipRelativeStoresToTarget(moduleBytes, creditsRva);
        _output.WriteLine($"Found {storeHits.Count} store instruction(s) targeting credits int:");
        foreach (var (rva, instrLen, hex) in storeHits)
        {
            var instrAddr = baseAddress + rva;
            var contextHex = ReadStoreContextHex(moduleBytes, rva, instrLen);
            _output.WriteLine($"  storeRVA=0x{rva:X}  addr=0x{instrAddr.ToInt64():X}  len={instrLen}  hex={hex}  context={contextHex}");
        }
    }

    private static string ReadStoreContextHex(byte[] moduleBytes, int rva, int instrLen)
    {
        var contextStart = Math.Max(0, rva - 10);
        var contextEnd = Math.Min(moduleBytes.Length, rva + instrLen + 10);
        return BitConverter.ToString(moduleBytes.AsSpan(contextStart, contextEnd - contextStart).ToArray()).Replace("-", " ");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<int> FindAllPatternOffsets(byte[] memory, AobPattern pattern, int maxHits)
    {
        var results = new List<int>();
        var sig = pattern.Bytes;
        if (sig.Length == 0)
        {
            return results;
        }

        var maxIndex = memory.Length - sig.Length;
        for (var i = 0; i <= maxIndex; i++)
        {
            var matched = true;
            for (var j = 0; j < sig.Length; j++)
            {
                if (sig[j] is byte expected && memory[i + j] != expected)
                {
                    matched = false;
                    break;
                }
            }
            if (matched)
            {
                results.Add(i);
                if (results.Count >= maxHits)
                {
                    break;
                }
            }
        }
        return results;
    }

    /// <summary>
    /// Starting from hitOffset, look within nextN bytes for a RIP-relative store
    /// (89 ModRM with ModRM &amp; 0xC7 == 0x05 or 0x0D) whose target RVA matches creditsRva.
    /// </summary>
    private static string FindNearbyRipRelativeStore(byte[] module, int hitOffset, int nextN, long creditsRva)
    {
        var maxOffset = Math.Min(hitOffset + nextN, module.Length);
        for (var offset = hitOffset; offset < maxOffset; offset++)
        {
            if (TryDescribePlainRipRelativeStore(module, offset, creditsRva, out var matchDescription))
            {
                return matchDescription;
            }

            if (TryDescribeRexRipRelativeStore(module, offset, creditsRva, out matchDescription))
            {
                return matchDescription;
            }
        }

        return "no";
    }

    private static bool TryDescribePlainRipRelativeStore(
        byte[] module,
        int offset,
        long targetRva,
        out string description)
    {
        description = string.Empty;
        if (offset + 6 >= module.Length || module[offset] != 0x89)
        {
            return false;
        }

        var modrm = module[offset + 1];
        if ((modrm & 0xC7) != 0x05)
        {
            return false;
        }

        var disp = BitConverter.ToInt32(module, offset + 2);
        var resolvedTarget = (long)(offset + 6) + disp;
        if (resolvedTarget != targetRva)
        {
            return false;
        }

        description = $"YES at insnRVA=0x{offset:X} (89 {module[offset + 1]:X2} disp=0x{disp:X})";
        return true;
    }

    private static bool TryDescribeRexRipRelativeStore(
        byte[] module,
        int offset,
        long targetRva,
        out string description)
    {
        description = string.Empty;
        if (offset + 7 >= module.Length || module[offset] is < 0x40 or > 0x4F || module[offset + 1] != 0x89)
        {
            return false;
        }

        var modrm = module[offset + 2];
        if ((modrm & 0xC7) != 0x05)
        {
            return false;
        }

        var disp = BitConverter.ToInt32(module, offset + 3);
        var resolvedTarget = (long)(offset + 7) + disp;
        if (resolvedTarget != targetRva)
        {
            return false;
        }

        description = $"YES at insnRVA=0x{offset:X} ({module[offset]:X2} 89 {modrm:X2} disp=0x{disp:X})";
        return true;
    }

    /// <summary>
    /// Scan the entire module for RIP-relative store instructions (89 XX) that write to the given target RVA.
    /// </summary>
    private static List<(int Rva, int InstrLen, string Hex)> FindAllRipRelativeStoresToTarget(byte[] module, long targetRva)
    {
        var results = new List<(int, int, string)>();
        for (var offset = 0; offset < module.Length; offset++)
        {
            TryAddPlainRipRelativeStore(module, targetRva, offset, results);
            TryAddRexRipRelativeStore(module, targetRva, offset, results);
            TryAddImmediateRipRelativeStore(module, targetRva, offset, results);
        }

        return results;
    }

    private static void TryAddPlainRipRelativeStore(
        byte[] module,
        long targetRva,
        int offset,
        ICollection<(int Rva, int InstrLen, string Hex)> results)
    {
        if (offset + 6 >= module.Length || module[offset] != 0x89)
        {
            return;
        }

        var modrm = module[offset + 1];
        if ((modrm & 0xC7) != 0x05)
        {
            return;
        }

        var disp = BitConverter.ToInt32(module, offset + 2);
        if ((long)(offset + 6) + disp != targetRva)
        {
            return;
        }

        results.Add((offset, 6, ToHex(module, offset, 6)));
    }

    private static void TryAddRexRipRelativeStore(
        byte[] module,
        long targetRva,
        int offset,
        ICollection<(int Rva, int InstrLen, string Hex)> results)
    {
        if (offset + 7 >= module.Length || module[offset] is < 0x40 or > 0x4F || module[offset + 1] != 0x89)
        {
            return;
        }

        var modrm = module[offset + 2];
        if ((modrm & 0xC7) != 0x05)
        {
            return;
        }

        var disp = BitConverter.ToInt32(module, offset + 3);
        if ((long)(offset + 7) + disp != targetRva)
        {
            return;
        }

        results.Add((offset, 7, ToHex(module, offset, 7)));
    }

    private static void TryAddImmediateRipRelativeStore(
        byte[] module,
        long targetRva,
        int offset,
        ICollection<(int Rva, int InstrLen, string Hex)> results)
    {
        // mov [rip+disp32], imm32 (C7 05 disp32 imm32)
        if (offset + 10 >= module.Length || module[offset] != 0xC7)
        {
            return;
        }

        var modrm = module[offset + 1];
        if ((modrm & 0xC7) != 0x05)
        {
            return;
        }

        var disp = BitConverter.ToInt32(module, offset + 2);
        var resolvedTarget = (long)(offset + 10) + disp;
        if (resolvedTarget != targetRva)
        {
            return;
        }

        results.Add((offset, 10, ToHex(module, offset, 10)));
    }

    private static string ToHex(byte[] module, int offset, int length)
    {
        return BitConverter.ToString(module.AsSpan(offset, length).ToArray()).Replace("-", " ");
    }

    private static async Task<IReadOnlyList<TrainerProfile>> ResolveProfilesAsync(FileSystemProfileRepository profileRepository)
    {
        var ids = await profileRepository.ListAvailableProfilesAsync();
        var profiles = new List<TrainerProfile>(ids.Count);
        foreach (var id in ids)
        {
            profiles.Add(await profileRepository.ResolveInheritedProfileAsync(id));
        }

        return profiles;
    }
}
