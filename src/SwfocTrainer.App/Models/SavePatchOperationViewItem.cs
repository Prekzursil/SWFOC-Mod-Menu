namespace SwfocTrainer.App.Models;

/// <summary>
/// UI row model for one Save Lab patch-pack operation preview line.
/// </summary>
public sealed record SavePatchOperationViewItem(
    string Kind,
    string FieldPath,
    string FieldId,
    string ValueType,
    string OldValue,
    string NewValue);
