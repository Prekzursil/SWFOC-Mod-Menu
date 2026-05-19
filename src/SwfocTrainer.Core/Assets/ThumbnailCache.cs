using System.Security.Cryptography;
using System.Text;

namespace SwfocTrainer.Core.Assets;

/// <summary>
/// 2026-05-07 (iter 307, Thread D iter 4 of 5) — C# read-side mirror of the
/// iter-306 Python tools/asset_extractor/thumbnail_cache.py cache layer.
///
/// The Python CLI generates thumbnail PNGs (via Pillow + DDS decoder) into a
/// content-keyed cache directory. The editor consumes those PNGs by computing
/// the same SHA256-based key the Python writer used, so a cache populated by
/// `python thumbnail_cache.py &lt;dds&gt;` is byte-identical from the editor's
/// perspective. Cache-key shape: <c>"&lt;sha256-of-dds-bytes&gt;_&lt;size&gt;.png"</c>.
///
/// Layout — must match Python iter-306 EXACTLY:
///   Windows: %LOCALAPPDATA%\swfoc_thumbnails\
///   Other:   ~/.cache/swfoc_thumbnails/
///   Override: SWFOC_THUMB_CACHE env var (test-friendly hermetic cache)
///
/// Iter-307 ships read-only consumption. The editor does NOT decode DDS
/// itself in iter-307 (deferred to iter-308 if needed). Operator workflow:
///   1. Operator runs Python `meg_parser.py --extract` then
///      `thumbnail_cache.py &lt;dds&gt;` from the CLI.
///   2. Editor calls TryGetCachedPath(dds_path, size) and binds the returned
///      PNG to a WPF Image control.
/// If the cache entry doesn't exist, TryGetCachedPath returns false and the
/// caller falls back to a placeholder (or no icon at all).
/// </summary>
public static class ThumbnailCache
{
    private const string CacheDirName = "swfoc_thumbnails";

    /// <summary>Default thumbnail size in pixels (matches Python DEFAULT_SIZE).</summary>
    public const int DefaultSize = 64;

    /// <summary>Sizes the Python writer accepts. Must match Python SUPPORTED_SIZES.</summary>
    public static readonly IReadOnlyList<int> SupportedSizes =
        new[] { 32, 48, 64, 96, 128, 256 };

    /// <summary>
    /// Resolves the operator-local cache root. Mirrors Python iter-306
    /// cache_root() exactly so both writers and readers agree.
    /// </summary>
    public static string CacheRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("SWFOC_THUMB_CACHE");
        if (!string.IsNullOrEmpty(overridePath))
        {
            return overridePath;
        }

        if (OperatingSystem.IsWindows())
        {
            var localApp = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localApp))
            {
                return Path.Combine(localApp, CacheDirName);
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".cache", CacheDirName);
    }

    /// <summary>
    /// Builds the canonical cache filename for a DDS file at a given size.
    /// Mirrors Python `_cache_filename(content_hash, size)` =&gt;
    /// <c>"&lt;sha256-hex-lowercase&gt;_&lt;size&gt;.png"</c>.
    /// </summary>
    public static string ComputeCacheFilename(string ddsFilePath, int size = DefaultSize)
    {
        ValidateSize(size);
        if (!File.Exists(ddsFilePath))
        {
            throw new FileNotFoundException(
                $"dds file not found: {ddsFilePath}", ddsFilePath);
        }

        var hash = ComputeFileSha256Hex(ddsFilePath);
        return $"{hash}_{size}.png";
    }

    /// <summary>
    /// Read-side cache lookup: if the Python writer has already cached a
    /// thumbnail for <paramref name="ddsFilePath"/> at <paramref name="size"/>,
    /// returns true and writes the PNG path. Otherwise returns false.
    ///
    /// This method NEVER generates a thumbnail — it only reads. Generation
    /// must happen via the Python CLI (iter-306) until iter-308+ adds an
    /// in-editor decoder. This split is intentional: the editor stays free of
    /// Pillow / WIC dependencies; the Python side handles encoding.
    /// </summary>
    public static bool TryGetCachedPath(string ddsFilePath, int size, out string cachePath)
    {
        cachePath = string.Empty;
        ValidateSize(size);

        if (!File.Exists(ddsFilePath))
        {
            return false;
        }

        var filename = ComputeCacheFilename(ddsFilePath, size);
        var candidate = Path.Combine(CacheRoot(), filename);
        if (File.Exists(candidate))
        {
            cachePath = candidate;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Convenience wrapper that returns the cached path if present, else null.
    /// Suited for direct WPF binding via a converter that handles null.
    /// </summary>
    public static string? GetCachedPathOrNull(string ddsFilePath, int size = DefaultSize)
    {
        return TryGetCachedPath(ddsFilePath, size, out var path) ? path : null;
    }

    private static void ValidateSize(int size)
    {
        if (!SupportedSizes.Contains(size))
        {
            throw new ArgumentException(
                $"size must be one of [{string.Join(",", SupportedSizes)}], got {size}",
                nameof(size));
        }
    }

    private static string ComputeFileSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(stream);
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
