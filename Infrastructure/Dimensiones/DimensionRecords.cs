using System.Text.Json.Serialization;

namespace SistemaOpiniones.Etl.Infrastructure.Dimensiones;

/// <summary>Fila de DimCliente.</summary>
public sealed record ClienteDimRecord(
    [property: JsonPropertyName("IdClienteNK")] string IdClienteNK,
    [property: JsonPropertyName("Nombre")] string Nombre,
    [property: JsonPropertyName("Email")] string Email);

/// <summary>Fila de DimProducto.</summary>
public sealed record ProductoDimRecord(
    [property: JsonPropertyName("IdProductoNK")] string IdProductoNK,
    [property: JsonPropertyName("Nombre")] string Nombre,
    [property: JsonPropertyName("Categoria")] string Categoria);

/// <summary>Resultado de cargar una dimension: cuantas filas se insertaron y cuantas se actualizaron.</summary>
public sealed record DimensionLoadResult(
    string Dimension,
    int Extraidas,
    int Nuevos,
    int Modificados,
    TimeSpan Duracion)
{
    public int SinCambios => Extraidas - Nuevos - Modificados;
}
