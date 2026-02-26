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
            if (TryDecodeInt32ReferenceNoRex(bytes, i, targetRva, out var reference))
            {
                refs.Add(reference);
                continue;
            }

            if (TryDecodeInt32ReferenceRex(bytes, i, targetRva, out reference))
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

        if (!TryResolveRipTarget(bytes, index, index + 2, insnLen: 6, valueSize: 4, out var decodedTarget))
        {
            return false;
        }

        if (decodedTarget != targetRva)
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

        if (!TryResolveRipTarget(bytes, index, index + 3, insnLen: 7, valueSize: 4, out var decodedTarget))
        {
            return false;
        }

        if (decodedTarget != targetRva)
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
            if (TryDecodeSseFloatReferenceNoRex(bytes, i, targetRva, out var reference))
            {
                refs.Add(reference);
                continue;
            }

            if (TryDecodeSseFloatReferenceRex(bytes, i, targetRva, out reference))
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

}
