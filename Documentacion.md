# Documento Tecnico - Proceso de Extraccion (E de ETL)
### Sistema de Analisis de Opiniones de Clientes

---

## 1. Objetivo

Disenar la arquitectura base y desarrollar el proceso de extraccion correspondiente al Sistema de Analisis de Opiniones de Clientes, garantizando rendimiento, escalabilidad, seguridad y mantenibilidad, segun los requisitos no funcionales de la practica.

El alcance de este entregable es la **E (Extraccion)** del pipeline ETL: leer las tres fuentes de datos y dejarlas en archivos de *staging*, listas para una etapa de transformacion y carga posterior hacia el Data Warehouse (`SistemaOpinionesDW`).

---

## 2. Diagrama de arquitectura

![Arquitectura](`Diagramas/Diagrama_Arquitectura.svg`)

### Componentes identificados

| Componente | Tecnologia | Responsabilidad |
|---|---|---|
| `CsvExtractor` (`CsvSurveyExtractor`) | C#, CsvHelper | Lee `surveys_part1.csv` (encuestas internas) |
| `DatabaseExtractor` (`RelationalReviewsExtractor`) | C#, MySqlConnector (ADO.NET) | Consulta `ResenaWeb` en la base relacional `SistemaOpiniones` |
| `ApiExtractor` (`RestCommentsExtractor`) | C#, `IHttpClientFactory` | Consume `GET /comments` de la API REST de comentarios sociales |
| `DataLoader` (`JsonStagingLoader`) | C# | Escribe cada lote extraido como JSON en `Staging/` |
| `LoggerService` | `ILogger` + `Stopwatch` | Registra eventos, errores y metricas de rendimiento por fuente y por ciclo |
| Base de datos analitica | MySQL (`SistemaOpinionesDW`) | Destino final tras la transformacion y carga (fuera del alcance de este proyecto) |
| Modulo de visualizacion | - | Dashboard sobre las vistas del DW (fuera del alcance de este proyecto) |

### Como la arquitectura garantiza los atributos de calidad

**Rendimiento.** Las tres fuentes se extraen de forma **asincrona y en paralelo** con `Task.WhenAll`, en lugar de secuencialmente. Un `SemaphoreSlim(MaxConcurrency)` limita cuantas fuentes corren a la vez, para no saturar la base de datos, la API o el disco si en el futuro se agregan mas fuentes. Cada extractor y cada ciclo completo se miden con `Stopwatch`, y los resultados (registros, milisegundos, registros/segundo) quedan en el log.

**Escalabilidad.** Todas las fuentes implementan la misma interfaz `IExtractor`. Agregar una fuente nueva no modifica el Worker ni las fuentes existentes: solo requiere una clase nueva que implemente `IExtractor` y su registro en el contenedor de dependencias (`Program.cs`). La configuracion de cada fuente (ruta de CSV, cadena de conexion, URL de API) vive en `appsettings.json` bajo `WorkerSources`, asi que cambiar un origen de datos no requiere recompilar el codigo, solo editar configuracion.

**Seguridad.** Ninguna credencial vive en el codigo ni en el archivo versionado en el repositorio. `appsettings.json` contiene unicamente valores de ejemplo o vacios (`TU_PASSWORD`, cadenas vacias), y `appsettings.json` esta en `.gitignore`. Las credenciales reales (cadena de conexion a la base de resenas, URL y clave de la API) se inyectan por **variables de entorno** en tiempo de ejecucion mediante `PostConfigure<WorkerSourceOptions>`, que tiene prioridad sobre el JSON. La clave de la API viaja en la cabecera `X-Api-Key`, nunca en la URL ni en el cuerpo.

**Mantenibilidad.** El codigo esta organizado por capas con responsabilidades separadas: `Domain` (contratos e interfaces: `IExtractor`, `IStagingLoader`, `ExtractedRecord`), `Infrastructure` (implementaciones concretas de cada extractor y del escritor de staging), `Worker` (orquestacion del ciclo) y `Configuration` (opciones y carga de ajustes). Esta separacion sigue el espiritu de Clean Architecture: el dominio no depende de ninguna tecnologia concreta, y las implementaciones de infraestructura son intercambiables. **Nota de alcance:** por simplicidad del proyecto academico, estas capas conviven como carpetas dentro de un unico proyecto/ensamblado, no como proyectos de Clase separados en la solucion; la separacion es logica, no fisica.

---

## 3. Diagrama de flujo del proceso ETL

![Flujo del proceso ETL](`Diagramas/Diagrama_Flujo_ETL.svg`)

### Descripcion del ciclo

1. El `EtlExtractionWorker` (un `BackgroundService`) inicia un ciclo y arranca un `Stopwatch`.
2. Lanza las tres tareas de extraccion con `Task.WhenAll`, cada una esperando un cupo del `SemaphoreSlim`.
3. Cada extractor hace su propio trabajo (leer CSV, consultar MySQL, llamar a la API) dentro de un `try/catch` individual: si una fuente falla, se registra el error con `ILogger.LogError` y **no detiene** a las otras dos ni al ciclo.
4. Cada lote exitoso se entrega a `JsonStagingLoader.SaveBatchAsync`, que escribe un archivo unico por fuente (marca de tiempo con milisegundos + sufijo aleatorio, para que nunca colisionen aunque dos fuentes terminen al mismo instante).
5. Al terminar las tres tareas, se registra el resumen del ciclo: duracion total, fuentes exitosas, registros totales, tasa de registros por segundo y la fuente mas lenta.
6. Si se solicito cancelacion (`Ctrl+C`), el host se apaga de forma limpia (`OperationCanceledException` se relanza y se registra). De lo contrario, el worker espera `IntervalMinutes` y repite el ciclo.

