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

public sealed class LiveActionSmokeTests
{
    private readonly ITestOutputHelper _output;

    private sealed record ModuleSnapshot(nint BaseAddress, int ModuleSize, byte[] Bytes);

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
        if (session.Process.ProcessId != running.ProcessId)
        {
            _output.WriteLine($"Best-match PID ({running.ProcessId}) differed from attach PID ({session.Process.ProcessId}); continuing with attached host process.");
        }
        session.Symbols.Symbols.Count.Should().BeGreaterThan(0);

        _output.WriteLine($"Resolved symbols: {session.Symbols.Symbols.Count}");
        foreach (var symbolName in session.Symbols.Symbols.Keys.OrderBy(x => x))
        {
            if (session.Symbols.Symbols.TryGetValue(symbolName, out var symbol))
            {
                _output.WriteLine(
                    $"{symbolName}: 0x{symbol.Address.ToInt64():X} source={symbol.Source} diag={symbol.Diagnostics}");
            }
        }

        byte? fog = null;
        byte? timerFreeze = null;
        byte? tacticalGod = null;
        byte? tacticalOneHit = null;
        int? credits = null;
        int? heroRespawn = null;
        float? instantBuild = null;
        float? selectedHp = null;
        int? planetOwner = null;
        var readFailures = new List<string>();

        try
        {
            credits = await runtime.ReadAsync<int>("credits");
            _output.WriteLine($"Credits read succeeded: {credits}");
        }
        catch (Exception ex)
        {
            readFailures.Add($"credits: {ex.Message}");
        }

        try
        {
            heroRespawn = await runtime.ReadAsync<int>("hero_respawn_timer");
            _output.WriteLine($"hero_respawn_timer read succeeded: {heroRespawn}");
        }
        catch (Exception ex)
        {
            readFailures.Add($"hero_respawn_timer: {ex.Message}");
        }

        try
        {
            instantBuild = await runtime.ReadAsync<float>("instant_build");
            _output.WriteLine($"instant_build read succeeded: {instantBuild}");
        }
        catch (Exception ex)
        {
            readFailures.Add($"instant_build: {ex.Message}");
        }

        try
        {
            selectedHp = await runtime.ReadAsync<float>("selected_hp");
            _output.WriteLine($"selected_hp read succeeded: {selectedHp}");
        }
        catch (Exception ex)
        {
            readFailures.Add($"selected_hp: {ex.Message}");
        }

        try
        {
            planetOwner = await runtime.ReadAsync<int>("planet_owner");
            _output.WriteLine($"planet_owner read succeeded: {planetOwner}");
        }
        catch (Exception ex)
        {
            readFailures.Add($"planet_owner: {ex.Message}");
        }

        try
        {
            fog = await runtime.ReadAsync<byte>("fog_reveal");
            _output.WriteLine($"fog_reveal read succeeded: {fog}");
        }
        catch (Exception ex)
        {
            readFailures.Add($"fog_reveal: {ex.Message}");
        }

        try
        {
            timerFreeze = await runtime.ReadAsync<byte>("game_timer_freeze");
            _output.WriteLine($"game_timer_freeze read succeeded: {timerFreeze}");
        }
        catch (Exception ex)
        {
            readFailures.Add($"game_timer_freeze: {ex.Message}");
        }

        try
        {
            tacticalGod = await runtime.ReadAsync<byte>("tactical_god_mode");
            _output.WriteLine($"tactical_god_mode read succeeded: {tacticalGod}");
        }
        catch (Exception ex)
        {
            readFailures.Add($"tactical_god_mode: {ex.Message}");
        }

        try
        {
            tacticalOneHit = await runtime.ReadAsync<byte>("tactical_one_hit_mode");
            _output.WriteLine($"tactical_one_hit_mode read succeeded: {tacticalOneHit}");
        }
        catch (Exception ex)
        {
            readFailures.Add($"tactical_one_hit_mode: {ex.Message}");
        }

        if (fog.HasValue && timerFreeze is null)
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

        if (credits is null)
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

