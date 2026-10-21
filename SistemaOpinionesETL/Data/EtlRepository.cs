using System.Data;
using MySqlConnector;
using SistemaOpiniones.Etl.Domain;

namespace SistemaOpiniones.Etl.Data;

/// <summary>
/// Acceso a MySQL. Toda la escritura se hace a traves de los procedimientos almacenados
/// definidos en 01_SistemaOpiniones_Schema.sql (sp_Insertar*, sp_IniciarLog, ...).
/// Los errores de negocio (duplicado, integridad referencial) se devuelven como InsertOutcome;
/// los errores de infraestructura (conexion caida, etc.) se propagan como MySqlException.
/// </summary>
public sealed class EtlRepository : IDisposable
{
    /// <summary>Tablas de datos, en el orden en que se cuentan al final del proceso.</summary>
    public static readonly string[] DataTables =
        { "FuenteDatos", "Cliente", "Producto", "ComentarioSocial", "ResenaWeb", "Encuesta" };

    private static readonly string[] RequiredProcedures =
    {
        "sp_ResolverFuentePorTipo", "sp_IniciarLog", "sp_FinalizarLog", "sp_RegistrarRechazo",
        "sp_InsertarCliente", "sp_InsertarProducto", "sp_InsertarFuenteDatos",
        "sp_InsertarComentarioSocial", "sp_InsertarEncuesta", "sp_InsertarResenaWeb",
        "sp_LimpiarDatos"
    };

    private const int MaxDetailLength = 500;
    private const int UserDefinedExceptionCode = 1644; // SIGNAL SQLSTATE '45000'

    private readonly MySqlConnection _connection;

    public EtlRepository(string connectionString)
    {
        _connection = new MySqlConnection(connectionString);
    }

    public string ServerName => _connection.DataSource;
    public string DatabaseName => _connection.Database;

    // ------------------------------------------------------------------
    // Conexion y verificacion
    // ------------------------------------------------------------------

    public void Open() => _connection.Open();

    /// <summary>Comprueba que el script de base de datos fue ejecutado (existen los procedimientos).</summary>
    public void VerifyDatabaseObjects()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText =
                "SELECT ROUTINE_NAME FROM information_schema.ROUTINES " +
                "WHERE ROUTINE_SCHEMA = DATABASE() AND ROUTINE_TYPE = 'PROCEDURE'";

