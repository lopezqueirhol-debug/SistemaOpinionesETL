using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;

namespace SistemaOpiniones.Etl.Infrastructure.Staging;

public sealed class JsonStagingLoader : IStagingLoader
{
    private readonly ILogger<JsonStagingLoader> _logger;
    private readonly WorkerSourceOptions _options;

    public JsonStagingLoader(ILogger<JsonStagingLoader> logger, IOptions<WorkerSourceOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task SaveBatchAsync(
        string sourceName,
        IReadOnlyList<ExtractedRecord> records,
        CancellationToken cancellationToken)
    {
        var stagingDir = Path.IsPathRooted(_options.StagingDirectory)
            ? _options.StagingDirectory
            : Path.Combine(Directory.GetCurrentDirectory(), _options.StagingDirectory);

        Directory.CreateDirectory(stagingDir);

        var safeName = string.Concat(sourceName.Where(char.IsLetterOrDigit));
        var unique = Guid.NewGuid().ToString("N")[..8];
        var filePath = Path.Combine(
            stagingDir,
            $"staging_{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{unique}.json");

        var json = System.Text.Json.JsonSerializer.Serialize(records, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        _logger.LogInformation("Guardados {Count} registros en staging: {File}", records.Count, filePath);
    }
}
