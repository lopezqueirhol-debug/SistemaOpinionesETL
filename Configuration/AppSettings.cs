namespace SistemaOpiniones.Etl.Configuration;

/// <summary>Estructura del archivo appsettings.json.</summary>
public sealed class RootSettings
{
    public Dictionary<string, string> ConnectionStrings { get; set; } = new();
    public EtlOptions Etl { get; set; } = new();
}

/// <summary>Opciones propias del proceso ETL.</summary>
public sealed class EtlOptions
{
    /// <summary>Carpeta con los CSV (relativa al ejecutable o al directorio actual, o absoluta).</summary>
    public string CsvDirectory { get; set; } = "Data";

    /// <summary>Carpeta donde se guardan el log de ejecucion y el CSV de rechazados.</summary>
    public string LogDirectory { get; set; } = "Logs";

    /// <summary>Cuantos rechazos de ejemplo se muestran por archivo en pantalla.</summary>
    public int MaxRejectionsShownPerFile { get; set; } = 5;

    /// <summary>Fecha minima aceptada (yyyy-MM-dd).</summary>
    public string MinDate { get; set; } = "2000-01-01";

    /// <summary>Si es true, se rechazan fechas posteriores a hoy.</summary>
    public bool RejectFutureDates { get; set; } = true;

    /// <summary>
    /// Regla para resolver IdFuente: tipo de fuente (FuenteDatos.TipoFuente) que
    /// corresponde a cada archivo transaccional.
    /// </summary>
    public Dictionary<string, string> TipoFuentePorArchivo { get; set; } = new();
}

/// <summary>Configuracion ya validada y lista para usar.</summary>
public sealed class AppSettings
{
    public AppSettings(string connectionString, EtlOptions etl)
    {
        ConnectionString = connectionString;
        Etl = etl;
    }

    public string ConnectionString { get; }
    public EtlOptions Etl { get; }
}