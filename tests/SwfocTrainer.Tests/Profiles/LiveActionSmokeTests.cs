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

// #lizard forgive global
public sealed class LiveActionSmokeTests
{
    private readonly ITestOutputHelper _output;

    private sealed record ModuleSnapshot(nint BaseAddress, int ModuleSize, byte[] Bytes);
    private sealed record LiveSmokeReadState(int? Credits, int? HeroRespawn, float? InstantBuild, float? SelectedHp, int? PlanetOwner, byte? Fog, byte? TimerFreeze, byte? TacticalGod, byte? TacticalOneHit, IReadOnlyList<string> ReadFailures);

    public LiveActionSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task LiveSmoke_Attach_Read_And_OptionalToggleRevert_Should_Succeed()
    {
        var locator = new ProcessLocator();
        var running = await locator.FindBestMatchAsync(ExeTarget.Swfoc);
        if (running is null)
        {
            return;
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
            $"Selected profile for live smoke: {profileId} (reason={context.Recommendation.ReasonCode}, confidence={context.Recommendation.Confidence:0.00})");
        var session = await runtime.AttachAsync(profileId);
        session.Process.ProcessId.Should().Be(running.ProcessId);
        session.Symbols.Symbols.Count.Should().BeGreaterThan(0);

        LogResolvedSymbols(session);
        var readState = await ReadSmokeSymbolsAsync(runtime);

        DumpConditionalDebugScans(session, readState);
        LogReadFailures(readState.ReadFailures);
        await RunOptionalToggleRevertChecksAsync(runtime, readState);
        await AssertOptionalToggleValuesAsync(runtime, readState);

        await runtime.DetachAsync();
        runtime.IsAttached.Should().BeFalse();
    }

    private void LogResolvedSymbols(AttachSession session)
    {
        _output.WriteLine($"Resolved symbols: {session.Symbols.Symbols.Count}");
        foreach (var symbolName in session.Symbols.Symbols.Keys.OrderBy(x => x))
        {
            if (session.Symbols.Symbols.TryGetValue(symbolName, out var symbol))
            {
                _output.WriteLine(
                    $"{symbolName}: 0x{symbol.Address.ToInt64():X} source={symbol.Source} diag={symbol.Diagnostics}");
            }
        }
    }

    private async Task<LiveSmokeReadState> ReadSmokeSymbolsAsync(RuntimeAdapter runtime)
    {
        var readFailures = new List<string>();
        var credits = await TryReadSymbolAsync<int>(runtime, "credits", "Credits", readFailures);
        var heroRespawn = await TryReadSymbolAsync<int>(runtime, "hero_respawn_timer", "hero_respawn_timer", readFailures);
        var instantBuild = await TryReadSymbolAsync<float>(runtime, "instant_build", "instant_build", readFailures);
        var selectedHp = await TryReadSymbolAsync<float>(runtime, "selected_hp", "selected_hp", readFailures);
        var planetOwner = await TryReadSymbolAsync<int>(runtime, "planet_owner", "planet_owner", readFailures);
        var fog = await TryReadSymbolAsync<byte>(runtime, "fog_reveal", "fog_reveal", readFailures);
        var timerFreeze = await TryReadSymbolAsync<byte>(runtime, "game_timer_freeze", "game_timer_freeze", readFailures);
        var tacticalGod = await TryReadSymbolAsync<byte>(runtime, "tactical_god_mode", "tactical_god_mode", readFailures);
        var tacticalOneHit = await TryReadSymbolAsync<byte>(runtime, "tactical_one_hit_mode", "tactical_one_hit_mode", readFailures);

        return new LiveSmokeReadState(
            Credits: credits,
            HeroRespawn: heroRespawn,
            InstantBuild: instantBuild,
            SelectedHp: selectedHp,
            PlanetOwner: planetOwner,
            Fog: fog,
            TimerFreeze: timerFreeze,
            TacticalGod: tacticalGod,
            TacticalOneHit: tacticalOneHit,
            ReadFailures: readFailures);
    }

    private async Task<T?> TryReadSymbolAsync<T>(
        RuntimeAdapter runtime,
        string symbolName,
        string successLabel,
        List<string> readFailures)
        where T : unmanaged
    {
        try
        {
            var value = await runtime.ReadAsync<T>(symbolName);
            _output.WriteLine($"{successLabel} read succeeded: {value}");
            return value;
        }
        catch (Exception ex)
        {
            readFailures.Add($"{symbolName}: {ex.Message}");
            return null;
        }
    }

    private void DumpConditionalDebugScans(AttachSession session, LiveSmokeReadState readState)
    {
        if (readState.Fog.HasValue && readState.TimerFreeze is null)
        {
            try
            {
                DumpNearbyRipRelativeByteTargets(session, "fog_reveal", window: 0x40);
                DumpNearbyRipRelativeByteTargets(session, "ai_enabled", window: 0x80);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Debug scan failed: {ex.Message}");
            }
        }

        if (readState.Credits is null)
        {
            try
            {
                DumpTopRipRelativeInt32Targets(session, top: 20);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Credits debug scan failed: {ex.Message}");
            }
        }

        DumpCalibrationDebugScan(session);
    }

    private void DumpCalibrationDebugScan(AttachSession session)
    {
        // Calibration helpers: if these are fallback-only, dump RIP-relative references to their current RVA
        // so we can craft stable AOB patterns for signatures.
        try
        {
            var snapshot = ReadMainModuleSnapshot(session);
            _output.WriteLine($"Main module snapshot: base=0x{snapshot.BaseAddress.ToInt64():X} size=0x{snapshot.ModuleSize:X}");
            DumpRipRelativeReferencesForFallbackOnly(session, snapshot, new[]
            {
                ("instant_build", SymbolValueType.Float),
                ("selected_hp", SymbolValueType.Float),
                ("planet_owner", SymbolValueType.Int32),
            });

            // If fallback offsets are stale (common on x64), dump candidate signatures based on code patterns instead.
            DumpInstantBuildCandidates(session, snapshot, maxTargets: 12);
            DumpSelectedHpCandidates(session, snapshot, maxHits: 12);
            DumpPlanetOwnerCandidates(session, snapshot, maxHits: 12);

            // If specific pattern scans come up empty, fall back to "top RIP-relative targets" to locate globals.
            DumpTopRipRelativeFloatTargets(snapshot, top: 20);
            DumpTopRipRelativeFloatArithmeticTargets(snapshot, top: 30);
            DumpTopRipRelativeInt32StoreTargets(snapshot, top: 20);
            DumpTopRipRelativeInt32Targets32BitOnly(snapshot, top: 40);
            DumpTopRipRelativeByteCompareTargets(snapshot, top: 40);
            DumpTopRipRelativeByteImmediateStoreTargets(snapshot, top: 40);

            DumpPlanetOwnerExpectedAobSanityCheck(session, snapshot);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Symbol calibration debug scan failed: {ex.Message}");
        }
    }

