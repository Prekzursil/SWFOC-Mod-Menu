using System.Xml.Linq;

namespace SwfocTrainer.Catalog.Parsing;

internal static class XmlObjectExtractor
{
    private static readonly string[] InterestingAttributes = ["Name", "ID", "Id", "Object_Name", "Type"];

    public static IReadOnlyList<string> ExtractObjectNames(string xmlPath)
    {
        try
        {
            var doc = XDocument.Load(xmlPath, LoadOptions.None);
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in doc.Descendants())
            {
                foreach (var attrName in InterestingAttributes)
                {
                    var attr = element.Attribute(attrName);
                    if (attr is null)
                    {
                        continue;
                    }

                    var value = attr.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(value) && value.Length <= 96)
                    {
                        values.Add(value);
                    }
                }
            }

            return values.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
