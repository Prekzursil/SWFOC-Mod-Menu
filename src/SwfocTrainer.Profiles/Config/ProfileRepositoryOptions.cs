namespace SwfocTrainer.Profiles.Config;

public sealed class ProfileRepositoryOptions
{
    public string ProfilesRootPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "profiles");

    public string ManifestFileName { get; init; } = "manifest.json";

    public string DownloadCachePath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwfocTrainer",
        "cache");

    public string? RemoteManifestUrl { get; init; }
}
