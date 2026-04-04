namespace SwfocTrainer.Helper.Config;

public sealed class HelperModOptions
{
    public string SourceRoot { get; init; } = Path.Join(AppContext.BaseDirectory, "profiles", "helper");

    public string InstallRoot { get; init; } = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwfocTrainer",
        "helper_mod");
}
