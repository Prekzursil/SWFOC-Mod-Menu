using System.Text;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Pins ModInventoryService against synthetic filesystem layouts that mirror
/// real-world Steam Workshop content and manual Mods folder structures
/// observed on the developer's install (see
/// `.remember/mod_authoring_scope_v1.md` reconnaissance).
/// </summary>
public sealed class ModInventoryServiceTests
{
    [Fact]
    public void Discover_ReturnsEmpty_WhenBothRootsAreNullOrMissing()
    {
        var fs = new FakeFs();
        var svc = new ModInventoryService(fs);

        svc.Discover(null, null).Should().BeEmpty();
        svc.Discover("", "").Should().BeEmpty();
        svc.Discover("/missing/workshop", "/missing/mods").Should().BeEmpty();
    }

    [Fact]
    public void Discover_ParsesWorkshopModinfo_ThrawnsRevenge()
    {
        var fs = new FakeFs();
        fs.AddDirectory("/workshop");
        fs.AddDirectory("/workshop/1125571106");
        fs.AddFile("/workshop/1125571106/modinfo.json",
            """
            {
                "name": "Thrawn's Revenge",
                "summary": "",
                "icon": "1125571106/TRIcon.ico",
                "version": "3.3",
                "steamdata": {
                    "publishedfileid": "1125571106",
                    "contentfolder": "1125571106",
                    "title": "Thrawn's Revenge",
                    "tags": ["FOC"]
                }
            }
            """);

        var svc = new ModInventoryService(fs);
        var entries = svc.Discover("/workshop", null);

        entries.Should().HaveCount(1);
        entries[0].DisplayName.Should().Be("Thrawn's Revenge");
        entries[0].Version.Should().Be("3.3");
        entries[0].SourceKind.Should().Be(ModInventorySourceKind.Workshop);
        entries[0].WorkshopId.Should().Be("1125571106");
        entries[0].FolderPath.Should().EndWith("1125571106");
        entries[0].Tags.Should().Equal("FOC");
        entries[0].IconRelativePath.Should().Be("1125571106/TRIcon.ico");
    }

    [Fact]
    public void Discover_SkipsNonNumericWorkshopFolders()
    {
        var fs = new FakeFs();
        fs.AddDirectory("/workshop");
        fs.AddDirectory("/workshop/1125571106");
        fs.AddDirectory("/workshop/orphan_folder");  // not numeric — should be skipped
        fs.AddDirectory("/workshop/.cache");

        var svc = new ModInventoryService(fs);
        var entries = svc.Discover("/workshop", null);

        entries.Should().HaveCount(1);
        entries[0].WorkshopId.Should().Be("1125571106");
    }

    [Fact]
    public void Discover_WorkshopMissingModinfo_FallsBackToIdPlaceholder()
    {
        var fs = new FakeFs();
        fs.AddDirectory("/workshop");
        fs.AddDirectory("/workshop/1130150761");  // no modinfo.json

        var svc = new ModInventoryService(fs);
        var entries = svc.Discover("/workshop", null);

        entries.Should().HaveCount(1);
        entries[0].DisplayName.Should().Be("Workshop mod 1130150761");
        entries[0].Version.Should().BeNull();
        entries[0].Tags.Should().BeEmpty();
        entries[0].WorkshopId.Should().Be("1130150761");
    }

    [Fact]
    public void Discover_WorkshopCorruptModinfo_DegradesGracefully()
    {
        var fs = new FakeFs();
        fs.AddDirectory("/workshop");
        fs.AddDirectory("/workshop/9999999999");
        fs.AddFile("/workshop/9999999999/modinfo.json", "{ this is not json");

        var svc = new ModInventoryService(fs);
        var entries = svc.Discover("/workshop", null);

        entries.Should().HaveCount(1);
        entries[0].DisplayName.Should().Be("Workshop mod 9999999999");
        entries[0].Version.Should().BeNull();
    }

