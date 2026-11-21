using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;

namespace SistemaOpiniones.Etl.Infrastructure;

public sealed class RestCommentsExtractor : IExtractor
{
    // System.Text.Json es sensible a mayusculas por defecto; una API externa (sobre todo si
    // no esta escrita en C#) rara vez devuelve exactamente "Id", "ClientId", etc. en PascalCase.
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorkerSourceOptions _options;

    public RestCommentsExtractor(IHttpClientFactory httpClientFactory, IOptions<WorkerSourceOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public string SourceName => "CommentsRestApi";
    public string SourceType => "API_REST";

    public async Task<IReadOnlyList<ExtractedRecord>> ExtractAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.CommentsApiBaseUrl))
            return [];

        var client = _httpClientFactory.CreateClient("CommentsApi");
        client.BaseAddress = new Uri(_options.CommentsApiBaseUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, _options.CommentsApiPath);
        if (!string.IsNullOrWhiteSpace(_options.CommentsApiKey))
            request.Headers.Add("X-Api-Key", _options.CommentsApiKey);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<ApiCommentItem>>(
                          JsonOptions, cancellationToken: cancellationToken)
                      ?? [];

        return payload.Select(c => new ExtractedRecord(
            SourceType: SourceType,
            SourceName: SourceName,
            ExternalId: c.Id,
            ClientId: c.ClientId,
            ProductId: c.ProductId,
            EventDateUtc: (c.CreatedAt ?? DateTime.UtcNow).ToUniversalTime(),
            Comment: c.Comment ?? string.Empty,
            Score: c.Score
        )).ToList();
    }

    private sealed record ApiCommentItem(
        string Id,
        string? ClientId,
        string? ProductId,
        DateTime? CreatedAt,
        string? Comment,
        int? Score
    );
}