#pragma warning disable S1481
#pragma warning disable S3267
#pragma warning disable S3459
#pragma warning disable S3776

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Scanning;

namespace SwfocTrainer.Runtime.Services;

public sealed class SignatureResolver : ISignatureResolver
{
    private const string GhidraSymbolPackRootOverrideEnv = "SWFOC_GHIDRA_SYMBOL_PACK_ROOT";

    private readonly ILogger<SignatureResolver> _logger;
    private readonly string _ghidraSymbolPackRoot;

    public SignatureResolver(ILogger<SignatureResolver> logger)
        : this(logger, ResolveDefaultGhidraSymbolPackRoot())
    {
    }

    public SignatureResolver(ILogger<SignatureResolver> logger, string ghidraSymbolPackRoot)
    {
        _logger = logger;
        _ghidraSymbolPackRoot = ghidraSymbolPackRoot;
    }

    public Task<SymbolMap> ResolveAsync(
        ProfileBuild profileBuild,
        IReadOnlyList<SignatureSet> signatureSets,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        CancellationToken cancellationToken)
    {
        // Signature scanning is CPU-bound and can take noticeable time. Run off the UI thread.
        return Task.Run(() => ResolveInternal(profileBuild, signatureSets, fallbackOffsets, cancellationToken), cancellationToken);
    }

    public Task<SymbolMap> ResolveAsync(
        ProfileBuild profileBuild,
        IReadOnlyList<SignatureSet> signatureSets,
        IReadOnlyDictionary<string, long> fallbackOffsets)
    {
        return ResolveAsync(profileBuild, signatureSets, fallbackOffsets, CancellationToken.None);
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
        TryHydrateSymbolsFromGhidraPack(module, signatureSets, symbols);

        foreach (var signatureSet in signatureSets)
        {
            foreach (var sig in signatureSet.Signatures)
            {
                if (symbols.ContainsKey(sig.Name))
                {
                    continue;
                }

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

    private void TryHydrateSymbolsFromGhidraPack(
        ProcessModule module,
        IReadOnlyList<SignatureSet> signatureSets,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (string.IsNullOrWhiteSpace(_ghidraSymbolPackRoot))
        {
            return;
        }

        if (!TryBuildFingerprintId(module, out var fingerprintId, out _))
        {
            return;
        }

        var packPath = Path.Combine(_ghidraSymbolPackRoot, $"{fingerprintId}.json");
        if (!File.Exists(packPath))
        {
            return;
        }

        try
        {
            var pack = JsonSerializer.Deserialize<GhidraSymbolPackDto>(File.ReadAllText(packPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (pack is null || pack.BinaryFingerprint is null)
            {
                return;
            }

            if (!string.Equals(pack.BinaryFingerprint.FingerprintId, fingerprintId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Ignoring ghidra symbol pack {Path}: fingerprint mismatch (expected {Expected}, actual {Actual})",
                    packPath, fingerprintId, pack.BinaryFingerprint.FingerprintId);
                return;
            }

            var valueTypes = BuildSymbolValueTypeIndex(signatureSets);
            foreach (var anchor in pack.Anchors ?? new List<GhidraAnchorDto>())
            {
                if (string.IsNullOrWhiteSpace(anchor.Id))
                {
                    continue;
                }

                if (!TryParseAddress(anchor.Address, out var address))
                {
                    continue;
                }

                if (symbols.ContainsKey(anchor.Id))
                {
                    continue;
                }

                var valueType = valueTypes.TryGetValue(anchor.Id, out var resolvedType)
                    ? resolvedType
                    : SymbolValueType.Int32;

                symbols[anchor.Id] = new SymbolInfo(
                    anchor.Id,
                    (nint)address,
                    valueType,
                    AddressSource.Signature,
                    $"ghidra_symbol_pack:{fingerprintId}",
                    Confidence: anchor.Confidence > 0 ? anchor.Confidence : 0.99d,
                    HealthStatus: SymbolHealthStatus.Healthy,
                    HealthReason: "ghidra_symbol_pack",
                    LastValidatedAt: DateTimeOffset.UtcNow);
            }

            _logger.LogInformation(
                "Loaded {Count} symbol(s) from ghidra pack {Path} for fingerprint {FingerprintId}",
                symbols.Count, packPath, fingerprintId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to consume ghidra symbol pack at {Path}", packPath);
        }
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
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Fallback test-read failed for {Symbol} at 0x{Address:X} (offset 0x{Offset:X})",
                symbolName,
                address.ToInt64(),
                offset);
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
            if (index > moduleBytes.Length - sizeof(uint))
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
            if (index > moduleBytes.Length - sizeof(int))
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

    private static bool TryBuildFingerprintId(ProcessModule module, out string fingerprintId, out string moduleName)
    {
        fingerprintId = string.Empty;
        moduleName = Path.GetFileName(module.FileName ?? module.ModuleName ?? "module");
        if (string.IsNullOrWhiteSpace(module.FileName) || !File.Exists(module.FileName))
        {
            return false;
        }

        using var stream = File.OpenRead(module.FileName);
        var hash = SHA256.HashData(stream);
        var sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        var normalizedModule = Path.GetFileNameWithoutExtension(moduleName)
            .ToLowerInvariant()
            .Replace(' ', '_');
        var hashPrefix = sha256[..16];
        fingerprintId = $"{normalizedModule}_{hashPrefix}";
        return true;
    }

    private static IReadOnlyDictionary<string, SymbolValueType> BuildSymbolValueTypeIndex(IReadOnlyList<SignatureSet> signatureSets)
    {
        var index = new Dictionary<string, SymbolValueType>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in signatureSets)
        {
            foreach (var signature in set.Signatures)
            {
                if (!index.ContainsKey(signature.Name))
                {
                    index[signature.Name] = signature.ValueType;
                }
            }
        }

        return index;
    }

    private static bool TryParseAddress(object? value, out long address)
    {
        address = 0;
        if (value is null)
        {
            return false;
        }

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
            {
                address = number;
                return true;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                return TryParseAddress(element.GetString(), out address);
            }
        }

        if (value is long int64)
        {
            address = int64;
            return true;
        }

        if (value is int int32)
        {
            address = int32;
            return true;
        }

        if (value is string str)
        {
            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(str.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hex))
            {
                address = hex;
                return true;
            }

            if (long.TryParse(str, out var parsed))
            {
                address = parsed;
                return true;
            }
        }

        return false;
    }

    private static string ResolveDefaultGhidraSymbolPackRoot()
    {
        var envOverride = Environment.GetEnvironmentVariable(GhidraSymbolPackRootOverrideEnv);
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return envOverride;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "profiles", "default", "sdk", "ghidra", "symbol-packs"));
    }

    private sealed class GhidraSymbolPackDto
    {
        public string? SchemaVersion { get; set; }

        public GhidraFingerprintDto? BinaryFingerprint { get; set; }

        public List<GhidraAnchorDto>? Anchors { get; set; }
    }

    private sealed class GhidraFingerprintDto
    {
        public string? FingerprintId { get; set; }
    }

    private sealed class GhidraAnchorDto
    {
        public string Id { get; set; } = string.Empty;

        public object? Address { get; set; }

        public double Confidence { get; set; }
    }
}
