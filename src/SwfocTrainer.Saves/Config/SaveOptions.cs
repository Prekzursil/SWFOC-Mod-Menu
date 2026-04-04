namespace SwfocTrainer.Saves.Config;

public sealed class SaveOptions
{
    public string SchemaRootPath { get; init; } = Path.Join(AppContext.BaseDirectory, "profiles", "schemas");

    public string DefaultSaveRootPath { get; init; } = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Saved Games",
        "Petroglyph");
}
