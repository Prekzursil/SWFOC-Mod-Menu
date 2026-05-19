using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class StoryEventService : IStoryEventService
{
    private const string StoryEventCatalogKey = "story_event_catalog";
    private const string StoryEventsKey = "story_events";
    internal const string FeatureId = "v5_story_event";

    private readonly ICatalogService _catalog;
    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<StoryEventService> _logger;

    public StoryEventService(
        ICatalogService catalog,
        ILuaBridgeExecutor bridge,
        ILogger<StoryEventService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _bridge = bridge;
        _logger = logger;
    }

    public StoryEventService(
        ICatalogService catalog,
        ILogger<StoryEventService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _bridge = null;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StoryEventEntry>> LoadEventsAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var catalog = await _catalog.LoadCatalogAsync(profileId, cancellationToken);

        IReadOnlyList<string> eventIds;
        if (catalog.TryGetValue(StoryEventCatalogKey, out var byKey))
        {
            eventIds = byKey;
        }
        else if (catalog.TryGetValue(StoryEventsKey, out var byAlt))
        {
            eventIds = byAlt;
        }
        else
        {
            _logger.LogDebug(
                "No story event catalog found for profile {ProfileId}", profileId);
            return Array.Empty<StoryEventEntry>();
        }

        var entries = new List<StoryEventEntry>(eventIds.Count);
        foreach (var eventId in eventIds)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                continue;
            }

            entries.Add(new StoryEventEntry(
                EventId: eventId,
                DisplayName: FormatDisplayName(eventId),
                Source: "catalog",
                Category: "story"));
        }

        _logger.LogInformation(
            "Loaded {Count} story events for profile {ProfileId}",
            entries.Count, profileId);

        return entries.AsReadOnly();
    }

    public async Task<ActionExecutionResult> FireEventAsync(
        string profileId, string eventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(eventId);

        if (string.IsNullOrWhiteSpace(eventId))
        {
            _logger.LogWarning(
                "Empty eventId provided for profile {ProfileId}", profileId);

            return new ActionExecutionResult(
                Succeeded: false,
                Message: "eventId must not be empty or whitespace",
                AddressSource: AddressSource.None);
        }

        var luaCall = BuildStoryEventLuaCommand(eventId);

        _logger.LogInformation(
            "Story event '{EventId}' executing as {LuaCall} for profile {ProfileId}",
            eventId, luaCall, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCall, FeatureId, cancellationToken);
        }

        // Fallback: return prepared result when no bridge is configured.
        var diagnostics = new Dictionary<string, object?>
        {
            ["lua_call"] = luaCall,
            ["event_id"] = eventId
        };

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Story event '{eventId}' prepared",
            AddressSource: AddressSource.None,
            Diagnostics: diagnostics);
    }

    /// <summary>
    /// Builds the Lua command string for triggering a story event.
    /// </summary>
    internal static string BuildStoryEventLuaCommand(string eventId)
    {
        ArgumentNullException.ThrowIfNull(eventId);
        return $"Story_Event(\"{eventId}\")";
    }

    internal static string FormatDisplayName(string eventId)
    {
        ArgumentNullException.ThrowIfNull(eventId);

        if (eventId.Length == 0)
        {
            return string.Empty;
        }

        var words = eventId.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            words[i] = word.Length <= 1
                ? word.ToUpperInvariant()
                : string.Concat(
                    char.ToUpperInvariant(word[0]).ToString(),
                    word[1..].ToLowerInvariant());
        }

        return string.Join(' ', words);
    }
}
