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

public sealed partial class LiveActionSmokeTests
{
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
            if (TryDecodeInstantBuildNoRex(bytes, i, out var hit))
            {
                AddRipTargetHit(hitsByTarget, hit);
                continue;
            }

            if (TryDecodeInstantBuildRex(bytes, i, out hit))
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

            if (TryDecodeSelectedHpStoreNoRex(bytes, i, out var hit))
            {
                hits.Add(hit);
                continue;
            }

            if (TryDecodeSelectedHpStoreRex(bytes, i, out hit))
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

}
