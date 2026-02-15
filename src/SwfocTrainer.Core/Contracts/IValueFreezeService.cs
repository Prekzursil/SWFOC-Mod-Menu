namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Service that periodically re-writes memory values to keep them frozen at a target value.
/// </summary>
public interface IValueFreezeService : IDisposable
{
    /// <summary>
    /// Freeze an integer symbol to the given value.
    /// </summary>
    void FreezeInt(string symbol, int value);

    /// <summary>
    /// Freeze an integer symbol using a high-frequency dedicated thread (~1 ms writes).
    /// Use this for symbols the game actively overwrites every frame (e.g., credits).
    /// </summary>
    void FreezeIntAggressive(string symbol, int value);

    /// <summary>
    /// Freeze a float symbol to the given value.
    /// </summary>
    void FreezeFloat(string symbol, float value);

    /// <summary>
    /// Freeze a bool/byte symbol to the given value.
    /// </summary>
    void FreezeBool(string symbol, bool value);

    /// <summary>
    /// Stop freezing the specified symbol.
    /// </summary>
    bool Unfreeze(string symbol);

    /// <summary>
    /// Stop freezing all symbols.
    /// </summary>
    void UnfreezeAll();

    /// <summary>
    /// Returns true if the specified symbol is currently frozen.
    /// </summary>
    bool IsFrozen(string symbol);

    /// <summary>
    /// Returns all currently frozen symbol names.
    /// </summary>
    IReadOnlyCollection<string> FrozenSymbols { get; }
}