            using var reader = cmd.ExecuteReader();
            while (reader.Read()) found.Add(reader.GetString(0));
        }

        var missing = RequiredProcedures.Where(name => !found.Contains(name)).ToList();
        if (missing.Count > 0)
        {
            throw new EtlException(
                $"Faltan procedimientos almacenados en la base '{DatabaseName}': {string.Join(", ", missing)}. " +
                "Ejecute primero Database/01_SistemaOpiniones_Schema.sql.");
        }
    }

    // ------------------------------------------------------------------
    // Utilidades del proceso
    // ------------------------------------------------------------------

    public void LimpiarDatos(bool borrarLogs)
    {
        using var cmd = CreateProcedure("sp_LimpiarDatos");
        In(cmd, "p_BorrarLogs", MySqlDbType.Int32, borrarLogs ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public Dictionary<string, long> ContarRegistros()
    {
        var counts = new Dictionary<string, long>();

        foreach (var table in DataTables)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM `{table}`";
            counts[table] = Convert.ToInt64(cmd.ExecuteScalar());
        }

        return counts;
    }

    /// <summary>
    /// Devuelve el IdFuente de menor valor para el TipoFuente indicado.
    /// Lanza FuenteNoEncontradaException si no existe ninguna.
    /// </summary>
    public string ResolverFuentePorTipo(string tipoFuente)
    {
        using var cmd = CreateProcedure("sp_ResolverFuentePorTipo");
        In(cmd, "p_TipoFuente", MySqlDbType.VarChar, tipoFuente);
        var output = Out(cmd, "p_IdFuente", MySqlDbType.VarChar, 10);

        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (MySqlException ex) when (ex.Number == UserDefinedExceptionCode)
        {
            throw new FuenteNoEncontradaException(
                $"No existe ninguna fuente de tipo '{tipoFuente}' en FuenteDatos. " +
                "Cargue primero fuente_datos.csv o revise Etl:TipoFuentePorArchivo.", ex);
        }

        return output.Value as string
               ?? throw new EtlException($"No se pudo resolver el IdFuente para el tipo '{tipoFuente}'.");
    }

    // ------------------------------------------------------------------
    // Control de carga (LogCarga / RegistroRechazado)
    // ------------------------------------------------------------------

    public long IniciarLog(Guid runId, string archivo, string tablaDestino, string? idFuente)
    {
        using var cmd = CreateProcedure("sp_IniciarLog");
        In(cmd, "p_IdEjecucion", MySqlDbType.VarChar, runId.ToString());
        In(cmd, "p_Archivo", MySqlDbType.VarChar, archivo);
        In(cmd, "p_TablaDestino", MySqlDbType.VarChar, tablaDestino);
        In(cmd, "p_IdFuenteAsignada", MySqlDbType.VarChar, idFuente);
        var output = Out(cmd, "p_IdLog", MySqlDbType.Int64);

        cmd.ExecuteNonQuery();
        return Convert.ToInt64(output.Value);
    }

    public void FinalizarLog(long idLog, int procesados, int insertados, int rechazados,
                             string estado, string? mensaje)
    {
        using var cmd = CreateProcedure("sp_FinalizarLog");
        In(cmd, "p_IdLog", MySqlDbType.Int64, idLog);
        In(cmd, "p_Procesados", MySqlDbType.Int32, procesados);
        In(cmd, "p_Insertados", MySqlDbType.Int32, insertados);
        In(cmd, "p_Rechazados", MySqlDbType.Int32, rechazados);
        In(cmd, "p_Estado", MySqlDbType.VarChar, estado);
        In(cmd, "p_Mensaje", MySqlDbType.Text, mensaje);
        cmd.ExecuteNonQuery();
    }

    public void RegistrarRechazo(long idLog, string archivo, int numeroFila,
                                 RejectionReason motivo, string detalle, string datosOriginales)
    {
        using var cmd = CreateProcedure("sp_RegistrarRechazo");
        In(cmd, "p_IdLog", MySqlDbType.Int64, idLog);
        In(cmd, "p_Archivo", MySqlDbType.VarChar, archivo);
        In(cmd, "p_NumeroFila", MySqlDbType.Int32, numeroFila);
        In(cmd, "p_Motivo", MySqlDbType.VarChar, motivo.ToDbValue());
        In(cmd, "p_Detalle", MySqlDbType.VarChar, Truncate(detalle, MaxDetailLength));
        In(cmd, "p_DatosOriginales", MySqlDbType.Text, datosOriginales);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------
    // Inserciones (un procedimiento por tabla)
    // ------------------------------------------------------------------

    public InsertOutcome Insertar(FuenteDatos e)
    {
        using var cmd = CreateProcedure("sp_InsertarFuenteDatos");
        In(cmd, "p_IdFuente", MySqlDbType.VarChar, e.IdFuente);
        In(cmd, "p_TipoFuente", MySqlDbType.VarChar, e.TipoFuente);
        In(cmd, "p_FechaCarga", MySqlDbType.Date, ToDateTime(e.FechaCarga));
        return ExecuteInsert(cmd);
    }

    public InsertOutcome Insertar(Cliente e)
    {
        using var cmd = CreateProcedure("sp_InsertarCliente");
        In(cmd, "p_IdCliente", MySqlDbType.VarChar, e.IdCliente);
        In(cmd, "p_Nombre", MySqlDbType.VarChar, e.Nombre);
        In(cmd, "p_Email", MySqlDbType.VarChar, e.Email);
        return ExecuteInsert(cmd);
    }

    public InsertOutcome Insertar(Producto e)
    {
        using var cmd = CreateProcedure("sp_InsertarProducto");
        In(cmd, "p_IdProducto", MySqlDbType.VarChar, e.IdProducto);
        In(cmd, "p_Nombre", MySqlDbType.VarChar, e.Nombre);
        In(cmd, "p_Categoria", MySqlDbType.VarChar, e.Categoria);
        return ExecuteInsert(cmd);
    }

    public InsertOutcome Insertar(ComentarioSocial e)
    {
        using var cmd = CreateProcedure("sp_InsertarComentarioSocial");
        In(cmd, "p_IdComment", MySqlDbType.VarChar, e.IdComment);
        In(cmd, "p_IdCliente", MySqlDbType.VarChar, e.IdCliente);   // puede ser NULL
        In(cmd, "p_IdProducto", MySqlDbType.VarChar, e.IdProducto);
        In(cmd, "p_IdFuente", MySqlDbType.VarChar, e.IdFuente);
        In(cmd, "p_Fecha", MySqlDbType.Date, ToDateTime(e.Fecha));
        In(cmd, "p_Plataforma", MySqlDbType.VarChar, e.Plataforma);
        In(cmd, "p_Comentario", MySqlDbType.Text, e.Comentario);
        return ExecuteInsert(cmd);
    }

    public InsertOutcome Insertar(Encuesta e)
    {
        using var cmd = CreateProcedure("sp_InsertarEncuesta");
        In(cmd, "p_IdOpinion", MySqlDbType.Int32, e.IdOpinion);
        In(cmd, "p_IdCliente", MySqlDbType.VarChar, e.IdCliente);
        In(cmd, "p_IdProducto", MySqlDbType.VarChar, e.IdProducto);
        In(cmd, "p_IdFuente", MySqlDbType.VarChar, e.IdFuente);
        In(cmd, "p_Fecha", MySqlDbType.Date, ToDateTime(e.Fecha));
        In(cmd, "p_Comentario", MySqlDbType.Text, e.Comentario);
        In(cmd, "p_Clasificacion", MySqlDbType.VarChar, e.Clasificacion);
        In(cmd, "p_Puntaje", MySqlDbType.Int32, e.PuntajeSatisfaccion);
        return ExecuteInsert(cmd);
    }

    public InsertOutcome Insertar(ResenaWeb e)
    {
        using var cmd = CreateProcedure("sp_InsertarResenaWeb");
        In(cmd, "p_IdReview", MySqlDbType.VarChar, e.IdReview);
        In(cmd, "p_IdCliente", MySqlDbType.VarChar, e.IdCliente);
        In(cmd, "p_IdProducto", MySqlDbType.VarChar, e.IdProducto);
        In(cmd, "p_IdFuente", MySqlDbType.VarChar, e.IdFuente);
        In(cmd, "p_Fecha", MySqlDbType.Date, ToDateTime(e.Fecha));
        In(cmd, "p_Comentario", MySqlDbType.Text, e.Comentario);
        In(cmd, "p_Rating", MySqlDbType.Int32, e.Rating);
        return ExecuteInsert(cmd);
    }

    public void Dispose() => _connection.Dispose();

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private MySqlCommand CreateProcedure(string name)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = name;
        return cmd;
    }

    private static void In(MySqlCommand cmd, string name, MySqlDbType type, object? value)
    {
        cmd.Parameters.Add(new MySqlParameter(name, type)
        {
            Direction = ParameterDirection.Input,
            Value = value ?? DBNull.Value
        });
    }

    private static MySqlParameter Out(MySqlCommand cmd, string name, MySqlDbType type, int size = 0)
    {
        var parameter = new MySqlParameter(name, type) { Direction = ParameterDirection.Output };
        if (size > 0) parameter.Size = size;
        cmd.Parameters.Add(parameter);
        return parameter;
    }

    /// <summary>Agrega los parametros de salida estandar (p_Resultado, p_Detalle) y ejecuta el insert.</summary>
    private static InsertOutcome ExecuteInsert(MySqlCommand cmd)
    {
        var result = Out(cmd, "p_Resultado", MySqlDbType.VarChar, 30);
        var detail = Out(cmd, "p_Detalle", MySqlDbType.VarChar, 255);

        cmd.ExecuteNonQuery();

        var resultText = result.Value as string;
        if (string.Equals(resultText, "OK", StringComparison.OrdinalIgnoreCase))
            return InsertOutcome.Ok;

        return new InsertOutcome(RejectionReasonExtensions.FromDbValue(resultText), detail.Value as string);
    }

    private static DateTime ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
