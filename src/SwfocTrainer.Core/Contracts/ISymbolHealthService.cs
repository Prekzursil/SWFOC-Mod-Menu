using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ISymbolHealthService
{
    SymbolValidationResult Evaluate(SymbolInfo symbol, TrainerProfile profile, RuntimeMode mode);
}
