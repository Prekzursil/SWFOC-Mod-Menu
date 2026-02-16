using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ISdkCapabilityResolver
{
    SdkCapabilityReport Resolve(TrainerProfile profile, ProcessMetadata process, SymbolMap symbols);
}
