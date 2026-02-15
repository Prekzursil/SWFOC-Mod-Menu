namespace SwfocTrainer.Catalog.Config;

public sealed class CatalogOptions
{
    public string CatalogRootPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "profiles", "catalog");

    public int MaxParsedXmlFiles { get; init; } = 4096;
}