    private void DumpPlanetOwnerExpectedAobSanityCheck(AttachSession session, ModuleSnapshot snapshot)
    {
        // Quick sanity check: ensure our expected planet_owner AOB actually exists in module bytes.
        try
        {
            using var process = Process.GetProcessById(session.Process.ProcessId);
            var expected = AobPattern.Parse("89 35 ?? ?? ?? ?? 48 C7 05 ?? ?? ?? ?? ?? ?? ?? ??");
            var hit = AobScanner.FindPattern(process, snapshot.Bytes, snapshot.BaseAddress, expected);
            var hitRva = hit == nint.Zero ? -1 : hit.ToInt64() - snapshot.BaseAddress.ToInt64();
            _output.WriteLine(hit == nint.Zero
                ? "Planet-owner expected AOB did NOT match anywhere in module."
                : $"Planet-owner expected AOB matched at RVA 0x{hitRva:X} (addr=0x{hit.ToInt64():X}).");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Planet-owner AOB sanity check failed: {ex.Message}");
        }
    }

    private void LogReadFailures(IReadOnlyList<string> readFailures)
    {
        if (readFailures.Count <= 0)
        {
            return;
        }

        _output.WriteLine("Read failures:");
        foreach (var failure in readFailures)
        {
            _output.WriteLine($" - {failure}");
        }
    }

    private async Task RunOptionalToggleRevertChecksAsync(RuntimeAdapter runtime, LiveSmokeReadState readState)
    {
        if (!HasReadableToggleTargets(readState))
        {
            _output.WriteLine("No target symbols were readable in this build. Likely requires signature/offset calibration.");
            return;
        }

        await RestoreKnownToggleValuesAsync(runtime, readState);
        var toggledAny = false;
        toggledAny |= await TryToggleByteSymbolAsync(runtime, "fog_reveal", readState.Fog);
        toggledAny |= await TryToggleByteSymbolAsync(runtime, "game_timer_freeze", readState.TimerFreeze);
        toggledAny |= await TryToggleByteSymbolAsync(runtime, "tactical_god_mode", readState.TacticalGod);
        toggledAny |= await TryToggleByteSymbolAsync(runtime, "tactical_one_hit_mode", readState.TacticalOneHit);

        if (!toggledAny)
        {
            _output.WriteLine("No toggle-safe symbols were readable in this build.");
        }
    }

    private static bool HasReadableToggleTargets(LiveSmokeReadState readState) =>
        readState.Credits is not null || readState.Fog is not null || readState.TimerFreeze is not null;

    private static async Task RestoreKnownToggleValuesAsync(RuntimeAdapter runtime, LiveSmokeReadState readState)
    {
        if (readState.Fog.HasValue)
        {
            await runtime.WriteAsync("fog_reveal", readState.Fog.Value);
        }

        if (readState.TimerFreeze.HasValue)
        {
            await runtime.WriteAsync("game_timer_freeze", readState.TimerFreeze.Value);
        }
    }

    private async Task<bool> TryToggleByteSymbolAsync(RuntimeAdapter runtime, string symbolName, byte? originalValue)
    {
        if (!originalValue.HasValue)
        {
            return false;
        }

        var toggledValue = (byte)(originalValue.Value == 0 ? 1 : 0);
        await runtime.WriteAsync(symbolName, toggledValue);
        await runtime.WriteAsync(symbolName, originalValue.Value);
        _output.WriteLine($"Toggle-revert check executed for {symbolName}.");
        return true;
    }

    private static async Task AssertOptionalToggleValuesAsync(RuntimeAdapter runtime, LiveSmokeReadState readState)
    {
        if (readState.Fog.HasValue)
        {
            var fogAfter = await runtime.ReadAsync<byte>("fog_reveal");
            fogAfter.Should().Be(readState.Fog.Value);
        }

        if (readState.TimerFreeze.HasValue)
        {
            var timerAfter = await runtime.ReadAsync<byte>("game_timer_freeze");
            timerAfter.Should().Be(readState.TimerFreeze.Value);
        }
    }

    private sealed class RipRelativeByteTargetSummary
    {
        public int Count { get; set; }

        public List<string> References { get; } = new();
    }

    private static bool IsRex(byte b) => b is >= 0x40 and <= 0x4F;
    private static bool IsRexNoW(byte b) => b is >= 0x40 and <= 0x47;
    private static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;

    private static bool IsFallbackSymbol(AttachSession session, string symbolName)
    {
        return session.Symbols.Symbols.TryGetValue(symbolName, out var symbol)
            && symbol.Source == AddressSource.Fallback;
    }

    private static bool TryResolveRipTarget(
        byte[] bytes,
        int insnRva,
        int dispIndex,
        int insnLen,
        int valueSize,
        out int targetRva)
    {
        targetRva = 0;
        if (dispIndex < 0 || dispIndex + 4 > bytes.Length)
        {
            return false;
        }

        var disp = BitConverter.ToInt32(bytes, dispIndex);
        targetRva = insnRva + insnLen + disp;
        return targetRva >= 0 && targetRva + valueSize <= bytes.Length;
    }

    private static bool IsMulssAt(byte[] bytes, int index)
    {
        if (index + 3 >= bytes.Length)
        {
            return false;
        }

        if (bytes[index] == 0xF3 && bytes[index + 1] == 0x0F && bytes[index + 2] == 0x59)
        {
            return true;
        }

        return bytes[index] == 0xF3
            && IsRex(bytes[index + 1])
            && bytes[index + 2] == 0x0F
            && bytes[index + 3] == 0x59;
    }

    private static void AddRipTargetHit(IDictionary<int, List<(int TargetRva, int InsnRva, int DispOffset, int InsnLen)>> hitsByTarget, (int TargetRva, int InsnRva, int DispOffset, int InsnLen) hit)
    {
        if (!hitsByTarget.TryGetValue(hit.TargetRva, out var hits))
        {
            hits = new List<(int TargetRva, int InsnRva, int DispOffset, int InsnLen)>();
            hitsByTarget[hit.TargetRva] = hits;
        }

        hits.Add(hit);
    }

