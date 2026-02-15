namespace SwfocTrainer.Saves.Config;

public sealed class SaveOptions
{
    public string SchemaRootPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "profiles", "schemas");

    public string DefaultSaveRootPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Saved Games",
        "Petroglyph");
}
