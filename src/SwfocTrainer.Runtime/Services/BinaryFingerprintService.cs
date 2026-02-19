using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class BinaryFingerprintService : IBinaryFingerprintService
{
    private readonly ILogger<BinaryFingerprintService> _logger;

    public BinaryFingerprintService(ILogger<BinaryFingerprintService> logger)
    {
        _logger = logger;
    }

    public async Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int? processId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            throw new ArgumentException("Module path is required.", nameof(modulePath));
        }

        var fullPath = Path.GetFullPath(modulePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Module not found: {fullPath}", fullPath);
        }

        string sha256;
        await using (var stream = File.OpenRead(fullPath))
        {
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        }

        var fileInfo = new FileInfo(fullPath);
        string? productVersion = null;
        string? fileVersion = null;

        try
        {
            var version = FileVersionInfo.GetVersionInfo(fullPath);
            productVersion = version.ProductVersion;
            fileVersion = version.FileVersion;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to read version metadata for {Path}", fullPath);
        }

        var moduleNames = await TryGetLoadedModulesAsync(processId, cancellationToken);
        var moduleName = Path.GetFileName(fullPath);
        var fingerprintId = BuildFingerprintId(moduleName, sha256);

        return new BinaryFingerprint(
            FingerprintId: fingerprintId,
            FileSha256: sha256,
            ModuleName: moduleName,
            ProductVersion: productVersion,
            FileVersion: fileVersion,
            TimestampUtc: fileInfo.LastWriteTimeUtc,
            ModuleList: moduleNames,
            SourcePath: fullPath);
    }

    private static string BuildFingerprintId(string moduleName, string sha256)
    {
        var normalizedModule = Path.GetFileNameWithoutExtension(moduleName)
            .ToLowerInvariant()
            .Replace(' ', '_');
        var hashPrefix = sha256.Length >= 16 ? sha256[..16] : sha256;
        return $"{normalizedModule}_{hashPrefix}";
    }

    private static Task<IReadOnlyList<string>> TryGetLoadedModulesAsync(int? processId, CancellationToken cancellationToken)
    {
        if (!processId.HasValue || processId.Value <= 0)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.GetProcessById(processId.Value);
            var modules = process.Modules
                .Cast<ProcessModule>()
                .Select(x => x.ModuleName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult<IReadOnlyList<string>>(modules);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }
}