    private void DumpNearbyRipRelativeByteTargets(AttachSession session, string anchorSymbol, int window)
    {
        if (!session.Symbols.Symbols.TryGetValue(anchorSymbol, out var anchor))
        {
            _output.WriteLine($"Anchor symbol '{anchorSymbol}' was not resolved; skipping debug scan.");
            return;
        }

        using var process = Process.GetProcessById(session.Process.ProcessId);
        var module = process.MainModule ?? throw new InvalidOperationException("Main module not available for target process.");
        var baseAddress = module.BaseAddress;
        var moduleSize = module.ModuleMemorySize;
        using var accessor = new ProcessMemoryAccessor(process.Id);
        var moduleBytes = accessor.ReadBytes(baseAddress, moduleSize);
        var anchorRva = anchor.Address.ToInt64() - baseAddress.ToInt64();

        _output.WriteLine($"Debug scan around '{anchorSymbol}': anchorRva=0x{anchorRva:X} window=0x{window:X} moduleSize=0x{moduleSize:X}");
        var targetSummaries = CollectNearbyRipRelativeByteTargets(moduleBytes, anchorRva, window);
        if (targetSummaries.Count == 0)
        {
            _output.WriteLine("No nearby RIP-relative byte targets found near anchor.");
            return;
        }

        _output.WriteLine("Nearby RIP-relative byte targets (decoded from 80 3D / C6 05):");
        foreach (var (targetRva, summary) in targetSummaries.OrderBy(x => x.Key))
        {
            var address = baseAddress + (nint)targetRva;
            var value = TryReadByte(accessor, address);
            _output.WriteLine($" - targetRva=0x{targetRva:X} (addr=0x{address.ToInt64():X}) refs={summary.Count} value={(value.HasValue ? value.Value.ToString() : "<unreadable>")}");
            foreach (var reference in summary.References)
            {
                _output.WriteLine($"   {reference}");
            }
        }
    }

    private static Dictionary<long, RipRelativeByteTargetSummary> CollectNearbyRipRelativeByteTargets(
        byte[] moduleBytes,
        long anchorRva,
        int window)
    {
        var summaries = new Dictionary<long, RipRelativeByteTargetSummary>();
        for (var index = 0; index + 7 < moduleBytes.Length; index++)
        {
            if (!TryDecodeRipRelativeByteTarget(moduleBytes, index, out var targetRva, out var kind))
            {
                continue;
            }

            if (Math.Abs(targetRva - anchorRva) > window)
            {
                continue;
            }

            RecordRipRelativeByteTarget(summaries, targetRva, index, kind, moduleBytes);
        }

        return summaries;
    }

    private static bool TryDecodeRipRelativeByteTarget(
        byte[] moduleBytes,
        int insnRva,
        out long targetRva,
        out string kind)
    {
        targetRva = 0;
        kind = string.Empty;
        var b0 = moduleBytes[insnRva];
        var b1 = moduleBytes[insnRva + 1];

        if (b0 == 0x80 && b1 == 0x3D)
        {
            targetRva = insnRva + 7 + BitConverter.ToInt32(moduleBytes, insnRva + 2);
            kind = "80 3D";
            return true;
        }

        if (b0 == 0xC6 && b1 == 0x05)
        {
            targetRva = insnRva + 7 + BitConverter.ToInt32(moduleBytes, insnRva + 2);
            kind = "C6 05";
            return true;
        }

        return false;
    }

    private static void RecordRipRelativeByteTarget(
        IDictionary<long, RipRelativeByteTargetSummary> summaries,
        long targetRva,
        int insnRva,
        string kind,
        byte[] moduleBytes)
    {
        if (!summaries.TryGetValue(targetRva, out var summary))
        {
            summary = new RipRelativeByteTargetSummary();
            summaries[targetRva] = summary;
        }

        summary.Count += 1;
        if (summary.References.Count >= 3)
        {
            return;
        }

        // include some nearby bytes to help craft a stable AOB signature
        var snippet = BytesToHex(moduleBytes, insnRva, 14);
        summary.References.Add($"insnRva=0x{insnRva:X} kind={kind} bytes={snippet}");
    }

    private static byte? TryReadByte(ProcessMemoryAccessor accessor, nint address)
    {
        try
        {
            return accessor.Read<byte>(address);
        }
        catch
        {
            return null;
        }
    }

    private ModuleSnapshot ReadMainModuleSnapshot(AttachSession session)
    {
        using var process = Process.GetProcessById(session.Process.ProcessId);
        var module = process.MainModule ?? throw new InvalidOperationException("Main module not available for target process.");
        var baseAddress = module.BaseAddress;
        var moduleSize = module.ModuleMemorySize;

        using var accessor = new ProcessMemoryAccessor(process.Id);
        var bytes = accessor.ReadBytes(baseAddress, moduleSize);
        return new ModuleSnapshot(baseAddress, moduleSize, bytes);
    }

    private void DumpRipRelativeReferencesForFallbackOnly(
        AttachSession session,
        ModuleSnapshot snapshot,
        IEnumerable<(string SymbolName, SymbolValueType ValueType)> candidates)
    {
        foreach (var (symbolName, valueType) in candidates)
        {
            if (!session.Symbols.Symbols.TryGetValue(symbolName, out var symbol))
            {
                continue;
            }

            if (symbol.Source != AddressSource.Fallback)
            {
                continue;
            }

            var rva = symbol.Address.ToInt64() - snapshot.BaseAddress.ToInt64();
            _output.WriteLine($"Calibration: '{symbolName}' is fallback-only at RVA 0x{rva:X} (addr=0x{symbol.Address.ToInt64():X}). Looking for RIP-relative refs...");

            if (valueType == SymbolValueType.Float)
            {
                DumpRipRelativeSseFloatRefs(snapshot, rva, max: 12);
            }
            else
            {
                DumpRipRelativeInt32Refs(snapshot, rva, max: 12);
            }
        }
    }

    private void DumpInstantBuildCandidates(AttachSession session, ModuleSnapshot snapshot, int maxTargets)
    {
        if (!IsFallbackSymbol(session, "instant_build"))
        {
            return;
        }

        var bytes = snapshot.Bytes;
        var hitsByTarget = CollectInstantBuildCandidates(bytes);
        if (hitsByTarget.Count == 0)
        {
            _output.WriteLine("Instant-build calibration: no movss[rip]+mulss candidates found.");
            return;
        }

        _output.WriteLine($"Instant-build calibration: {hitsByTarget.Count} candidate global float target(s) found (top {maxTargets}):");
        foreach (var entry in hitsByTarget.OrderByDescending(x => x.Value.Count).Take(maxTargets))
        {
            LogInstantBuildCandidate(bytes, entry.Key, entry.Value);
        }
    }

    private static Dictionary<int, List<(int TargetRva, int InsnRva, int DispOffset, int InsnLen)>> CollectInstantBuildCandidates(byte[] bytes)
    {
        var hitsByTarget = new Dictionary<int, List<(int TargetRva, int InsnRva, int DispOffset, int InsnLen)>>();
        for (var i = 0; i + 16 < bytes.Length; i++)
        {
            if (TryDecodeInstantBuildNoRex(bytes, i, out var hit)
                || TryDecodeInstantBuildRex(bytes, i, out hit))
            {
                AddRipTargetHit(hitsByTarget, hit);
            }
        }

        return hitsByTarget;
    }

