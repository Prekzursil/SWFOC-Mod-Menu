using System.Text;
using System.Text.Json;
using SwfocTrainer.Core.Contracts;

namespace SwfocTrainer.Core.Logging;

public sealed class FileAuditLogger : IAuditLogger
{
    private readonly string _logDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FileAuditLogger(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwfocTrainer",
            "logs");

        Directory.CreateDirectory(_logDirectory);
    }

    public async Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken = default)
    {
        var fileName = $"audit-{record.Timestamp:yyyy-MM-dd}.jsonl";
        var path = Path.Combine(_logDirectory, fileName);
        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;

        await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken);
    }
}
