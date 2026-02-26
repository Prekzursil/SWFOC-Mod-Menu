using System;
using System.IO;
using System.IO.Compression;

namespace SwfocTrainer.Profiles.Services;

internal static class GitHubProfileUpdateExtractionHelpers
{
    internal static void ExtractToDirectorySafely(string zipPath, string extractDir)
    {
        var extractionRoot = Path.GetFullPath(extractDir);
        Directory.CreateDirectory(extractionRoot);
        var extractionRootPrefix = extractionRoot.EndsWith(Path.DirectorySeparatorChar)
            ? extractionRoot
            : extractionRoot + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            var normalizedEntryPath = NormalizeEntryPath(entry);
            if (normalizedEntryPath is null)
            {
                continue;
            }

            var destinationPath = ResolveDestinationPath(
                extractionRoot,
                extractionRootPrefix,
                normalizedEntryPath,
                entry.FullName);

            if (IsDirectoryEntry(normalizedEntryPath))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            EnsureDestinationDirectory(destinationPath);
            ExtractEntryToFile(entry, destinationPath);
        }
    }

    private static string? NormalizeEntryPath(ZipArchiveEntry entry)
    {
        var normalized = entry.FullName.Replace('\\', '/');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string ResolveDestinationPath(
        string extractionRoot,
        string extractionRootPrefix,
        string normalizedEntryPath,
        string originalEntryPath)
    {
        if (IsDriveQualifiedPath(normalizedEntryPath))
        {
            throw new InvalidDataException($"Archive entry uses drive-qualified path: {originalEntryPath}");
        }

        if (Path.IsPathRooted(normalizedEntryPath) || normalizedEntryPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Archive entry uses rooted path: {originalEntryPath}");
        }

        var relativePath = normalizedEntryPath.Replace('/', Path.DirectorySeparatorChar);
        var destinationPath = Path.GetFullPath(Path.Combine(extractionRoot, relativePath));
        if (!destinationPath.StartsWith(extractionRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Archive entry escapes extraction root: {originalEntryPath}");
        }

        return destinationPath;
    }

    private static bool IsDirectoryEntry(string normalizedEntryPath)
    {
        return normalizedEntryPath.EndsWith("/", StringComparison.Ordinal);
    }

    private static void EnsureDestinationDirectory(string destinationPath)
    {
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }
    }

    private static void ExtractEntryToFile(ZipArchiveEntry entry, string destinationPath)
    {
        using var entryStream = entry.Open();
        using var outputStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        entryStream.CopyTo(outputStream);
    }

    private static bool IsDriveQualifiedPath(string path)
    {
        return path.Length >= 2 &&
               char.IsLetter(path[0]) &&
               path[1] == ':';
    }
}