    private static bool TryDecodeInstantBuildNoRex(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, int InsnLen) hit)
    {
        hit = default;
        if (bytes[index] != 0xF3 || bytes[index + 1] != 0x0F || bytes[index + 2] != 0x10)
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[index + 3])
            || !TryResolveRipTarget(bytes, index, index + 4, insnLen: 8, valueSize: 4, out var targetRva))
        {
            return false;
        }

        if (!IsMulssAt(bytes, index + 8))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 4, InsnLen: 8);
        return true;
    }

    private static bool TryDecodeInstantBuildRex(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, int InsnLen) hit)
    {
        hit = default;
        if (bytes[index] != 0xF3 || !IsRex(bytes[index + 1]) || bytes[index + 2] != 0x0F || bytes[index + 3] != 0x10)
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[index + 4])
            || !TryResolveRipTarget(bytes, index, index + 5, insnLen: 9, valueSize: 4, out var targetRva))
        {
            return false;
        }

        if (!IsMulssAt(bytes, index + 9))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 5, InsnLen: 9);
        return true;
    }

    private void LogInstantBuildCandidate(byte[] bytes, int targetRva, IReadOnlyList<(int TargetRva, int InsnRva, int DispOffset, int InsnLen)> hits)
    {
        var refs = hits.Count;
        var value = BitConverter.ToSingle(bytes, targetRva);
        var example = hits[0];
        var snippet = BytesToHex(bytes, example.InsnRva, 18);
        var suggested = BytesToAobPatternWithWildcards(
            bytes,
            example.InsnRva,
            18,
            (example.InsnRva + example.DispOffset, 4));

        _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs}");
        _output.WriteLine($"   exampleInsnRva=0x{example.InsnRva:X} bytes={snippet}");
        _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
    }

    private void DumpSelectedHpCandidates(AttachSession session, ModuleSnapshot snapshot, int maxHits)
    {
        if (!IsFallbackSymbol(session, "selected_hp"))
        {
            return;
        }

        var bytes = snapshot.Bytes;
        var hits = CollectSelectedHpCandidates(bytes);
        if (hits.Count == 0)
        {
            _output.WriteLine("Selected-hp calibration: no movss[rcx+disp32] -> movss[rip+disp32] candidates found.");
            return;
        }

        var grouped = hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(maxHits).ToArray();
        _output.WriteLine($"Selected-hp calibration: {hits.Count} hit(s), {grouped.Length} unique target(s) (top {grouped.Length}):");
        foreach (var group in grouped)
        {
            LogSelectedHpCandidate(group, bytes);
        }
    }

    private static List<(int MatchRva, int StoreDispOffset, int TargetRva)> CollectSelectedHpCandidates(byte[] bytes)
    {
        var hits = new List<(int MatchRva, int StoreDispOffset, int TargetRva)>();
        for (var i = 0; i + 32 < bytes.Length; i++)
        {
            if (bytes[i] != 0xF3 || bytes[i + 1] != 0x0F || bytes[i + 2] != 0x10 || bytes[i + 3] != 0x81)
            {
                continue;
            }

            if (TryDecodeSelectedHpStoreNoRex(bytes, i, out var hit) || TryDecodeSelectedHpStoreRex(bytes, i, out hit))
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    private static bool TryDecodeSelectedHpStoreNoRex(byte[] bytes, int matchRva, out (int MatchRva, int StoreDispOffset, int TargetRva) hit)
    {
        hit = default;
        var storeStart = matchRva + 8;
        if (bytes[storeStart] != 0xF3 || bytes[storeStart + 1] != 0x0F || bytes[storeStart + 2] != 0x11)
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[storeStart + 3])
            || !TryResolveRipTarget(bytes, storeStart, storeStart + 4, insnLen: 8, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (matchRva, StoreDispOffset: 12, targetRva);
        return true;
    }

    private static bool TryDecodeSelectedHpStoreRex(byte[] bytes, int matchRva, out (int MatchRva, int StoreDispOffset, int TargetRva) hit)
    {
        hit = default;
        var storeStart = matchRva + 8;
        if (bytes[storeStart] != 0xF3
            || !IsRex(bytes[storeStart + 1])
            || bytes[storeStart + 2] != 0x0F
            || bytes[storeStart + 3] != 0x11)
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[storeStart + 4])
            || !TryResolveRipTarget(bytes, storeStart, storeStart + 5, insnLen: 9, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (matchRva, StoreDispOffset: 13, targetRva);
        return true;
    }

    private void LogSelectedHpCandidate(IGrouping<int, (int MatchRva, int StoreDispOffset, int TargetRva)> group, byte[] bytes)
    {
        var targetRva = group.Key;
        var refs = group.Count();
        var value = BitConverter.ToSingle(bytes, targetRva);
        var example = group.First();
        var snippet = BytesToHex(bytes, example.MatchRva, 20);
        var suggested = BytesToAobPatternWithWildcards(
            bytes,
            example.MatchRva,
            20,
            (example.MatchRva + 4, 4),
            (example.MatchRva + example.StoreDispOffset, 4));

        _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs}");
        _output.WriteLine($"   exampleMatchRva=0x{example.MatchRva:X} storeDispOffset={example.StoreDispOffset} bytes={snippet}");
        _output.WriteLine($"   suggestedPattern={suggested} offset={example.StoreDispOffset} addressMode=ReadRipRelative32AtOffset");
    }

    private void DumpPlanetOwnerCandidates(AttachSession session, ModuleSnapshot snapshot, int maxHits)
    {
        if (!IsFallbackSymbol(session, "planet_owner"))
        {
            return;
        }

        var bytes = snapshot.Bytes;
        var hits = CollectPlanetOwnerCandidates(bytes);
        if (hits.Count == 0)
        {
            _output.WriteLine("Planet-owner calibration: no mov[rsi+disp32] -> mov[rip+disp32] candidates found.");
            return;
        }

        var grouped = hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(maxHits).ToArray();
        _output.WriteLine($"Planet-owner calibration: {hits.Count} hit(s), {grouped.Length} unique target(s) (top {grouped.Length}):");
        foreach (var group in grouped)
        {
            LogPlanetOwnerCandidate(group, bytes);
        }
    }

    private static List<(int MatchRva, int StoreDispOffset, int TargetRva)> CollectPlanetOwnerCandidates(byte[] bytes)
    {
        var hits = new List<(int MatchRva, int StoreDispOffset, int TargetRva)>();
        for (var i = 0; i + 32 < bytes.Length; i++)
        {
            if (TryDecodePlanetOwnerCandidate(bytes, i, loadModRm: 0x8E, storeModRm: 0x0D, out var hit)
                || TryDecodePlanetOwnerCandidate(bytes, i, loadModRm: 0x86, storeModRm: 0x05, out hit))
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    private static bool TryDecodePlanetOwnerCandidate(
        byte[] bytes,
        int matchRva,
        byte loadModRm,
        byte storeModRm,
        out (int MatchRva, int StoreDispOffset, int TargetRva) hit)
    {
        hit = default;
        if (matchRva + 16 >= bytes.Length || bytes[matchRva] != 0x8B || bytes[matchRva + 1] != loadModRm)
        {
            return false;
        }

        var storeStart = matchRva + 6;
        if (bytes[storeStart] != 0x89 || bytes[storeStart + 1] != storeModRm)
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, storeStart, storeStart + 2, insnLen: 6, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (matchRva, StoreDispOffset: 8, targetRva);
        return true;
    }

    private void LogPlanetOwnerCandidate(IGrouping<int, (int MatchRva, int StoreDispOffset, int TargetRva)> group, byte[] bytes)
    {
        var targetRva = group.Key;
        var refs = group.Count();
        var value = BitConverter.ToInt32(bytes, targetRva);
        var example = group.First();
        var snippet = BytesToHex(bytes, example.MatchRva, 18);
        var suggested = BytesToAobPatternWithWildcards(
            bytes,
            example.MatchRva,
            18,
            (example.MatchRva + 2, 4),
            (example.MatchRva + example.StoreDispOffset, 4));

        _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs}");
        _output.WriteLine($"   exampleMatchRva=0x{example.MatchRva:X} storeDispOffset={example.StoreDispOffset} bytes={snippet}");
        _output.WriteLine($"   suggestedPattern={suggested} offset={example.StoreDispOffset} addressMode=ReadRipRelative32AtOffset");
    }

    private void DumpTopRipRelativeFloatTargets(ModuleSnapshot snapshot, int top)
    {
        var bytes = snapshot.Bytes;
        var hits = CollectRipRelativeMovssHits(bytes);
        if (hits.Count == 0)
        {
            _output.WriteLine("Float scan: no RIP-relative movss load/store targets found.");
            return;
        }

        _output.WriteLine($"Float scan: top {top} RIP-relative float LOAD targets (F3 0F 10):");
        LogFloatOpRanking(bytes, hits.Where(x => x.Op == 0x10).ToArray(), top);
        _output.WriteLine($"Float scan: top {top} RIP-relative float STORE targets (F3 0F 11):");
        LogFloatOpRanking(bytes, hits.Where(x => x.Op == 0x11).ToArray(), top);
    }

    private static List<(int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes)> CollectRipRelativeMovssHits(byte[] bytes)
    {
        var hits = new List<(int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes)>();
        for (var i = 0; i + 12 < bytes.Length; i++)
        {
            if (TryDecodeMovssNoRex(bytes, i, out var hit) || TryDecodeMovssRex(bytes, i, out hit))
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    private static bool TryDecodeMovssNoRex(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes) hit)
    {
        hit = default;
        if (bytes[index] != 0xF3 || bytes[index + 1] != 0x0F || (bytes[index + 2] != 0x10 && bytes[index + 2] != 0x11))
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[index + 3])
            || !TryResolveRipTarget(bytes, index, index + 4, insnLen: 8, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 4, Op: bytes[index + 2], BytesToHex(bytes, index, 16));
        return true;
    }

    private static bool TryDecodeMovssRex(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes) hit)
    {
        hit = default;
        if (bytes[index] != 0xF3 || !IsRex(bytes[index + 1]) || bytes[index + 2] != 0x0F || (bytes[index + 3] != 0x10 && bytes[index + 3] != 0x11))
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[index + 4])
            || !TryResolveRipTarget(bytes, index, index + 5, insnLen: 9, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 5, Op: bytes[index + 3], BytesToHex(bytes, index, 16));
        return true;
    }

    private void LogFloatOpRanking(byte[] bytes, IReadOnlyList<(int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes)> hits, int top)
    {
        foreach (var group in hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(top))
        {
            var targetRva = group.Key;
            var refs = group.Count();
            var value = BitConverter.ToSingle(bytes, targetRva);
            var example = group.First();
            var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));
            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} insnRva=0x{example.InsnRva:X} exampleBytes={example.Bytes}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
    }

    private void DumpTopRipRelativeFloatArithmeticTargets(ModuleSnapshot snapshot, int top)
    {
        var bytes = snapshot.Bytes;
        var hits = CollectRipRelativeFloatArithmeticHits(bytes);
        if (hits.Count == 0)
        {
            _output.WriteLine("Float arithmetic scan: no RIP-relative addss/mulss/subss/divss targets found.");
            return;
        }

        _output.WriteLine($"Float arithmetic scan: top {top} RIP-relative float targets (F3 0F 58/59/5C/5D/5E/5F):");
        foreach (var group in hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(top))
        {
            LogFloatArithmeticTarget(bytes, group);
        }
    }

    private static List<(int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes)> CollectRipRelativeFloatArithmeticHits(byte[] bytes)
    {
        var hits = new List<(int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes)>();
        for (var i = 0; i + 12 < bytes.Length; i++)
        {
            if (TryDecodeFloatArithmeticNoRex(bytes, i, out var hit) || TryDecodeFloatArithmeticRex(bytes, i, out hit))
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    private static bool TryDecodeFloatArithmeticNoRex(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes) hit)
    {
        hit = default;
        if (bytes[index] != 0xF3 || bytes[index + 1] != 0x0F || !IsArithmeticFloatOp(bytes[index + 2]))
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[index + 3])
            || !TryResolveRipTarget(bytes, index, index + 4, insnLen: 8, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 4, Op: bytes[index + 2], BytesToHex(bytes, index, 16));
        return true;
    }

    private static bool TryDecodeFloatArithmeticRex(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes) hit)
    {
        hit = default;
        if (bytes[index] != 0xF3 || !IsRex(bytes[index + 1]) || bytes[index + 2] != 0x0F || !IsArithmeticFloatOp(bytes[index + 3]))
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[index + 4])
            || !TryResolveRipTarget(bytes, index, index + 5, insnLen: 9, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 5, Op: bytes[index + 3], BytesToHex(bytes, index, 16));
        return true;
    }

    private static bool IsArithmeticFloatOp(byte op) => op is 0x58 or 0x59 or 0x5C or 0x5D or 0x5E or 0x5F;

    private static string FloatArithmeticOpName(byte op) => op switch
    {
        0x58 => "addss",
        0x59 => "mulss",
        0x5C => "subss",
        0x5D => "minss",
        0x5E => "divss",
        0x5F => "maxss",
        _ => $"op_{op:X2}"
    };

    private void LogFloatArithmeticTarget(byte[] bytes, IGrouping<int, (int TargetRva, int InsnRva, int DispOffset, byte Op, string Bytes)> group)
    {
        var targetRva = group.Key;
        var refs = group.Count();
        var value = BitConverter.ToSingle(bytes, targetRva);
        var example = group.First();
        var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));
        _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} op={FloatArithmeticOpName(example.Op)} insnRva=0x{example.InsnRva:X} bytes={example.Bytes}");
        _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
    }

    private void DumpTopRipRelativeInt32StoreTargets(ModuleSnapshot snapshot, int top)
    {
        var bytes = snapshot.Bytes;
        var hits = CollectRipRelativeInt32StoreHits(bytes);
        if (hits.Count == 0)
        {
            _output.WriteLine("Int32 store scan: no RIP-relative 89 store targets found.");
            return;
        }

        _output.WriteLine($"Int32 store scan: top {top} RIP-relative Int32 STORE targets (89 [rip+disp32], reg):");
        foreach (var group in hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(top))
        {
            LogInt32StoreTarget(bytes, group);
        }
    }

    private static List<(int TargetRva, int InsnRva, int DispOffset, string Kind)> CollectRipRelativeInt32StoreHits(byte[] bytes)
    {
        var hits = new List<(int TargetRva, int InsnRva, int DispOffset, string Kind)>();
        for (var i = 0; i + 10 < bytes.Length; i++)
        {
            if (TryDecodeInt32StoreNoRex(bytes, i, out var hit) || TryDecodeInt32StoreRex(bytes, i, out hit))
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    private static bool TryDecodeInt32StoreNoRex(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, string Kind) hit)
    {
        hit = default;
        if (bytes[index] != 0x89 || !IsRipRelativeModRm(bytes[index + 1]))
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 2, insnLen: 6, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 2, Kind: "89 (no REX)");
        return true;
    }

    private static bool TryDecodeInt32StoreRex(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, string Kind) hit)
    {
        hit = default;
        if (!IsRex(bytes[index]) || bytes[index + 1] != 0x89 || !IsRipRelativeModRm(bytes[index + 2]))
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 3, insnLen: 7, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 3, Kind: $"{bytes[index]:X2} 89 (REX)");
        return true;
    }

    private void LogInt32StoreTarget(byte[] bytes, IGrouping<int, (int TargetRva, int InsnRva, int DispOffset, string Kind)> group)
    {
        var targetRva = group.Key;
        var refs = group.Count();
        var value = BitConverter.ToInt32(bytes, targetRva);
        var example = group.First();
        var snippet = BytesToHex(bytes, example.InsnRva, 16);
        var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));
        _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} kind={example.Kind} exampleBytes={snippet}");
        _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
    }

    private void DumpTopRipRelativeInt32Targets32BitOnly(ModuleSnapshot snapshot, int top)
    {
        var bytes = snapshot.Bytes;
        var hits = CollectRipRelativeInt32AccessHits32BitOnly(bytes);
        _output.WriteLine($"Int32 scan (32-bit only): top {top} RIP-relative LOAD targets (8B [rip+disp32])");
        LogInt32AccessRanking(bytes, hits.Where(x => x.IsLoad).ToArray(), top);
        _output.WriteLine($"Int32 scan (32-bit only): top {top} RIP-relative STORE targets (89 [rip+disp32], r32)");
        LogInt32AccessRanking(bytes, hits.Where(x => !x.IsLoad).ToArray(), top);
    }

    private static List<(int TargetRva, int InsnRva, int DispOffset, string Kind, bool IsLoad)> CollectRipRelativeInt32AccessHits32BitOnly(byte[] bytes)
    {
        var hits = new List<(int TargetRva, int InsnRva, int DispOffset, string Kind, bool IsLoad)>();
        for (var i = 0; i + 10 < bytes.Length; i++)
        {
            if (TryDecodeInt32AccessNoRex(bytes, i, out var hit) || TryDecodeInt32AccessRexNoW(bytes, i, out hit))
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    private static bool TryDecodeInt32AccessNoRex(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, string Kind, bool IsLoad) hit)
    {
        hit = default;
        var op = bytes[index];
        if ((op != 0x8B && op != 0x89) || !IsRipRelativeModRm(bytes[index + 1]))
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 2, insnLen: 6, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 2, Kind: op == 0x8B ? "8B" : "89", IsLoad: op == 0x8B);
        return true;
    }

    private static bool TryDecodeInt32AccessRexNoW(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, string Kind, bool IsLoad) hit)
    {
        hit = default;
        var op = bytes[index + 1];
        if (!IsRexNoW(bytes[index]) || (op != 0x8B && op != 0x89) || !IsRipRelativeModRm(bytes[index + 2]))
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 3, insnLen: 7, valueSize: 4, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 3, Kind: $"{bytes[index]:X2} {op:X2}", IsLoad: op == 0x8B);
        return true;
    }

    private void LogInt32AccessRanking(byte[] bytes, IReadOnlyList<(int TargetRva, int InsnRva, int DispOffset, string Kind, bool IsLoad)> hits, int top)
    {
        foreach (var group in hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(top))
        {
            var targetRva = group.Key;
            var refs = group.Count();
            var value = BitConverter.ToInt32(bytes, targetRva);
            var example = group.OrderBy(x => x.Kind.Length).ThenBy(x => x.InsnRva).First();
            var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));
            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} kind={example.Kind} insnRva=0x{example.InsnRva:X} bytes={BytesToHex(bytes, example.InsnRva, 16)}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
    }

    private void DumpTopRipRelativeByteCompareTargets(ModuleSnapshot snapshot, int top)
    {
        var bytes = snapshot.Bytes;
        var hits = CollectByteCompareHits(bytes);
        if (hits.Count == 0)
        {
            _output.WriteLine("Byte compare scan: no RIP-relative cmp byte [rip+disp32],0 with short jcc found.");
            return;
        }

        _output.WriteLine($"Byte compare scan: top {top} RIP-relative byte CMP targets (80 3D disp32 00 74/75):");
        foreach (var group in hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(top))
        {
            LogByteCompareTarget(bytes, group);
        }
    }

    private static List<(int TargetRva, int InsnRva, int DispOffset, int Rel8Offset, byte Imm8, byte Jcc, string Bytes)> CollectByteCompareHits(byte[] bytes)
    {
        var hits = new List<(int TargetRva, int InsnRva, int DispOffset, int Rel8Offset, byte Imm8, byte Jcc, string Bytes)>();
        for (var i = 0; i + 9 < bytes.Length; i++)
        {
            if (TryDecodeByteCompareHit(bytes, i, out var hit))
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    private static bool TryDecodeByteCompareHit(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, int Rel8Offset, byte Imm8, byte Jcc, string Bytes) hit)
    {
        hit = default;
        if (bytes[index] != 0x80 || bytes[index + 1] != 0x3D)
        {
            return false;
        }

        var imm8 = bytes[index + 6];
        var jcc = bytes[index + 7];
        if (imm8 != 0x00 || (jcc != 0x74 && jcc != 0x75))
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 2, insnLen: 7, valueSize: 1, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 2, Rel8Offset: 8, imm8, jcc, BytesToHex(bytes, index, 16));
        return true;
    }

    private void LogByteCompareTarget(byte[] bytes, IGrouping<int, (int TargetRva, int InsnRva, int DispOffset, int Rel8Offset, byte Imm8, byte Jcc, string Bytes)> group)
    {
        var targetRva = group.Key;
        var refs = group.Count();
        var value = bytes[targetRva];
        var example = group.First();
        var suggested = BytesToAobPatternWithWildcards(
            bytes,
            example.InsnRva,
            16,
            (example.InsnRva + example.DispOffset, 4),
            (example.InsnRva + example.Rel8Offset, 1));

        _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} jcc={example.Jcc:X2} insnRva=0x{example.InsnRva:X} bytes={example.Bytes}");
        _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
    }

    private void DumpTopRipRelativeByteImmediateStoreTargets(ModuleSnapshot snapshot, int top)
    {
        var bytes = snapshot.Bytes;
        var hits = CollectByteImmediateStoreHits(bytes);
        if (hits.Count == 0)
        {
            _output.WriteLine("Byte store scan: no RIP-relative mov byte [rip+disp32], imm8 found.");
            return;
        }

        _output.WriteLine($"Byte store scan: top {top} RIP-relative byte STORE targets (C6 05 disp32 imm8):");
        foreach (var group in hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(top))
        {
            LogByteImmediateStoreTarget(bytes, group);
        }
    }

    private static List<(int TargetRva, int InsnRva, int DispOffset, byte Imm8, string Bytes)> CollectByteImmediateStoreHits(byte[] bytes)
    {
        var hits = new List<(int TargetRva, int InsnRva, int DispOffset, byte Imm8, string Bytes)>();
        for (var i = 0; i + 8 < bytes.Length; i++)
        {
            if (TryDecodeByteImmediateStoreHit(bytes, i, out var hit))
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    private static bool TryDecodeByteImmediateStoreHit(byte[] bytes, int index, out (int TargetRva, int InsnRva, int DispOffset, byte Imm8, string Bytes) hit)
    {
        hit = default;
        if (bytes[index] != 0xC6 || bytes[index + 1] != 0x05)
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 2, insnLen: 7, valueSize: 1, out var targetRva))
        {
            return false;
        }

        hit = (targetRva, index, DispOffset: 2, bytes[index + 6], BytesToHex(bytes, index, 16));
        return true;
    }

    private void LogByteImmediateStoreTarget(byte[] bytes, IGrouping<int, (int TargetRva, int InsnRva, int DispOffset, byte Imm8, string Bytes)> group)
    {
        var targetRva = group.Key;
        var refs = group.Count();
        var value = bytes[targetRva];
        var example = group.First();
        var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));
        _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} imm8={example.Imm8:X2} insnRva=0x{example.InsnRva:X} bytes={example.Bytes}");
        _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
    }

    private void DumpRipRelativeInt32Refs(ModuleSnapshot snapshot, long targetRva, int max)
    {
        var bytes = snapshot.Bytes;
        var refs = CollectRipRelativeInt32Refs(bytes, targetRva);
        if (refs.Count == 0)
        {
            _output.WriteLine(" - No 8B/89 RIP-relative refs found.");
            return;
        }

        _output.WriteLine($" - Found {refs.Count} RIP-relative Int32 ref(s) (showing up to {max}):");
        foreach (var reference in refs.Take(max))
        {
            LogRipRelativeReference(bytes, reference, patternLength: 18);
        }
    }

    private static List<(int InsnRva, int DispOffset, string Kind)> CollectRipRelativeInt32Refs(byte[] bytes, long targetRva)
    {
        var refs = new List<(int InsnRva, int DispOffset, string Kind)>();
        for (var i = 0; i + 7 < bytes.Length; i++)
        {
            if (TryDecodeInt32ReferenceNoRex(bytes, i, targetRva, out var reference)
                || TryDecodeInt32ReferenceRex(bytes, i, targetRva, out reference))
            {
                refs.Add(reference);
            }
        }

        return refs;
    }

    private static bool TryDecodeInt32ReferenceNoRex(byte[] bytes, int index, long targetRva, out (int InsnRva, int DispOffset, string Kind) hit)
    {
        hit = default;
        var op = bytes[index];
        if ((op != 0x8B && op != 0x89) || !IsRipRelativeModRm(bytes[index + 1]))
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 2, insnLen: 6, valueSize: 4, out var decodedTarget) || decodedTarget != targetRva)
        {
            return false;
        }

        hit = (index, DispOffset: 2, Kind: $"{op:X2} (no REX)");
        return true;
    }

    private static bool TryDecodeInt32ReferenceRex(byte[] bytes, int index, long targetRva, out (int InsnRva, int DispOffset, string Kind) hit)
    {
        hit = default;
        var op = bytes[index + 1];
        if (!IsRex(bytes[index]) || (op != 0x8B && op != 0x89) || !IsRipRelativeModRm(bytes[index + 2]))
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 3, insnLen: 7, valueSize: 4, out var decodedTarget) || decodedTarget != targetRva)
        {
            return false;
        }

        hit = (index, DispOffset: 3, Kind: $"{bytes[index]:X2} {op:X2} (REX)");
        return true;
    }

    private void DumpRipRelativeSseFloatRefs(ModuleSnapshot snapshot, long targetRva, int max)
    {
        var bytes = snapshot.Bytes;
        var refs = CollectRipRelativeSseFloatRefs(bytes, targetRva);
        if (refs.Count == 0)
        {
            _output.WriteLine(" - No F3 0F 10/11/59 RIP-relative float refs found.");
            return;
        }

        _output.WriteLine($" - Found {refs.Count} RIP-relative float ref(s) (showing up to {max}):");
        foreach (var reference in refs.Take(max))
        {
            LogRipRelativeReference(bytes, reference, patternLength: 20);
        }
    }

    private static List<(int InsnRva, int DispOffset, string Kind)> CollectRipRelativeSseFloatRefs(byte[] bytes, long targetRva)
    {
        var refs = new List<(int InsnRva, int DispOffset, string Kind)>();
        for (var i = 0; i + 10 < bytes.Length; i++)
        {
            if (TryDecodeSseFloatReferenceNoRex(bytes, i, targetRva, out var reference)
                || TryDecodeSseFloatReferenceRex(bytes, i, targetRva, out reference))
            {
                refs.Add(reference);
            }
        }

        return refs;
    }

    private static bool TryDecodeSseFloatReferenceNoRex(byte[] bytes, int index, long targetRva, out (int InsnRva, int DispOffset, string Kind) hit)
    {
        hit = default;
        if (bytes[index] != 0xF3)
        {
            return false;
        }

        if (bytes[index + 1] != 0x0F)
        {
            return false;
        }

        var op = bytes[index + 2];
        if (!IsSseReferenceOp(op))
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[index + 3]))
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 4, insnLen: 8, valueSize: 4, out var decodedTarget))
        {
            return false;
        }

        if (decodedTarget != targetRva)
        {
            return false;
        }

        hit = (index, DispOffset: 4, Kind: $"F3 0F {op:X2} (no REX)");
        return true;
    }

    private static bool TryDecodeSseFloatReferenceRex(byte[] bytes, int index, long targetRva, out (int InsnRva, int DispOffset, string Kind) hit)
    {
        hit = default;
        if (bytes[index] != 0xF3)
        {
            return false;
        }

        var rex = bytes[index + 1];
        if (!IsRex(rex))
        {
            return false;
        }

        if (bytes[index + 2] != 0x0F)
        {
            return false;
        }

        var op = bytes[index + 3];
        if (!IsSseReferenceOp(op))
        {
            return false;
        }

        if (!IsRipRelativeModRm(bytes[index + 4]))
        {
            return false;
        }

        if (!TryResolveRipTarget(bytes, index, index + 5, insnLen: 9, valueSize: 4, out var decodedTarget))
        {
            return false;
        }

        if (decodedTarget != targetRva)
        {
            return false;
        }

        hit = (index, DispOffset: 5, Kind: $"F3 {rex:X2} 0F {op:X2} (REX)");
        return true;
    }

    private static bool IsSseReferenceOp(byte op) => op is 0x10 or 0x11 or 0x59;

    private void LogRipRelativeReference(byte[] bytes, (int InsnRva, int DispOffset, string Kind) reference, int patternLength)
    {
        var snippet = BytesToHex(bytes, reference.InsnRva, patternLength);
        var suggested = BytesToAobPattern(bytes, reference.InsnRva, patternLength, wildcardStart: reference.InsnRva + reference.DispOffset, wildcardCount: 4);
        _output.WriteLine($"   insnRva=0x{reference.InsnRva:X} kind={reference.Kind} bytes={snippet}");
        _output.WriteLine($"   suggestedPattern={suggested} offset={reference.DispOffset} addressMode=ReadRipRelative32AtOffset");
    }

    private static string BytesToHex(byte[] bytes, int offset, int count)
    {
        var end = Math.Min(offset + count, bytes.Length);
        return string.Join(' ', bytes.AsSpan(offset, end - offset).ToArray().Select(x => x.ToString("X2")));
    }

    private static string BytesToAobPattern(byte[] bytes, int offset, int count, int wildcardStart, int wildcardCount)
    {
        var end = Math.Min(offset + count, bytes.Length);
        var tokens = new List<string>(end - offset);

        for (var i = offset; i < end; i++)
        {
            if (i >= wildcardStart && i < wildcardStart + wildcardCount)
            {
                tokens.Add("??");
            }
            else
            {
                tokens.Add(bytes[i].ToString("X2"));
            }
        }

        return string.Join(' ', tokens);
    }

    private static string BytesToAobPatternWithWildcards(byte[] bytes, int offset, int count, params (int Start, int Length)[] wildcards)
    {
        var end = Math.Min(offset + count, bytes.Length);
        var tokens = new List<string>(end - offset);

        bool IsWildcardIndex(int index)
        {
            foreach (var wc in wildcards)
            {
                if (index >= wc.Start && index < wc.Start + wc.Length)
                {
                    return true;
                }
            }

            return false;
        }

        for (var i = offset; i < end; i++)
        {
            tokens.Add(IsWildcardIndex(i) ? "??" : bytes[i].ToString("X2"));
        }

        return string.Join(' ', tokens);
    }

    private void DumpTopRipRelativeInt32Targets(AttachSession session, int top)
    {
        using var process = Process.GetProcessById(session.Process.ProcessId);
        var module = process.MainModule ?? throw new InvalidOperationException("Main module not available for target process.");
        var baseAddress = module.BaseAddress;
        using var accessor = new ProcessMemoryAccessor(process.Id);
        var moduleBytes = accessor.ReadBytes(baseAddress, module.ModuleMemorySize);

        var scan = CollectTopRipRelativeInt32Targets(moduleBytes);
        if (scan.Counts.Count == 0)
        {
            _output.WriteLine("No RIP-relative Int32 targets found in module scan.");
            return;
        }

        _output.WriteLine($"Top {top} RIP-relative Int32 targets by reference count (8B/89 rip-relative):");
        foreach (var entry in scan.Counts.OrderByDescending(x => x.Value).Take(top))
        {
            LogTopRipRelativeInt32Target(baseAddress, accessor, entry.Key, entry.Value, scan.ExampleReferences);
        }
    }

    private static (Dictionary<long, int> Counts, Dictionary<long, List<string>> ExampleReferences) CollectTopRipRelativeInt32Targets(byte[] moduleBytes)
    {
        var counts = new Dictionary<long, int>();
        var exampleRefs = new Dictionary<long, List<string>>();
        for (var i = 0; i + 6 < moduleBytes.Length; i++)
        {
            if (!TryDecodeTopInt32Target(moduleBytes, i, out var op, out var modrm, out var targetRva))
            {
                continue;
            }

            counts[targetRva] = counts.TryGetValue(targetRva, out var current) ? current + 1 : 1;
            RecordTopInt32ExampleRef(moduleBytes, exampleRefs, targetRva, i, op, modrm);
        }

        return (counts, exampleRefs);
    }

    private static bool TryDecodeTopInt32Target(byte[] bytes, int index, out byte op, out byte modrm, out long targetRva)
    {
        op = bytes[index];
        modrm = bytes[index + 1];
        targetRva = 0;
        if ((op != 0x8B && op != 0x89) || !IsRipRelativeModRm(modrm))
        {
            return false;
        }

        return TryResolveRipTarget(bytes, index, index + 2, insnLen: 6, valueSize: 4, out var target)
            && (targetRva = target) >= 0;
    }

    private static void RecordTopInt32ExampleRef(
        byte[] moduleBytes,
        IDictionary<long, List<string>> exampleRefs,
        long targetRva,
        int insnRva,
        byte op,
        byte modrm)
    {
        if (!exampleRefs.TryGetValue(targetRva, out var refs))
        {
            refs = new List<string>();
            exampleRefs[targetRva] = refs;
        }

        if (refs.Count >= 2)
        {
            return;
        }

        var snippet = BytesToHex(moduleBytes, insnRva, 16);
        refs.Add($"insnRva=0x{insnRva:X} op={(op == 0x8B ? "8B" : "89")} modrm=0x{modrm:X2} bytes={snippet}");
    }

    private void LogTopRipRelativeInt32Target(
        nint baseAddress,
        ProcessMemoryAccessor accessor,
        long targetRva,
        int refs,
        IReadOnlyDictionary<long, List<string>> exampleRefs)
    {
        var addr = baseAddress + (nint)targetRva;
        var value = TryReadInt32(accessor, addr);
        _output.WriteLine($" - targetRva=0x{targetRva:X} addr=0x{addr.ToInt64():X} refs={refs} value={FormatInt32Value(value)}");
        if (!exampleRefs.TryGetValue(targetRva, out var references))
        {
            return;
        }

        foreach (var reference in references)
        {
            _output.WriteLine($"   {reference}");
        }
    }

    private static int? TryReadInt32(ProcessMemoryAccessor accessor, nint address)
    {
        try
        {
            return accessor.Read<int>(address);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatInt32Value(int? value)
    {
        if (!value.HasValue)
        {
            return "<unreadable>";
        }

        return value.Value is >= -1000 and <= 50_000_000
            ? value.Value.ToString()
            : value.Value.ToString();
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
