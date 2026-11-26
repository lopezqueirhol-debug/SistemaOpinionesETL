-- Grupo A Indicadores de resenas -> FactResenaWeb
--  Grupo B Vista base que une las 3 fuentes de opinion
--  Grupo C Vistas generales -> vw_OpinionesUnificadas
-- =====================================================================

SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;
USE SistemaOpinionesDW;


-- =====================================================================
-- GRUPO A - INDICADORES DE RESENAS
-- =====================================================================

-- Total, positivas, neutras, negativas, % de satisfaccion y calificacion promedio.
CREATE VIEW vw_ResenasResumen AS
SELECT COUNT(*)                                                         AS TotalResenas,
       COALESCE(SUM(c.Clasificacion = 'Positiva'), 0)                   AS ResenasPositivas,
       COALESCE(SUM(c.Clasificacion = 'Neutra'), 0)                     AS ResenasNeutras,
       COALESCE(SUM(c.Clasificacion = 'Negativa'), 0)                   AS ResenasNegativas,
       ROUND(SUM(c.Clasificacion = 'Positiva') * 100 / NULLIF(COUNT(*), 0), 2) AS PorcentajeSatisfaccion,
       ROUND(AVG(r.Rating), 2)                                          AS CalificacionPromedio
  FROM FactResenaWeb r
  JOIN DimClasificacion c ON c.IdClasificacionKey = r.IdClasificacionKey;


-- Resenas por producto.
CREATE VIEW vw_ResenasPorProducto AS
SELECT p.IdProductoNK,
       p.Nombre AS Producto,
       p.Categoria,
       COUNT(*) AS TotalResenas,
       SUM(c.Clasificacion = 'Positiva') AS ResenasPositivas,
       SUM(c.Clasificacion = 'Neutra') AS ResenasNeutras,
       SUM(c.Clasificacion = 'Negativa') AS ResenasNegativas,
       ROUND(AVG(r.Rating), 2) AS CalificacionPromedio,
       ROUND(SUM(c.Clasificacion = 'Positiva') * 100 / NULLIF(COUNT(*), 0), 2) AS PorcentajeSatisfaccion
  FROM FactResenaWeb r
  JOIN DimProducto p ON p.IdProductoKey = r.IdProductoKey
  JOIN DimClasificacion c ON c.IdClasificacionKey = r.IdClasificacionKey
 GROUP BY p.IdProductoNK, p.Nombre, p.Categoria;


-- Tendencia de resenas por mes.
CREATE VIEW vw_ResenasTendencia AS
SELECT t.Anio,
       t.Mes,
       t.NombreMes,
       COUNT(*) AS TotalResenas,
       ROUND(AVG(r.Rating), 2) AS CalificacionPromedio
  FROM FactResenaWeb r
  JOIN DimTiempo t ON t.IdTiempoKey = r.IdTiempoKey
 GROUP BY t.Anio, t.Mes, t.NombreMes
 ORDER BY t.Anio, t.Mes;


-- Tendencia de resenas por fecha exacta.
CREATE VIEW vw_ResenasTendenciaDiaria AS
SELECT t.Fecha,
       COUNT(*) AS TotalResenas,
       ROUND(AVG(r.Rating), 2) AS CalificacionPromedio
  FROM FactResenaWeb r
  JOIN DimTiempo t ON t.IdTiempoKey = r.IdTiempoKey
 GROUP BY t.Fecha
 ORDER BY t.Fecha;


-- =====================================================================
-- GRUPO B - VISTA BASE: LAS 3 FUENTES EN UN FORMATO COMUN
-- =====================================================================
CREATE VIEW vw_OpinionesUnificadas AS
SELECT 'Encuesta' COLLATE utf8mb4_unicode_ci AS Origen,
       IdTiempoKey, IdProductoKey, IdClienteKey, IdFuenteKey,
       IdClasificacionKey,
       PuntajeSatisfaccion AS Puntaje
  FROM FactEncuesta
UNION ALL
SELECT 'ResenaWeb' COLLATE utf8mb4_unicode_ci,
       IdTiempoKey, IdProductoKey, IdClienteKey, IdFuenteKey,
       IdClasificacionKey,
       Rating
  FROM FactResenaWeb
UNION ALL
SELECT 'ComentarioSocial' COLLATE utf8mb4_unicode_ci,
       IdTiempoKey, IdProductoKey, IdClienteKey, IdFuenteKey,
       NULL,
       NULL
  FROM FactComentarioSocial;


