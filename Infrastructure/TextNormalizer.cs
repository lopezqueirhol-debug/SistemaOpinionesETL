using System.Globalization;
using System.Text;

namespace SistemaOpiniones.Etl.Infrastructure;

public static class TextNormalizer
{
    /// <summary>
    /// Convierte un texto en una clave comparable: minusculas, sin acentos y solo
    /// letras/digitos. Ej.: "Clasificación" -> "clasificacion", "Puntaje Satisfacción" -> "puntajesatisfaccion".
    /// Tambien elimina el caracter BOM (U+FEFF) si viniera pegado al primer encabezado.
    /// </summary>
    public static string NormalizeKey(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}