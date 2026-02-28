namespace SwfocTrainer.Core.Models;

public sealed record ProcessLocatorOptions(
    IReadOnlyList<string>? ForcedWorkshopIds = null,
    string? ForcedProfileId = null)
{
    public static ProcessLocatorOptions None { get; } = new();
}
