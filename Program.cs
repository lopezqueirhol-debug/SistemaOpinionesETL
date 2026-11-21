using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;
using SistemaOpiniones.Etl.Infrastructure;
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