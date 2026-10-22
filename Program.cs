using System.Text;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;
using SistemaOpiniones.Etl.Presentation;
using SistemaOpiniones.Etl.Services;

namespace SistemaOpiniones.Etl;

/// <summary>
/// Punto de entrada del ETL de opiniones de clientes.
/// Codigos de salida: 0 = correcto | 1 = errores en la carga | 2 = error de configuracion/argumentos.
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitErrors = 1;
    private const int ExitConfiguration = 2;

    private static int Main(string[] args)
    {
        TrySetUtf8Output();

        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            PrintError(null, $"Argumento invalido: {ex.Message}");
            Console.WriteLine();
            CommandLineOptions.PrintHelp();
            return ExitConfiguration;
        }

        if (options.ShowHelp)
        {
            CommandLineOptions.PrintHelp();
            return ExitOk;
        }

        ConsoleReporter? reporter = null;
        int exitCode;

        try
        {
            var settings = SettingsLoader.Load();
            reporter = new ConsoleReporter(SettingsLoader.ResolveOutputDirectory(settings.Etl.LogDirectory));

            var run = new EtlOrchestrator(settings, options, reporter).Run();
            exitCode = run.HasErrors ? ExitErrors : ExitOk;
        }
        catch (ConfigurationException ex)
        {
            PrintError(reporter, $"Error de configuracion: {ex.Message}");
            exitCode = ExitConfiguration;
        }
        catch (EtlException ex)
        {
            PrintError(reporter, ex.Message);
            exitCode = ExitErrors;
        }
        catch (Exception ex)
        {
            PrintError(reporter, $"Error inesperado ({ex.GetType().Name}): {ex.Message}");
            exitCode = ExitErrors;
        }
        finally
        {
            reporter?.Dispose();
        }

        WaitForKey(options);
        return exitCode;
    }

    private static void PrintError(ConsoleReporter? reporter, string message)
    {
        if (reporter is not null)
        {
            reporter.Error(message);
            return;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"  [ERROR] {message}");
        Console.ResetColor();
    }

    private static void TrySetUtf8Output()
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

    private static void WaitForKey(CommandLineOptions options)
    {
        if (options.NoPause || Console.IsInputRedirected) return;

        try
        {
            Console.WriteLine();
            Console.Write("  Presione cualquier tecla para salir...");
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            // Sin consola interactiva.
        }
    }
}