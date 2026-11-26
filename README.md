# ETL – Sistema de Análisis de Opiniones de Clientes (proceso de extracción)

**Worker Service en .NET 10** que extrae opiniones desde tres fuentes (CSV, base de datos relacional y API REST)
y las deja en archivos JSON de *staging* para la etapa posterior de carga al data warehouse.

## 1. Arquitectura implementada

```
Program.cs ............... Host.CreateApplicationBuilder, DI, HttpClientFactory, arranque del worker
Configuration/ ........... WorkerSourceOptions, AppSettings, SettingsLoader (JSON + variables de entorno)
Domain/ .................. IExtractor, IStagingLoader, ExtractedRecord
Infrastructure/ .......... CsvSurveyExtractor, RelationalReviewsExtractor, RestCommentsExtractor
Infrastructure/Staging/ .. JsonStagingLoader (escritura concurrente segura)
Worker/ .................. EtlExtractionWorker (BackgroundService, Task.WhenAll, SemaphoreSlim, Stopwatch)
Database/ ................ Scripts SQL de la BD transaccional (usan otra etapa del proyecto)
Services/ Validation/ Data/ Presentation/ ... código de la etapa de carga; Program.cs ya no lo registra
Staging/ ................. JSON generado por cada ejecución
mock_api.py .............. Mock local de la API REST de comentarios (ver sección 3.1)
```

Flujo por ciclo: `IExtractor` × 3 → `Task.WhenAll` (concurrencia limitada por `MaxConcurrency`)
→ `JsonStagingLoader.SaveBatchAsync` → métricas con `ILogger` → espera `IntervalMinutes`.

La arquitectura de destino incluye también la **base de datos analítica** y el **dashboard**; esos dos
componentes se alimentan de `Staging/` en una etapa posterior y **no están implementados en este proyecto**.
Diagramas de arquitectura y de flujo: `Diagrama_Arquitectura.svg` y `Diagrama_Flujo_ETL.svg`.
Justificación completa de decisiones: `Documento_Tecnico_Extraccion.md`.

## 2. Requisitos

| Componente | Versión |
|---|---|
| .NET SDK | 8.0 o superior (`net8.0`, `RollForward=LatestMajor`) |
| MySQL Server | 8.4.x (para la fuente relacional) |
| Python 3 | solo para `mock_api.py` (usa únicamente la librería estándar) |
| NuGet | `CsvHelper 33.0.1`, `MySqlConnector 2.6.2`, `Microsoft.Extensions.Hosting 8.0.1`, `Microsoft.Extensions.Http 8.0.1`, `Microsoft.Extensions.Options 8.0.2` |

## 3. Configuración y credenciales

`appsettings.json` **no contiene contraseñas**: la cadena de conexión usa el marcador `TU_PASSWORD`.
Las credenciales reales se entregan mediante variables de entorno (`PostConfigure<WorkerSourceOptions>`
en `Program.cs`), que tienen prioridad sobre el JSON.

| Variable de entorno | Uso |
|---|---|
| `ETL_CONNECTION_STRING` | Cadena de la BD transaccional `SistemaOpiniones` (etapa de carga) |
| `ETL_SURVEYS_CSV_PATH` | Ruta al CSV de encuestas |
| `ETL_REVIEWS_CONNECTION_STRING` | Cadena de la BD de reseñas web (fuente relacional) |
| `ETL_REVIEWS_QUERY` | Consulta de extracción de reseñas |
| `ETL_COMMENTS_API_URL` | Base URL de la API REST de comentarios |
| `ETL_COMMENTS_API_PATH` | Ruta relativa dentro de la API |
| `ETL_COMMENTS_API_KEY` | Clave enviada como cabecera `X-Api-Key` |
| `ETL_STAGING_DIRECTORY` | Carpeta de salida (por defecto `Staging`) |
| `ETL_INTERVAL_MINUTES` | Intervalo entre ciclos (por defecto 15) |
| `ETL_MAX_CONCURRENCY` | Máximo de fuentes en paralelo (por defecto 3) |

### 3.1 Base de datos relacional (fuente de reseñas)

Usa la base `SistemaOpiniones` ya creada y poblada en la práctica de carga (`Database/01_SistemaOpiniones_Schema.sql`
+ el ETL de carga). No hace falta volver a ejecutar ningún script si esa base ya tiene datos.

```powershell
$env:ETL_REVIEWS_CONNECTION_STRING = "Server=localhost;Port=3306;Database=SistemaOpiniones;User ID=root;Password=TU_PASSWORD;CharSet=utf8mb4;AllowUserVariables=true"
```

### 3.2 API REST (mock local)

`mock_api.py` sirve los comentarios de `Data/social_comments.csv` como JSON en `GET /comments`, protegido con la
cabecera `X-Api-Key`. Solo usa la librería estándar de Python (`http.server`), sin dependencias externas.

```bash
# Terminal 1: levantar el mock (desde la raíz del proyecto, junto al .csproj)
python mock_api.py
```

```powershell
# Terminal 2: variables de la API antes de correr el worker
$env:ETL_COMMENTS_API_URL = "http://localhost:5000/"
$env:ETL_COMMENTS_API_PATH = "/comments"
$env:ETL_COMMENTS_API_KEY = "local-dev-key"
```

Si una fuente no tiene configuración (cadena o URL vacía), se omite con `0` registros y queda registrado
en el log; no detiene a las demás.

## 4. Ejecución

```bash
dotnet restore
dotnet build
dotnet run
```

