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
                   ?? throw new ConfigurationException($"El archivo '{path}' esta vacío.");
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
