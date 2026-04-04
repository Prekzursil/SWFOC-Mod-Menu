using System.Xml.Linq;

namespace SwfocTrainer.Catalog.Parsing;

internal static class XmlObjectExtractor
{
    private static readonly string[] InterestingAttributes = ["Name", "ID", "Id", "Object_Name", "Type"];

    public static IReadOnlyList<string> ExtractObjectNames(string xmlPath)
    {
        ArgumentNullException.ThrowIfNull(xmlPath);
        try
        {
            var doc = XDocument.Load(xmlPath, LoadOptions.None);
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var value in doc.Descendants()
                .SelectMany(_ => InterestingAttributes, (element, attrName) => element.Attribute(attrName))
                .Where(attr => attr is not null)
                .Select(attr => attr!.Value?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value) && value!.Length <= 96))
            {
                values.Add(value!);
            }

            return values.ToArray();
        }
        catch (System.Xml.XmlException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }
}
