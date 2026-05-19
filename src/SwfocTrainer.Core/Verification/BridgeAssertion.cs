using System.Text.Json.Nodes;

namespace SwfocTrainer.Core.Verification;

/// <summary>
/// Declarative contract for a bridge command + its expected side effect.
/// PASS only when the post-state delta matches the contract — eliminates
/// the false-positive class where a bridge call returns OK but no game state changed.
/// </summary>
public sealed record BridgeAssertion
{
    /// <summary>Lua snippet that reads the relevant pre-state and returns it (any Lua-serializable shape).</summary>
    public required string PreStateProbe { get; init; }

    /// <summary>The Lua command being tested. May mutate state, may be a no-op.</summary>
    public required string LuaCommand { get; init; }

    /// <summary>Lua snippet that reads the post-state and returns it. Usually identical to PreStateProbe.</summary>
    public required string PostStateProbe { get; init; }

    /// <summary>
    /// Predicate that must return true for the assertion to PASS. Receives raw bridge response strings
    /// (the bridge protocol returns strings, never structured JSON).
    /// </summary>
    public required Func<string, string, bool> ExpectDelta { get; init; }

    /// <summary>Human-readable description for failure messages.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Result of running a BridgeAssertion. Carries the pre/post probe responses
/// and any error so failure messages are actionable.
/// </summary>
public sealed record BridgeAssertionResult(
    bool Passed,
    string PreState,
    string PostState,
    string? CommandResponse,
    string? FailureReason);
