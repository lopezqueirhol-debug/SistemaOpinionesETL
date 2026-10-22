namespace SistemaOpiniones.Etl.Domain;

/// <summary>Error general del proceso ETL (detiene el procesamiento del archivo actual).</summary>
public class EtlException : Exception
{
    public EtlException(string message) : base(message) { }
    public EtlException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Problema en appsettings.json, variables de entorno o argumentos.</summary>
public sealed class ConfigurationException : EtlException
{
    public ConfigurationException(string message) : base(message) { }
    public ConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Archivo ausente, vacio, ilegible o con encabezados que no cumplen la estructura esperada.</summary>
public sealed class CsvStructureException : EtlException
{
    public CsvStructureException(string message) : base(message) { }
    public CsvStructureException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>No existe en FuenteDatos ninguna fuente del tipo requerido.</summary>
public sealed class FuenteNoEncontradaException : EtlException
{
    public FuenteNoEncontradaException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Un registro individual no es valido. NO detiene el proceso: el registro se
/// rechaza, se registra el motivo y se continua con el siguiente.
/// </summary>
public sealed class RowValidationException : Exception
{
    public RowValidationException(RejectionReason reason, string message) : base(message)
    {
        Reason = reason;
    }

    public RejectionReason Reason { get; }
}