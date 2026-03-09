using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ICatalogService
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId)
    {
        if (profileId is null)
        {
            throw new ArgumentNullException(nameof(profileId));
        }

        var normalizedProfileId = profileId.Trim();
        if (normalizedProfileId.Length == 0)
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(profileId));
        }

        return LoadCatalogAsync(normalizedProfileId, CancellationToken.None);
    }

    async Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        if (profileId is null)
        {
            throw new ArgumentNullException(nameof(profileId));
        }

        var normalizedProfileId = profileId.Trim();
        if (normalizedProfileId.Length == 0)
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(profileId));
        }

        var legacyCatalog = await LoadCatalogAsync(normalizedProfileId, cancellationToken).ConfigureAwait(false);
        if (legacyCatalog is null)
        {
            throw new InvalidOperationException("Catalog service returned a null legacy catalog.");
        }

        return EntityCatalogSnapshot.FromLegacy(normalizedProfileId, legacyCatalog);
    }

    Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId)
    {
        if (profileId is null)
        {
            throw new ArgumentNullException(nameof(profileId));
        }

        var normalizedProfileId = profileId.Trim();
        if (normalizedProfileId.Length == 0)
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(profileId));
        }

        return LoadTypedCatalogAsync(normalizedProfileId, CancellationToken.None);
    }
}