-- =====================================================================
-- GRUPO C - VISTAS GENERALES
-- =====================================================================

-- Totales globales y promedio general de satisfaccion (una sola fila).
CREATE VIEW vw_ResumenGeneral AS
SELECT COUNT(*) AS TotalOpiniones,
       SUM(o.Origen = 'Encuesta') AS TotalEncuestas,
       SUM(o.Origen = 'ResenaWeb') AS TotalResenasWeb,
       SUM(o.Origen = 'ComentarioSocial') AS TotalComentariosSociales,
       ROUND(AVG(o.Puntaje), 2) AS PuntajePromedioGeneral,
       SUM(o.IdClasificacionKey IS NOT NULL) AS Clasificadas,
       SUM(o.IdClasificacionKey = 1) AS Positivas,
       SUM(o.IdClasificacionKey = 2) AS Neutras,
       SUM(o.IdClasificacionKey = 3) AS Negativas,
       ROUND(SUM(o.IdClasificacionKey = 1) * 100
             / NULLIF(SUM(o.IdClasificacionKey IS NOT NULL), 0), 2) AS PctPositivasSobreClasificadas,
       ROUND(SUM(o.IdClasificacionKey = 1) * 100
             / NULLIF(COUNT(*), 0), 2)                AS PctPositivasSobreTotal
  FROM vw_OpinionesUnificadas o;


-- Cuantas opiniones aporta cada tipo de canal.
CREATE VIEW vw_ComentariosPorFuente AS
SELECT f.TipoFuente,
       COUNT(*) AS TotalComentarios,
       ROUND(COUNT(*) * 100 / (SELECT COUNT(*) FROM vw_OpinionesUnificadas), 2) AS PorcentajeDelTotal
  FROM vw_OpinionesUnificadas o
  JOIN DimFuente f ON f.IdFuenteKey = o.IdFuenteKey
 GROUP BY f.TipoFuente;


-- Distribucion positiva / neutra / negativa
CREATE VIEW vw_DistribucionClasificacion AS
SELECT c.Clasificacion,
       COUNT(*) AS Total,
       ROUND(COUNT(*) * 100 / (SELECT COUNT(*) FROM vw_OpinionesUnificadas
                                WHERE IdClasificacionKey IS NOT NULL), 2) AS Porcentaje
  FROM vw_OpinionesUnificadas o
  JOIN DimClasificacion c ON c.IdClasificacionKey = o.IdClasificacionKey
 GROUP BY c.IdClasificacionKey, c.Clasificacion;


-- Resumen por producto volumen, calificacion y satisfaccion.
CREATE VIEW vw_ResumenPorProducto AS
SELECT p.IdProductoNK,
       p.Nombre AS Producto,
       p.Categoria,
       COUNT(*) AS TotalOpiniones,
       SUM(o.IdClasificacionKey IS NOT NULL) AS Clasificadas,
       SUM(o.IdClasificacionKey = 1) AS Positivas,
       SUM(o.IdClasificacionKey = 2) AS Neutras,
       SUM(o.IdClasificacionKey = 3) AS Negativas,
       ROUND(AVG(o.Puntaje), 2) AS PuntajePromedio,
       ROUND(SUM(o.IdClasificacionKey = 1) * 100
             / NULLIF(SUM(o.IdClasificacionKey IS NOT NULL), 0), 2) AS PorcentajeSatisfaccion
  FROM vw_OpinionesUnificadas o
  JOIN DimProducto p ON p.IdProductoKey = o.IdProductoKey
 GROUP BY p.IdProductoNK, p.Nombre, p.Categoria;


-- Evolucion de la satisfaccion de cada producto mes a mes.
CREATE VIEW vw_SatisfaccionProductoMensual AS
SELECT p.IdProductoNK,
       p.Nombre AS Producto,
       t.Anio,
       t.Mes,
       t.NombreMes,
       COUNT(*) AS TotalOpiniones,
       ROUND(AVG(o.Puntaje), 2) AS PuntajePromedio,
       ROUND(SUM(o.IdClasificacionKey = 1) * 100
             / NULLIF(SUM(o.IdClasificacionKey IS NOT NULL), 0), 2) AS PorcentajeSatisfaccion
  FROM vw_OpinionesUnificadas o
  JOIN DimProducto p ON p.IdProductoKey = o.IdProductoKey
  JOIN DimTiempo t ON t.IdTiempoKey = o.IdTiempoKey
 GROUP BY p.IdProductoNK, p.Nombre, t.Anio, t.Mes, t.NombreMes
 ORDER BY p.IdProductoNK, t.Anio, t.Mes;


