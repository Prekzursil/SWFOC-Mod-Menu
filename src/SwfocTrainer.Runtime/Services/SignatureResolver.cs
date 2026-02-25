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
    private const string ArtifactIndexFileName = "artifact-index.json";
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
        ResolveSignatures(
            process,
            signatureSets,
            fallbackOffsets,
            cancellationToken,
            baseAddress,
            moduleBytes,
            accessor,
            symbols);
        ApplyStandaloneFallbacks(fallbackOffsets, accessor, baseAddress, symbols);

        return new SymbolMap(symbols);
    }

    private void TryHydrateSymbolsFromGhidraPack(
        ProcessModule module,
        IReadOnlyList<SignatureSet> signatureSets,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (!TryLoadGhidraSymbolPack(module, out var fingerprintId, out var packPath, out var pack))
        {
            return;
        }

        var valueTypes = BuildSymbolValueTypeIndex(signatureSets);
        foreach (var anchor in pack.Anchors ?? new List<GhidraAnchorDto>())
        {
            if (!TryBuildAnchorSymbol(anchor, valueTypes, fingerprintId, symbols, out var symbol))
            {
                continue;
            }

            symbols[anchor.Id] = symbol;
        }

        _logger.LogInformation(
            "Loaded {Count} symbol(s) from ghidra pack {Path} for fingerprint {FingerprintId}",
            symbols.Count, packPath, fingerprintId);
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
        IDictionary<string, SymbolInfo> symbols,
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
        return TryParseJsonAddress(value, out address) ||
               TryParseNumericAddress(value, out address) ||
               (value is string str && TryParseAddressString(str, out address));
    }

    private void ResolveSignatures(
        Process process,
        IReadOnlyList<SignatureSet> signatureSets,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        CancellationToken cancellationToken,
        nint baseAddress,
        byte[] moduleBytes,
        ProcessMemoryAccessor accessor,
        IDictionary<string, SymbolInfo> symbols)
    {
        foreach (var signatureSet in signatureSets)
        {
            foreach (var signature in signatureSet.Signatures)
            {
                ResolveSignature(
                    process,
                    signatureSet,
                    signature,
                    fallbackOffsets,
                    cancellationToken,
                    baseAddress,
                    moduleBytes,
                    accessor,
                    symbols);
            }
        }
    }

    private void ResolveSignature(
        Process process,
        SignatureSet signatureSet,
        SignatureSpec signature,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        CancellationToken cancellationToken,
        nint baseAddress,
        byte[] moduleBytes,
        ProcessMemoryAccessor accessor,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (symbols.ContainsKey(signature.Name))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var pattern = AobPattern.Parse(signature.Pattern);
        var hit = AobScanner.FindPattern(process, moduleBytes, baseAddress, pattern);
        if (hit == nint.Zero)
        {
            HandleSignatureMiss(signature, fallbackOffsets, accessor, baseAddress, symbols);
            return;
        }

        HandleSignatureHit(signatureSet, signature, hit, fallbackOffsets, accessor, baseAddress, moduleBytes, symbols);
    }

    private void HandleSignatureHit(
        SignatureSet signatureSet,
        SignatureSpec signature,
        nint hit,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        ProcessMemoryAccessor accessor,
        nint baseAddress,
        byte[] moduleBytes,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (TryResolveAddress(signature, hit, baseAddress, moduleBytes, out var address, out var diagnostics))
        {
            symbols[signature.Name] = CreateSignatureSymbol(signatureSet, signature, address);
            return;
        }

        if (!fallbackOffsets.TryGetValue(signature.Name, out var fallback))
        {
            _logger.LogWarning(
                "Signature address resolution failed for {Symbol} and no fallback offset available. Details: {Diagnostics}",
                signature.Name,
                diagnostics ?? "<none>");
            return;
        }

        if (TryApplyFallback(
                accessor,
                baseAddress,
                signature.Name,
                signature.ValueType,
                fallback,
                symbols,
                diagnostics ?? "Fallback after address resolution failure"))
        {
            _logger.LogWarning(
                "Signature address resolution failed for {Symbol}; fallback offset applied (0x{Offset:X}). Details: {Diagnostics}",
                signature.Name, fallback, diagnostics ?? "<none>");
            return;
        }

        _logger.LogWarning(
            "Signature address resolution failed for {Symbol} and fallback offset 0x{Offset:X} is not readable. Details: {Diagnostics}",
            signature.Name, fallback, diagnostics ?? "<none>");
    }

    private void HandleSignatureMiss(
        SignatureSpec signature,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        ProcessMemoryAccessor accessor,
        nint baseAddress,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (fallbackOffsets.TryGetValue(signature.Name, out var fallback) && !symbols.ContainsKey(signature.Name))
        {
            if (TryApplyFallback(accessor, baseAddress, signature.Name, signature.ValueType, fallback, symbols, "Fallback offset"))
            {
                _logger.LogWarning("Signature miss for {Symbol}; fallback offset applied (0x{Offset:X})", signature.Name, fallback);
                return;
            }

            _logger.LogWarning(
                "Signature miss for {Symbol}; fallback offset 0x{Offset:X} is not readable",
                signature.Name, fallback);
            return;
        }

        _logger.LogWarning("Signature miss for {Symbol} and no fallback offset available", signature.Name);
    }

    private static SymbolInfo CreateSignatureSymbol(SignatureSet signatureSet, SignatureSpec signature, nint address)
    {
        return new SymbolInfo(
            signature.Name,
            address,
            signature.ValueType,
            AddressSource.Signature,
            $"{signatureSet.Name}:{signature.Pattern} [{signature.AddressMode}]",
            Confidence: 0.95d,
            HealthStatus: SymbolHealthStatus.Healthy,
            HealthReason: "signature_resolved",
            LastValidatedAt: DateTimeOffset.UtcNow);
    }

    private void ApplyStandaloneFallbacks(
        IReadOnlyDictionary<string, long> fallbackOffsets,
        ProcessMemoryAccessor accessor,
        nint baseAddress,
        IDictionary<string, SymbolInfo> symbols)
    {
        foreach (var fallback in fallbackOffsets)
        {
            if (symbols.ContainsKey(fallback.Key))
            {
                continue;
            }

            if (TryApplyFallback(accessor, baseAddress, fallback.Key, SymbolValueType.Int32, fallback.Value, symbols, "Standalone fallback"))
            {
                continue;
            }

            _logger.LogWarning(
                "Standalone fallback for {Symbol} skipped: offset 0x{Offset:X} is not readable",
                fallback.Key, fallback.Value);
        }
    }

    private bool TryLoadGhidraSymbolPack(
        ProcessModule module,
        out string fingerprintId,
        out string packPath,
        out GhidraSymbolPackDto pack)
    {
        pack = null!;
        if (!TryResolveGhidraPackPath(module, out fingerprintId, out packPath))
        {
            return false;
        }

        if (!TryDeserializeGhidraSymbolPack(packPath, out var candidate))
        {
            return false;
        }

        if (!IsMatchingFingerprint(candidate.BinaryFingerprint?.FingerprintId, fingerprintId))
        {
            _logger.LogWarning(
                "Ignoring ghidra symbol pack {Path}: fingerprint mismatch (expected {Expected}, actual {Actual})",
                packPath, fingerprintId, candidate.BinaryFingerprint?.FingerprintId);
            return false;
        }

        pack = candidate;
        return true;
    }

    private static bool TryBuildAnchorSymbol(
        GhidraAnchorDto anchor,
        IReadOnlyDictionary<string, SymbolValueType> valueTypes,
        string fingerprintId,
        IDictionary<string, SymbolInfo> symbols,
        out SymbolInfo symbol)
    {
        symbol = null!;
        if (string.IsNullOrWhiteSpace(anchor.Id) || symbols.ContainsKey(anchor.Id))
        {
            return false;
        }

        if (!TryParseAddress(anchor.Address, out var address))
        {
            return false;
        }

        var valueType = valueTypes.TryGetValue(anchor.Id, out var resolvedType)
            ? resolvedType
            : SymbolValueType.Int32;
        symbol = new SymbolInfo(
            anchor.Id,
            (nint)address,
            valueType,
            AddressSource.Signature,
            $"ghidra_symbol_pack:{fingerprintId}",
            Confidence: anchor.Confidence > 0 ? anchor.Confidence : 0.99d,
            HealthStatus: SymbolHealthStatus.Healthy,
            HealthReason: "ghidra_symbol_pack",
            LastValidatedAt: DateTimeOffset.UtcNow);
        return true;
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

    private static bool TryParseJsonAddress(object? value, out long address)
    {
        address = 0;
        if (value is not JsonElement element)
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
        {
            address = number;
            return true;
        }

        return element.ValueKind == JsonValueKind.String &&
               TryParseAddressString(element.GetString() ?? string.Empty, out address);
    }

    private static bool TryParseNumericAddress(object? value, out long address)
    {
        switch (value)
        {
            case long int64:
                address = int64;
                return true;
            case int int32:
                address = int32;
                return true;
            default:
                address = 0;
                return false;
        }
    }

    private static bool TryParseAddressString(string str, out long address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(str))
        {
            return false;
        }

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

        return false;
    }

    private bool TryResolveGhidraPackPath(ProcessModule module, out string fingerprintId, out string packPath)
    {
        fingerprintId = string.Empty;
        packPath = string.Empty;
        if (string.IsNullOrWhiteSpace(_ghidraSymbolPackRoot))
        {
            return false;
        }

        if (!TryBuildFingerprintId(module, out fingerprintId, out _))
        {
            return false;
        }

        packPath = SelectBestGhidraPackPath(_ghidraSymbolPackRoot, fingerprintId) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(packPath);
    }

    internal static string? SelectBestGhidraPackPath(string symbolPackRoot, string fingerprintId)
    {
        if (string.IsNullOrWhiteSpace(symbolPackRoot) ||
            string.IsNullOrWhiteSpace(fingerprintId) ||
            !Directory.Exists(symbolPackRoot))
        {
            return null;
        }

        var candidates = new List<PackSelectionCandidate>();
        var exactPath = Path.Combine(symbolPackRoot, $"{fingerprintId}.json");
        TryAddPackCandidate(candidates, exactPath, precedence: 0, fingerprintId);

        var indexedPath = ResolvePackPathFromArtifactIndex(symbolPackRoot, fingerprintId);
        if (!string.IsNullOrWhiteSpace(indexedPath))
        {
            TryAddPackCandidate(candidates, indexedPath!, precedence: 1, fingerprintId);
        }

        foreach (var path in EnumeratePackCandidates(symbolPackRoot))
        {
            if (string.Equals(path, exactPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(indexedPath) &&
                string.Equals(path, indexedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryAddPackCandidate(candidates, path, precedence: 2, fingerprintId);
        }

        return candidates
            .OrderBy(candidate => candidate.Precedence)
            .ThenByDescending(candidate => candidate.GeneratedAtUtc)
            .ThenBy(candidate => candidate.NormalizedPath, StringComparer.Ordinal)
            .Select(candidate => candidate.PackPath)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumeratePackCandidates(string symbolPackRoot)
    {
        try
        {
            return Directory.EnumerateFiles(symbolPackRoot, "*.json", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).Equals(ArtifactIndexFileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void TryAddPackCandidate(
        ICollection<PackSelectionCandidate> candidates,
        string path,
        int precedence,
        string expectedFingerprintId)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (!TryReadPackMetadata(path, out var fingerprintId, out var generatedAtUtc))
        {
            return;
        }

        if (!string.Equals(fingerprintId, expectedFingerprintId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();
        candidates.Add(new PackSelectionCandidate(path, precedence, generatedAtUtc, normalizedPath));
    }

    private static bool TryReadPackMetadata(string packPath, out string fingerprintId, out DateTimeOffset generatedAtUtc)
    {
        fingerprintId = string.Empty;
        generatedAtUtc = DateTimeOffset.MinValue;
        try
        {
            var pack = JsonSerializer.Deserialize<GhidraSymbolPackDto>(File.ReadAllText(packPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (pack?.BinaryFingerprint is null ||
                string.IsNullOrWhiteSpace(pack.BinaryFingerprint.FingerprintId))
            {
                return false;
            }

            fingerprintId = pack.BinaryFingerprint.FingerprintId!;
            generatedAtUtc = pack.BuildMetadata?.GeneratedAtUtc ?? DateTimeOffset.MinValue;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolvePackPathFromArtifactIndex(string symbolPackRoot, string fingerprintId)
    {
        var indexPath = Path.Combine(symbolPackRoot, ArtifactIndexFileName);
        if (!File.Exists(indexPath))
        {
            return null;
        }

        try
        {
            var index = JsonSerializer.Deserialize<GhidraArtifactIndexDto>(File.ReadAllText(indexPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (index?.BinaryFingerprint is null ||
                !string.Equals(index.BinaryFingerprint.FingerprintId, fingerprintId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var configuredPath = index.ArtifactPointers?.SymbolPackPath;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return null;
            }

            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.GetFullPath(Path.Combine(symbolPackRoot, configuredPath));
        }
        catch
        {
            return null;
        }
    }

    private bool TryDeserializeGhidraSymbolPack(string packPath, out GhidraSymbolPackDto pack)
    {
        pack = null!;
        try
        {
            var candidate = JsonSerializer.Deserialize<GhidraSymbolPackDto>(File.ReadAllText(packPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (candidate is null || candidate.BinaryFingerprint is null)
            {
                return false;
            }

            pack = candidate;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to consume ghidra symbol pack at {Path}", packPath);
            return false;
        }
    }

    private static bool IsMatchingFingerprint(string? actualFingerprintId, string expectedFingerprintId)
    {
        return string.Equals(actualFingerprintId, expectedFingerprintId, StringComparison.OrdinalIgnoreCase);
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

        public GhidraBuildMetadataDto? BuildMetadata { get; set; }

        public List<GhidraAnchorDto>? Anchors { get; set; }
    }

    private sealed class GhidraBuildMetadataDto
    {
        public DateTimeOffset GeneratedAtUtc { get; set; }
    }

    private sealed class GhidraArtifactIndexDto
    {
        public GhidraFingerprintDto? BinaryFingerprint { get; set; }

        public GhidraArtifactPointersDto? ArtifactPointers { get; set; }
    }

    private sealed class GhidraArtifactPointersDto
    {
        public string? SymbolPackPath { get; set; }
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

    private sealed record PackSelectionCandidate(
        string PackPath,
        int Precedence,
        DateTimeOffset GeneratedAtUtc,
        string NormalizedPath);
}
