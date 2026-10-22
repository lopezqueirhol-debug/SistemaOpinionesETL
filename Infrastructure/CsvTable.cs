using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using SistemaOpiniones.Etl.Domain;

namespace SistemaOpiniones.Etl.Infrastructure;

/// <summary>Un registro (fila) de datos de un CSV.</summary>
public sealed class CsvRow
{
    private readonly string[] _fields;
    private readonly IReadOnlyDictionary<string, int> _index;

    public CsvRow(int rowNumber, string[] fields, string rawData,
                  IReadOnlyDictionary<string, int> index, int expectedFieldCount)
    {
        RowNumber = rowNumber;
        RawData = rawData;
        _fields = fields;
        _index = index;
        IsWellFormed = fields.Length == expectedFieldCount;
    }

    /// <summary>Numero del registro de datos (1 = primer registro despues del encabezado).</summary>
    public int RowNumber { get; }

    /// <summary>Contenido original del registro tal como aparece en el archivo.</summary>
    public string RawData { get; }

    public int FieldCount => _fields.Length;

    /// <summary>False si el registro tiene mas o menos campos que el encabezado.</summary>
    public bool IsWellFormed { get; }

    /// <summary>Valor (sin limpiar) de la columna indicada. El nombre ignora mayusculas y acentos.</summary>
    public string this[string column]
    {
        get
        {
            var key = TextNormalizer.NormalizeKey(column);
            if (!_index.TryGetValue(key, out var position))
                throw new CsvStructureException($"La columna '{column}' no existe en el archivo.");

            return position < _fields.Length ? _fields[position] : string.Empty;
        }
    }
}

/// <summary>
/// Lector de CSV (RFC 4180: respeta comillas, comas y saltos de linea dentro de un campo)
/// que valida que el encabezado contenga las columnas esperadas.
/// </summary>
public sealed class CsvTable : IDisposable
{
    private readonly StreamReader _reader;
    private readonly CsvParser _parser;
    private readonly Dictionary<string, int> _index;
    private int _dataRow;

    private CsvTable(string filePath, StreamReader reader, CsvParser parser,
                     Dictionary<string, int> index, IReadOnlyList<string> headers,
                     IReadOnlyList<string> extraColumns)
    {
        FilePath = filePath;
        _reader = reader;
        _parser = parser;
        _index = index;
        Headers = headers;
        ExtraColumns = extraColumns;
    }

    public string FilePath { get; }
    public IReadOnlyList<string> Headers { get; }

    /// <summary>Columnas presentes en el archivo que el ETL no utiliza (se ignoran).</summary>
    public IReadOnlyList<string> ExtraColumns { get; }

    public int ColumnCount => Headers.Count;

    /// <summary>
    /// Abre el archivo y valida su estructura.
    /// Lanza CsvStructureException si esta vacio, tiene encabezados duplicados o faltan columnas.
    /// </summary>
    public static CsvTable Open(string path, IEnumerable<string> expectedColumns)
    {
        var fileName = Path.GetFileName(path);
        StreamReader? reader = null;
        CsvParser? parser = null;

        try
        {
            reader = new StreamReader(path, DetectEncoding(path), detectEncodingFromByteOrderMarks: true);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,          // el encabezado se procesa manualmente
                Delimiter = ",",
                IgnoreBlankLines = true,
                TrimOptions = TrimOptions.None,   // la limpieza se hace en la capa de validacion
                BadDataFound = null               // comillas mal formadas: no lanzar, el registro se valida despues
            };

            parser = new CsvParser(reader, config);

            if (!parser.Read() || parser.Record is null)
                throw new CsvStructureException($"El archivo '{fileName}' esta vacio.");

            var headers = parser.Record.Select(h => h.Trim()).ToList();
            var index = new Dictionary<string, int>();

            for (var i = 0; i < headers.Count; i++)
            {
                var key = TextNormalizer.NormalizeKey(headers[i]);
                if (key.Length == 0) continue;

                if (!index.TryAdd(key, i))
                    throw new CsvStructureException(
                        $"El archivo '{fileName}' tiene la columna '{headers[i]}' duplicada en el encabezado.");
            }

            var expected = expectedColumns.ToList();
            var missing = expected
                .Where(column => !index.ContainsKey(TextNormalizer.NormalizeKey(column)))
                .ToList();

            if (missing.Count > 0)
                throw new CsvStructureException(
                    $"Estructura invalida en '{fileName}': faltan las columnas [{string.Join(", ", missing)}]. " +
                    $"Encabezado encontrado: [{string.Join(", ", headers)}].");

            var expectedKeys = expected.Select(TextNormalizer.NormalizeKey).ToHashSet();
            var extra = headers
                .Where(h => !expectedKeys.Contains(TextNormalizer.NormalizeKey(h)))
                .ToList();

            return new CsvTable(path, reader, parser, index, headers, extra);
        }
        catch (CsvHelperException ex)
        {
            parser?.Dispose();
            reader?.Dispose();
            throw new CsvStructureException($"No se pudo leer '{fileName}': {ex.Message}", ex);
        }
        catch
        {
            parser?.Dispose();
            reader?.Dispose();
            throw;
        }
    }

    /// <summary>Lee el siguiente registro de datos. Devuelve false al llegar al final del archivo.</summary>
    public bool TryRead([NotNullWhen(true)] out CsvRow? row)
    {
        row = null;

        try
        {
            while (_parser.Read())
            {
                var record = _parser.Record;

                // Saltar lineas vacias (solo separadores o espacios).
                if (record is null || record.All(string.IsNullOrWhiteSpace)) continue;

                _dataRow++;
                var raw = _parser.RawRecord.TrimEnd('\r', '\n');
                row = new CsvRow(_dataRow, record, raw, _index, Headers.Count);
                return true;
            }
        }
        catch (CsvHelperException ex)
        {
            throw new CsvStructureException(
                $"Error al leer '{Path.GetFileName(FilePath)}' cerca del registro {_dataRow + 1}: {ex.Message}", ex);
        }

        return false;
    }

    public void Dispose()
    {
        _parser.Dispose();
        _reader.Dispose();
    }

    /// <summary>UTF-8 si el archivo es UTF-8 valido; de lo contrario Latin-1 (Windows-1252 aprox.).</summary>
    private static Encoding DetectEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
    }
}
