using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IContentTransplantService
{
    Task<TransplantResult> ExecuteAsync(TransplantPlan plan, CancellationToken cancellationToken);

    Task<TransplantResult> ExecuteAsync(TransplantPlan plan)
    {
        return ExecuteAsync(plan, CancellationToken.None);
    }
}
