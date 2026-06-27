using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.DataIndex.Services;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

public sealed class DataIndexWave11CoverageTests
{
    // ── EffectiveGameDataIndexService L99-102: MegaFiles.xml diagnostics forwarded ──
    // When MegaFilesXmlIndexBuilder returns diagnostics, the foreach loop
    // at L99-102 adds them prefixed with "MegaFiles.xml: ".
    // We create a real MegaFiles.xml with intentionally problematic content
    // that produces diagnostics from the builder.
    [Fact]
    public void Build_MegaFilesXmlWithDiagnostics_ShouldForwardDiagnostics()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"didx11_{Guid.NewGuid():N}");
        var dataDir = Path.Join(tempDir, "Data");
        Directory.CreateDirectory(dataDir);
        try
        {
            // Write a MegaFiles.xml with a File element that has no filename attribute.
            // MegaFilesXmlIndexBuilder will emit a diagnostic for the skipped entry.
            var megaFilesXml = "<MegaFiles><File Enabled=\"true\" /><File Name=\"test.meg\" /></MegaFiles>";
            File.WriteAllText(Path.Join(dataDir, "MegaFiles.xml"), megaFilesXml);

            // Create the referenced meg file as a valid format1 archive
            var megBytes = BuildFormat1Archive("DATA\\UNITS.XML", new byte[] { 0x01 });
            File.WriteAllBytes(Path.Join(dataDir, "test.meg"), megBytes);

            var service = new EffectiveGameDataIndexService();
            var request = new EffectiveGameDataIndexRequest(
                ProfileId: "test",
                GameRootPath: tempDir);

            var report = service.Build(request);

            // Should contain forwarded diagnostic from MegaFilesXmlIndexBuilder
            report.Diagnostics.Should().Contain(d => d.Contains("MegaFiles.xml:"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // ── EffectiveGameDataIndexService L208-210: ResolveMegaPath finds direct path ──
    // The partial branch at L208 is File.Exists(direct) returning true.
    [Fact]
    public void ResolveMegaPath_DirectPathExists_ShouldReturnDirectPath()
    {
        var method = typeof(EffectiveGameDataIndexService).GetMethod(
            "ResolveMegaPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tempDir = Path.Join(Path.GetTempPath(), $"didx11_resolve_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create the file at the direct join path
            var megFile = Path.Join(tempDir, "test.meg");
            File.WriteAllBytes(megFile, new byte[] { 0x00 });

            var result = (string?)method!.Invoke(null, new object[] { tempDir, "test.meg" });
            result.Should().NotBeNull();
            result.Should().Be(megFile);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // ── EffectiveGameDataIndexService L208-210: ResolveMegaPath finds under Data/ ──
    [Fact]
    public void ResolveMegaPath_UnderDataPathExists_ShouldReturnDataPath()
    {
        var method = typeof(EffectiveGameDataIndexService).GetMethod(
            "ResolveMegaPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tempDir = Path.Join(Path.GetTempPath(), $"didx11_data_{Guid.NewGuid():N}");
        var dataDir = Path.Join(tempDir, "Data");
        Directory.CreateDirectory(dataDir);
        try
        {
            // Create file only under Data/
            var megFile = Path.Join(dataDir, "test.meg");
            File.WriteAllBytes(megFile, new byte[] { 0x00 });

            var result = (string?)method!.Invoke(null, new object[] { tempDir, "test.meg" });
            result.Should().NotBeNull();
            result.Should().Be(megFile);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // ── Helper: Build minimal Format1 MEG archive ──
    private static byte[] BuildFormat1Archive(string fileName, byte[] data)
    {
        var nameBytes = Encoding.ASCII.GetBytes(fileName);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((uint)1); // nameCount
        bw.Write((uint)1); // fileCount

        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)0);
        bw.Write(nameBytes);

        var dataStart = (int)ms.Position + 20;

        bw.Write((uint)0);             // crc32
        bw.Write((uint)0);             // index
        bw.Write((uint)data.Length);    // size
        bw.Write((uint)dataStart);      // start
        bw.Write((uint)0);             // nameIndex

        bw.Write(data);
        bw.Flush();

        return ms.ToArray();
    }
}
