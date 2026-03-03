using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IWorkshopInventoryService
{
    Task<WorkshopInventoryGraph> DiscoverInstalledAsync(WorkshopInventoryRequest request, CancellationToken cancellationToken);

    Task<WorkshopInventoryGraph> DiscoverInstalledAsync(WorkshopInventoryRequest request)
    {
        return DiscoverInstalledAsync(request, CancellationToken.None);
    }
}
