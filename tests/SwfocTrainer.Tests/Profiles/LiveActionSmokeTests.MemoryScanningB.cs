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
            if (TryDecodePlanetOwnerCandidate(bytes, i, loadModRm: 0x8E, storeModRm: 0x0D, out var hit))
            {
                hits.Add(hit);
                continue;
            }

            if (TryDecodePlanetOwnerCandidate(bytes, i, loadModRm: 0x86, storeModRm: 0x05, out hit))
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
            if (TryDecodeMovssNoRex(bytes, i, out var hit))
            {
                hits.Add(hit);
                continue;
            }

            if (TryDecodeMovssRex(bytes, i, out hit))
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
            if (TryDecodeFloatArithmeticNoRex(bytes, i, out var hit))
            {
                hits.Add(hit);
                continue;
            }

            if (TryDecodeFloatArithmeticRex(bytes, i, out hit))
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
            if (TryDecodeInt32StoreNoRex(bytes, i, out var hit))
            {
                hits.Add(hit);
                continue;
            }

            if (TryDecodeInt32StoreRex(bytes, i, out hit))
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
            if (TryDecodeInt32AccessNoRex(bytes, i, out var hit))
            {
                hits.Add(hit);
                continue;
            }

            if (TryDecodeInt32AccessRexNoW(bytes, i, out hit))
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

}
