using System.Diagnostics;
using MySqlConnector;
using SistemaOpiniones.Etl.Data;
using SistemaOpiniones.Etl.Domain;
using SistemaOpiniones.Etl.Infrastructure;

namespace SistemaOpiniones.Etl.Services;

/// <summary>
/// Cargador generico de un archivo CSV. Implementa el flujo comun del ETL:
///   1. (si aplica) resolver IdFuente por tipo de fuente
///   2. registrar el inicio en LogCarga
///   3. abrir el CSV y validar su estructura
///   4. por cada registro: validar/transformar -> detectar duplicados -> insertar
///   5. registrar rechazos y cerrar el log con los totales
/// Los errores de un registro lo rechazan y continuan; los errores de archivo/BD lo detienen.
/// </summary>
public sealed class FileLoader<T> : IFileLoader
{
    private readonly string[] _expectedColumns;
    private readonly Func<CsvRow, string?, T> _map;
    private readonly Func<T, string> _keyOf;
    private readonly Func<T, string?>? _contentKeyOf;
    private readonly Func<EtlRepository, T, InsertOutcome> _insert;
    private readonly string? _tipoFuente;

    public FileLoader(
        string fileName,
        string tableName,
        bool isMaster,
        string[] expectedColumns,
        Func<CsvRow, string?, T> map,
        Func<T, string> keyOf,
        Func<T, string?>? contentKeyOf,
        Func<EtlRepository, T, InsertOutcome> insert,
        string? tipoFuente = null)
    {
        FileName = fileName;
        TableName = tableName;
        IsMaster = isMaster;
        _expectedColumns = expectedColumns;
        _map = map;
        _keyOf = keyOf;
        _contentKeyOf = contentKeyOf;
        _insert = insert;
        _tipoFuente = tipoFuente;
    }

    public string FileName { get; }
    public string TableName { get; }
    public bool IsMaster { get; }

    public FileLoadResult Load(LoadContext context)
    {
        var result = new FileLoadResult(FileName, TableName, IsMaster) { TipoFuente = _tipoFuente };
        var stopwatch = Stopwatch.StartNew();
        long idLog = 0;

        // Claves ya insertadas en esta corrida (deteccion de duplicados dentro del archivo).
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var path = Path.Combine(context.CsvDirectory, FileName);
            if (!File.Exists(path))
                throw new CsvStructureException($"No se encontro el archivo '{path}'.");

            // 1. Fuente de datos segun el tipo configurado (ComentarioSocial, ResenaWeb, Encuesta).
            if (_tipoFuente is not null)
                result.IdFuente = context.Repository.ResolverFuentePorTipo(_tipoFuente);

            // 2. Inicio del log de carga.
            idLog = context.Repository.IniciarLog(context.RunId, FileName, TableName, result.IdFuente);

            // 3. Lectura y validacion de estructura.
            using var csv = CsvTable.Open(path, _expectedColumns);

            // 4. Procesamiento registro por registro.
            while (csv.TryRead(out var row))
            {
                result.Procesados++;

                try
                {
                    if (!row.IsWellFormed)
                        throw new RowValidationException(RejectionReason.Formato,
                            $"El registro tiene {row.FieldCount} campos y el encabezado define {csv.ColumnCount}.");

                    // Validacion de formato, obligatorios, tipos y transformacion.
                    var entity = _map(row, result.IdFuente);

                    // Duplicados dentro del archivo.
                    var key = _keyOf(entity);
                    if (keys.Contains(key))
                        throw new RowValidationException(RejectionReason.Duplicado,
                            $"Identificador duplicado dentro del archivo: {key}.");

                    var contentKey = _contentKeyOf?.Invoke(entity);
                    if (contentKey is not null && contentKeys.Contains(contentKey))
                        throw new RowValidationException(RejectionReason.Duplicado,
                            "Registro identico (mismo cliente, producto, fecha y comentario) repetido en el archivo.");

                    // Duplicados en BD, integridad referencial y reglas: los resuelve el procedimiento almacenado.
                    var outcome = _insert(context.Repository, entity);

                    if (outcome.IsOk)
                    {
                        result.Insertados++;
                        keys.Add(key);
                        if (contentKey is not null) contentKeys.Add(contentKey);
                    }
                    else
                    {
                        Reject(context, result, idLog, row, outcome.Reason ?? RejectionReason.ErrorBd,
                               outcome.Detail ?? "Rechazado por la base de datos.");
                    }
                }
                catch (RowValidationException ex)
                {
                    Reject(context, result, idLog, row, ex.Reason, ex.Message);
                }
            }

            result.Status = result.Rechazados > 0 ? LoadStatus.CompletadoConRechazos : LoadStatus.Completado;
        }
        catch (EtlException ex)
        {
            result.Fail(ex.Message);
        }
        catch (MySqlException ex)
        {
            result.Fail($"Error de base de datos (codigo {ex.Number}): {ex.Message}");
        }
        catch (Exception ex)
        {
            result.Fail($"Error inesperado ({ex.GetType().Name}): {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            CloseLog(context, result, idLog);
        }

        return result;
    }

    private void Reject(LoadContext context, FileLoadResult result, long idLog, CsvRow row,
                        RejectionReason reason, string detail)
    {
        result.Rechazados++;
        result.RejectionsByReason[reason] = result.RejectionsByReason.GetValueOrDefault(reason) + 1;
        result.Rejections.Add(new RejectedRow(row.RowNumber, reason, detail, row.RawData));

        context.Repository.RegistrarRechazo(idLog, FileName, row.RowNumber, reason, detail, row.RawData);
    }

    /// <summary>Cierra el registro en LogCarga. Un fallo aqui no debe ocultar el resultado de la carga.</summary>
    private void CloseLog(LoadContext context, FileLoadResult result, long idLog)
    {
        try
        {
            // Si el proceso fallo antes de abrir el log (archivo inexistente, fuente no encontrada), se crea ahora.
            if (idLog == 0)
                idLog = context.Repository.IniciarLog(context.RunId, FileName, TableName, result.IdFuente);

            context.Repository.FinalizarLog(
                idLog,
                result.Procesados,
                result.Insertados,
                result.Rechazados,
                result.Status.ToDbValue(),
                result.ErrorMessage ?? BuildRejectionSummary(result));
        }
        catch (Exception ex)
        {
            result.LogWarning = $"No se pudo cerrar el registro en LogCarga: {ex.Message}";
        }
    }

    private static string? BuildRejectionSummary(FileLoadResult result)
    {
        if (result.RejectionsByReason.Count == 0) return null;

        return "Rechazos: " + string.Join(", ",
            result.RejectionsByReason
                  .OrderByDescending(pair => pair.Value)
                  .Select(pair => $"{pair.Key.ToDbValue()}={pair.Value}"));
    }
}