using Microsoft.Extensions.Options;
using MySqlConnector;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;

namespace SistemaOpiniones.Etl.Infrastructure;

public sealed class RelationalReviewsExtractor : IExtractor
{
    private readonly WorkerSourceOptions _options;

    public RelationalReviewsExtractor(IOptions<WorkerSourceOptions> options) => _options = options.Value;

    public string SourceName => "RelationalReviews";
    public string SourceType => "DATABASE";

    public async Task<IReadOnlyList<ExtractedRecord>> ExtractAsync(CancellationToken cancellationToken)
    {
        var results = new List<ExtractedRecord>();
        if (string.IsNullOrWhiteSpace(_options.ReviewsConnectionString))
            return results;

        await using var conn = new MySqlConnection(_options.ReviewsConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _options.ReviewsQuery;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.IsDBNull(0) ? Guid.NewGuid().ToString() : reader.GetValue(0).ToString()!;
            var clientId = reader.IsDBNull(1) ? null : reader.GetValue(1).ToString();
            var productId = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString();
            var date = reader.IsDBNull(3) ? DateTime.UtcNow : Convert.ToDateTime(reader.GetValue(3)).ToUniversalTime();
            var comment = reader.IsDBNull(4) ? string.Empty : reader.GetValue(4).ToString()!;
            int? score = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5));

            results.Add(new ExtractedRecord(SourceType, SourceName, id, clientId, productId, date, comment, score));
        }

        return results;
    }
}