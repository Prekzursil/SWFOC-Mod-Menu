using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Scanning;

namespace SwfocTrainer.Runtime.Services;

public sealed partial class SignatureResolver : ISignatureResolver
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

    private void ResolveSignatures(
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
        var hit = AobScanner.FindPattern(moduleBytes, baseAddress, pattern);
        if (hit == nint.Zero)
        {
            HandleSignatureMiss(signature, fallbackOffsets, accessor, baseAddress, symbols);
            return;
        }

        HandleSignatureHit(signatureSet, signature, hit, fallbackOffsets, accessor, baseAddress, moduleBytes, symbols);
    }
}
