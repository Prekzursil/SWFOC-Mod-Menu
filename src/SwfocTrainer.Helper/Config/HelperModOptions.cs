namespace SwfocTrainer.Helper.Config;

public sealed class HelperModOptions
{
    public string SourceRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "profiles", "helper");

    public string InstallRoot { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwfocTrainer",
        "helper_mod");
}
