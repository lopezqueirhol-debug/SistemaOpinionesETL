using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;
using SistemaOpiniones.Etl.Infrastructure;
using SistemaOpiniones.Etl.Infrastructure.Dimensiones;
using SistemaOpiniones.Etl.Infrastructure.Staging;
using SistemaOpiniones.Etl.Worker;

TrySetUtf8Output();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerSourceOptions>(
    builder.Configuration.GetSection("WorkerSources"));
builder.Services.PostConfigure<WorkerSourceOptions>(SettingsLoader.ApplyEnvironmentOverrides);

builder.Services.AddHttpClient("CommentsApi");

builder.Services.AddSingleton<IExtractor, CsvSurveyExtractor>();
builder.Services.AddSingleton<IExtractor, RelationalReviewsExtractor>();
builder.Services.AddSingleton<IExtractor, RestCommentsExtractor>();
builder.Services.AddSingleton<IStagingLoader, JsonStagingLoader>();

builder.Services.AddHostedService<EtlExtractionWorker>();

builder.Services.AddSingleton(_ => new DimensionSourceReader(
    Environment.GetEnvironmentVariable("ETL_OLTP_CONNECTION_STRING")
    ?? Environment.GetEnvironmentVariable("ETL_REVIEWS_CONNECTION_STRING")
    ?? string.Empty));

builder.Services.AddSingleton(sp => new DimensionLoader(
    Environment.GetEnvironmentVariable("ETL_DW_CONNECTION_STRING") ?? string.Empty,
    sp.GetRequiredService<ILogger<DimensionLoader>>()));

builder.Services.AddHostedService<DimensionLoadService>();

var host = builder.Build();
await host.RunAsync();

static void TrySetUtf8Output()
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
        // Sin consola asociada (salida redirigida): se ignora.
    }
}