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
    private readonly string _ghidraSymbolPackRoot;

    public SignatureResolver(ILogger<SignatureResolver> logger)
        : this(logger, SignatureResolverSymbolHydration.ResolveDefaultGhidraSymbolPackRoot())
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
        SignatureResolverSymbolHydration.TryHydrateSymbolsFromGhidraPack(
            _ghidraSymbolPackRoot,
            _logger,
            module,
            signatureSets,
            symbols);
        ResolveSignatures(
            signatureSets,
            fallbackOffsets,
            cancellationToken,
            baseAddress,
            moduleBytes,
            accessor,
            symbols);
        SignatureResolverFallbacks.ApplyStandaloneFallbacks(_logger, fallbackOffsets, accessor, baseAddress, symbols);

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

    internal static string? SelectBestGhidraPackPath(string symbolPackRoot, string fingerprintId)
    {
        return SignatureResolverSymbolHydration.SelectBestGhidraPackPath(symbolPackRoot, fingerprintId);
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
        var context = new SignatureResolutionContext(
            fallbackOffsets,
            cancellationToken,
            baseAddress,
            moduleBytes,
            accessor,
            symbols);
        foreach (var signatureSet in signatureSets)
        {
            foreach (var signature in signatureSet.Signatures)
            {
                ResolveSignature(signatureSet, signature, context);
            }
        }
    }

    private void ResolveSignature(
        SignatureSet signatureSet,
        SignatureSpec signature,
        SignatureResolutionContext context)
    {
        if (context.Symbols.ContainsKey(signature.Name))
        {
            return;
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        var pattern = AobPattern.Parse(signature.Pattern);
        var hit = AobScanner.FindPattern(context.ModuleBytes, context.BaseAddress, pattern);
        if (hit == nint.Zero)
        {
            SignatureResolverFallbacks.HandleSignatureMiss(
                _logger,
                signature,
                context.FallbackOffsets,
                context.Accessor,
                context.BaseAddress,
                context.Symbols);
            return;
        }

        SignatureResolverFallbacks.HandleSignatureHit(
            _logger,
            signatureSet,
            signature,
            hit,
            new SignatureResolverFallbacks.SignatureHitContext(
                context.FallbackOffsets,
                context.Accessor,
                context.BaseAddress,
                context.ModuleBytes,
                context.Symbols));
    }

    private readonly record struct SignatureResolutionContext(
        IReadOnlyDictionary<string, long> FallbackOffsets,
        CancellationToken CancellationToken,
        nint BaseAddress,
        byte[] ModuleBytes,
        ProcessMemoryAccessor Accessor,
        IDictionary<string, SymbolInfo> Symbols);
}
