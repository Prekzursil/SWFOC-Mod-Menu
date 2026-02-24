#pragma warning disable S4136
using System.Text;
using System.Text.Json;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;

namespace SwfocTrainer.Core.Logging;

public sealed class FileAuditLogger : IAuditLogger
{
    private readonly string _logDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FileAuditLogger(string? logDirectory)
    {
        var appRoot = TrustedPathPolicy.GetOrCreateAppDataRoot();
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? TrustedPathPolicy.CombineUnderRoot(appRoot, "logs")
            : TrustedPathPolicy.EnsureSubPath(appRoot, logDirectory);

        Directory.CreateDirectory(_logDirectory);
    }

    public async Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken)
    {
        var fileName = $"audit-{record.Timestamp:yyyy-MM-dd}.jsonl";
        var path = Path.Combine(_logDirectory, fileName);
        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;

        await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken);
    }

    public FileAuditLogger()
        : this(null)
    {
    }

    public Task WriteAsync(ActionAuditRecord record)
    {
        return WriteAsync(record, CancellationToken.None);
    }
}
