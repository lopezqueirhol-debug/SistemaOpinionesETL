using SistemaOpiniones.Etl.Data;
using SistemaOpiniones.Etl.Domain;

namespace SistemaOpiniones.Etl.Services;

/// <summary>Datos compartidos por todos los cargadores durante una ejecucion.</summary>
public sealed record LoadContext(EtlRepository Repository, Guid RunId, string CsvDirectory);

/// <summary>Carga un archivo CSV en su tabla destino.</summary>
public interface IFileLoader
{
    string FileName { get; }
    string TableName { get; }

    /// <summary>True para las tablas maestras (FuenteDatos, Cliente, Producto).</summary>
    bool IsMaster { get; }

    FileLoadResult Load(LoadContext context);
}