            // Quick sanity check: ensure our expected planet_owner AOB actually exists in module bytes.
            try
            {
                using var p = Process.GetProcessById(session.Process.ProcessId);
                var expected = AobPattern.Parse("89 35 ?? ?? ?? ?? 48 C7 05 ?? ?? ?? ?? ?? ?? ?? ??");
                var hit = AobScanner.FindPattern(p, snapshot.Bytes, snapshot.BaseAddress, expected);
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
        catch (Exception ex)
        {
            _output.WriteLine($"Symbol calibration debug scan failed: {ex.Message}");
        }

        if (readFailures.Count > 0)
        {
            _output.WriteLine("Read failures:");
            foreach (var failure in readFailures)
            {
                _output.WriteLine($" - {failure}");
            }
        }

        if (credits is null && fog is null && timerFreeze is null)
        {
            _output.WriteLine("No target symbols were readable in this build. Likely requires signature/offset calibration.");
        }
        else
        {
            if (fog.HasValue)
            {
                await runtime.WriteAsync("fog_reveal", fog.Value);
            }
            if (timerFreeze.HasValue)
            {
                await runtime.WriteAsync("game_timer_freeze", timerFreeze.Value);
            }

            {
                var toggledAny = false;
                if (fog.HasValue)
                {
                    var toggledFog = (byte)(fog.Value == 0 ? 1 : 0);
                    await runtime.WriteAsync("fog_reveal", toggledFog);
                    await runtime.WriteAsync("fog_reveal", fog.Value);
                    toggledAny = true;
                    _output.WriteLine("Toggle-revert check executed for fog_reveal.");
                }

                if (timerFreeze.HasValue)
                {
                    var toggledTimer = (byte)(timerFreeze.Value == 0 ? 1 : 0);
                    await runtime.WriteAsync("game_timer_freeze", toggledTimer);
                    await runtime.WriteAsync("game_timer_freeze", timerFreeze.Value);
                    toggledAny = true;
                    _output.WriteLine("Toggle-revert check executed for game_timer_freeze.");
                }

                if (tacticalGod.HasValue)
                {
                    var toggledGod = (byte)(tacticalGod.Value == 0 ? 1 : 0);
                    await runtime.WriteAsync("tactical_god_mode", toggledGod);
                    await runtime.WriteAsync("tactical_god_mode", tacticalGod.Value);
                    toggledAny = true;
                    _output.WriteLine("Toggle-revert check executed for tactical_god_mode.");
                }

                if (tacticalOneHit.HasValue)
                {
                    var toggledOneHit = (byte)(tacticalOneHit.Value == 0 ? 1 : 0);
                    await runtime.WriteAsync("tactical_one_hit_mode", toggledOneHit);
                    await runtime.WriteAsync("tactical_one_hit_mode", tacticalOneHit.Value);
                    toggledAny = true;
                    _output.WriteLine("Toggle-revert check executed for tactical_one_hit_mode.");
                }

                if (!toggledAny)
                {
                    _output.WriteLine("No toggle-safe symbols were readable in this build.");
                }
            }

            if (fog.HasValue)
            {
                var fogAfter = await runtime.ReadAsync<byte>("fog_reveal");
                fogAfter.Should().Be(fog.Value);
            }

            if (timerFreeze.HasValue)
            {
                var timerAfter = await runtime.ReadAsync<byte>("game_timer_freeze");
                timerAfter.Should().Be(timerFreeze.Value);
            }
        }

        await runtime.DetachAsync();
        runtime.IsAttached.Should().BeFalse();
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

        var uniqueTargets = new Dictionary<long, int>();
        var exampleRefs = new Dictionary<long, List<string>>();

        static long DecodeTargetRva80_3D(int insnRva, int disp) => insnRva + 7 + disp;
        static long DecodeTargetRvaC6_05(int insnRva, int disp) => insnRva + 7 + disp;

        static string BytesToHex(byte[] bytes, int offset, int count)
        {
            var end = Math.Min(offset + count, bytes.Length);
            return string.Join(' ', bytes.AsSpan(offset, end - offset).ToArray().Select(x => x.ToString("X2")));
        }

        void Record(long targetRva, int insnRva, string kind)
        {
            uniqueTargets.TryGetValue(targetRva, out var count);
            uniqueTargets[targetRva] = count + 1;

            if (!exampleRefs.TryGetValue(targetRva, out var list))
            {
                list = new List<string>();
                exampleRefs[targetRva] = list;
            }

            if (list.Count < 3)
            {
                // include some nearby bytes to help craft a stable AOB signature
                var snippet = BytesToHex(moduleBytes, insnRva, 14);
                list.Add($"insnRva=0x{insnRva:X} kind={kind} bytes={snippet}");
            }
        }

        for (var i = 0; i + 7 < moduleBytes.Length; i++)
        {
            var b0 = moduleBytes[i];
            var b1 = moduleBytes[i + 1];

            if (b0 == 0x80 && b1 == 0x3D)
            {
                var disp = BitConverter.ToInt32(moduleBytes, i + 2);
                var targetRva = DecodeTargetRva80_3D(i, disp);
                if (Math.Abs(targetRva - anchorRva) <= window)
                {
                    Record(targetRva, i, "80 3D");
                }
            }

            if (b0 == 0xC6 && b1 == 0x05)
            {
                var disp = BitConverter.ToInt32(moduleBytes, i + 2);
                var targetRva = DecodeTargetRvaC6_05(i, disp);
                if (Math.Abs(targetRva - anchorRva) <= window)
                {
                    Record(targetRva, i, "C6 05");
                }
            }
        }

        if (uniqueTargets.Count == 0)
        {
            _output.WriteLine("No nearby RIP-relative byte targets found near anchor.");
            return;
        }

        _output.WriteLine("Nearby RIP-relative byte targets (decoded from 80 3D / C6 05):");
        foreach (var kv in uniqueTargets.OrderBy(x => x.Key))
        {
            var addr = baseAddress + (nint)kv.Key;
            byte? value = null;
            try
            {
                value = accessor.Read<byte>(addr);
            }
            catch
            {
                // ignore
            }

            _output.WriteLine($" - targetRva=0x{kv.Key:X} (addr=0x{addr.ToInt64():X}) refs={kv.Value} value={(value.HasValue ? value.Value.ToString() : "<unreadable>")}");
            if (exampleRefs.TryGetValue(kv.Key, out var refs))
            {
                foreach (var reference in refs)
                {
                    _output.WriteLine($"   {reference}");
                }
            }
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
        if (!session.Symbols.Symbols.TryGetValue("instant_build", out var symbol) || symbol.Source != AddressSource.Fallback)
        {
            return;
        }

        // Look for sequences like:
        //   movss xmm?, dword ptr [rip+disp32]
        //   mulss xmm?, ...
        // This matches the intent of the original profile signature and works well on x86-64.
        var bytes = snapshot.Bytes;
        var hitsByTarget = new Dictionary<int, List<(int InsnRva, int DispOffset, int InsnLen)>>();

        static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;
        static bool IsRex(byte b) => b is >= 0x40 and <= 0x4F;

        bool IsMulssAt(int index)
        {
            if (index + 3 >= bytes.Length)
            {
                return false;
            }

            // mulss: F3 0F 59 /r or F3 <rex> 0F 59 /r
            if (bytes[index] == 0xF3 && bytes[index + 1] == 0x0F && bytes[index + 2] == 0x59)
            {
                return true;
            }
            if (bytes[index] == 0xF3 && IsRex(bytes[index + 1]) && bytes[index + 2] == 0x0F && bytes[index + 3] == 0x59)
            {
                return true;
            }

            return false;
        }

        for (var i = 0; i + 16 < bytes.Length; i++)
        {
            if (bytes[i] != 0xF3)
            {
                continue;
            }

            // movss load: F3 0F 10 /r or F3 <rex> 0F 10 /r
            if (bytes[i + 1] == 0x0F && bytes[i + 2] == 0x10)
            {
                var modrm = bytes[i + 3];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var disp = BitConverter.ToInt32(bytes, i + 4);
                var insnLen = 8;
                var targetRva = i + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                if (!IsMulssAt(i + insnLen))
                {
                    continue;
                }

                hitsByTarget.TryGetValue(targetRva, out var list);
                list ??= new List<(int, int, int)>();
                list.Add((InsnRva: i, DispOffset: 4, InsnLen: insnLen));
                hitsByTarget[targetRva] = list;
                continue;
            }

            if (IsRex(bytes[i + 1]) && bytes[i + 2] == 0x0F && bytes[i + 3] == 0x10)
            {
                var modrm = bytes[i + 4];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var disp = BitConverter.ToInt32(bytes, i + 5);
                var insnLen = 9;
                var targetRva = i + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                if (!IsMulssAt(i + insnLen))
                {
                    continue;
                }

                hitsByTarget.TryGetValue(targetRva, out var list);
                list ??= new List<(int, int, int)>();
                list.Add((InsnRva: i, DispOffset: 5, InsnLen: insnLen));
                hitsByTarget[targetRva] = list;
            }
        }

        if (hitsByTarget.Count == 0)
        {
            _output.WriteLine("Instant-build calibration: no movss[rip]+mulss candidates found.");
            return;
        }

        _output.WriteLine($"Instant-build calibration: {hitsByTarget.Count} candidate global float target(s) found (top {maxTargets}):");
        foreach (var kv in hitsByTarget.OrderByDescending(x => x.Value.Count).Take(maxTargets))
        {
            var targetRva = kv.Key;
            var refs = kv.Value.Count;
            var value = BitConverter.ToSingle(bytes, targetRva);
            var example = kv.Value[0];
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
    }

    private void DumpSelectedHpCandidates(AttachSession session, ModuleSnapshot snapshot, int maxHits)
    {
        if (!session.Symbols.Symbols.TryGetValue("selected_hp", out var symbol) || symbol.Source != AddressSource.Fallback)
        {
            return;
        }

        // Look for:
        //   movss xmm0, dword ptr [rcx+disp32]   (F3 0F 10 81 ?? ?? ?? ??)
        //   movss dword ptr [rip+disp32], xmm0   (F3 0F 11 05 ?? ?? ?? ??)
        // The second disp32 points at a global scratch variable for "selected hp".
        var bytes = snapshot.Bytes;
        var hits = new List<(int MatchRva, int StoreDispOffset, int StoreInsnRva, int StoreDispIndex, int StoreInsnLen, int TargetRva)>();

        static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;
        static bool IsRex(byte b) => b is >= 0x40 and <= 0x4F;

        for (var i = 0; i + 32 < bytes.Length; i++)
        {
            if (bytes[i] != 0xF3 || bytes[i + 1] != 0x0F || bytes[i + 2] != 0x10 || bytes[i + 3] != 0x81)
            {
                continue;
            }

            // load length fixed at 8
            var storeStart = i + 8;

            // store: F3 0F 11 /r (rip-relative modrm) OR F3 <rex> 0F 11 /r
            if (bytes[storeStart] == 0xF3 && bytes[storeStart + 1] == 0x0F && bytes[storeStart + 2] == 0x11)
            {
                var modrm = bytes[storeStart + 3];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var dispIndex = storeStart + 4;
                var disp = BitConverter.ToInt32(bytes, dispIndex);
                var insnLen = 8;
                var targetRva = storeStart + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                hits.Add((MatchRva: i, StoreDispOffset: dispIndex - i, StoreInsnRva: storeStart, StoreDispIndex: dispIndex, StoreInsnLen: insnLen, TargetRva: targetRva));
                continue;
            }

            if (bytes[storeStart] == 0xF3 && IsRex(bytes[storeStart + 1]) && bytes[storeStart + 2] == 0x0F && bytes[storeStart + 3] == 0x11)
            {
                var modrm = bytes[storeStart + 4];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var dispIndex = storeStart + 5;
                var disp = BitConverter.ToInt32(bytes, dispIndex);
                var insnLen = 9;
                var targetRva = storeStart + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                hits.Add((MatchRva: i, StoreDispOffset: dispIndex - i, StoreInsnRva: storeStart, StoreDispIndex: dispIndex, StoreInsnLen: insnLen, TargetRva: targetRva));
            }
        }

        if (hits.Count == 0)
        {
            _output.WriteLine("Selected-hp calibration: no movss[rcx+disp32] -> movss[rip+disp32] candidates found.");
            return;
        }

        // Group by target to reduce noise.
        var grouped = hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(maxHits).ToArray();
        _output.WriteLine($"Selected-hp calibration: {hits.Count} hit(s), {grouped.Length} unique target(s) (top {grouped.Length}):");

        foreach (var group in grouped)
        {
            var targetRva = group.Key;
            var refs = group.Count();
            var value = BitConverter.ToSingle(bytes, targetRva);
            var example = group.First();
            var snippet = BytesToHex(bytes, example.MatchRva, 20);

            // Wildcard both disp32 values: the source disp32 (unit struct offset) and the dest disp32 (global var).
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
    }

    private void DumpPlanetOwnerCandidates(AttachSession session, ModuleSnapshot snapshot, int maxHits)
    {
        if (!session.Symbols.Symbols.TryGetValue("planet_owner", out var symbol) || symbol.Source != AddressSource.Fallback)
        {
            return;
        }

        // Try to find a global scratch variable updated from a planet object:
        //   mov reg32, dword ptr [rsi+disp32]      (8B 8E/86 ?? ?? ?? ??)
        //   mov dword ptr [rip+disp32], reg32      (89 0D/05 ?? ?? ?? ??)
        // If found, resolve the second disp32.
        var bytes = snapshot.Bytes;
        var hits = new List<(int MatchRva, int StoreDispOffset, int TargetRva)>();

        bool TryMatch(int i, byte loadModRm, byte storeModRm, out int storeDispOffset, out int targetRva)
        {
            storeDispOffset = 0;
            targetRva = 0;
            if (i + 16 >= bytes.Length)
            {
                return false;
            }

            if (bytes[i] != 0x8B || bytes[i + 1] != loadModRm)
            {
                return false;
            }

            var storeStart = i + 6;
            if (bytes[storeStart] != 0x89 || bytes[storeStart + 1] != storeModRm)
            {
                return false;
            }

            var dispIndex = storeStart + 2;
            var disp = BitConverter.ToInt32(bytes, dispIndex);
            var insnLen = 6;
            targetRva = storeStart + insnLen + disp;
            if (targetRva < 0 || targetRva + 4 > bytes.Length)
            {
                return false;
            }

            storeDispOffset = dispIndex - i;
            return true;
        }

        for (var i = 0; i + 32 < bytes.Length; i++)
        {
            if (TryMatch(i, loadModRm: 0x8E, storeModRm: 0x0D, out var storeDispOffset, out var targetRva))
            {
                hits.Add((MatchRva: i, StoreDispOffset: storeDispOffset, TargetRva: targetRva));
            }
            if (TryMatch(i, loadModRm: 0x86, storeModRm: 0x05, out storeDispOffset, out targetRva))
            {
                hits.Add((MatchRva: i, StoreDispOffset: storeDispOffset, TargetRva: targetRva));
            }
        }

        if (hits.Count == 0)
        {
            _output.WriteLine("Planet-owner calibration: no mov[rsi+disp32] -> mov[rip+disp32] candidates found.");
            return;
        }

        var grouped = hits.GroupBy(x => x.TargetRva).OrderByDescending(x => x.Count()).Take(maxHits).ToArray();
        _output.WriteLine($"Planet-owner calibration: {hits.Count} hit(s), {grouped.Length} unique target(s) (top {grouped.Length}):");

        foreach (var group in grouped)
        {
            var targetRva = group.Key;
            var refs = group.Count();
            var value = BitConverter.ToInt32(bytes, targetRva);
            var example = group.First();
            var snippet = BytesToHex(bytes, example.MatchRva, 18);

            // Wildcard both disps: the load disp32 (planet struct offset) and the store disp32 (global var).
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
    }

    private void DumpTopRipRelativeFloatTargets(ModuleSnapshot snapshot, int top)
    {
        static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;
        static bool IsRex(byte b) => b is >= 0x40 and <= 0x4F;

        var bytes = snapshot.Bytes;
        var loadCounts = new Dictionary<int, int>();
        var storeCounts = new Dictionary<int, int>();
        var examples = new Dictionary<int, (int InsnRva, int DispOffset, int InsnLen, byte Op)>();

        void Record(int targetRva, int insnRva, int dispOffset, int insnLen, byte op)
        {
            var dict = op == 0x10 ? loadCounts : storeCounts;
            dict.TryGetValue(targetRva, out var c);
            dict[targetRva] = c + 1;

            if (!examples.ContainsKey(targetRva))
            {
                examples[targetRva] = (insnRva, dispOffset, insnLen, op);
            }
        }

        for (var i = 0; i + 12 < bytes.Length; i++)
        {
            if (bytes[i] != 0xF3)
            {
                continue;
            }

            // No REX: F3 0F <op> <modrm> <disp32>
            if (bytes[i + 1] == 0x0F && (bytes[i + 2] == 0x10 || bytes[i + 2] == 0x11))
            {
                var op = bytes[i + 2];
                var modrm = bytes[i + 3];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var disp = BitConverter.ToInt32(bytes, i + 4);
                var insnLen = 8;
                var targetRva = i + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                Record(targetRva, i, dispOffset: 4, insnLen, op);
                continue;
            }

            // REX: F3 <rex> 0F <op> <modrm> <disp32>
            if (IsRex(bytes[i + 1]) && bytes[i + 2] == 0x0F && (bytes[i + 3] == 0x10 || bytes[i + 3] == 0x11))
            {
                var op = bytes[i + 3];
                var modrm = bytes[i + 4];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var disp = BitConverter.ToInt32(bytes, i + 5);
                var insnLen = 9;
                var targetRva = i + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                Record(targetRva, i, dispOffset: 5, insnLen, op);
            }
        }

        if (loadCounts.Count == 0 && storeCounts.Count == 0)
        {
            _output.WriteLine("Float scan: no RIP-relative movss load/store targets found.");
            return;
        }

        _output.WriteLine($"Float scan: top {top} RIP-relative float LOAD targets (F3 0F 10):");
        foreach (var kv in loadCounts.OrderByDescending(x => x.Value).Take(top))
        {
            var targetRva = kv.Key;
            var refs = kv.Value;
            var value = BitConverter.ToSingle(bytes, targetRva);
            var example = examples[targetRva];
            var snippet = BytesToHex(bytes, example.InsnRva, 16);
            var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));
            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} insnRva=0x{example.InsnRva:X} exampleBytes={snippet}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }

        _output.WriteLine($"Float scan: top {top} RIP-relative float STORE targets (F3 0F 11):");
        foreach (var kv in storeCounts.OrderByDescending(x => x.Value).Take(top))
        {
            var targetRva = kv.Key;
            var refs = kv.Value;
            var value = BitConverter.ToSingle(bytes, targetRva);
            var example = examples[targetRva];
            var snippet = BytesToHex(bytes, example.InsnRva, 16);
            var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));
            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} insnRva=0x{example.InsnRva:X} exampleBytes={snippet}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
    }

    private void DumpTopRipRelativeFloatArithmeticTargets(ModuleSnapshot snapshot, int top)
    {
        static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;
        static bool IsRex(byte b) => b is >= 0x40 and <= 0x4F;

        static string OpName(byte op) => op switch
        {
            0x58 => "addss",
            0x59 => "mulss",
            0x5C => "subss",
            0x5E => "divss",
            0x5F => "maxss",
            0x5D => "minss",
            _ => $"op_{op:X2}"
        };

        var bytes = snapshot.Bytes;
        var counts = new Dictionary<int, int>();
        var examples = new Dictionary<int, (int InsnRva, int DispOffset, byte Op, string Bytes)>();

        void Record(int targetRva, int insnRva, int dispOffset, byte op)
        {
            counts.TryGetValue(targetRva, out var c);
            counts[targetRva] = c + 1;
            if (!examples.ContainsKey(targetRva))
            {
                examples[targetRva] = (insnRva, dispOffset, op, BytesToHex(bytes, insnRva, 16));
            }
        }

        for (var i = 0; i + 12 < bytes.Length; i++)
        {
            if (bytes[i] != 0xF3)
            {
                continue;
            }

            // No REX: F3 0F <op> <modrm> <disp32>
            if (bytes[i + 1] == 0x0F && bytes[i + 2] is 0x58 or 0x59 or 0x5C or 0x5D or 0x5E or 0x5F)
            {
                var op = bytes[i + 2];
                var modrm = bytes[i + 3];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var disp = BitConverter.ToInt32(bytes, i + 4);
                var insnLen = 8;
                var targetRva = i + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                Record(targetRva, i, dispOffset: 4, op);
                continue;
            }

            // REX: F3 <rex> 0F <op> <modrm> <disp32>
            if (IsRex(bytes[i + 1]) && bytes[i + 2] == 0x0F && bytes[i + 3] is 0x58 or 0x59 or 0x5C or 0x5D or 0x5E or 0x5F)
            {
                var op = bytes[i + 3];
                var modrm = bytes[i + 4];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var disp = BitConverter.ToInt32(bytes, i + 5);
                var insnLen = 9;
                var targetRva = i + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                Record(targetRva, i, dispOffset: 5, op);
            }
        }

        if (counts.Count == 0)
        {
            _output.WriteLine("Float arithmetic scan: no RIP-relative addss/mulss/subss/divss targets found.");
            return;
        }

        _output.WriteLine($"Float arithmetic scan: top {top} RIP-relative float targets (F3 0F 58/59/5C/5D/5E/5F):");
        foreach (var kv in counts.OrderByDescending(x => x.Value).Take(top))
        {
            var targetRva = kv.Key;
            var refs = kv.Value;
            var value = BitConverter.ToSingle(bytes, targetRva);
            var ex = examples[targetRva];
            var suggested = BytesToAobPatternWithWildcards(bytes, ex.InsnRva, 16, (ex.InsnRva + ex.DispOffset, 4));
            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} op={OpName(ex.Op)} insnRva=0x{ex.InsnRva:X} bytes={ex.Bytes}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={ex.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
    }

    private void DumpTopRipRelativeInt32StoreTargets(ModuleSnapshot snapshot, int top)
    {
        static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;
        static bool IsRex(byte b) => b is >= 0x40 and <= 0x4F;

        var bytes = snapshot.Bytes;
        var counts = new Dictionary<int, int>();
        var examples = new Dictionary<int, (int InsnRva, int DispOffset, int InsnLen, string Kind)>();

        void Record(int targetRva, int insnRva, int dispOffset, int insnLen, string kind)
        {
            counts.TryGetValue(targetRva, out var c);
            counts[targetRva] = c + 1;
            if (!examples.ContainsKey(targetRva))
            {
                examples[targetRva] = (insnRva, dispOffset, insnLen, kind);
            }
        }

        for (var i = 0; i + 10 < bytes.Length; i++)
        {
            // No REX store: 89 <modrm> <disp32>
            if (bytes[i] == 0x89 && IsRipRelativeModRm(bytes[i + 1]))
            {
                var disp = BitConverter.ToInt32(bytes, i + 2);
                var insnLen = 6;
                var targetRva = i + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                Record(targetRva, i, dispOffset: 2, insnLen, kind: "89 (no REX)");
                continue;
            }

            // REX store: <rex> 89 <modrm> <disp32>
            if (IsRex(bytes[i]) && bytes[i + 1] == 0x89 && IsRipRelativeModRm(bytes[i + 2]))
            {
                var disp = BitConverter.ToInt32(bytes, i + 3);
                var insnLen = 7;
                var targetRva = i + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                Record(targetRva, i, dispOffset: 3, insnLen, kind: $"{bytes[i]:X2} 89 (REX)");
            }
        }

        if (counts.Count == 0)
        {
            _output.WriteLine("Int32 store scan: no RIP-relative 89 store targets found.");
            return;
        }

        _output.WriteLine($"Int32 store scan: top {top} RIP-relative Int32 STORE targets (89 [rip+disp32], reg):");
        foreach (var kv in counts.OrderByDescending(x => x.Value).Take(top))
        {
            var targetRva = kv.Key;
            var refs = kv.Value;
            var value = BitConverter.ToInt32(bytes, targetRva);
            var example = examples[targetRva];
            var snippet = BytesToHex(bytes, example.InsnRva, 16);
            var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));
            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} kind={example.Kind} exampleBytes={snippet}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
    }

    private void DumpTopRipRelativeInt32Targets32BitOnly(ModuleSnapshot snapshot, int top)
    {
        static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;
        static bool IsRexNoW(byte b) => b is >= 0x40 and <= 0x47; // W bit clear

        var bytes = snapshot.Bytes;
        var loadCounts = new Dictionary<int, int>();
        var storeCounts = new Dictionary<int, int>();
        var examples = new Dictionary<(int TargetRva, string Kind), (int InsnRva, int DispOffset, int InsnLen, string Bytes)>();

        void Record(Dictionary<int, int> dict, int targetRva, string kind, int insnRva, int dispOffset, int insnLen)
        {
            dict.TryGetValue(targetRva, out var c);
            dict[targetRva] = c + 1;

            var key = (targetRva, kind);
            if (!examples.ContainsKey(key))
            {
                examples[key] = (insnRva, dispOffset, insnLen, BytesToHex(bytes, insnRva, 16));
            }
        }

        for (var i = 0; i + 10 < bytes.Length; i++)
        {
            // 32-bit load: 8B <modrm> <disp32>
            if (bytes[i] == 0x8B && IsRipRelativeModRm(bytes[i + 1]))
            {
                var disp = BitConverter.ToInt32(bytes, i + 2);
                var insnLen = 6;
                var targetRva = i + insnLen + disp;
                if (targetRva >= 0 && targetRva + 4 <= bytes.Length)
                {
                    Record(loadCounts, targetRva, "8B", i, dispOffset: 2, insnLen);
                }
                continue;
            }

            // 32-bit store: 89 <modrm> <disp32>
            if (bytes[i] == 0x89 && IsRipRelativeModRm(bytes[i + 1]))
            {
                var disp = BitConverter.ToInt32(bytes, i + 2);
                var insnLen = 6;
                var targetRva = i + insnLen + disp;
                if (targetRva >= 0 && targetRva + 4 <= bytes.Length)
                {
                    Record(storeCounts, targetRva, "89", i, dispOffset: 2, insnLen);
                }
                continue;
            }

            // REX (W=0) + 8B/89 rip-relative
            if (IsRexNoW(bytes[i]) && (bytes[i + 1] == 0x8B || bytes[i + 1] == 0x89) && IsRipRelativeModRm(bytes[i + 2]))
            {
                var op = bytes[i + 1];
                var disp = BitConverter.ToInt32(bytes, i + 3);
                var insnLen = 7;
                var targetRva = i + insnLen + disp;
                if (targetRva < 0 || targetRva + 4 > bytes.Length)
                {
                    continue;
                }

                if (op == 0x8B)
                {
                    Record(loadCounts, targetRva, $"{bytes[i]:X2} 8B", i, dispOffset: 3, insnLen);
                }
                else
                {
                    Record(storeCounts, targetRva, $"{bytes[i]:X2} 89", i, dispOffset: 3, insnLen);
                }
            }
        }

        _output.WriteLine($"Int32 scan (32-bit only): top {top} RIP-relative LOAD targets (8B [rip+disp32])");
        foreach (var kv in loadCounts.OrderByDescending(x => x.Value).Take(top))
        {
            var targetRva = kv.Key;
            var refs = kv.Value;
            var value = BitConverter.ToInt32(bytes, targetRva);

            // Prefer the simplest kind for examples.
            var kind = examples.Keys.Where(k => k.TargetRva == targetRva).Select(k => k.Kind).OrderBy(k => k.Length).FirstOrDefault() ?? "8B";
            var example = examples[(targetRva, kind)];
            var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));

            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} kind={kind} insnRva=0x{example.InsnRva:X} bytes={example.Bytes}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }

        _output.WriteLine($"Int32 scan (32-bit only): top {top} RIP-relative STORE targets (89 [rip+disp32], r32)");
        foreach (var kv in storeCounts.OrderByDescending(x => x.Value).Take(top))
        {
            var targetRva = kv.Key;
            var refs = kv.Value;
            var value = BitConverter.ToInt32(bytes, targetRva);

            var kind = examples.Keys.Where(k => k.TargetRva == targetRva).Select(k => k.Kind).OrderBy(k => k.Length).FirstOrDefault() ?? "89";
            var example = examples[(targetRva, kind)];
            var suggested = BytesToAobPatternWithWildcards(bytes, example.InsnRva, 16, (example.InsnRva + example.DispOffset, 4));

            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} kind={kind} insnRva=0x{example.InsnRva:X} bytes={example.Bytes}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={example.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
    }

    private void DumpTopRipRelativeByteCompareTargets(ModuleSnapshot snapshot, int top)
    {
        var bytes = snapshot.Bytes;

        // cmp byte ptr [rip+disp32], imm8
        //   80 3D <disp32> <imm8>
        // Followed by common short jcc, e.g. 74/75 <rel8>.
        var counts = new Dictionary<int, int>();
        var examples = new Dictionary<int, (int InsnRva, int DispOffset, int Rel8Offset, byte Imm8, byte Jcc, string Bytes)>();

        for (var i = 0; i + 9 < bytes.Length; i++)
        {
            if (bytes[i] != 0x80 || bytes[i + 1] != 0x3D)
            {
                continue;
            }

            var disp = BitConverter.ToInt32(bytes, i + 2);
            var imm8 = bytes[i + 6];
            var jcc = bytes[i + 7];

            // Keep output focused on the exact form our profiles use today.
            if (imm8 != 0x00 || (jcc != 0x74 && jcc != 0x75))
            {
                continue;
            }

            var insnLen = 7;
            var targetRva = i + insnLen + disp;
            if (targetRva < 0 || targetRva >= bytes.Length)
            {
                continue;
            }

            counts.TryGetValue(targetRva, out var c);
            counts[targetRva] = c + 1;

            if (!examples.ContainsKey(targetRva))
            {
                examples[targetRva] = (
                    InsnRva: i,
                    DispOffset: 2,
                    Rel8Offset: 8,
                    Imm8: imm8,
                    Jcc: jcc,
                    Bytes: BytesToHex(bytes, i, 16));
            }
        }

        if (counts.Count == 0)
        {
            _output.WriteLine("Byte compare scan: no RIP-relative cmp byte [rip+disp32],0 with short jcc found.");
            return;
        }

        _output.WriteLine($"Byte compare scan: top {top} RIP-relative byte CMP targets (80 3D disp32 00 74/75):");
        foreach (var kv in counts.OrderByDescending(x => x.Value).Take(top))
        {
            var targetRva = kv.Key;
            var refs = kv.Value;
            var value = bytes[targetRva];

            var ex = examples[targetRva];
            var suggested = BytesToAobPatternWithWildcards(
                bytes,
                ex.InsnRva,
                16,
                (ex.InsnRva + ex.DispOffset, 4), // disp32
                (ex.InsnRva + ex.Rel8Offset, 1)); // jcc rel8

            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} jcc={ex.Jcc:X2} insnRva=0x{ex.InsnRva:X} bytes={ex.Bytes}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={ex.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
    }

    private void DumpTopRipRelativeByteImmediateStoreTargets(ModuleSnapshot snapshot, int top)
    {
        var bytes = snapshot.Bytes;

        // mov byte ptr [rip+disp32], imm8
        //   C6 05 <disp32> <imm8>
        var counts = new Dictionary<int, int>();
        var examples = new Dictionary<int, (int InsnRva, int DispOffset, byte Imm8, string Bytes)>();

        for (var i = 0; i + 8 < bytes.Length; i++)
        {
            if (bytes[i] != 0xC6 || bytes[i + 1] != 0x05)
            {
                continue;
            }

            var disp = BitConverter.ToInt32(bytes, i + 2);
            var imm8 = bytes[i + 6];
            var insnLen = 7;
            var targetRva = i + insnLen + disp;
            if (targetRva < 0 || targetRva >= bytes.Length)
            {
                continue;
            }

            counts.TryGetValue(targetRva, out var c);
            counts[targetRva] = c + 1;

            if (!examples.ContainsKey(targetRva))
            {
                examples[targetRva] = (
                    InsnRva: i,
                    DispOffset: 2,
                    Imm8: imm8,
                    Bytes: BytesToHex(bytes, i, 16));
            }
        }

        if (counts.Count == 0)
        {
            _output.WriteLine("Byte store scan: no RIP-relative mov byte [rip+disp32], imm8 found.");
            return;
        }

        _output.WriteLine($"Byte store scan: top {top} RIP-relative byte STORE targets (C6 05 disp32 imm8):");
        foreach (var kv in counts.OrderByDescending(x => x.Value).Take(top))
        {
            var targetRva = kv.Key;
            var refs = kv.Value;
            var value = bytes[targetRva];

            var ex = examples[targetRva];
            var suggested = BytesToAobPatternWithWildcards(
                bytes,
                ex.InsnRva,
                16,
                (ex.InsnRva + ex.DispOffset, 4)); // disp32

            _output.WriteLine($" - targetRva=0x{targetRva:X} value={value} refs={refs} imm8={ex.Imm8:X2} insnRva=0x{ex.InsnRva:X} bytes={ex.Bytes}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={ex.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
    }

    private void DumpRipRelativeInt32Refs(ModuleSnapshot snapshot, long targetRva, int max)
    {
        static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;

        var refs = new List<(int InsnRva, int InsnLen, int DispOffset, string Kind)>();
        var bytes = snapshot.Bytes;

        for (var i = 0; i + 7 < bytes.Length; i++)
        {
            // 8B / 89 RIP-relative (no REX)
            var op = bytes[i];
            if ((op == 0x8B || op == 0x89) && IsRipRelativeModRm(bytes[i + 1]))
            {
                var disp = BitConverter.ToInt32(bytes, i + 2);
                var insnLen = 6;
                var decodedTarget = i + insnLen + disp;
                if (decodedTarget == targetRva)
                {
                    refs.Add((i, insnLen, DispOffset: 2, Kind: $"{op:X2} (no REX)"));
                }
                continue;
            }

            // REX + 8B/89 RIP-relative
            var rex = bytes[i];
            if (rex is >= 0x40 and <= 0x4F)
            {
                op = bytes[i + 1];
                if ((op == 0x8B || op == 0x89) && IsRipRelativeModRm(bytes[i + 2]))
                {
                    var disp = BitConverter.ToInt32(bytes, i + 3);
                    var insnLen = 7;
                    var decodedTarget = i + insnLen + disp;
                    if (decodedTarget == targetRva)
                    {
                        refs.Add((i, insnLen, DispOffset: 3, Kind: $"{rex:X2} {op:X2} (REX)"));
                    }
                }
            }
        }

        if (refs.Count == 0)
        {
            _output.WriteLine(" - No 8B/89 RIP-relative refs found.");
            return;
        }

        _output.WriteLine($" - Found {refs.Count} RIP-relative Int32 ref(s) (showing up to {max}):");
        foreach (var r in refs.Take(max))
        {
            var snippet = BytesToHex(bytes, r.InsnRva, 18);
            var suggested = BytesToAobPattern(bytes, r.InsnRva, 18, wildcardStart: r.InsnRva + r.DispOffset, wildcardCount: 4);
            _output.WriteLine($"   insnRva=0x{r.InsnRva:X} kind={r.Kind} bytes={snippet}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={r.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
    }

    private void DumpRipRelativeSseFloatRefs(ModuleSnapshot snapshot, long targetRva, int max)
    {
        static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;

        var refs = new List<(int InsnRva, int InsnLen, int DispOffset, string Kind)>();
        var bytes = snapshot.Bytes;

        // Scan for common SSE single-precision ops that use disp32 RIP-relative:
        // - F3 0F 10 /r (movss load)
        // - F3 0F 11 /r (movss store)
        // - F3 0F 59 /r (mulss)
        // Some encodings include a REX prefix between F3 and 0F.
        for (var i = 0; i + 10 < bytes.Length; i++)
        {
            if (bytes[i] != 0xF3)
            {
                continue;
            }

            // No REX form: F3 0F <op> <modrm> <disp32>
            if (bytes[i + 1] == 0x0F && (bytes[i + 2] == 0x10 || bytes[i + 2] == 0x11 || bytes[i + 2] == 0x59))
            {
                var op = bytes[i + 2];
                var modrm = bytes[i + 3];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var disp = BitConverter.ToInt32(bytes, i + 4);
                var insnLen = 8;
                var decodedTarget = i + insnLen + disp;
                if (decodedTarget == targetRva)
                {
                    refs.Add((i, insnLen, DispOffset: 4, Kind: $"F3 0F {op:X2} (no REX)"));
                }

                continue;
            }

            // REX form: F3 <rex> 0F <op> <modrm> <disp32>
            var rex = bytes[i + 1];
            if (rex is >= 0x40 and <= 0x4F && bytes[i + 2] == 0x0F && (bytes[i + 3] == 0x10 || bytes[i + 3] == 0x11 || bytes[i + 3] == 0x59))
            {
                var op = bytes[i + 3];
                var modrm = bytes[i + 4];
                if (!IsRipRelativeModRm(modrm))
                {
                    continue;
                }

                var disp = BitConverter.ToInt32(bytes, i + 5);
                var insnLen = 9;
                var decodedTarget = i + insnLen + disp;
                if (decodedTarget == targetRva)
                {
                    refs.Add((i, insnLen, DispOffset: 5, Kind: $"F3 {rex:X2} 0F {op:X2} (REX)"));
                }
            }
        }

        if (refs.Count == 0)
        {
            _output.WriteLine(" - No F3 0F 10/11/59 RIP-relative float refs found.");
            return;
        }

        _output.WriteLine($" - Found {refs.Count} RIP-relative float ref(s) (showing up to {max}):");
        foreach (var r in refs.Take(max))
        {
            var snippet = BytesToHex(bytes, r.InsnRva, 20);
            var suggested = BytesToAobPattern(bytes, r.InsnRva, 20, wildcardStart: r.InsnRva + r.DispOffset, wildcardCount: 4);
            _output.WriteLine($"   insnRva=0x{r.InsnRva:X} kind={r.Kind} bytes={snippet}");
            _output.WriteLine($"   suggestedPattern={suggested} offset={r.DispOffset} addressMode=ReadRipRelative32AtOffset");
        }
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
        var moduleSize = module.ModuleMemorySize;

        using var accessor = new ProcessMemoryAccessor(process.Id);
        var moduleBytes = accessor.ReadBytes(baseAddress, moduleSize);

        var counts = new Dictionary<long, int>();
        var exampleRefs = new Dictionary<long, List<string>>();

        static bool IsRipRelativeModRm(byte modrm) => (modrm & 0xC7) == 0x05;

        static string BytesToHex(byte[] bytes, int offset, int count)
        {
            var end = Math.Min(offset + count, bytes.Length);
            return string.Join(' ', bytes.AsSpan(offset, end - offset).ToArray().Select(x => x.ToString("X2")));
        }

        for (var i = 0; i + 6 < moduleBytes.Length; i++)
        {
            var op = moduleBytes[i];
            var modrm = moduleBytes[i + 1];
            if ((op == 0x8B || op == 0x89) && IsRipRelativeModRm(modrm))
            {
                var disp = BitConverter.ToInt32(moduleBytes, i + 2);
                var targetRva = i + 6 + disp;
                if (targetRva < 0 || targetRva + 4 > moduleBytes.Length)
                {
                    continue;
                }

                counts.TryGetValue(targetRva, out var current);
                counts[targetRva] = current + 1;

                if (!exampleRefs.TryGetValue(targetRva, out var list))
                {
                    list = new List<string>();
                    exampleRefs[targetRva] = list;
                }

                if (list.Count < 2)
                {
                    var snippet = BytesToHex(moduleBytes, i, 16);
                    list.Add($"insnRva=0x{i:X} op={(op == 0x8B ? "8B" : "89")} modrm=0x{modrm:X2} bytes={snippet}");
                }
            }
        }

        if (counts.Count == 0)
        {
            _output.WriteLine("No RIP-relative Int32 targets found in module scan.");
            return;
        }

        _output.WriteLine($"Top {top} RIP-relative Int32 targets by reference count (8B/89 rip-relative):");
        foreach (var kv in counts.OrderByDescending(x => x.Value).Take(top))
        {
            var addr = baseAddress + (nint)kv.Key;
            int? value = null;
            try
            {
                value = accessor.Read<int>(addr);
            }
            catch
            {
                // ignore
            }

            if (value.HasValue && value.Value is >= -1000 and <= 50_000_000)
            {
                _output.WriteLine($" - targetRva=0x{kv.Key:X} addr=0x{addr.ToInt64():X} refs={kv.Value} value={value.Value}");
            }
            else
            {
                _output.WriteLine($" - targetRva=0x{kv.Key:X} addr=0x{addr.ToInt64():X} refs={kv.Value} value={(value.HasValue ? value.Value.ToString() : "<unreadable>")}");
            }

            if (exampleRefs.TryGetValue(kv.Key, out var refs))
            {
                foreach (var reference in refs)
                {
                    _output.WriteLine($"   {reference}");
                }
            }
        }
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
