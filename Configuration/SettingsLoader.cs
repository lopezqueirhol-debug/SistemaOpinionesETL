using System.Text.Json;
using SistemaOpiniones.Etl.Domain;

namespace SistemaOpiniones.Etl.Configuration;

public static class SettingsLoader
{
    private const string SettingsFileName = "appsettings.json";
    private const string ConnectionName = "SistemaOpiniones";
    private const string ConnectionEnvVar = "ETL_CONNECTION_STRING";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Lee appsettings.json. La variable de entorno ETL_CONNECTION_STRING, si existe,
    /// tiene prioridad sobre la cadena de conexion del archivo.
    /// </summary>
    public static AppSettings Load()
    {
        var path = FindSettingsFile();

        RootSettings root;
        try
        {
            var json = File.ReadAllText(path);
            root = JsonSerializer.Deserialize<RootSettings>(json, JsonOptions)
                   ?? throw new ConfigurationException($"El archivo '{path}' esta vacio.");
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"El archivo '{path}' no es un JSON valido: {ex.Message}", ex);
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            root.ConnectionStrings.TryGetValue(ConnectionName, out connectionString);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ConfigurationException(
                $"No hay cadena de conexion. Defina ConnectionStrings:{ConnectionName} en {SettingsFileName} " +
                $"o la variable de entorno {ConnectionEnvVar}.");
        }

        if (connectionString.Contains("TU_PASSWORD", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException(
                $"Configure la contraseña de MySQL en {SettingsFileName} (reemplace TU_PASSWORD) " +
                $"o use la variable de entorno {ConnectionEnvVar}.");
        }

        // Los nombres de archivo no distinguen mayusculas/minusculas.
        root.Etl.TipoFuentePorArchivo = new Dictionary<string, string>(
            root.Etl.TipoFuentePorArchivo, StringComparer.OrdinalIgnoreCase);

        return new AppSettings(connectionString, root.Etl);
    }

    /// <summary>
    /// Aplica overrides de entorno a las fuentes del worker. Una variable definida tiene
    /// prioridad sobre appsettings.json, de modo que ninguna credencial quede en el repositorio.
    /// </summary>
    public static void ApplyEnvironmentOverrides(WorkerSourceOptions options)
    {
        Apply("ETL_SURVEYS_CSV_PATH", value => options.SurveysCsvPath = value);
        Apply("ETL_REVIEWS_CONNECTION_STRING", value => options.ReviewsConnectionString = value);
        Apply("ETL_REVIEWS_QUERY", value => options.ReviewsQuery = value);
        Apply("ETL_COMMENTS_API_URL", value => options.CommentsApiBaseUrl = value);
        Apply("ETL_COMMENTS_API_PATH", value => options.CommentsApiPath = value);
        Apply("ETL_COMMENTS_API_KEY", value => options.CommentsApiKey = value);
        Apply("ETL_STAGING_DIRECTORY", value => options.StagingDirectory = value);
        ApplyInt("ETL_INTERVAL_MINUTES", value => options.IntervalMinutes = value);
        ApplyInt("ETL_MAX_CONCURRENCY", value => options.MaxConcurrency = value);
    }

    private static void Apply(string name, Action<string> setter)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value)) setter(value);
    }

    private static void ApplyInt(string name, Action<int> setter)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(value, out var parsed)) setter(parsed);
    }

    /// <summary>Resuelve una carpeta de salida (logs) relativa al ejecutable.</summary>
    public static string ResolveOutputDirectory(string configured)
    {
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    private static string FindSettingsFile()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, SettingsFileName),
            Path.Combine(Directory.GetCurrentDirectory(), SettingsFileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        throw new ConfigurationException(
            $"No se encontro {SettingsFileName}. Se busco en: {string.Join(" | ", candidates)}");
    }
}