    [Fact]
    public void Discover_ManualMod_NoMetadata_UsesFolderName()
    {
        var fs = new FakeFs();
        fs.AddDirectory("/Mods");
        fs.AddDirectory("/Mods/aotr");
        fs.AddDirectory("/Mods/roe");

        var svc = new ModInventoryService(fs);
        var entries = svc.Discover(null, "/Mods");

        entries.Should().HaveCount(2);
        entries.Select(e => e.DisplayName).Should().BeEquivalentTo("aotr", "roe");
        entries.All(e => e.SourceKind == ModInventorySourceKind.Manual).Should().BeTrue();
        entries.All(e => e.WorkshopId is null).Should().BeTrue();
    }

    [Fact]
    public void Discover_ManualMod_WithModinfoJson_PrefersMetadataName()
    {
        var fs = new FakeFs();
        fs.AddDirectory("/Mods");
        fs.AddDirectory("/Mods/raw");
        fs.AddFile("/Mods/raw/modinfo.json",
            """
            { "name": "Republic at War", "version": "1.4" }
            """);

        var svc = new ModInventoryService(fs);
        var entries = svc.Discover(null, "/Mods");

        entries.Should().HaveCount(1);
        entries[0].DisplayName.Should().Be("Republic at War");
        entries[0].Version.Should().Be("1.4");
        entries[0].SourceKind.Should().Be(ModInventorySourceKind.Manual);
    }

    [Fact]
    public void Discover_BothRoots_ConcatenatesResults()
    {
        var fs = new FakeFs();
        fs.AddDirectory("/workshop");
        fs.AddDirectory("/workshop/1125571106");
        fs.AddFile("/workshop/1125571106/modinfo.json", "{ \"name\": \"TR\" }");
        fs.AddDirectory("/Mods");
        fs.AddDirectory("/Mods/aotr");

        var svc = new ModInventoryService(fs);
        var entries = svc.Discover("/workshop", "/Mods");

        entries.Should().HaveCount(2);
        entries.Select(e => e.SourceKind).Should().BeEquivalentTo(new[]
        {
            ModInventorySourceKind.Workshop,
            ModInventorySourceKind.Manual,
        });
    }

    [Fact]
    public void Discover_VersionAsNumber_AcceptsAndStringifies()
    {
        // Some real-world modinfo files write "version": 3.3 (number, not string).
        var fs = new FakeFs();
        fs.AddDirectory("/workshop");
        fs.AddDirectory("/workshop/12345");
        fs.AddFile("/workshop/12345/modinfo.json",
            """
            { "name": "TolerantVersion", "version": 3.3 }
            """);

        var svc = new ModInventoryService(fs);
        var entries = svc.Discover("/workshop", null);

        entries.Should().HaveCount(1);
        entries[0].Version.Should().Be("3.3");
    }

    // -------- Fake filesystem (in-memory) --------

    private sealed class FakeFs : IModInventoryFileSystem
    {
        private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddDirectory(string path) => _dirs.Add(Normalize(path));

        public void AddFile(string path, string content)
        {
            var n = Normalize(path);
            _files[n] = Encoding.UTF8.GetBytes(content);
            // Make sure parent directories exist (forward-slash arithmetic;
            // Path.GetDirectoryName mangles slashes on Windows).
            var idx = n.LastIndexOf('/');
            while (idx > 0)
            {
                _dirs.Add(n.Substring(0, idx));
                idx = n.LastIndexOf('/', idx - 1);
            }
        }

        public bool DirectoryExists(string path) => _dirs.Contains(Normalize(path));
        public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            var normalized = Normalize(path);
            var prefix = normalized.TrimEnd('/') + "/";
            foreach (var dir in _dirs)
            {
                if (dir == normalized) continue;
                if (!dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var tail = dir.Substring(prefix.Length);
                if (tail.Contains('/')) continue; // not a direct child
                yield return dir;
            }
        }

        public Stream OpenRead(string path) => new MemoryStream(_files[Normalize(path)], writable: false);

        private static string Normalize(string path) => path.Replace('\\', '/');
    }
}
