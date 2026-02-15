using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IModDependencyValidator
{
    DependencyValidationResult Validate(TrainerProfile profile, ProcessMetadata process);
}
