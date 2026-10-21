using System.Globalization;
using System.Text.RegularExpressions;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;
using SistemaOpiniones.Etl.Infrastructure;

namespace SistemaOpiniones.Etl.Validation;

/// <summary>
/// Validaciones y transformaciones de campos individuales.
/// Cada metodo devuelve el valor limpio o lanza RowValidationException con el motivo del rechazo:
///   CAMPO_OBLIGATORIO | FORMATO | TIPO_DATO | REGLA_NEGOCIO
/// </summary>
public sealed class FieldValidator
{
    private static readonly Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhiteSpaceRegex =
        new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy" };

    // Textos que representan "sin dato" aunque el campo no venga vacio.
    private static readonly HashSet<string> MissingTokens =
        new(StringComparer.OrdinalIgnoreCase) { "null", "nan", "n/a" };

    private static readonly Dictionary<string, string> Classifications = new()
    {
        ["positiva"] = "Positiva",
        ["neutra"] = "Neutra",
        ["negativa"] = "Negativa"
    };

    private readonly DateOnly _minDate;
    private readonly bool _rejectFutureDates;

    public FieldValidator(EtlOptions options)
    {
        if (!DateOnly.TryParseExact(options.MinDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out _minDate))
        {
            throw new ConfigurationException(
                $"Etl:MinDate inválida ('{options.MinDate}'). Use el formato yyyy-MM-dd.");
        }

        _rejectFutureDates = options.RejectFutureDates;
    }

    // ------------------------------------------------------------------
    // Limpieza
    // ------------------------------------------------------------------

