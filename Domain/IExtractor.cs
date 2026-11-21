namespace SistemaOpiniones.Etl.Domain;

public interface IExtractor
{
    string SourceName { get; }
    string SourceType { get; }
    Task<IReadOnlyList<ExtractedRecord>> ExtractAsync(CancellationToken cancellationToken);
}