using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Scanning;

namespace SwfocTrainer.Runtime.Services;

public sealed class SignatureResolver : ISignatureResolver
{
    private readonly ILogger<SignatureResolver> _logger;

    public SignatureResolver(ILogger<SignatureResolver> logger)
    {
        _logger = logger;
    }

    public Task<SymbolMap> ResolveAsync(
        ProfileBuild profileBuild,
        IReadOnlyList<SignatureSet> signatureSets,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        CancellationToken cancellationToken = default)
    {
        // Signature scanning is CPU-bound and can take noticeable time. Run off the UI thread.
        return Task.Run(() => ResolveInternal(profileBuild, signatureSets, fallbackOffsets, cancellationToken), cancellationToken);
    }

    private SymbolMap ResolveInternal(
        ProfileBuild profileBuild,
        IReadOnlyList<SignatureSet> signatureSets,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        CancellationToken cancellationToken)
    {
        var process = TryGetProcess(profileBuild);
        if (process is null)
        {
            throw new InvalidOperationException($"Could not find running process for profile '{profileBuild.ProfileId}' (exe='{profileBuild.ExecutablePath}', pid={profileBuild.ProcessId}).");
        }

        var module = process.MainModule ?? throw new InvalidOperationException("Main module not available.");
        var baseAddress = module.BaseAddress;
        var moduleSize = module.ModuleMemorySize;

        using var accessor = new ProcessMemoryAccessor(process.Id);
        var moduleBytes = accessor.ReadBytes(baseAddress, moduleSize);

        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var signatureSet in signatureSets)
        {
            foreach (var sig in signatureSet.Signatures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pattern = AobPattern.Parse(sig.Pattern);
                var hit = AobScanner.FindPattern(process, moduleBytes, baseAddress, pattern);
                if (hit != nint.Zero)
                {
                    if (TryResolveAddress(sig, hit, baseAddress, moduleBytes, out var address, out var diagnostics))
                    {
                        symbols[sig.Name] = new SymbolInfo(
                            sig.Name,
                            address,
                            sig.ValueType,
                            AddressSource.Signature,
                            $"{signatureSet.Name}:{sig.Pattern} [{sig.AddressMode}]",
                            Confidence: 0.95d,
                            HealthStatus: SymbolHealthStatus.Healthy,
                            HealthReason: "signature_resolved",
                            LastValidatedAt: DateTimeOffset.UtcNow);
                    }
                    else if (fallbackOffsets.TryGetValue(sig.Name, out var fallback))
                    {
                        if (TryApplyFallback(accessor, baseAddress, sig.Name, sig.ValueType, fallback, symbols,
                                diagnostics ?? "Fallback after address resolution failure"))
                        {
                            _logger.LogWarning(
                                "Signature address resolution failed for {Symbol}; fallback offset applied (0x{Offset:X}). Details: {Diagnostics}",
                                sig.Name, fallback, diagnostics ?? "<none>");
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Signature address resolution failed for {Symbol} and fallback offset 0x{Offset:X} is not readable. Details: {Diagnostics}",
                                sig.Name, fallback, diagnostics ?? "<none>");
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Signature address resolution failed for {Symbol} and no fallback offset available. Details: {Diagnostics}",
                            sig.Name,
                            diagnostics ?? "<none>");
                    }
                }
                else if (fallbackOffsets.TryGetValue(sig.Name, out var fallback) && !symbols.ContainsKey(sig.Name))
                {
                    if (TryApplyFallback(accessor, baseAddress, sig.Name, sig.ValueType, fallback, symbols, "Fallback offset"))
                    {
                        _logger.LogWarning("Signature miss for {Symbol}; fallback offset applied (0x{Offset:X})", sig.Name, fallback);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Signature miss for {Symbol}; fallback offset 0x{Offset:X} is not readable",
                            sig.Name, fallback);
                    }
                }
                else if (hit == nint.Zero)
                {
                    _logger.LogWarning("Signature miss for {Symbol} and no fallback offset available", sig.Name);
                }
            }
        }

        foreach (var fallback in fallbackOffsets)
        {
            if (symbols.ContainsKey(fallback.Key))
            {
                continue;
            }

            if (!TryApplyFallback(accessor, baseAddress, fallback.Key, SymbolValueType.Int32, fallback.Value, symbols, "Standalone fallback"))
            {
                _logger.LogWarning(
                    "Standalone fallback for {Symbol} skipped: offset 0x{Offset:X} is not readable",
                    fallback.Key, fallback.Value);
            }
        }

        return new SymbolMap(symbols);
    }

    private static Process? TryGetProcess(ProfileBuild profileBuild)
    {
        // Prefer PID from the process locator so we never attach to the wrong instance.
        if (profileBuild.ProcessId != 0)
        {
            try
            {
                return Process.GetProcessById(profileBuild.ProcessId);
            }
            catch
            {
                // fall back to name search below
            }
        }

        var name = Path.GetFileNameWithoutExtension(profileBuild.ExecutablePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Process.GetProcessesByName(name).FirstOrDefault();
    }

    /// <summary>
    /// Validates a fallback offset by attempting a test read at <c>baseAddress + offset</c>.
    /// Profile fallback offsets are author-curated, so rather than guessing module bounds we
    /// simply ask the OS whether the address is readable. If it is, the symbol is registered.
    /// </summary>
    private bool TryApplyFallback(
        ProcessMemoryAccessor accessor,
        nint baseAddress,
        string symbolName,
        SymbolValueType valueType,
        long offset,
        Dictionary<string, SymbolInfo> symbols,
        string diagnosticsText)
    {
        if (offset <= 0) return false;

        var address = baseAddress + (nint)offset;
        try
        {
            accessor.Read<int>(address); // test read — throws if not readable
            symbols[symbolName] = new SymbolInfo(
                symbolName,
                address,
                valueType,
                AddressSource.Fallback,
                diagnosticsText,
                Confidence: 0.65d,
                HealthStatus: SymbolHealthStatus.Degraded,
                HealthReason: "fallback_offset",
                LastValidatedAt: DateTimeOffset.UtcNow);
            return true;
        }
        catch
        {
            _logger.LogDebug("Fallback test-read failed for {Symbol} at 0x{Address:X} (offset 0x{Offset:X})",
                symbolName, address.ToInt64(), offset);
            return false;
        }
    }

    private static bool TryResolveAddress(
        SignatureSpec signature,
        nint hitAddress,
        nint baseAddress,
        byte[] moduleBytes,
        out nint resolvedAddress,
        out string? diagnostics)
    {
        resolvedAddress = nint.Zero;
        diagnostics = null;

        var hitOffset = hitAddress.ToInt64() - baseAddress.ToInt64();
        var valueOffset = hitOffset + signature.Offset;
        if (valueOffset < 0 || valueOffset > int.MaxValue)
        {
            diagnostics = $"Computed value offset out of range for {signature.Name}: {valueOffset}";
            return false;
        }

        var index = (int)valueOffset;

        if (signature.AddressMode == SignatureAddressMode.HitPlusOffset)
        {
            resolvedAddress = hitAddress + signature.Offset;
            return true;
        }

        if (signature.AddressMode == SignatureAddressMode.ReadAbsolute32AtOffset)
        {
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

        if (signature.AddressMode == SignatureAddressMode.ReadRipRelative32AtOffset)
        {
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

        diagnostics = $"Unsupported signature address mode '{signature.AddressMode}'";
        return false;
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
}
