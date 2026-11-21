namespace SistemaOpiniones.Etl.Domain;

public sealed record ExtractedRecord(
    string SourceType,
    string SourceName,
    string ExternalId,
    string? ClientId,
    string? ProductId,
    DateTime EventDateUtc,
    string Comment,
    int? Score
);