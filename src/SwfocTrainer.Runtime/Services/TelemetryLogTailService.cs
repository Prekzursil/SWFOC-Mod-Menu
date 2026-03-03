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

    private readonly object _sync = new();
    private readonly Dictionary<string, long> _cursorByPath = new(StringComparer.OrdinalIgnoreCase);

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

    private static ParsedTelemetryLine? ParseLatestTelemetry(IEnumerable<string> lines)
    {
        foreach (var line in lines.Reverse())
        {
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
}
