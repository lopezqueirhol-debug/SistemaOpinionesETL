namespace SistemaOpiniones.Etl.Configuration;

public sealed class WorkerSourceOptions
{
    public string SurveysCsvPath { get; set; } = "Data/surveys_part1.csv";
    public string ReviewsConnectionString { get; set; } = string.Empty;
    public string ReviewsQuery { get; set; } =
        "SELECT IdReview, IdCliente, IdProducto, Fecha, Comentario, Rating FROM ResenaWeb";
    public string CommentsApiBaseUrl { get; set; } = string.Empty;
    public string CommentsApiPath { get; set; } = "/comments";
    public string CommentsApiKey { get; set; } = string.Empty;
    public string StagingDirectory { get; set; } = "Staging";
    public int IntervalMinutes { get; set; } = 15;
    public int MaxConcurrency { get; set; } = 3;
}