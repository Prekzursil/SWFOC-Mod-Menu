using System.Globalization;
using System.Text.RegularExpressions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class TelemetryLogTailService : ITelemetryLogTailService
{
    private static readonly Regex TelemetryLineRegex = new(
        @"SWFOC_TRAINER_TELEMETRY\s+timestamp=(?<timestamp>\S+)\s+mode=(?<mode>[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(250));

    private const string HelperOperationPrefix = "SWFOC_TRAINER_";

    private readonly object _sync = new();
    private readonly Dictionary<string, long> _cursorByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _operationCursorByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _autoloadCursorByPath = new(StringComparer.OrdinalIgnoreCase);

    public TelemetryModeResolution ResolveLatestMode(
        string? processPath,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return TelemetryModeResolution.Unavailable("telemetry_process_path_missing");
        }

        var logPath = ResolveLogPath(processPath);
        if (logPath is null)
        {
            return TelemetryModeResolution.Unavailable("telemetry_log_missing");
        }

        ParsedTelemetryLine? parsed = null;
        lock (_sync)
        {
            var cursor = _cursorByPath.TryGetValue(logPath, out var stored) ? stored : 0L;
            parsed = ReadLatestTelemetryLine(logPath, ref cursor);
            _cursorByPath[logPath] = cursor;
        }

        if (parsed is null)
        {
            return TelemetryModeResolution.Unavailable("telemetry_line_missing");
        }

        return ResolveTelemetry(parsed, logPath, nowUtc, freshnessWindow);
    }


    public HelperOperationVerification VerifyOperationToken(
        string? processPath,
        string operationToken,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow)
    {
        if (string.IsNullOrWhiteSpace(operationToken))
        {
            return HelperOperationVerification.Unavailable("helper_operation_token_missing");
        }

        if (string.IsNullOrWhiteSpace(processPath))
        {
            return HelperOperationVerification.Unavailable("telemetry_process_path_missing");
        }

        var resolvedProcessPath = processPath;
        var resolvedOperationToken = operationToken;
        return VerifyOperationTokenCore(resolvedProcessPath, resolvedOperationToken, nowUtc, freshnessWindow);
    }

    public HelperAutoloadVerification VerifyAutoloadProfile(
        string? processPath,
        string? profileId,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return HelperAutoloadVerification.Unavailable("helper_autoload_profile_missing");
        }

        if (string.IsNullOrWhiteSpace(processPath))
        {
            return HelperAutoloadVerification.Unavailable("telemetry_process_path_missing");
        }

        return VerifyAutoloadProfileCore(processPath, profileId, nowUtc, freshnessWindow);
    }


    private HelperOperationVerification VerifyOperationTokenCore(
        string processPath,
        string operationToken,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow)
    {
        var logPath = ResolveLogPath(processPath);
        if (logPath is null)
        {
            return HelperOperationVerification.Unavailable("telemetry_log_missing");
        }

        ParsedHelperOperationLine? parsed = null;
        lock (_sync)
        {
            var cursor = _operationCursorByPath.TryGetValue(logPath, out var stored) ? stored : 0L;
            parsed = ReadLatestHelperOperationLine(logPath, operationToken, ref cursor);
            _operationCursorByPath[logPath] = cursor;
        }

        if (parsed is null)
        {
            return HelperOperationVerification.Unavailable("helper_operation_token_not_found");
        }

        var timestamp = parsed.TimestampUtc ?? File.GetLastWriteTimeUtc(logPath);
        var timestampUtc = new DateTimeOffset(timestamp, TimeSpan.Zero);
        if (nowUtc - timestampUtc > freshnessWindow)
        {
            return HelperOperationVerification.Unavailable("helper_operation_token_stale");
        }

        if (!parsed.Applied)
        {
            return HelperOperationVerification.Unavailable("helper_operation_reported_failed");
        }

        return new HelperOperationVerification(
            Verified: true,
            ReasonCode: "helper_operation_token_verified",
            SourcePath: logPath,
            TimestampUtc: timestampUtc,
            RawLine: parsed.RawLine);
    }

    private HelperAutoloadVerification VerifyAutoloadProfileCore(
        string processPath,
        string profileId,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow)
    {
        var logPath = ResolveLogPath(processPath);
        if (logPath is null)
        {
            return HelperAutoloadVerification.Unavailable("telemetry_log_missing");
        }

        ParsedHelperAutoloadLine? parsed = null;
        lock (_sync)
        {
            var cursor = _autoloadCursorByPath.TryGetValue(logPath, out var stored) ? stored : 0L;
            parsed = ReadLatestHelperAutoloadLine(logPath, profileId, ref cursor);
            _autoloadCursorByPath[logPath] = cursor;
        }

        if (parsed is null)
        {
            return HelperAutoloadVerification.Unavailable("helper_autoload_not_found");
        }

        var timestamp = parsed.TimestampUtc ?? File.GetLastWriteTimeUtc(logPath);
        var timestampUtc = new DateTimeOffset(timestamp, TimeSpan.Zero);
        if (nowUtc - timestampUtc > freshnessWindow)
        {
            return HelperAutoloadVerification.Unavailable("helper_autoload_stale");
        }

        if (!parsed.Ready)
        {
            return HelperAutoloadVerification.Unavailable("helper_autoload_reported_failed");
        }

        return new HelperAutoloadVerification(
            Ready: true,
            ReasonCode: "helper_autoload_ready",
            SourcePath: logPath,
            TimestampUtc: timestampUtc,
            RawLine: parsed.RawLine,
            Strategy: parsed.Strategy,
            Script: parsed.Script);
    }


    private static string? ResolveLogPath(string processPath)
    {
        var processDirectory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(processDirectory))
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(processDirectory, "_LogFile.txt"),
            Path.Combine(processDirectory, "LogFile.txt"),
            Path.Combine(processDirectory, "corruption", "LogFile.txt"),
            Path.Combine(Directory.GetParent(processDirectory)?.FullName ?? processDirectory, "corruption", "LogFile.txt")
        };

        return candidates
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static ParsedTelemetryLine? ReadLatestTelemetryLine(string logPath, ref long cursor)
    {
        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (cursor < 0 || cursor > stream.Length)
        {
            cursor = 0;
        }

        stream.Seek(cursor, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        cursor = stream.Position;
        var fromNewLines = ParseLatestTelemetry(lines);
        if (fromNewLines is not null)
        {
            return fromNewLines;
        }

        // Fallback for process attach scenarios where telemetry was emitted before cursor initialization.
        stream.Seek(0, SeekOrigin.Begin);
        reader.DiscardBufferedData();
        var allLines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (!string.IsNullOrWhiteSpace(line))
            {
                allLines.Add(line);
            }
        }

        return ParseLatestTelemetry(allLines.TakeLast(256));
    }

    private static ParsedHelperOperationLine? ReadLatestHelperOperationLine(string logPath, string operationToken, ref long cursor)
    {
        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (cursor < 0 || cursor > stream.Length)
        {
            cursor = 0;
        }

        stream.Seek(cursor, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        cursor = stream.Position;
        var fromNewLines = ParseLatestHelperOperation(lines, operationToken);
        if (fromNewLines is not null)
        {
            return fromNewLines;
        }

        stream.Seek(0, SeekOrigin.Begin);
        reader.DiscardBufferedData();
        var allLines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (!string.IsNullOrWhiteSpace(line))
            {
                allLines.Add(line);
            }
        }

        return ParseLatestHelperOperation(allLines.TakeLast(512).ToArray(), operationToken);
    }

    private static ParsedHelperAutoloadLine? ReadLatestHelperAutoloadLine(string logPath, string profileId, ref long cursor)
    {
        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (cursor < 0 || cursor > stream.Length)
        {
            cursor = 0;
        }

        stream.Seek(cursor, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        cursor = stream.Position;
        var fromNewLines = ParseLatestHelperAutoload(lines, profileId);
        if (fromNewLines is not null)
        {
            return fromNewLines;
        }

        stream.Seek(0, SeekOrigin.Begin);
        reader.DiscardBufferedData();
        var allLines = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (!string.IsNullOrWhiteSpace(line))
            {
                allLines.Add(line);
            }
        }

        return ParseLatestHelperAutoload(allLines.TakeLast(512).ToArray(), profileId);
    }

    private static ParsedHelperOperationLine? ParseLatestHelperOperation(IReadOnlyList<string>? lines, string operationToken)
    {
        var safeLines = lines;
        if (safeLines is null || safeLines.Count == 0)
        {
            return null;
        }

        for (var index = safeLines.Count - 1; index >= 0; index--)
        {
            var line = safeLines[index];
            var parsed = ParseHelperOperationLine(line, operationToken);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static ParsedHelperOperationLine? ParseHelperOperationLine(string line, string operationToken)
    {
        if (line is null || string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var safeLine = line;
        var tokens = safeLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return null;
        }

        if (!TryResolveOperationStatus(tokens[0], out var isApplied))
        {
            return null;
        }

        if (!tokens[1].Equals(operationToken, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ParsedHelperOperationLine(safeLine, isApplied, null);
    }

    private static bool TryResolveOperationStatus(string statusToken, out bool isApplied)
    {
        isApplied = false;
        var safeStatusToken = statusToken ?? string.Empty;
        if (string.IsNullOrWhiteSpace(safeStatusToken) || !safeStatusToken.StartsWith(HelperOperationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var status = safeStatusToken[HelperOperationPrefix.Length..];
        if (status.Equals("APPLIED", StringComparison.OrdinalIgnoreCase))
        {
            isApplied = true;
            return true;
        }

        return status.Equals("FAILED", StringComparison.OrdinalIgnoreCase);
    }

    private static ParsedHelperAutoloadLine? ParseLatestHelperAutoload(IReadOnlyList<string>? lines, string profileId)
    {
        if (lines is null || lines.Count == 0)
        {
            return null;
        }

        for (var index = lines.Count - 1; index >= 0; index--)
        {
            var parsed = ParseHelperAutoloadLine(lines[index], profileId);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static ParsedHelperAutoloadLine? ParseHelperAutoloadLine(string? line, string profileId)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return null;
        }

        var statusToken = tokens[0];
        bool? ready = statusToken.Equals("SWFOC_TRAINER_HELPER_AUTOLOAD_READY", StringComparison.OrdinalIgnoreCase)
            ? true
            : statusToken.Equals("SWFOC_TRAINER_HELPER_AUTOLOAD_FAILED", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
        if (ready is null)
        {
            return null;
        }

        var values = ParseKeyValueTokens(tokens.Skip(1));
        if (!values.TryGetValue("profile", out var lineProfile) ||
            !lineProfile.Equals(profileId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        values.TryGetValue("strategy", out var strategy);
        values.TryGetValue("script", out var script);
        return new ParsedHelperAutoloadLine(line, ready.Value, null, strategy, script);
    }

    private static Dictionary<string, string> ParseKeyValueTokens(IEnumerable<string> tokens)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var separatorIndex = token.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= token.Length - 1)
            {
                continue;
            }

            var key = token[..separatorIndex];
            var value = token[(separatorIndex + 1)..];
            values[key] = value;
        }

        return values;
    }

    private static ParsedTelemetryLine? ParseLatestTelemetry(IEnumerable<string> lines)
    {
        if (lines is null)
        {
            return null;
        }

        foreach (var line in lines.Reverse())
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var match = TelemetryLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            DateTime? timestamp = null;
            var timestampValue = match.Groups["timestamp"].Value;
            if (DateTime.TryParse(
                    timestampValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedTimestamp))
            {
                timestamp = parsedTimestamp;
            }

            return new ParsedTelemetryLine(
                RawLine: line,
                Mode: match.Groups["mode"].Value,
                TimestampUtc: timestamp);
        }
        return null;
    }

    private static bool TryParseRuntimeMode(string rawMode, out RuntimeMode mode)
    {
        if (rawMode.Equals("Galactic", StringComparison.OrdinalIgnoreCase))
        {
            mode = RuntimeMode.Galactic;
            return true;
        }

        if (rawMode.Equals("TacticalLand", StringComparison.OrdinalIgnoreCase) ||
            rawMode.Equals("Land", StringComparison.OrdinalIgnoreCase))
        {
            mode = RuntimeMode.TacticalLand;
            return true;
        }

        if (rawMode.Equals("TacticalSpace", StringComparison.OrdinalIgnoreCase) ||
            rawMode.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            mode = RuntimeMode.TacticalSpace;
            return true;
        }

        if (rawMode.Equals("AnyTactical", StringComparison.OrdinalIgnoreCase))
        {
            mode = RuntimeMode.AnyTactical;
            return true;
        }

        mode = RuntimeMode.Unknown;
        return false;
    }

    private static TelemetryModeResolution ResolveTelemetry(
        ParsedTelemetryLine parsed,
        string logPath,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow)
    {
        if (!TryParseRuntimeMode(parsed.Mode, out var mode))
        {
            return TelemetryModeResolution.Unavailable("telemetry_mode_unknown");
        }

        var timestamp = parsed.TimestampUtc ?? File.GetLastWriteTimeUtc(logPath);
        var timestampUtc = new DateTimeOffset(timestamp, TimeSpan.Zero);
        if (nowUtc - timestampUtc > freshnessWindow)
        {
            return TelemetryModeResolution.Unavailable("telemetry_stale");
        }

        return new TelemetryModeResolution(
            Available: true,
            Mode: mode,
            ReasonCode: "telemetry_mode_fresh",
            SourcePath: logPath,
            TimestampUtc: timestampUtc,
            RawLine: parsed.RawLine);
    }

    private sealed record ParsedTelemetryLine(string RawLine, string Mode, DateTime? TimestampUtc);

    private sealed record ParsedHelperOperationLine(string RawLine, bool Applied, DateTime? TimestampUtc);

    private sealed record ParsedHelperAutoloadLine(
        string RawLine,
        bool Ready,
        DateTime? TimestampUtc,
        string? Strategy,
        string? Script);
}
