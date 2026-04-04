using System.Diagnostics;

namespace SwfocTrainer.Runtime.Scanning;

internal static class AobScanner
{
    public static nint FindPattern(Process process, byte[] memory, nint baseAddress, AobPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(pattern);
        return FindPattern(memory, baseAddress, pattern);
    }

    public static nint FindPattern(byte[] memory, nint baseAddress, AobPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(pattern);
        var sig = pattern.Bytes;
        if (sig.Length == 0)
        {
            return nint.Zero;
        }

        var max = memory.Length - sig.Length;
        for (var i = 0; i <= max; i++)
        {
            var matched = true;
            for (var j = 0; j < sig.Length; j++)
            {
                var expected = sig[j];
                if (expected.HasValue && memory[i + j] != expected.Value)
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return baseAddress + i;
            }
        }

        return nint.Zero;
    }
}
