using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SwfocTrainer.Core.Diagnostics;

/// <summary>
/// Confidence tier for a ledger entry. Mirrors the Python linter's
/// VALID_CONFIDENCE set (tools/verifier/ledger_lint.py) so the C#
/// loader rejects the same shapes the lint step does — drift here
/// would let a malformed entry slip past the editor even while the
/// Python lint stays green.
/// </summary>
public enum VerifiedFactConfidence
{
    Unknown = 0,
    Verified,
    VerifiedNegative,
    VerifiedExists,
    LiveObserved,
    Unverified,
    Refuted,
    Deprecated,
}

/// <summary>
/// Single ledger entry — typed mirror of a JSON record in
/// <c>knowledge-base/verified_facts.json</c>. Field names match the
/// JSON keys exactly so callers who previously did ad-hoc
/// <c>JsonNode</c> walks can migrate one call at a time.
/// </summary>
public sealed record VerifiedFact(
    string Id,
    string Claim,
    string Category,
    VerifiedFactConfidence Confidence,
    string? Rva,
    IReadOnlyList<string> ToolsConsensus,
    string? FirstDocumented,
    string? LastVerified,
    string? EngineBuildHash,
    string? Notes,
    string? Supersedes)
{
    /// <summary>
    /// Parse the RVA field (which in the ledger is a hex string like
    /// "0x7B8890") into an integer. Returns null for entries that don't
    /// have an RVA (some categories like <c>behavior_finding</c> don't).
    /// </summary>
    public long? RvaAsLong()
    {
        if (string.IsNullOrWhiteSpace(Rva))
        {
            return null;
        }
        var span = Rva.AsSpan();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }
        return long.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Returns true if this entry is confirmed by ≥2 independent tools —
    /// the bar the Ralph loop's "hard rule" uses for hooks. Callers that
    /// install detours should gate on this predicate, not on
    /// <see cref="VerifiedFactConfidence.Verified"/> alone.
    /// </summary>
    public bool HasMultiToolConsensus => ToolsConsensus.Count >= 2;
}

/// <summary>
/// Thrown when the ledger JSON violates schema invariants that the
/// editor depends on. The whole point of the typed wrapper is that the
/// editor crashes loudly at startup rather than limping along with a
/// drifted ledger — the message includes every offending entry so the
/// operator can fix the ledger in one edit cycle.
/// </summary>
public sealed class LedgerDriftException : Exception
{
    public LedgerDriftException(string message) : base(message)
    {
    }

    public LedgerDriftException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Loads and validates <c>knowledge-base/verified_facts.json</c>.
///
/// Schema invariants enforced on load:
///   1. Every entry has a non-empty <c>claim</c>, <c>category</c>, and
///      <c>confidence</c>.
///   2. <c>confidence</c> value maps to a known
///      <see cref="VerifiedFactConfidence"/>.
///   3. <c>tools_consensus</c> is an array of strings (can be empty).
///   4. Every entry labelled <see cref="VerifiedFactConfidence.Verified"/>
///      (the multi-tool variant) has at least two entries in its
///      <c>tools_consensus</c>. A single-tool VERIFIED claim is
///      ambiguous and must either be downgraded or cross-validated
///      before the editor trusts it; crashing loud here avoids
///      silently installing a hook on a single-tool claim.
///   5. <c>rva</c>, when present, parses as a hex integer.
///
/// Invariants NOT enforced (delegated to the Python linter):
///   * <c>last_verified</c> freshness-vs-today window
///   * <c>evidence_ref</c> shape per tool
///   * category value membership (the Python linter has the
///     authoritative list; this wrapper is permissive so a new
///     category can land in the JSON before the C# side is updated)
/// </summary>
public static class VerifiedFactsLoader
{
    private static readonly IReadOnlyDictionary<string, VerifiedFactConfidence> _confidenceMap =
        new Dictionary<string, VerifiedFactConfidence>(StringComparer.OrdinalIgnoreCase)
        {
            ["VERIFIED"] = VerifiedFactConfidence.Verified,
            ["VERIFIED-NEGATIVE"] = VerifiedFactConfidence.VerifiedNegative,
            ["VERIFIED-EXISTS"] = VerifiedFactConfidence.VerifiedExists,
            ["LIVE_OBSERVED"] = VerifiedFactConfidence.LiveObserved,
            ["UNVERIFIED"] = VerifiedFactConfidence.Unverified,
            ["REFUTED"] = VerifiedFactConfidence.Refuted,
            ["DEPRECATED"] = VerifiedFactConfidence.Deprecated,
        };

