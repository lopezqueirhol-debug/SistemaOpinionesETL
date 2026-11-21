using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;

namespace SistemaOpiniones.Etl.Infrastructure;

public sealed class CsvSurveyExtractor : IExtractor
{
    private readonly WorkerSourceOptions _options;
    private readonly ILogger<CsvSurveyExtractor> _logger;

    public CsvSurveyExtractor(IOptions<WorkerSourceOptions> options, ILogger<CsvSurveyExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string SourceName => "surveys_part1.csv";
    public string SourceType => "CSV";

    public async Task<IReadOnlyList<ExtractedRecord>> ExtractAsync(CancellationToken cancellationToken)
    {
        var items = new List<ExtractedRecord>();
        if (!File.Exists(_options.SurveysCsvPath))
        {
            _logger.LogWarning("Archivo CSV no encontrado en {Path}", _options.SurveysCsvPath);
            return items;
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null
        };

        using var reader = new StreamReader(_options.SurveysCsvPath);
        using var csv = new CsvReader(reader, config);

        await foreach (var row in csv.GetRecordsAsync<SurveyRow>(cancellationToken))
        {
            items.Add(new ExtractedRecord(
                SourceType: SourceType,
                SourceName: SourceName,
                ExternalId: row.IdOpinion ?? Guid.NewGuid().ToString(),
                ClientId: row.IdCliente,
                ProductId: row.IdProducto,
                EventDateUtc: (row.Fecha ?? DateTime.UtcNow).ToUniversalTime(),
                Comment: row.Comentario ?? string.Empty,
                Score: row.PuntajeSatisfaccion
            ));
        }

        return items;
    }

    private sealed class SurveyRow
    {
        public string? IdOpinion { get; set; }
        public string? IdCliente { get; set; }
        public string? IdProducto { get; set; }
        public DateTime? Fecha { get; set; }
        public string? Comentario { get; set; }

        // El CSV trae el encabezado con tilde ("PuntajeSatisfacción"). CsvHelper compara
        // nombres ignorando mayusculas pero NO ignora tildes, asi que sin este atributo
        // la columna nunca se enlaza y Score sale siempre null.
        [Name("PuntajeSatisfacción")]
        public int? PuntajeSatisfaccion { get; set; }
    }
}