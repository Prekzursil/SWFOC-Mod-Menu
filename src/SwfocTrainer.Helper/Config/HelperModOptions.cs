namespace SwfocTrainer.Helper.Config;

public sealed class HelperModOptions
{
    private const string GameRootOverrideEnvVar = "SWFOC_GAME_ROOT";

    public string SourceRoot { get; init; } = Path.Combine(AppContext.BaseDirectory, "profiles", "helper");

    public string InstallRoot { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwfocTrainer",
        "helper_mod");

    public IReadOnlyList<string> OriginalScriptSearchRoots { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GameRootCandidates { get; init; } = BuildDefaultGameRootCandidates();

    public IReadOnlyList<string> WorkshopContentRoots { get; init; } = BuildDefaultWorkshopContentRoots();

    private static IReadOnlyList<string> BuildDefaultGameRootCandidates()
    {
        var values = new List<string>();
        AddIfPresent(values, Environment.GetEnvironmentVariable(GameRootOverrideEnvVar));
        AddIfPresent(values, @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War");
        AddIfPresent(values, @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Empire at War");
        return values;
    }

    private static IReadOnlyList<string> BuildDefaultWorkshopContentRoots()
    {
        return new[]
        {
            @"D:\SteamLibrary\steamapps\workshop\content\32470",
            @"C:\Program Files (x86)\Steam\steamapps\workshop\content\32470"
        };
    }

    private static void AddIfPresent(ICollection<string> values, string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            values.Add(candidate.Trim());
        }
    }
}
