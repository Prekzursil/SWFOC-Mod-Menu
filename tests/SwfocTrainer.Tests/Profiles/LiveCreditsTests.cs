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
        running.Should().NotBeNull();

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

        // ── 2. RESOLVE CREDITS SYMBOL ────────────────────────────────────────
        int originalCredits;
        SymbolInfo creditsSymbol;
        try
        {
            if (!session.Symbols.TryGetValue("credits", out var sym) || sym is null)
            {
                _output.WriteLine("FAIL — 'credits' symbol not resolved. Symbols available: " +
                    string.Join(", ", session.Symbols.Symbols.Keys));
                await runtime.DetachAsync();
                return;
            }
            creditsSymbol = sym;
            originalCredits = await runtime.ReadAsync<int>("credits");
            _output.WriteLine($"Credits symbol: addr=0x{creditsSymbol.Address.ToInt64():X}  source={creditsSymbol.Source}  currentValue={originalCredits}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"FAIL — Cannot read credits: {ex.Message}");
            await runtime.DetachAsync();
            return;
        }

        // ── 3. TEST: RAW INT WRITE + IMMEDIATE READBACK ─────────────────────
        var testValue = originalCredits + 12345;
        _output.WriteLine($"\n=== TEST 1: Direct int write ===");
        _output.WriteLine($"Writing {testValue} to credits int address...");
        await runtime.WriteAsync("credits", testValue);
        var immediateRead = await runtime.ReadAsync<int>("credits");
        _output.WriteLine($"Immediate readback: {immediateRead}  (expected {testValue}, match={immediateRead == testValue})");

        await Task.Delay(200);
        var delayedRead = await runtime.ReadAsync<int>("credits");
        _output.WriteLine($"After 200ms: {delayedRead}  (expected {testValue}, match={delayedRead == testValue})");

        var rawWritePersists = delayedRead == testValue;
        _output.WriteLine($"Raw int write persists after 200ms: {rawWritePersists}");

        // Restore
        await runtime.WriteAsync("credits", originalCredits);

        // ── 4. SCAN FOR cvttss2si INSTRUCTIONS ──────────────────────────────
        _output.WriteLine($"\n=== SCANNING for credits float→int sync instruction ===");

        using var proc = Process.GetProcessById(session.Process.ProcessId);
        var mainModule = proc.MainModule!;
        var baseAddr = mainModule.BaseAddress;
        var moduleSize = mainModule.ModuleMemorySize;

        using var accessor = new ProcessMemoryAccessor(proc.Id);
        var moduleBytes = accessor.ReadBytes(baseAddr, moduleSize);

        _output.WriteLine($"Module: base=0x{baseAddr.ToInt64():X}  size=0x{moduleSize:X}");

        // Pattern variants for cvttss2si:
        //   F3 0F 2C D0+r R0+r  →  cvttss2si reg32, [reg+imm8]
        //   Specifically: F3 0F 2C 50 70 = cvttss2si edx, [rax+0x70]
        // But register encoding or offset might differ. Scan with wildcards.
        var patterns = new (string Name, string Aob)[]
        {
            ("exact_hook",      "F3 0F 2C 50 70 89 57"),     // original hook pattern
            ("cvttss2si_edx_rax_70", "F3 0F 2C 50 70"),      // cvttss2si edx,[rax+0x70]
            ("cvttss2si_ecx_rax_70", "F3 0F 2C 48 70"),      // cvttss2si ecx,[rax+0x70]
            ("cvttss2si_eax_rax_70", "F3 0F 2C 40 70"),      // cvttss2si eax,[rax+0x70]
            ("cvttss2si_edx_rcx_70", "F3 0F 2C 51 70"),      // cvttss2si edx,[rcx+0x70]
            ("cvttss2si_edx_rdx_70", "F3 0F 2C 52 70"),      // cvttss2si edx,[rdx+0x70]
            ("cvttss2si_edx_rbx_70", "F3 0F 2C 53 70"),      // cvttss2si edx,[rbx+0x70]
            ("cvttss2si_edx_rax_78", "F3 0F 2C 50 78"),      // cvttss2si edx,[rax+0x78]
            ("cvttss2si_edx_rax_68", "F3 0F 2C 50 68"),      // cvttss2si edx,[rax+0x68]
            ("cvttss2si_any_70",     "F3 0F 2C ?? 70"),       // cvttss2si ???,[???+0x70]
            ("cvttss2si_any_any",    "F3 0F 2C"),             // any cvttss2si (broad)
        };

        // Collect the credits int RVA so we can correlate store instructions
        var creditsRva = creditsSymbol.Address.ToInt64() - baseAddr.ToInt64();
        _output.WriteLine($"Credits int RVA: 0x{creditsRva:X}");

        var bestHitAddress = nint.Zero;
        byte[]? bestOriginalBytes = null;
        var bestPatternName = "";

        foreach (var (name, aob) in patterns)
        {
            var pattern = AobPattern.Parse(aob);
            var allHits = FindAllPatternOffsets(moduleBytes, pattern, maxHits: 50);
            _output.WriteLine($"  Pattern '{name}' ({aob}): {allHits.Count} hit(s)");

            foreach (var hitOffset in allHits)
            {
                var hitAddr = baseAddr + hitOffset;
                // Show context bytes: 16 bytes starting at hit
                var contextEnd = Math.Min(hitOffset + 20, moduleBytes.Length);
                var contextBytes = moduleBytes.AsSpan(hitOffset, contextEnd - hitOffset).ToArray();
                var contextHex = BitConverter.ToString(contextBytes).Replace("-", " ");

                // Check if a subsequent instruction stores to our credits int address via RIP-relative
                // Look for mov [rip+disp32], reg patterns (89 XX) within 20 bytes after the cvttss2si
                var storeInfo = FindNearbyRipRelativeStore(moduleBytes, hitOffset, 20, creditsRva, baseAddr.ToInt64());

                _output.WriteLine($"    hit RVA=0x{hitOffset:X}  addr=0x{hitAddr.ToInt64():X}  bytes={contextHex}  storeToCredits={storeInfo}");

                // If this cvttss2si is followed by a store to our credits int, it's THE instruction
                if (storeInfo.StartsWith("YES") && bestHitAddress == nint.Zero && name != "cvttss2si_any_any")
                {
                    bestHitAddress = hitAddr;
                    bestOriginalBytes = contextBytes[..5]; // save 5 bytes of cvttss2si
                    bestPatternName = name;
                    _output.WriteLine($"    *** IDENTIFIED as credits sync instruction! ***");
                }
            }
        }

        // ── 5. TEST NOP PATCH IF FOUND ──────────────────────────────────────
        if (bestHitAddress != nint.Zero && bestOriginalBytes is not null)
        {
            _output.WriteLine($"\n=== TEST 2: NOP-patch credits sync ===");
            _output.WriteLine($"Patching 5 bytes at 0x{bestHitAddress.ToInt64():X} (pattern: {bestPatternName})");

            var currentBeforeNop = await runtime.ReadAsync<int>("credits");
            _output.WriteLine($"Credits before NOP: {currentBeforeNop}");

            // NOP the cvttss2si (5 bytes)
            var nopBytes = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 };
            accessor.WriteBytes(bestHitAddress, nopBytes, executablePatch: true);

            // Now write our test value
            testValue = currentBeforeNop + 99999;
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

            // Restore original bytes
            accessor.WriteBytes(bestHitAddress, bestOriginalBytes, executablePatch: true);
            _output.WriteLine("Restored original bytes.");

            // Restore credits
            await runtime.WriteAsync("credits", currentBeforeNop);

            if (nopPatchWorks)
            {
                _output.WriteLine($"\n=== RESULT: NOP PATCH WORKS ===");
                _output.WriteLine($"Address: 0x{bestHitAddress.ToInt64():X}");
                _output.WriteLine($"Pattern: {bestPatternName}");
                _output.WriteLine($"Original bytes: {BitConverter.ToString(bestOriginalBytes)}");
                _output.WriteLine($"This should be added as a CodePatch action in the profile.");
            }
        }
        else
        {
            _output.WriteLine("\n=== No credits sync instruction identified via store correlation. ===");
        }

        // ── TEST 3: Aggressive freeze (~1 ms) vs regular freeze (50 ms) ────
        {
            _output.WriteLine($"\n=== TEST 3a: Regular freeze (50ms) ===");
            var regularFreeze = new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance, pulseIntervalMs: 50);
            var currentVal = await runtime.ReadAsync<int>("credits");
            var freezeTarget = currentVal + 77777;

            regularFreeze.FreezeInt("credits", freezeTarget);
            _output.WriteLine($"Froze credits to {freezeTarget}. Sampling over 2 seconds...");

            var samples = new List<int>();
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(50);
                samples.Add(await runtime.ReadAsync<int>("credits"));
            }

            regularFreeze.Unfreeze("credits");
            regularFreeze.Dispose();

            var matchCount = samples.Count(s => s == freezeTarget);
            _output.WriteLine($"Regular freeze samples: {matchCount}/{samples.Count} matched target ({100.0 * matchCount / samples.Count:F1}%)");
            _output.WriteLine($"Sample values: {string.Join(", ", samples.Take(20))}");

            // Restore
            await runtime.WriteAsync("credits", currentVal);

            _output.WriteLine($"\n=== TEST 3b: Aggressive freeze (~1ms) ===");
            var aggressiveFreeze = new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance);
            currentVal = await runtime.ReadAsync<int>("credits");
            var aggressiveTarget = currentVal + 88888;

            aggressiveFreeze.FreezeIntAggressive("credits", aggressiveTarget);
            _output.WriteLine($"Aggressive-froze credits to {aggressiveTarget}. Sampling over 2 seconds...");

            var aggressiveSamples = new List<int>();
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(50);
                aggressiveSamples.Add(await runtime.ReadAsync<int>("credits"));
            }

            aggressiveFreeze.Unfreeze("credits");
            aggressiveFreeze.Dispose();

            var aggressiveMatch = aggressiveSamples.Count(s => s == aggressiveTarget);
            _output.WriteLine($"Aggressive freeze samples: {aggressiveMatch}/{aggressiveSamples.Count} matched target ({100.0 * aggressiveMatch / aggressiveSamples.Count:F1}%)");
            _output.WriteLine($"Sample values: {string.Join(", ", aggressiveSamples.Take(20))}");

            // Restore
            await runtime.WriteAsync("credits", currentVal);

            _output.WriteLine($"\n=== FREEZE COMPARISON ===");
            _output.WriteLine($"Regular 50ms: {100.0 * matchCount / samples.Count:F1}% match rate");
            _output.WriteLine($"Aggressive ~1ms: {100.0 * aggressiveMatch / aggressiveSamples.Count:F1}% match rate");
        }

        // ── 6. ALSO SCAN: instructions that store to credits int via RIP-relative ──
        _output.WriteLine($"\n=== Scanning for ALL stores to credits int address (RVA 0x{creditsRva:X}) ===");
        var storeHits = FindAllRipRelativeStoresToTarget(moduleBytes, creditsRva, baseAddr.ToInt64());
        _output.WriteLine($"Found {storeHits.Count} store instruction(s) targeting credits int:");
        foreach (var (rva, instrLen, hex) in storeHits)
        {
            var instrAddr = baseAddr + rva;
            // Read surrounding context
            var ctxStart = Math.Max(0, rva - 10);
            var ctxEnd = Math.Min(moduleBytes.Length, rva + instrLen + 10);
            var contextHex = BitConverter.ToString(moduleBytes.AsSpan(ctxStart, ctxEnd - ctxStart).ToArray()).Replace("-", " ");
            _output.WriteLine($"  storeRVA=0x{rva:X}  addr=0x{instrAddr.ToInt64():X}  len={instrLen}  hex={hex}  context={contextHex}");
        }

        await runtime.DetachAsync();
        _output.WriteLine("\nDone. Detached.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<int> FindAllPatternOffsets(byte[] memory, AobPattern pattern, int maxHits)
    {
        var results = new List<int>();
        var sig = pattern.Bytes;
        if (sig.Length == 0) return results;

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
                if (results.Count >= maxHits) break;
            }
        }
        return results;
    }

    /// <summary>
    /// Starting from hitOffset, look within nextN bytes for a RIP-relative store
    /// (89 ModRM with ModRM &amp; 0xC7 == 0x05 or 0x0D) whose target RVA matches creditsRva.
    /// </summary>
    private static string FindNearbyRipRelativeStore(byte[] module, int hitOffset, int nextN, long creditsRva, long moduleBase)
    {
        for (var i = hitOffset; i < hitOffset + nextN && i + 6 < module.Length; i++)
        {
            // Check for 89 XX patterns (mov [rip+disp32], reg32)
            if (module[i] == 0x89)
            {
                var modrm = module[i + 1];
                // ModRM with mod=00, rm=101 (RIP-relative) → (modrm & 0xC7) == 0x05
                if ((modrm & 0xC7) == 0x05)
                {
                    var disp = BitConverter.ToInt32(module, i + 2);
                    var nextInsnRva = (long)(i + 6);
                    var targetRva = nextInsnRva + disp;
                    if (targetRva == creditsRva)
                    {
                        return $"YES at insnRVA=0x{i:X} (89 {module[i + 1]:X2} disp=0x{disp:X})";
                    }
                }
            }

            // Also check for REX prefix + 89 (e.g., 48 89 ...)
            if (module[i] is >= 0x40 and <= 0x4F && i + 1 < module.Length && module[i + 1] == 0x89)
            {
                var modrm = module[i + 2];
                if ((modrm & 0xC7) == 0x05 && i + 7 < module.Length)
                {
                    var disp = BitConverter.ToInt32(module, i + 3);
                    var nextInsnRva = (long)(i + 7);
                    var targetRva = nextInsnRva + disp;
                    if (targetRva == creditsRva)
                    {
                        return $"YES at insnRVA=0x{i:X} ({module[i]:X2} 89 {modrm:X2} disp=0x{disp:X})";
                    }
                }
            }
        }

        return "no";
    }

    /// <summary>
    /// Scan the entire module for RIP-relative store instructions (89 XX) that write to the given target RVA.
    /// </summary>
    private static List<(int Rva, int InstrLen, string Hex)> FindAllRipRelativeStoresToTarget(
        byte[] module, long targetRva, long moduleBase)
    {
        var results = new List<(int, int, string)>();

        for (var i = 0; i + 6 < module.Length; i++)
        {
            // Plain: 89 ModRM disp32
            if (module[i] == 0x89)
            {
                var modrm = module[i + 1];
                if ((modrm & 0xC7) == 0x05)
                {
                    var disp = BitConverter.ToInt32(module, i + 2);
                    var nextRva = (long)(i + 6);
                    if (nextRva + disp == targetRva)
                    {
                        var len = 6;
                        var hex = BitConverter.ToString(module.AsSpan(i, len).ToArray()).Replace("-", " ");
                        results.Add((i, len, hex));
                    }
                }
            }

            // REX + 89 ModRM disp32
            if (module[i] is >= 0x40 and <= 0x4F && i + 7 < module.Length && module[i + 1] == 0x89)
            {
                var modrm = module[i + 2];
                if ((modrm & 0xC7) == 0x05)
                {
                    var disp = BitConverter.ToInt32(module, i + 3);
                    var nextRva = (long)(i + 7);
                    if (nextRva + disp == targetRva)
                    {
                        var len = 7;
                        var hex = BitConverter.ToString(module.AsSpan(i, len).ToArray()).Replace("-", " ");
                        results.Add((i, len, hex));
                    }
                }
            }

            // Also check mov [rip+disp32], imm32 (C7 05 disp32 imm32)
            if (module[i] == 0xC7 && i + 10 < module.Length)
            {
                var modrm = module[i + 1];
                if ((modrm & 0xC7) == 0x05)
                {
                    var disp = BitConverter.ToInt32(module, i + 2);
                    var nextRva = (long)(i + 6); // + 4 imm, but disp is relative to end of modrm+disp, not imm
                    // Actually for C7 05: instr is C7 05 disp32 imm32 = 10 bytes, nextPC = i + 10
                    // But disp is relative to end of disp32 field = i + 6
                    // Wait, x64: C7 /0 disp32 imm32, modrm=05 means [rip+disp32]
                    // The RIP value used = address of next instruction = i + 10 (from base)
                    // So target = (i + 10) + disp... no.
                    // For RIP-relative: effective addr = RIP + disp, where RIP = address of NEXT instruction
                    // For C7 05 disp32 imm32: next instruction is at i + 10, so target = (i + 10) + disp
                    var actualTarget = (long)(i + 10) + disp;
                    if (actualTarget == targetRva)
                    {
                        var len = 10;
                        var hex = BitConverter.ToString(module.AsSpan(i, len).ToArray()).Replace("-", " ");
                        results.Add((i, len, hex));
                    }
                }
            }
        }

        return results;
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
