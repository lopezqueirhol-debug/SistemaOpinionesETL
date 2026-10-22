using System.Text;
using MySqlConnector;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Data;
using SistemaOpiniones.Etl.Domain;
using SistemaOpiniones.Etl.Presentation;
using SistemaOpiniones.Etl.Validation;

namespace SistemaOpiniones.Etl.Services;

/// <summary>
/// Coordina el proceso completo: conexion, verificacion, carga de cada archivo en orden de
/// dependencias, exportacion de rechazados y resumen final.
/// </summary>
public sealed class EtlOrchestrator
{
    private readonly AppSettings _settings;
    private readonly CommandLineOptions _options;
    private readonly ConsoleReporter _reporter;

    public EtlOrchestrator(AppSettings settings, CommandLineOptions options, ConsoleReporter reporter)
    {
        _settings = settings;
        _options = options;
        _reporter = reporter;
    }

    public EtlRunResult Run()
    {
        var run = new EtlRunResult { RunId = Guid.NewGuid(), StartedAt = DateTime.Now };

        // Validaciones previas (lanzan ConfigurationException / EtlException antes de tocar datos).
        var csvDirectory = ResolveCsvDirectory();
        var validator = new FieldValidator(_settings.Etl);
        var loaders = LoaderCatalog.Create(_settings.Etl, validator);

        using var repository = new EtlRepository(_settings.ConnectionString);
        OpenConnection(repository);
        repository.VerifyDatabaseObjects();

        _reporter.Banner(run, repository.ServerName, repository.DatabaseName, csvDirectory);

        if (_options.ClearData || _options.ClearAll)
        {
            repository.LimpiarDatos(_options.ClearAll);
            _reporter.Warning(_options.ClearAll
                ? "Se vaciaron las tablas de datos y el historial de cargas (--limpiar-todo)."
                : "Se vaciaron las tablas de datos (--limpiar). Se conserva el historial de cargas.");
        }

        var context = new LoadContext(repository, run.RunId, csvDirectory);

        for (var i = 0; i < loaders.Count; i++)
        {
            var loader = loaders[i];
            _reporter.FileStarted(i + 1, loaders.Count, loader);

            var result = loader.Load(context);
            run.Files.Add(result);
            _reporter.FileFinished(result, _settings.Etl.MaxRejectionsShownPerFile);

            // Si falla una tabla maestra no se puede garantizar la integridad referencial de las demas.
            if (loader.IsMaster && result.Status == LoadStatus.Error)
            {
                run.Aborted = true;
                run.AbortReason = $"Fallo la carga de la tabla maestra {loader.TableName}; " +
                                  "no se cargan las demas tablas para preservar la integridad referencial.";

                for (var j = i + 1; j < loaders.Count; j++)
                    run.SkippedFiles.Add(loaders[j].FileName);

                break;
            }
        }

        run.FinishedAt = DateTime.Now;

        try
        {
            run.TableCounts = repository.ContarRegistros();
        }
        catch (MySqlException ex)
        {
            _reporter.Warning($"No se pudo contar los registros de las tablas: {ex.Message}");
        }

        run.RejectedCsvPath = ExportRejections(run);
        _reporter.Summary(run);

        return run;
    }

    // ------------------------------------------------------------------

    private static void OpenConnection(EtlRepository repository)
    {
        try
        {
            repository.Open();
        }
        catch (MySqlException ex)
        {
            throw new EtlException(
                $"No se pudo conectar a MySQL (codigo {ex.Number}): {ex.Message}. " +
                "Verifique que el servidor este activo y revise la cadena de conexion en appsettings.json.", ex);
        }
    }

    /// <summary>Busca la carpeta de CSV: --ruta, o Etl:CsvDirectory (junto al ejecutable o en el directorio actual).</summary>
    private string ResolveCsvDirectory()
    {
        var configured = _options.CsvDirectory ?? _settings.Etl.CsvDirectory;

        var candidates = Path.IsPathRooted(configured)
            ? new[] { configured }
            : new[]
            {
                Path.Combine(AppContext.BaseDirectory, configured),
                Path.Combine(Directory.GetCurrentDirectory(), configured)
            };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        throw new ConfigurationException(
            $"No se encontro la carpeta de CSV '{configured}'. Se busco en: {string.Join(" | ", candidates)}");
    }

    /// <summary>Guarda todos los registros rechazados en un CSV (util como evidencia). Devuelve la ruta o null.</summary>
    private string? ExportRejections(EtlRunResult run)
    {
        if (run.Files.All(f => f.Rejections.Count == 0)) return null;

        try
        {
            var path = Path.Combine(_reporter.LogDirectory, $"rechazados_{run.StartedAt:yyyyMMdd_HHmmss}.csv");

            using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine("Archivo,Registro,Motivo,Detalle,DatosOriginales");

            foreach (var file in run.Files)
            {
                foreach (var rejection in file.Rejections)
                {
                    writer.WriteLine(string.Join(",",
                        Escape(file.FileName),
                        rejection.RowNumber,
                        Escape(rejection.Reason.ToDbValue()),
                        Escape(rejection.Detail),
                        Escape(rejection.RawData)));
                }
            }

            return path;
        }
        catch (IOException ex)
        {
            _reporter.Warning($"No se pudo escribir el CSV de rechazados: {ex.Message}");
            return null;
        }
    }

    private static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}