`Ctrl+C` detiene el ciclo de forma limpia: la cancelación se propaga por el `CancellationToken` y se
relanza `OperationCanceledException` desde el worker.

## 5. Resiliencia, concurrencia y métricas

- `Task.WhenAll` ejecuta los extractores en paralelo; cada tarea devuelve un `ExtractionOutcome`
  (fuente, tipo, éxito/fallo, registros, milisegundos y motivo de error).
- Cada extractor tiene su propio `try/catch`: un fallo se registra y **no tumba** a las otras fuentes
  ni al ciclo; la cancelación del host se relanza para detener el proceso.
- `SemaphoreSlim(MaxConcurrency)` evita saturar la BD, la API y el disco si se agregan fuentes.
- Cada lote se escribe en un archivo único (`timestamp` con milisegundos + sufijo aleatorio),
  por lo que no hay colisiones aunque varias fuentes terminen al mismo tiempo.
- `ILogger` registra por fuente (registros, duración) y por ciclo (tiempo total, fuentes correctas,
  registros totales, tasa `reg/s`, fuente más lenta y concurrencia usada).

## 6. Evidencia de ejecución (3/3 fuentes con datos reales)

Corrida con: CSV real, `SistemaOpiniones.ResenaWeb` poblada por la práctica anterior, y `mock_api.py`
sirviendo `social_comments.csv`.

```
info: SistemaOpiniones.Etl.Worker.EtlExtractionWorker[0]
      EtlExtractionWorker iniciado: 3 fuentes, concurrencia maxima 3, intervalo 15 min
info: SistemaOpiniones.Etl.Worker.EtlExtractionWorker[0]
      Ciclo 1 - extrayendo surveys_part1.csv (CSV)
info: SistemaOpiniones.Etl.Worker.EtlExtractionWorker[0]
      Ciclo 1 - extrayendo RelationalReviews (DATABASE)
info: SistemaOpiniones.Etl.Worker.EtlExtractionWorker[0]
      Ciclo 1 - extrayendo CommentsRestApi (API_REST)
info: SistemaOpiniones.Etl.Infrastructure.Staging.JsonStagingLoader[0]
      Guardados 500 registros en staging: ...\Staging\staging_surveyspart1csv_*.json
info: SistemaOpiniones.Etl.Infrastructure.Staging.JsonStagingLoader[0]
      Guardados 200 registros en staging: ...\Staging\staging_RelationalReviews_*.json
info: SistemaOpiniones.Etl.Infrastructure.Staging.JsonStagingLoader[0]
      Guardados 200 registros en staging: ...\Staging\staging_CommentsRestApi_*.json
info: SistemaOpiniones.Etl.Worker.EtlExtractionWorker[0]
      Ciclo 1 finalizado en ... ms | fuentes 3/3 | registros 900 | tasa ... reg/s | ...
```

| Fuente | Registros | Resultado |
|---|---:|---|
| `surveys_part1.csv` (CSV) | 500 | ✅ `Score` real (ver sección 8, bug corregido) |
| `RelationalReviews` (BD) | 200 | ✅ leídos de `ResenaWeb` |
| `CommentsRestApi` (API REST) | 200 | ✅ servidos por `mock_api.py` |
| **Total** | **900** | **3/3 fuentes correctas** |

## 7. Validación de la base de datos transaccional

Para la etapa de carga (no ejecutada por el worker):

1. `Database/01_SistemaOpiniones_Schema.sql` crea BD, tablas, restricciones, procedimientos y vistas.
2. `Database/02_Consultas_Validacion.sql` contiene 9 bloques de consultas con su resultado esperado.

## 8. Bugs detectados y corregidos durante las pruebas

| Síntoma | Causa | Corrección |
|---|---|---|
| `Score` de `surveys_part1.csv` siempre `null` en staging | El CSV trae el encabezado `PuntajeSatisfacción` (con tilde); la propiedad C# era `PuntajeSatisfaccion` (sin tilde). CsvHelper ignora mayúsculas pero no tildes, así que el enlace fallaba en silencio | `[Name("PuntajeSatisfacción")]` explícito en `SurveyRow.PuntajeSatisfaccion` (`CsvSurveyExtractor.cs`) |
| Riesgo: API externa con JSON en minúscula/camelCase se leería como todo `null` | `System.Text.Json` es sensible a mayúsculas por defecto; `ReadFromJsonAsync` no tenía opciones | `JsonSerializerOptions { PropertyNameCaseInsensitive = true }` en `RestCommentsExtractor.cs` |

## 9. Problemas frecuentes

| Síntoma | Solución |
|---|---|
| `Configure la contraseña de MySQL...` | Definir `ETL_CONNECTION_STRING` (no editar el JSON con secretos) |
| `Authentication method 'caching_sha2_password' failed` | Añadir `AllowPublicKeyRetrieval=true` a la cadena de conexión |
| `Archivo CSV no encontrado` | Revisar `ETL_SURVEYS_CSV_PATH` o dejar el CSV en `Data/` |
| Fuente BD con 0 registros | Definir `ETL_REVIEWS_CONNECTION_STRING` y confirmar que `ResenaWeb` tiene datos |
| Fuente API con 0 registros | Levantar `python mock_api.py` antes de `dotnet run` y definir `ETL_COMMENTS_API_URL` |
| API responde `401` | La clave en `ETL_COMMENTS_API_KEY` no coincide con `API_KEY` de `mock_api.py` |
| Caracteres raros en consola | Usar Windows Terminal o `chcp 65001` |
