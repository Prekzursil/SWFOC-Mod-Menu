using System.Runtime.InteropServices;

namespace SwfocTrainer.Core.IO;

public static class TrustedPathPolicy
{
    private static readonly StringComparison PathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static string GetOrCreateAppDataRoot()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwfocTrainer");

        return EnsureDirectory(root);
    }

    public static string EnsureDirectory(string path)
    {
        var normalized = NormalizeAbsolute(path);
        Directory.CreateDirectory(normalized);
        return normalized;
    }

    public static string NormalizeAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Path cannot be empty.");
        }

        return Path.GetFullPath(path.Trim());
    }

    public static string CombineUnderRoot(string rootPath, params string[] segments)
    {
        var root = NormalizeAbsolute(rootPath);
        var current = root;
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            current = Path.Combine(current, segment);
        }

        var candidate = NormalizeAbsolute(current);
        EnsureSubPath(root, candidate);
        return candidate;
    }

    public static string EnsureSubPath(string rootPath, string candidatePath)
    {
        var root = NormalizeAbsolute(rootPath);
        var candidate = NormalizeAbsolute(candidatePath);
        if (!IsSubPath(root, candidate))
        {
            throw new InvalidOperationException($"Path '{candidate}' is outside trusted root '{root}'.");
        }

        return candidate;
    }

    public static bool IsSubPath(string rootPath, string candidatePath)
    {
        var root = NormalizeAbsolute(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = NormalizeAbsolute(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(root, candidate, PathComparison))
        {
            return true;
        }

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, PathComparison);
    }

    public static void EnsureAllowedExtension(string path, params string[] allowedExtensions)
    {
        if (allowedExtensions.Length == 0)
        {
            return;
        }

        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext))
        {
            throw new InvalidOperationException("Path does not have an allowed file extension.");
        }

        if (!allowedExtensions.Any(x => ext.Equals(x, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Unsupported extension '{ext}'.");
        }
    }

    public static string BuildSiblingFilePath(string sourcePath, string suffix)
    {
        var source = NormalizeAbsolute(sourcePath);
        var directory = Path.GetDirectoryName(source);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Cannot resolve directory for '{sourcePath}'.");
        }

        var outputName = $"{Path.GetFileNameWithoutExtension(source)}{suffix}{Path.GetExtension(source)}";
        var candidate = NormalizeAbsolute(Path.Combine(directory, outputName));
        EnsureSubPath(directory, candidate);
        return candidate;
    }
}
