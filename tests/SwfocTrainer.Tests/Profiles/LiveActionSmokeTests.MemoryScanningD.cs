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

        if (!TryResolveRipTarget(bytes, index, index + 2, insnLen: 6, valueSize: 4, out var target))
        {
            return false;
        }

        targetRva = target;
        return targetRva >= 0;
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
