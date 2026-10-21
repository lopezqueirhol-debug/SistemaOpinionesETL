namespace SistemaOpiniones.Etl.Configuration;

/// <summary>Argumentos opcionales de linea de comandos.</summary>
public sealed class CommandLineOptions
{
    /// <summary>--limpiar: vacia las 6 tablas de datos antes de cargar (conserva el historial de cargas).</summary>
    public bool ClearData { get; private set; }

    /// <summary>--limpiar-todo: vacia tambien LogCarga y RegistroRechazado.</summary>
    public bool ClearAll { get; private set; }

    /// <summary>--ruta &lt;carpeta&gt;: carpeta con los CSV (reemplaza a Etl:CsvDirectory).</summary>
    public string? CsvDirectory { get; private set; }

    /// <summary>--sin-pausa: no espera una tecla al terminar.</summary>
    public bool NoPause { get; private set; }

    public bool ShowHelp { get; private set; }

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--limpiar":
                    options.ClearData = true;
                    break;
                case "--limpiar-todo":
                    options.ClearAll = true;
                    break;
                case "--sin-pausa":
                    options.NoPause = true;
                    break;
                case "--ayuda":
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--ruta":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("La opción --ruta requiere una carpeta.");
                    options.CsvDirectory = args[++i];
                    break;
                default:
                    throw new ArgumentException($"Opción desconocida: '{args[i]}'.");
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Uso: SistemaOpiniones.Etl [opciones]");
        Console.WriteLine();
        Console.WriteLine("  --limpiar        Vacía las tablas de datos antes de cargar (conserva el historial de cargas).");
        Console.WriteLine("  --limpiar-todo   Vacía también LogCarga y RegistroRechazado.");
        Console.WriteLine("  --ruta <carpeta> Carpeta con los CSV (por defecto: Etl:CsvDirectory de appsettings.json).");
        Console.WriteLine("  --sin-pausa      No espera una tecla al finalizar.");
        Console.WriteLine("  --ayuda          Muestra esta ayuda.");
        Console.WriteLine();
        Console.WriteLine("Sin opciones, el ETL carga los CSV tal cual: si los datos ya existen,");
        Console.WriteLine("los registros se rechazan como DUPLICADO (el proceso es seguro de re-ejecutar).");
    }
}
