using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ICatalogService
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId)
    {
        return LoadCatalogAsync(profileId, CancellationToken.None);
    }

    async Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId, CancellationToken cancellationToken)
    {
        var legacyCatalog = await LoadCatalogAsync(profileId, cancellationToken).ConfigureAwait(false);
        return EntityCatalogSnapshot.FromLegacy(profileId, legacyCatalog);
    }

    Task<EntityCatalogSnapshot> LoadTypedCatalogAsync(string profileId)
    {
        return LoadTypedCatalogAsync(profileId, CancellationToken.None);
    }
}