---

## 4. Justificacion de decisiones tecnicas

### 4.1 Worker Service en vez de una aplicacion de consola simple

Un `BackgroundService` permite que el proceso de extraccion corra de forma continua y desatendida (un ciclo cada N minutos), que es el comportamiento esperado de un componente de integracion de datos en produccion, a diferencia de una ejecucion manual unica.

### 4.2 Staging en JSON, no insercion directa a la base analitica

Separar la extraccion de la carga permite que cada etapa falle y se reintente de forma independiente: si la transformacion o la carga tienen un error, los datos ya extraidos no se pierden ni hay que volver a golpear las fuentes originales (especialmente relevante para la API externa, cuyo costo de llamado puede ser mayor que el de leer un archivo local).

### 4.3 Equivalente en MySQL a Table-Valued Parameters (SQL Server) 

La practica pidio considerar la documentacion de Microsoft sobre *Table-Valued Parameters vs. BULK INSERT* ([referencia](https://learn.microsoft.com/en-us/sql/relational-databases/tables/use-table-valued-parameters-database-engine?view=sql-server-ver17)). Los TVP son una caracteristica exclusiva de SQL Server y no existen en MySQL 8.4, el motor usado en este proyecto. La tabla siguiente traduce la recomendacion de Microsoft a su equivalente en MySQL:

| Concepto SQL Server | Cuando se recomienda | Equivalente en MySQL |
|---|---|---|
| Table-Valued Parameter (TVP) | Lotes pequenos o medianos, con logica de validacion en el procedimiento | Parametro JSON enviado a un procedimiento almacenado, descompuesto con `JSON_TABLE` |
| `BULK INSERT` / `SqlBulkCopy` | Volumenes grandes, carga masiva sin validacion por fila | `LOAD DATA LOCAL INFILE` / `MySqlBulkCopy` (MySqlConnector) |

Para los volumenes de este proyecto (500 encuestas, 200 resenas, 200 comentarios), la estrategia adecuada es la via JSON + procedimiento, que es la que corresponde a la etapa de **carga** (fuera del alcance de este entregable, que cubre solo la extraccion a *staging*). La via de *bulk copy* queda documentada como la opcion a usar si el volumen de alguna fuente creciera significativamente.

### 4.4 Deteccion y correccion de un problema real de codificacion

Durante las pruebas se detecto que el campo `PuntajeSatisfaccion` de las encuestas se extraia siempre como `null`, a pesar de que el CSV si contiene el dato. La causa fue que el encabezado real del archivo es `PuntajeSatisfaccion` (con tilde), mientras que la propiedad de enlace en C# era `PuntajeSatisfaccion` (sin tilde). CsvHelper compara nombres de columna ignorando mayusculas, pero **no** ignora tildes, por lo que el enlace automatico fallaba de forma silenciosa (sin lanzar una excepcion). La correccion fue declarar explicitamente el nombre real de la columna con el atributo `[Name("PuntajeSatisfacción")]`. Esta decision quedo documentada en el propio codigo como advertencia para futuros mantenedores.

Por el mismo motivo se hizo robusto el extractor de la API: `System.Text.Json` es sensible a mayusculas por defecto, asi que se configuro `PropertyNameCaseInsensitive = true` para que la deserializacion no dependa de que la API externa use exactamente el mismo casing (`PascalCase`) que las clases en C#.

---

## 5. Evidencia de ejecucion

Corrida con las tres fuentes configuradas (CSV real, base de datos `SistemaOpiniones` poblada por la practica anterior, y un mock de la API REST servido localmente):

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

| Fuente | Registros extraidos | Resultado |
|---|---:|---|
| `surveys_part1.csv` (CSV) | 500 | Correcto, con `Score` (`PuntajeSatisfaccion`) real tras la correccion |
| `RelationalReviews` (Base de datos) | 200 | Correcto, leidos de `SistemaOpiniones.ResenaWeb` |
| `CommentsRestApi` (API REST) | 200 | Correcto, servidos por el mock local protegido con `X-Api-Key` |
| **Total** | **900** | **3/3 fuentes correctas** |

El contenido de cada archivo de *staging* se valido abriendo el JSON generado y confirmando que los campos de cada fuente (`ExternalId`, `ClientId`, `ProductId`, `EventDateUtc`, `Comment`, `Score`) contienen valores reales, no nulos salvo donde corresponde (comentarios sociales sin puntaje, o comentarios de clientes anonimos sin `ClientId`).

---

## 6. Limitaciones y trabajo futuro

- La API REST consumida es un **mock local** construido para esta practica (`mock_api.py`), que sirve los datos de `social_comments.csv` como si vinieran de una red social externa. No hay una API real de terceros integrada.
- La separacion en capas (`Domain`, `Infrastructure`, `Worker`, `Configuration`) es logica dentro de un unico proyecto, no una solucion con multiples proyectos de clase (Clean Architecture completa con ensamblados separados).
- La transformacion, la carga al Data Warehouse y el dashboard de visualizacion no estan implementados; son continuacion natural de este trabajo.
- La estrategia de carga masiva (`MySqlBulkCopy` / `LOAD DATA INFILE`) queda documentada pero no implementada, porque los volumenes actuales no la requieren.