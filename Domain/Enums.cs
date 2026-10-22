namespace SistemaOpiniones.Etl.Domain;

/// <summary>
/// Motivo por el que se rechaza un registro. Los valores en base de datos
/// coinciden con el ENUM RegistroRechazado.Motivo del script SQL.
/// </summary>
public enum RejectionReason
{
    CampoObligatorio,
    Formato,
    TipoDato,
    Duplicado,
    IntegridadReferencial,
    ReglaNegocio,
    ErrorBd
}

public static class RejectionReasonExtensions
{
    public static string ToDbValue(this RejectionReason reason) => reason switch
    {
        RejectionReason.CampoObligatorio => "CAMPO_OBLIGATORIO",
        RejectionReason.Formato => "FORMATO",
        RejectionReason.TipoDato => "TIPO_DATO",
        RejectionReason.Duplicado => "DUPLICADO",
        RejectionReason.IntegridadReferencial => "INTEGRIDAD_REFERENCIAL",
        RejectionReason.ReglaNegocio => "REGLA_NEGOCIO",
        _ => "ERROR_BD"
    };

    public static RejectionReason FromDbValue(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "CAMPO_OBLIGATORIO" => RejectionReason.CampoObligatorio,
        "FORMATO" => RejectionReason.Formato,
        "TIPO_DATO" => RejectionReason.TipoDato,
        "DUPLICADO" => RejectionReason.Duplicado,
        "INTEGRIDAD_REFERENCIAL" => RejectionReason.IntegridadReferencial,
        "REGLA_NEGOCIO" => RejectionReason.ReglaNegocio,
        _ => RejectionReason.ErrorBd
    };
}

/// <summary>Estado final de la carga de un archivo (ENUM LogCarga.Estado).</summary>
public enum LoadStatus
{
    Completado,
    CompletadoConRechazos,
    Error
}

public static class LoadStatusExtensions
{
    public static string ToDbValue(this LoadStatus status) => status switch
    {
        LoadStatus.Completado => "Completado",
        LoadStatus.CompletadoConRechazos => "CompletadoConRechazos",
        _ => "Error"
    };
}