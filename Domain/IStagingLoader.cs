namespace SistemaOpiniones.Etl.Domain;

public interface IStagingLoader
{
    Task SaveBatchAsync(
        string sourceName,
        IReadOnlyList<ExtractedRecord> records,
        CancellationToken cancellationToken);
}