    /// <summary>
    /// Load the ledger from a file path. Throws
    /// <see cref="LedgerDriftException"/> if the JSON is malformed or
    /// any entry fails a schema invariant.
    /// </summary>
    public static VerifiedFactsLedger LoadFromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Verified facts ledger not found at '{path}'", path);
        }
        var text = File.ReadAllText(path);
        return LoadFromJson(text);
    }

    /// <summary>
    /// Load the ledger from a JSON string. Exposed separately so tests
    /// can pass synthetic fixtures without touching the filesystem.
    /// </summary>
    public static VerifiedFactsLedger LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LedgerDriftException($"Ledger JSON is malformed: {ex.Message}", ex);
        }
        if (rootNode is not JsonObject root)
        {
            throw new LedgerDriftException(
                "Ledger root must be a JSON object whose keys are entry ids.");
        }

        var facts = new Dictionary<string, VerifiedFact>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var kv in root)
        {
            var entryId = kv.Key;
            if (kv.Value is not JsonObject entry)
            {
                errors.Add($"{entryId}: expected JSON object, got {kv.Value?.GetType().Name ?? "null"}");
                continue;
            }
            try
            {
                facts[entryId] = ParseEntry(entryId, entry);
            }
            catch (LedgerDriftException ex)
            {
                errors.Add(ex.Message);
            }
        }

        if (errors.Count > 0)
        {
            throw new LedgerDriftException(
                "Ledger failed schema validation:\n  - " +
                string.Join("\n  - ", errors));
        }

        return new VerifiedFactsLedger(facts);
    }

    private static VerifiedFact ParseEntry(string entryId, JsonObject entry)
    {
        var claim = RequireString(entryId, entry, "claim");
        var category = RequireString(entryId, entry, "category");
        var confidenceRaw = RequireString(entryId, entry, "confidence");
        if (!_confidenceMap.TryGetValue(confidenceRaw, out var confidence))
        {
            throw new LedgerDriftException(
                $"{entryId}: unknown confidence '{confidenceRaw}' " +
                "(expected one of: VERIFIED, VERIFIED-NEGATIVE, VERIFIED-EXISTS, " +
                "LIVE_OBSERVED, UNVERIFIED, REFUTED, DEPRECATED)");
        }

        var toolsConsensus = ReadStringArray(entryId, entry, "tools_consensus");
        if (confidence == VerifiedFactConfidence.Verified && toolsConsensus.Count < 2)
        {
            throw new LedgerDriftException(
                $"{entryId}: confidence=VERIFIED requires tools_consensus length >= 2 " +
                $"(got {toolsConsensus.Count}) — downgrade to UNVERIFIED or add the " +
                "second tool's evidence_ref.");
        }

        var rva = OptionalString(entry, "rva");
        if (rva is not null)
        {
            ValidateRvaFormat(entryId, rva);
        }

        return new VerifiedFact(
            Id: entryId,
            Claim: claim,
            Category: category,
            Confidence: confidence,
            Rva: rva,
            ToolsConsensus: toolsConsensus,
            FirstDocumented: OptionalString(entry, "first_documented"),
            LastVerified: OptionalString(entry, "last_verified"),
            EngineBuildHash: OptionalString(entry, "engine_build_hash"),
            Notes: OptionalString(entry, "notes"),
            Supersedes: OptionalString(entry, "supersedes"));
    }

    private static string RequireString(string entryId, JsonObject entry, string field)
    {
        if (!entry.TryGetPropertyValue(field, out var node) || node is null)
        {
            throw new LedgerDriftException($"{entryId}: missing required field '{field}'");
        }
        var value = node.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LedgerDriftException($"{entryId}: field '{field}' must be non-empty");
        }
        return value;
    }

    private static string? OptionalString(JsonObject entry, string field)
    {
        if (!entry.TryGetPropertyValue(field, out var node) || node is null)
        {
            return null;
        }
        try
        {
            return node.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadStringArray(string entryId, JsonObject entry, string field)
    {
        if (!entry.TryGetPropertyValue(field, out var node) || node is null)
        {
            throw new LedgerDriftException($"{entryId}: missing required field '{field}'");
        }
        if (node is not JsonArray array)
        {
            throw new LedgerDriftException($"{entryId}: field '{field}' must be an array");
        }
        var results = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is null)
            {
                continue;
            }
            try
            {
                results.Add(item.GetValue<string>());
            }
            catch (InvalidOperationException)
            {
                throw new LedgerDriftException(
                    $"{entryId}: field '{field}' must contain only strings");
            }
        }
        return results;
    }

    private static void ValidateRvaFormat(string entryId, string rva)
    {
        var span = rva.AsSpan();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }
        if (span.Length == 0 ||
            !long.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            throw new LedgerDriftException(
                $"{entryId}: rva='{rva}' is not a hex integer (expected '0xNNNN' or bare hex).");
        }
    }
}

/// <summary>
/// Typed query surface over the loaded ledger. Immutable; the
/// underlying dictionary is wrapped in a read-only view.
/// </summary>
public sealed class VerifiedFactsLedger
{
    private readonly IReadOnlyDictionary<string, VerifiedFact> _entries;

    internal VerifiedFactsLedger(IReadOnlyDictionary<string, VerifiedFact> entries)
    {
        _entries = entries;
    }

    /// <summary>Every entry keyed by id.</summary>
    public IReadOnlyDictionary<string, VerifiedFact> Entries => _entries;

    /// <summary>Total entry count (VERIFIED, DEPRECATED, etc. all included).</summary>
    public int Count => _entries.Count;

    /// <summary>Lookup an entry by id. Returns null when not found.</summary>
    public VerifiedFact? TryGet(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _entries.TryGetValue(id, out var entry) ? entry : null;
    }

    /// <summary>
    /// Get the RVA for an entry. Returns null if the entry is missing,
    /// has no RVA, or the RVA field is not hex-parseable.
    /// </summary>
    public long? GetRva(string id)
    {
        return TryGet(id)?.RvaAsLong();
    }

    /// <summary>
    /// True when the entry exists and is VERIFIED with multi-tool
    /// consensus. This is the predicate hook-install code should use.
    /// </summary>
    public bool IsMultiToolVerified(string id)
    {
        var entry = TryGet(id);
        return entry is { Confidence: VerifiedFactConfidence.Verified }
            && entry.HasMultiToolConsensus;
    }

    /// <summary>Filter by category (exact match).</summary>
    public IEnumerable<VerifiedFact> ByCategory(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return _entries.Values.Where(e =>
            string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Filter by confidence tier.</summary>
    public IEnumerable<VerifiedFact> ByConfidence(VerifiedFactConfidence confidence)
    {
        return _entries.Values.Where(e => e.Confidence == confidence);
    }
}
