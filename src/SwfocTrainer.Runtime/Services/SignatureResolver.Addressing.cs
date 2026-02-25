using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Scanning;

namespace SwfocTrainer.Runtime.Services;

internal static class SignatureResolverAddressing
{
    internal static bool TryResolveAddress(
        SignatureSpec signature,
        nint hitAddress,
        nint baseAddress,
        byte[] moduleBytes,
        out nint resolvedAddress,
        out string? diagnostics)
    {
        resolvedAddress = nint.Zero;
        diagnostics = null;

        if (!TryComputeValueIndex(signature, hitAddress, baseAddress, out var index, out diagnostics))
        {
            return false;
        }

        switch (signature.AddressMode)
        {
            case SignatureAddressMode.HitPlusOffset:
                resolvedAddress = hitAddress + signature.Offset;
                return true;
            case SignatureAddressMode.ReadAbsolute32AtOffset:
                return TryResolveAbsolute32(signature, index, moduleBytes, out resolvedAddress, out diagnostics);
            case SignatureAddressMode.ReadRipRelative32AtOffset:
                return TryResolveRipRelative32(signature, hitAddress, index, moduleBytes, out resolvedAddress, out diagnostics);
            default:
                diagnostics = $"Unsupported signature address mode '{signature.AddressMode}'";
                return false;
        }
    }

    private static int GuessRipImmediateLength(string pattern)
    {
        // Very small heuristic for common patterns used by profiles.
        // - "80 3D ?? ?? ?? ?? 00" => cmp byte ptr [rip+disp32], imm8 (1 byte immediate)
        // - "C6 05 ?? ?? ?? ?? 01" => mov byte ptr [rip+disp32], imm8 (1 byte immediate)
        // Everything else defaults to 0 (disp32 at end of instruction).
        var parsed = AobPattern.Parse(pattern).Bytes;
        if (parsed.Length >= 2 && parsed[0] == 0x80 && parsed[1] == 0x3D)
        {
            return 1;
        }

        if (parsed.Length >= 2 && parsed[0] == 0xC6 && parsed[1] == 0x05)
        {
            return 1;
        }

        return 0;
    }

    private static bool TryComputeValueIndex(
        SignatureSpec signature,
        nint hitAddress,
        nint baseAddress,
        out int index,
        out string? diagnostics)
    {
        index = 0;
        diagnostics = null;
        var hitOffset = hitAddress.ToInt64() - baseAddress.ToInt64();
        var valueOffset = hitOffset + signature.Offset;
        if (valueOffset < 0 || valueOffset > int.MaxValue)
        {
            diagnostics = $"Computed value offset out of range for {signature.Name}: {valueOffset}";
            return false;
        }

        index = (int)valueOffset;
        return true;
    }

    private static bool TryResolveAbsolute32(
        SignatureSpec signature,
        int index,
        byte[] moduleBytes,
        out nint resolvedAddress,
        out string? diagnostics)
    {
        resolvedAddress = nint.Zero;
        diagnostics = null;
        if (index + sizeof(uint) > moduleBytes.Length)
        {
            diagnostics = $"Not enough bytes to decode absolute 32-bit address at index {index} for {signature.Name}";
            return false;
        }

        var rawAddress = BitConverter.ToUInt32(moduleBytes, index);
        if (rawAddress == 0)
        {
            diagnostics = $"Decoded null absolute address for {signature.Name}";
            return false;
        }

        resolvedAddress = (nint)(long)rawAddress;
        return true;
    }

    private static bool TryResolveRipRelative32(
        SignatureSpec signature,
        nint hitAddress,
        int index,
        byte[] moduleBytes,
        out nint resolvedAddress,
        out string? diagnostics)
    {
        resolvedAddress = nint.Zero;
        diagnostics = null;
        if (index + sizeof(int) > moduleBytes.Length)
        {
            diagnostics = $"Not enough bytes to decode RIP-relative disp32 at index {index} for {signature.Name}";
            return false;
        }

        // In x86-64, many globals are accessed as [RIP + disp32], where disp32 is relative to the *end of the instruction*.
        // We don't fully decode x86 here, but for the opcodes we use in profiles, the end-of-disp is a good base.
        var disp32 = BitConverter.ToInt32(moduleBytes, index);
        var endOfDisp = hitAddress + signature.Offset + sizeof(int);

        // Some opcodes include an imm8 after the disp32 (e.g., cmp [rip+disp32], imm8; mov byte [rip+disp32], imm8).
        // Adjust by 1 byte for those known forms so we land at end-of-instruction.
        var extra = GuessRipImmediateLength(signature.Pattern);
        resolvedAddress = endOfDisp + extra + disp32;
        return true;
    }
}
