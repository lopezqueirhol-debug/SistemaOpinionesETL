using System.Text;
using SistemaOpiniones.Etl.Domain;
using SistemaOpiniones.Etl.Services;

namespace SistemaOpiniones.Etl.Presentation;

/// <summary>
/// Muestra el avance y el resumen del proceso en consola y, a la vez, lo guarda en un archivo de log.
/// </summary>
public sealed class ConsoleReporter : IDisposable
{
    private const int LineWidth = 82;

    private readonly StreamWriter _logWriter;

    public ConsoleReporter(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        LogDirectory = logDirectory;
        LogFilePath = Path.Combine(logDirectory, $"etl_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        _logWriter = new StreamWriter(LogFilePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            AutoFlush = true
        };
    }

    public string LogDirectory { get; }
    public string LogFilePath { get; }

    // ------------------------------------------------------------------
    // Escritura basica
    // ------------------------------------------------------------------

    public void Line(string text = "", ConsoleColor? color = null)
    {
        if (color.HasValue) Console.ForegroundColor = color.Value;
        Console.WriteLine(text);
        if (color.HasValue) Console.ResetColor();

        _logWriter.WriteLine(text);
    }

    public void Info(string text) => Line($"  {text}");
    public void Warning(string text) => Line($"  [!] {text}", ConsoleColor.Yellow);
    public void Error(string text) => Line($"  [ERROR] {text}", ConsoleColor.Red);

    private void Separator(char ch) => Line(new string(ch, LineWidth));

    // ------------------------------------------------------------------
    // Etapas del proceso
    // ------------------------------------------------------------------

    public void Banner(EtlRunResult run, string server, string database, string csvDirectory)
    {
        Separator('=');
        Line("  SISTEMA DE ANALISIS DE OPINIONES DE CLIENTES  -  PROCESO ETL", ConsoleColor.Cyan);
        Separator('=');
        Info($"Ejecucion    : {run.RunId}");
        Info($"Inicio       : {run.StartedAt:yyyy-MM-dd HH:mm:ss}");
        Info($"Servidor    : {server}   |   Base de datos: {database}");
        Info($"Carpeta CSV  : {csvDirectory}");
        Info($"Log          : {LogFilePath}");
        Separator('-');
    }

    public void FileStarted(int index, int total, IFileLoader loader)
    {
        Line();
        var kind = loader.IsMaster ? "maestra" : "transaccional";
        Line($"[{index}/{total}] {loader.FileName}  ->  {loader.TableName}   ({kind})", ConsoleColor.White);
    }

    public void FileFinished(FileLoadResult result, int maxRejectionsShown)
    {
        if (result.IdFuente is not null)
            Info($"   Fuente asignada: {result.IdFuente} (tipo '{result.TipoFuente}')");

        var color = StatusColor(result.Status);

        Line($"     Procesados: {result.Procesados,5}   Insertados: {result.Insertados,5}   " +
             $"Rechazados: {result.Rechazados,5}   ({result.Duration.TotalSeconds:0.00} s)", color);

        if (result.Status == LoadStatus.Error)
        {
            Line($"     [ERROR] {result.ErrorMessage}", ConsoleColor.Red);
            if (result.Procesados > 0)
                Line($"     El archivo se detuvo tras {result.Procesados} registros; los ya insertados se conservan.",
                     ConsoleColor.Yellow);
        }

        if (result.RejectionsByReason.Count > 0)
        {
            var breakdown = string.Join(", ", result.RejectionsByReason
                .OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key.ToDbValue()} = {pair.Value}"));

            Line($"     Rechazos por motivo: {breakdown}", ConsoleColor.Yellow);

            var shown = Math.Min(maxRejectionsShown, result.Rejections.Count);
            if (shown > 0)
            {
                Line($"     Ejemplos de rechazos (primeros {shown} de {result.Rejections.Count}):");
                foreach (var rejection in result.Rejections.Take(shown))
                    Line($"       registro {rejection.RowNumber,4}: {rejection.Detail}");
            }
        }

        if (result.LogWarning is not null)
            Line($"     [!] {result.LogWarning}", ConsoleColor.Yellow);
    }

    public void Summary(EtlRunResult run)
    {
        Line();
        Separator('=');
        Line("  RESUMEN FINAL DEL PROCESO", ConsoleColor.Cyan);
        Separator('=');
        Line($"  {"Archivo",-22}{"Tabla destino",-19}{"Procesados",11}{"Insertados",11}{"Rechazados",11}  Estado");
        Separator('-');

        foreach (var file in run.Files)
        {
            Line($"  {file.FileName,-22}{file.TableName,-19}{file.Procesados,11}{file.Insertados,11}{file.Rechazados,11}  {file.Status}",
                 StatusColor(file.Status));
        }

        foreach (var skipped in run.SkippedFiles)
            Line($"  {skipped,-22}{"",-19}{"-",11}{"-",11}{"-",11}  No ejecutado", ConsoleColor.DarkYellow);

        Separator('-');
        Line($"  {"TOTAL",-22}{"",-19}{run.TotalProcesados,11}{run.TotalInsertados,11}{run.TotalRechazados,11}",
             ConsoleColor.White);

        if (run.TableCounts.Count > 0)
        {
            Line();
            Line("  Registros actualmente en la base de datos:");
            foreach (var pair in run.TableCounts)
                Line($"    {pair.Key,-20}{pair.Value,8}");
        }

        Line();
        Info($"Fin          : {run.FinishedAt:yyyy-MM-dd HH:mm:ss}   (duración total: {(run.FinishedAt - run.StartedAt).TotalSeconds:0.0} s)");
        Info($"Log          : {LogFilePath}");
        if (run.RejectedCsvPath is not null)
            Info($"Rechazados   : {run.RejectedCsvPath}");

        Separator('-');

        if (run.Aborted)
            Line($"  RESULTADO: PROCESO DETENIDO. {run.AbortReason}", ConsoleColor.Red);
        else if (run.HasErrors)
            Line("  RESULTADO: FINALIZO CON ERRORES (revise el detalle de cada archivo).", ConsoleColor.Red);
        else if (run.TotalRechazados > 0)
            Line("  RESULTADO: FINALIZO CORRECTAMENTE, CON REGISTROS RECHAZADOS (ver detalle arriba).", ConsoleColor.Yellow);
        else
            Line("  RESULTADO: FINALIZO CORRECTAMENTE SIN RECHAZOS.", ConsoleColor.Green);

        Separator('=');
    }

    public void Dispose() => _logWriter.Dispose();

    private static ConsoleColor StatusColor(LoadStatus status) => status switch
    {
        LoadStatus.Completado => ConsoleColor.Green,
        LoadStatus.CompletadoConRechazos => ConsoleColor.Yellow,
        _ => ConsoleColor.Red
    };
}
