namespace SwfocTrainer.App.Models;

/// <summary>
/// UI row model for Save Lab compatibility diagnostics.
/// </summary>
public sealed record SavePatchCompatibilityViewItem(
    string Severity,
    string Code,
    string Message);
