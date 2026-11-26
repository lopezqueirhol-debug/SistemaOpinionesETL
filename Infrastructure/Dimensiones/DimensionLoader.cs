using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace SistemaOpiniones.Etl.Infrastructure.Dimensiones;

/// <summary>
/// Envia lotes de dimensiones a SistemaOpinionesDW mediante procedimientos
/// Cada llamada es un upsert por clave natural y se pueden repetir sin duplicar filas.
/// </summary>
public sealed class DimensionLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _dwConnectionString;
    private readonly ILogger<DimensionLoader> _logger;

    public DimensionLoader(string dwConnectionString, ILogger<DimensionLoader> logger)
    {
        _dwConnectionString = dwConnectionString;
        _logger = logger;
    }

    public Task<DimensionLoadResult> CargarClientesAsync(
        IReadOnlyList<ClienteDimRecord> clientes, CancellationToken ct) =>
        EjecutarAsync("DimCliente", "sp_CargarDimCliente", clientes, ct);

    public Task<DimensionLoadResult> CargarProductosAsync(
        IReadOnlyList<ProductoDimRecord> productos, CancellationToken ct) =>
        EjecutarAsync("DimProducto", "sp_CargarDimProducto", productos, ct);

    private async Task<DimensionLoadResult> EjecutarAsync<T>(
        string dimension, string procedimiento, IReadOnlyList<T> lote, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (lote.Count == 0)
        {
            _logger.LogWarning("{Dimension}: el origen no devolvio filas; no se llama al procedimiento", dimension);
            return new DimensionLoadResult(dimension, 0, 0, 0, sw.Elapsed);
        }

        // Serializacion con los nombres de propiedad exactos
        var json = JsonSerializer.Serialize(lote, JsonOptions);

        await using var connection = new MySqlConnection(_dwConnectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand(procedimiento, connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        command.Parameters.Add(new MySqlParameter("p_Lote", MySqlDbType.JSON) { Value = json });
        var nuevos = new MySqlParameter("p_Nuevos", MySqlDbType.Int32) { Direction = System.Data.ParameterDirection.Output };
        var modificados = new MySqlParameter("p_Modificados", MySqlDbType.Int32) { Direction = System.Data.ParameterDirection.Output };
        command.Parameters.Add(nuevos);
        command.Parameters.Add(modificados);

        try
        {
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "{Dimension}: fallo {Procedimiento} (codigo {Codigo}); el lote no se aplico",
                dimension, procedimiento, ex.Number);
            throw;
        }

        var result = new DimensionLoadResult(
            Dimension: dimension,
            Extraidas: lote.Count,
            Nuevos: Convert.ToInt32(nuevos.Value),
            Modificados: Convert.ToInt32(modificados.Value),
            Duracion: sw.Elapsed);

        _logger.LogInformation(
            "{Dimension}: extraidas {Extraidas} | nuevas {Nuevos} | modificadas {Modificados} | sin cambios {SinCambios} | {Ms} ms",
            dimension, result.Extraidas, result.Nuevos, result.Modificados, result.SinCambios,
            result.Duracion.TotalMilliseconds.ToString("0"));

        return result;
    }
}
