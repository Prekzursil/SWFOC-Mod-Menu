using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Evaluates action reliability based on runtime mode, symbol health, and helper/dependency gates.
/// </summary>
public interface IActionReliabilityService
{
    /// <summary>
    /// Computes reliability state and reason code for each profile action in the current attach session.
    /// </summary>
    /// <param name="profile">Resolved profile whose actions are evaluated.</param>
    /// <param name="session">Current runtime attach session.</param>
    /// <param name="catalog">Optional catalog map used for dependency-aware action checks.</param>
    /// <returns>Reliability entries keyed by action identifier.</returns>
    IReadOnlyList<ActionReliabilityInfo> Evaluate(
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog);

    IReadOnlyList<ActionReliabilityInfo> Evaluate(
        TrainerProfile profile,
        AttachSession session)
    {
        return Evaluate(profile, session, null);
    }
}
