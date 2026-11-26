using MySqlConnector;

namespace SistemaOpiniones.Etl.Infrastructure.Dimensiones;

/// <summary>
/// Lee Cliente y Producto de la base transaccional SistemaOpiniones.
/// Solo SELECT: nunca escribe en el origen.
/// </summary>
public sealed class DimensionSourceReader
{
    private readonly string _connectionString;

    public DimensionSourceReader(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<ClienteDimRecord>> LeerClientesAsync(CancellationToken ct)
    {
        const string sql = "SELECT IdCliente, Nombre, Email FROM Cliente ORDER BY IdCliente";
        var filas = new List<ClienteDimRecord>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            filas.Add(new ClienteDimRecord(
                IdClienteNK: reader.GetString(0).Trim(),
                Nombre: reader.GetString(1).Trim(),
                Email: reader.GetString(2).Trim()));
        }

        return filas;
    }

    public async Task<IReadOnlyList<ProductoDimRecord>> LeerProductosAsync(CancellationToken ct)
    {
        const string sql = "SELECT IdProducto, Nombre, Categoria FROM Producto ORDER BY IdProducto";
        var filas = new List<ProductoDimRecord>();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            filas.Add(new ProductoDimRecord(
                IdProductoNK: reader.GetString(0).Trim(),
                Nombre: reader.GetString(1).Trim(),
                Categoria: reader.GetString(2).Trim()));
        }

        return filas;
    }
}
