using System.Xml.Linq;
using SwfocTrainer.DataIndex.Models;

namespace SwfocTrainer.DataIndex.Services;

public sealed class MegaFilesXmlIndexBuilder
{
    private static readonly string[] NameAttributeCandidates = ["Name", "File", "Filename", "Path"];

    public MegaFilesIndex Build(string megaFilesXmlContent)
    {
        if (string.IsNullOrWhiteSpace(megaFilesXmlContent))
        {
            return MegaFilesIndex.Empty;
        }

        var diagnostics = new List<string>();
        XDocument document;
        try
        {
            document = XDocument.Parse(megaFilesXmlContent, LoadOptions.None);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Invalid MegaFiles XML: {ex.Message}");
            return new MegaFilesIndex(Array.Empty<MegaFileEntry>(), diagnostics);
        }

        var files = new List<MegaFileEntry>();
        var loadOrder = 0;
        foreach (var node in document.Descendants())
        {
            if (!IsMegaFileElement(node))
            {
                continue;
            }

            var fileName = ReadFirstNonEmptyAttribute(node, NameAttributeCandidates);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                diagnostics.Add($"Skipped MegaFile entry at line with no filename attribute (order={loadOrder}).");
                continue;
            }

            var attributes = node
                .Attributes()
                .ToDictionary(
                    attribute => attribute.Name.LocalName,
                    attribute => attribute.Value,
                    StringComparer.OrdinalIgnoreCase);

            var enabled = !string.Equals(
                ReadFirstNonEmptyAttribute(node, "Enabled", "IsEnabled"),
                "false",
                StringComparison.OrdinalIgnoreCase);

            files.Add(new MegaFileEntry(fileName!, loadOrder, enabled, attributes));
            loadOrder++;
        }

        return new MegaFilesIndex(files, diagnostics);
    }

    private static bool IsMegaFileElement(XElement element)
    {
        var name = element.Name.LocalName;
        return name.Equals("MegaFile", StringComparison.OrdinalIgnoreCase)
               || name.Equals("File", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadFirstNonEmptyAttribute(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = element.Attribute(name)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
