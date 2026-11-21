using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;

namespace SistemaOpiniones.Etl.Worker;

public sealed class EtlExtractionWorker : BackgroundService
{
    private readonly IReadOnlyList<IExtractor> _extractors;
    private readonly IStagingLoader _stagingLoader;
    private readonly WorkerSourceOptions _options;
    private readonly ILogger<EtlExtractionWorker> _logger;

    public EtlExtractionWorker(
        IEnumerable<IExtractor> extractors,
        IStagingLoader stagingLoader,
        IOptions<WorkerSourceOptions> options,
        ILogger<EtlExtractionWorker> logger)
    {
        _extractors = extractors.ToArray();
        _stagingLoader = stagingLoader;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var maxConcurrency = Math.Max(1, _options.MaxConcurrency);
        _logger.LogInformation(
            "EtlExtractionWorker iniciado: {Sources} fuentes, concurrencia maxima {Concurrency}, intervalo {Interval} min",
            _extractors.Count, maxConcurrency, Math.Max(1, _options.IntervalMinutes));

        var cycle = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            cycle++;
            var cycleSw = Stopwatch.StartNew();

            using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = _extractors.Select(source => ExtractSourceAsync(source, gate, cycle, stoppingToken));

            ExtractionOutcome[] outcomes;
            try
            {
                outcomes = await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                cycleSw.Stop();
                _logger.LogInformation("Ciclo {Cycle} cancelado tras {ElapsedMs} ms", cycle, cycleSw.ElapsedMilliseconds);
                throw;
            }

            cycleSw.Stop();
            LogCycle(cycle, outcomes, cycleSw, maxConcurrency);

            foreach (var failed in outcomes.Where(x => !x.Success))
            {
                _logger.LogWarning(
                    "Ciclo {Cycle} - fuente {Source} ({Type}) sin datos: {Error}",
                    cycle, failed.SourceName, failed.SourceType, failed.ErrorMessage);
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)), stoppingToken);
        }
    }

    private async Task<ExtractionOutcome> ExtractSourceAsync(
        IExtractor extractor,
        SemaphoreSlim gate,
        int cycle,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await gate.WaitAsync(cancellationToken);

        try
        {
            _logger.LogInformation(
                "Ciclo {Cycle} - extrayendo {Source} ({Type})",
                cycle, extractor.SourceName, extractor.SourceType);

            var data = await extractor.ExtractAsync(cancellationToken);
            await _stagingLoader.SaveBatchAsync(extractor.SourceName, data, cancellationToken);
            sw.Stop();

            _logger.LogInformation(
                "Ciclo {Cycle} - fuente {Source}: {Count} registros en {ElapsedMs} ms",
                cycle, extractor.SourceName, data.Count, sw.ElapsedMilliseconds);

            return new ExtractionOutcome(
                extractor.SourceName, extractor.SourceType, true, data.Count, sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "Ciclo {Cycle} - fallo en {Source} tras {ElapsedMs} ms",
                cycle, extractor.SourceName, sw.ElapsedMilliseconds);

            return new ExtractionOutcome(
                extractor.SourceName, extractor.SourceType, false, 0, sw.ElapsedMilliseconds, ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    private void LogCycle(
        int cycle,
        IReadOnlyList<ExtractionOutcome> outcomes,
        Stopwatch cycleSw,
        int maxConcurrency)
    {
        var succeeded = outcomes.Count(x => x.Success);
        var records = outcomes.Sum(x => x.RecordCount);
        var slowestMs = outcomes.Count == 0 ? 0 : outcomes.Max(x => x.ElapsedMs);
        var rate = cycleSw.Elapsed.TotalSeconds > 0 ? records / cycleSw.Elapsed.TotalSeconds : 0;

        _logger.LogInformation(
            "Ciclo {Cycle} finalizado en {TotalMs} ms | fuentes {Ok}/{Total} | registros {Records} | " +
            "tasa {Rate:N0} reg/s | fuente mas lenta {SlowestMs} ms | concurrencia {Concurrency}",
            cycle, cycleSw.ElapsedMilliseconds, succeeded, outcomes.Count, records, rate, slowestMs, maxConcurrency);
    }

    private sealed record ExtractionOutcome(
        string SourceName,
        string SourceType,
        bool Success,
        int RecordCount,
        long ElapsedMs,
        string? ErrorMessage);
}