    /// <summary>Quita espacios al inicio/fin y colapsa espacios internos repetidos.</summary>
    public static string CleanText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return WhiteSpaceRegex.Replace(raw.Trim(), " ");
    }

    private static bool IsMissing(string cleanValue) =>
        cleanValue.Length == 0 || MissingTokens.Contains(cleanValue);

    private static RowValidationException Missing(string field) =>
        new(RejectionReason.CampoObligatorio, $"El campo '{field}' es obligatorio y viene vacío.");

    // ------------------------------------------------------------------
    // Texto
    // ------------------------------------------------------------------

    public string RequiredText(string? raw, string field, int maxLength)
    {
        var value = CleanText(raw);
        if (IsMissing(value)) throw Missing(field);

        if (value.Length > maxLength)
            throw new RowValidationException(RejectionReason.Formato,
                $"'{field}' excede el máximo de {maxLength} caracteres (tiene {value.Length}).");

        return value;
    }

    public string Email(string? raw, string field, int maxLength)
    {
        var value = RequiredText(raw, field, maxLength).ToLowerInvariant();

        if (!EmailRegex.IsMatch(value))
            throw new RowValidationException(RejectionReason.Formato,
                $"'{field}': el correo '{value}' no tiene un formato válido.");

        return value;
    }

    // ------------------------------------------------------------------
    // Identificadores (transformacion a formato de la base de datos)
    // ------------------------------------------------------------------

    /// <summary>
    /// Normaliza un identificador al formato de la base de datos: prefijo + digitos con ceros a la izquierda.
    /// Acepta un numero ("1" -> "C001") o un codigo ya formateado ("c19" / "C019" -> "C019").
    /// </summary>
    public string Code(string? raw, char prefix, int padWidth, string field, int maxLength = 10)
    {
        var value = CleanText(raw);
        if (IsMissing(value)) throw Missing(field);

        var upperPrefix = char.ToUpperInvariant(prefix);
        string digits;

        if (value.All(char.IsAsciiDigit))
        {
            digits = value;
        }
        else if (value.Length > 1
                 && char.ToUpperInvariant(value[0]) == upperPrefix
                 && value.Skip(1).All(char.IsAsciiDigit))
        {
            digits = value.Substring(1);
        }
        else
        {
            throw new RowValidationException(RejectionReason.Formato,
                $"'{field}': formato de identificador inválido '{value}' (se esperaba {upperPrefix}### o un número).");
        }

        digits = digits.TrimStart('0');
        if (digits.Length == 0)
            throw new RowValidationException(RejectionReason.ReglaNegocio,
                $"'{field}': el identificador no puede ser cero ('{value}').");

        var code = upperPrefix + digits.PadLeft(padWidth, '0');

        if (code.Length > maxLength)
            throw new RowValidationException(RejectionReason.Formato,
                $"'{field}': el identificador '{code}' excede {maxLength} caracteres.");

        return code;
    }

    /// <summary>Igual que Code, pero un valor vacio o "NULL" devuelve null (campo opcional).</summary>
    public string? OptionalCode(string? raw, char prefix, int padWidth, string field, int maxLength = 10)
    {
        var value = CleanText(raw);
        return IsMissing(value) ? null : Code(value, prefix, padWidth, field, maxLength);
    }

    // ------------------------------------------------------------------
    // Numeros
    // ------------------------------------------------------------------

    public int Integer(string? raw, string field)
    {
        var value = CleanText(raw);
        if (IsMissing(value)) throw Missing(field);

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            throw new RowValidationException(RejectionReason.TipoDato,
                $"'{field}': se esperaba un número entero y se recibió '{value}'.");

        return number;
    }

    public int PositiveInteger(string? raw, string field)
    {
        var number = Integer(raw, field);
        if (number <= 0)
            throw new RowValidationException(RejectionReason.ReglaNegocio,
                $"'{field}': debe ser un entero positivo (se recibió {number}).");

        return number;
    }

    public int IntegerInRange(string? raw, string field, int min, int max)
    {
        var number = Integer(raw, field);
        if (number < min || number > max)
            throw new RowValidationException(RejectionReason.ReglaNegocio,
                $"'{field}': el valor {number} está fuera del rango permitido ({min}-{max}).");

        return number;
    }

    // ------------------------------------------------------------------
    // Fechas
    // ------------------------------------------------------------------

    public DateOnly Date(string? raw, string field)
    {
        var value = CleanText(raw);
        if (IsMissing(value)) throw Missing(field);

        if (!DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var parsed))
        {
            throw new RowValidationException(RejectionReason.Formato,
                $"'{field}': la fecha '{value}' no tiene un formato válido (se esperaba yyyy-MM-dd).");
        }

        var date = DateOnly.FromDateTime(parsed);

        if (date < _minDate)
            throw new RowValidationException(RejectionReason.ReglaNegocio,
                $"'{field}': la fecha {date:yyyy-MM-dd} es anterior al mínimo permitido ({_minDate:yyyy-MM-dd}).");

        if (_rejectFutureDates && date > DateOnly.FromDateTime(DateTime.Today))
            throw new RowValidationException(RejectionReason.ReglaNegocio,
                $"'{field}': la fecha {date:yyyy-MM-dd} es futura.");

        return date;
    }

    // ------------------------------------------------------------------
    // Reglas de dominio de las encuestas
    // ------------------------------------------------------------------

    /// <summary>Normaliza la clasificacion a Positiva | Neutra | Negativa.</summary>
    public string Classification(string? raw, string field)
    {
        var value = CleanText(raw);
        if (IsMissing(value)) throw Missing(field);

        if (!Classifications.TryGetValue(TextNormalizer.NormalizeKey(value), out var canonical))
            throw new RowValidationException(RejectionReason.ReglaNegocio,
                $"'{field}': el valor '{value}' no es válido (use Positiva, Neutra o Negativa).");

        return canonical;
    }

    /// <summary>Regla: 1-2 Negativa | 3 Neutra | 4-5 Positiva.</summary>
    public static void EnsureClassificationMatchesScore(string classification, int score)
    {
        var expected = score switch
        {
            <= 2 => "Negativa",
            3 => "Neutra",
            _ => "Positiva"
        };

        if (!string.Equals(classification, expected, StringComparison.Ordinal))
            throw new RowValidationException(RejectionReason.ReglaNegocio,
                $"La clasificación '{classification}' es incoherente con el puntaje {score} (corresponde '{expected}').");
    }
}
