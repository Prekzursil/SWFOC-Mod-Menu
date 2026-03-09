using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ICatalogService
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(profileId));
        }

        return LoadCatalogAsync(profileId, CancellationToken.None);
    }

    async Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(profileId));
        }

        var legacyCatalog = await LoadCatalogAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (legacyCatalog is null)
        {
            throw new InvalidOperationException("Catalog service returned a null legacy catalog.");
        }

        return EntityCatalogSnapshot.FromLegacy(profileId, legacyCatalog);
    }

    Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(profileId));
        }

        return LoadTypedCatalogAsync(profileId, CancellationToken.None);
    }
}
