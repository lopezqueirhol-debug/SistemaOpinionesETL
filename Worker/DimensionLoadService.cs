using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaOpiniones.Etl.Infrastructure.Dimensiones;

namespace SistemaOpiniones.Etl.Worker;

/// <summary>
/// Ciclo de carga de dimensiones: lee Cliente y Producto de SistemaOpiniones
/// y los aplica a SistemaOpinionesDW mediante upsert. Corre en su propio ciclo,
/// independiente de la extraccion de fuentes.
///
/// Variables de entorno:
///   ETL_OLTP_CONNECTION_STRING  origen (SistemaOpiniones). Si no existe, usa ETL_REVIEWS_CONNECTION_STRING.
///   ETL_DW_CONNECTION_STRING    destino (SistemaOpinionesDW). Obligatoria para cargar.
///   ETL_DIMENSIONES_MINUTOS     intervalo entre ciclos (por defecto 15).
/// </summary>
public sealed class DimensionLoadService : BackgroundService
{
    private readonly DimensionSourceReader _reader;
    private readonly DimensionLoader _loader;
    private readonly ILogger<DimensionLoadService> _logger;
    private readonly TimeSpan _intervalo;

    public DimensionLoadService(
        DimensionSourceReader reader,
        DimensionLoader loader,
        ILogger<DimensionLoadService> logger)
    {
        _reader = reader;
        _loader = loader;
        _logger = logger;

        var minutos = int.TryParse(Environment.GetEnvironmentVariable("ETL_DIMENSIONES_MINUTOS"), out var m) && m > 0
            ? m
            : 15;
        _intervalo = TimeSpan.FromMinutes(minutos);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DimensionLoadService iniciado: intervalo {Minutos} min", _intervalo.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarCicloAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo en el ciclo no detiene el servicio se registra y se reintenta en el siguiente ciclo.
                _logger.LogError(ex, "Ciclo de carga de dimensiones fallido; se reintentara en {Minutos} min",
                    _intervalo.TotalMinutes);
            }

            try
            {
                await Task.Delay(_intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("DimensionLoadService detenido");
    }

    private async Task EjecutarCicloAsync(CancellationToken ct)
    {
        var dwConnection = Environment.GetEnvironmentVariable("ETL_DW_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(dwConnection))
        {
            _logger.LogWarning("ETL_DW_CONNECTION_STRING no esta definida; se omite la carga de dimensiones");
            return;
        }

        var inicio = DateTime.UtcNow;

        var clientes = await _reader.LeerClientesAsync(ct);
        await _loader.CargarClientesAsync(clientes, ct);

        var productos = await _reader.LeerProductosAsync(ct);
        await _loader.CargarProductosAsync(productos, ct);

        _logger.LogInformation("Ciclo de dimensiones finalizado en {Ms} ms",
            (DateTime.UtcNow - inicio).TotalMilliseconds.ToString("0"));
    }
}
