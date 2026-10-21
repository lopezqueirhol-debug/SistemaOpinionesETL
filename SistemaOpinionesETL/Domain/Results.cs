namespace SistemaOpiniones.Etl.Domain;

/// <summary>Resultado de llamar a un procedimiento sp_Insertar*.</summary>
public sealed record InsertOutcome(RejectionReason? Reason, string? Detail)
{
    public static InsertOutcome Ok { get; } = new(null, null);

    public bool IsOk => Reason is null;
}

/// <summary>Un registro rechazado, con su motivo y el contenido original del CSV.</summary>
public sealed record RejectedRow(int RowNumber, RejectionReason Reason, string Detail, string RawData);

/// <summary>Resultado de cargar un archivo CSV.</summary>
public sealed class FileLoadResult
{
    public FileLoadResult(string fileName, string tableName, bool isMaster)
    {
        FileName = fileName;
        TableName = tableName;
        IsMaster = isMaster;
    }

    public string FileName { get; }
    public string TableName { get; }
    public bool IsMaster { get; }

    /// <summary>Tipo de fuente configurado para el archivo (solo tablas transaccionales).</summary>
    public string? TipoFuente { get; set; }

    /// <summary>IdFuente resuelto para el archivo (solo tablas transaccionales).</summary>
    public string? IdFuente { get; set; }

    public int Procesados { get; set; }
    public int Insertados { get; set; }
    public int Rechazados { get; set; }

    public LoadStatus Status { get; set; } = LoadStatus.Error;
    public string? ErrorMessage { get; set; }
    public string? LogWarning { get; set; }
    public TimeSpan Duration { get; set; }

    public Dictionary<RejectionReason, int> RejectionsByReason { get; } = new();
    public List<RejectedRow> Rejections { get; } = new();

    public void Fail(string message)
    {
        Status = LoadStatus.Error;
        ErrorMessage = message;
    }
}

/// <summary>Resultado de toda la ejecucion del ETL.</summary>
public sealed class EtlRunResult
{
    public Guid RunId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; set; }

    public List<FileLoadResult> Files { get; } = new();
    public Dictionary<string, long> TableCounts { get; set; } = new();

    /// <summary>Archivos que no se ejecutaron porque el proceso se detuvo antes.</summary>
    public List<string> SkippedFiles { get; } = new();

    public bool Aborted { get; set; }
    public string? AbortReason { get; set; }
    public string? RejectedCsvPath { get; set; }

    public int TotalProcesados => Files.Sum(f => f.Procesados);
    public int TotalInsertados => Files.Sum(f => f.Insertados);
    public int TotalRechazados => Files.Sum(f => f.Rechazados);

    public bool HasErrors => Aborted || Files.Any(f => f.Status == LoadStatus.Error);
}