-- Actividad de cada cliente identificado (los comentarios anonimos no se atribuyen).
CREATE VIEW vw_ActividadPorCliente AS
SELECT cl.IdClienteNK,
       cl.Nombre AS Cliente,
       cl.Email,
       SUM(o.Origen = 'Encuesta') AS Encuestas,
       SUM(o.Origen = 'ResenaWeb') AS ResenasWeb,
       SUM(o.Origen = 'ComentarioSocial') AS ComentariosSociales,
       COUNT(*) AS TotalOpiniones
  FROM vw_OpinionesUnificadas o
  JOIN DimCliente cl ON cl.IdClienteKey = o.IdClienteKey
 GROUP BY cl.IdClienteNK, cl.Nombre, cl.Email
 ORDER BY TotalOpiniones DESC;


-- Tendencia general mes a mes: volumen, puntaje y satisfaccion.
CREATE VIEW vw_TendenciaGeneral AS
SELECT t.Anio,
       t.Mes,
       t.NombreMes,
       COUNT(*) AS TotalOpiniones,
       SUM(o.IdClasificacionKey IS NOT NULL) AS Clasificadas,
       SUM(o.IdClasificacionKey = 1) AS Positivas,
       SUM(o.IdClasificacionKey = 2) AS Neutras,
       SUM(o.IdClasificacionKey = 3) AS Negativas,
       ROUND(AVG(o.Puntaje), 2) AS PuntajePromedio,
       ROUND(SUM(o.IdClasificacionKey = 1) * 100
             / NULLIF(SUM(o.IdClasificacionKey IS NOT NULL), 0), 2) AS PorcentajeSatisfaccion
  FROM vw_OpinionesUnificadas o
  JOIN DimTiempo t ON t.IdTiempoKey = o.IdTiempoKey
 GROUP BY t.Anio, t.Mes, t.NombreMes
 ORDER BY t.Anio, t.Mes;


-- Tendencia general trimestre a trimestre.
CREATE VIEW vw_TendenciaTrimestral AS
SELECT t.Anio,
       t.Trimestre,
       COUNT(*) AS TotalOpiniones,
       SUM(o.IdClasificacionKey IS NOT NULL) AS Clasificadas,
       SUM(o.IdClasificacionKey = 1) AS Positivas,
       SUM(o.IdClasificacionKey = 2) AS Neutras,
       SUM(o.IdClasificacionKey = 3) AS Negativas,
       ROUND(AVG(o.Puntaje), 2) AS PuntajePromedio,
       ROUND(SUM(o.IdClasificacionKey = 1) * 100
             / NULLIF(SUM(o.IdClasificacionKey IS NOT NULL), 0), 2) AS PorcentajeSatisfaccion
  FROM vw_OpinionesUnificadas o
  JOIN DimTiempo t ON t.IdTiempoKey = o.IdTiempoKey
 GROUP BY t.Anio, t.Trimestre
 ORDER BY t.Anio, t.Trimestre;

CREATE VIEW vw_ComparativaCanales AS
SELECT f.TipoFuente,
       f.Plataforma,
       COUNT(*)                                  AS TotalOpiniones,
       SUM(o.IdClasificacionKey IS NOT NULL)     AS Clasificadas,
       SUM(o.IdClasificacionKey = 1)             AS Positivas,
       SUM(o.IdClasificacionKey = 2)             AS Neutras,
       SUM(o.IdClasificacionKey = 3)             AS Negativas,
       ROUND(SUM(o.IdClasificacionKey = 1) * 100
             / NULLIF(SUM(o.IdClasificacionKey IS NOT NULL), 0), 2) AS PorcentajePositivas,
       ROUND(SUM(o.IdClasificacionKey = 3) * 100
             / NULLIF(SUM(o.IdClasificacionKey IS NOT NULL), 0), 2) AS PorcentajeNegativas,
       ROUND(AVG(o.Puntaje), 2)                  AS PuntajePromedio
  FROM vw_OpinionesUnificadas o
  JOIN DimFuente f ON f.IdFuenteKey = o.IdFuenteKey
 GROUP BY f.TipoFuente, f.Plataforma;