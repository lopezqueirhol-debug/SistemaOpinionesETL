-- =====================================================================
--  CONSULTAS DE VALIDACION DE LA CARGA  |  MySQL 8.4.x
--  Ejecutar DESPUES de correr el ETL.
--  Cada consulta indica el resultado esperado.
-- =====================================================================
SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;
USE SistemaOpiniones;


-- ---------------------------------------------------------------------
-- 1. CONTEO DE REGISTROS POR TABLA
--    Esperado (segun el analisis de los CSV):
--      Cliente 500 | Producto 200 | FuenteDatos 100
--      ComentarioSocial 200 | ResenaWeb 200
--      Encuesta 6 (494 de 500 se rechazan por cliente/producto inexistente)
-- ---------------------------------------------------------------------
SELECT 'Cliente'          AS Tabla, COUNT(*) AS Registros FROM Cliente
UNION ALL SELECT 'Producto',         COUNT(*) FROM Producto
UNION ALL SELECT 'FuenteDatos',      COUNT(*) FROM FuenteDatos
UNION ALL SELECT 'ComentarioSocial', COUNT(*) FROM ComentarioSocial
UNION ALL SELECT 'Encuesta',         COUNT(*) FROM Encuesta
UNION ALL SELECT 'ResenaWeb',        COUNT(*) FROM ResenaWeb;


-- ---------------------------------------------------------------------
-- 2. INTEGRIDAD REFERENCIAL: REGISTROS HUERFANOS EN CADA FK
--    Esperado: Huerfanos = 0 en las nueve filas.
-- ---------------------------------------------------------------------
SELECT 'ComentarioSocial -> Cliente' AS Relacion, COUNT(*) AS Huerfanos
  FROM ComentarioSocial cs
  LEFT JOIN Cliente c ON c.IdCliente = cs.IdCliente
 WHERE cs.IdCliente IS NOT NULL AND c.IdCliente IS NULL
UNION ALL
SELECT 'ComentarioSocial -> Producto', COUNT(*)
  FROM ComentarioSocial cs
  LEFT JOIN Producto p ON p.IdProducto = cs.IdProducto
 WHERE p.IdProducto IS NULL
UNION ALL
SELECT 'ComentarioSocial -> FuenteDatos', COUNT(*)
  FROM ComentarioSocial cs
  LEFT JOIN FuenteDatos f ON f.IdFuente = cs.IdFuente
 WHERE f.IdFuente IS NULL
UNION ALL
SELECT 'Encuesta -> Cliente', COUNT(*)
  FROM Encuesta e
  LEFT JOIN Cliente c ON c.IdCliente = e.IdCliente
 WHERE c.IdCliente IS NULL
UNION ALL
SELECT 'Encuesta -> Producto', COUNT(*)
  FROM Encuesta e
  LEFT JOIN Producto p ON p.IdProducto = e.IdProducto
 WHERE p.IdProducto IS NULL
UNION ALL
SELECT 'Encuesta -> FuenteDatos', COUNT(*)
  FROM Encuesta e
  LEFT JOIN FuenteDatos f ON f.IdFuente = e.IdFuente
 WHERE f.IdFuente IS NULL
UNION ALL
SELECT 'ResenaWeb -> Cliente', COUNT(*)
  FROM ResenaWeb r
  LEFT JOIN Cliente c ON c.IdCliente = r.IdCliente
 WHERE c.IdCliente IS NULL
UNION ALL
SELECT 'ResenaWeb -> Producto', COUNT(*)
  FROM ResenaWeb r
  LEFT JOIN Producto p ON p.IdProducto = r.IdProducto
 WHERE p.IdProducto IS NULL
UNION ALL
SELECT 'ResenaWeb -> FuenteDatos', COUNT(*)
  FROM ResenaWeb r
  LEFT JOIN FuenteDatos f ON f.IdFuente = r.IdFuente
 WHERE f.IdFuente IS NULL;


-- ---------------------------------------------------------------------
-- 3. DUPLICADOS
--    3a. Emails repetidos en Cliente.          Esperado: 0 filas.
--    3b. Opiniones identicas repetidas
--        (mismo cliente, producto, fecha y comentario).  Esperado: 0 filas.
-- ---------------------------------------------------------------------
SELECT Email, COUNT(*) AS Repeticiones
  FROM Cliente
 GROUP BY Email
HAVING COUNT(*) > 1;

SELECT Origen, IdCliente, IdProducto, Fecha, Comentario, COUNT(*) AS Repeticiones
  FROM vw_OpinionesUnificadas
 WHERE IdCliente IS NOT NULL
 GROUP BY Origen, IdCliente, IdProducto, Fecha, Comentario
HAVING COUNT(*) > 1;


-- ---------------------------------------------------------------------
-- 4. RANGOS Y COHERENCIA DE DATOS
--    Esperado: Violaciones = 0 en todas las filas.
-- ---------------------------------------------------------------------
SELECT 'Encuesta: puntaje fuera de 1-5' AS Regla, COUNT(*) AS Violaciones
  FROM Encuesta
 WHERE PuntajeSatisfaccion NOT BETWEEN 1 AND 5
UNION ALL
SELECT 'ResenaWeb: rating fuera de 1-5', COUNT(*)
  FROM ResenaWeb
 WHERE Rating NOT BETWEEN 1 AND 5
UNION ALL
SELECT 'Encuesta: clasificacion incoherente con puntaje', COUNT(*)
  FROM Encuesta
 WHERE NOT ((Clasificacion = 'Negativa' AND PuntajeSatisfaccion <= 2)
         OR (Clasificacion = 'Neutra'   AND PuntajeSatisfaccion = 3)
         OR (Clasificacion = 'Positiva' AND PuntajeSatisfaccion >= 4))
UNION ALL
SELECT 'Cliente: email sin formato valido', COUNT(*)
  FROM Cliente
 WHERE Email NOT LIKE '_%@_%._%' OR Email LIKE '% %';


-- ---------------------------------------------------------------------
-- 5. COMENTARIOS SOCIALES CON CLIENTE NULO
--    Esperado: Anonimos = 88 de 200 (permitido por el modelo).
-- ---------------------------------------------------------------------
SELECT COUNT(*)                   AS TotalComentarios,
       SUM(IdCliente IS NULL)     AS Anonimos,
       SUM(IdCliente IS NOT NULL) AS ConCliente
  FROM ComentarioSocial;


-- ---------------------------------------------------------------------
-- 6. FUENTE ASIGNADA POR TIPO (regla de IdFuente)
--    Esperado: cada tabla transaccional usa una sola fuente
--      ComentarioSocial -> 'Red Social' (F005)
--      ResenaWeb        -> 'Web'        (F001)
--      Encuesta         -> 'CSV'        (F002)
-- ---------------------------------------------------------------------
SELECT 'ComentarioSocial' AS Tabla, cs.IdFuente, f.TipoFuente, COUNT(*) AS Registros
  FROM ComentarioSocial cs JOIN FuenteDatos f ON f.IdFuente = cs.IdFuente
 GROUP BY cs.IdFuente, f.TipoFuente
UNION ALL
SELECT 'ResenaWeb', r.IdFuente, f.TipoFuente, COUNT(*)
  FROM ResenaWeb r JOIN FuenteDatos f ON f.IdFuente = r.IdFuente
 GROUP BY r.IdFuente, f.TipoFuente
UNION ALL
SELECT 'Encuesta', e.IdFuente, f.TipoFuente, COUNT(*)
  FROM Encuesta e JOIN FuenteDatos f ON f.IdFuente = e.IdFuente
 GROUP BY e.IdFuente, f.TipoFuente;


-- ---------------------------------------------------------------------
-- 7. CONCILIACION ENTRE EL LOG DEL ETL Y LAS TABLAS
--    Para la ultima ejecucion:
--      * Procesados = Insertados + Rechazados  (CuadraConteo = 'SI')
--      * Insertados del log = filas reales en la tabla (CuadraConTabla = 'SI')
-- ---------------------------------------------------------------------
SELECT l.Archivo,
       l.TablaDestino,
       l.Procesados, l.Insertados, l.Rechazados,
       IF(l.Procesados = l.Insertados + l.Rechazados, 'SI', 'NO') AS CuadraConteo,
       CASE l.TablaDestino
            WHEN 'Cliente'          THEN (SELECT COUNT(*) FROM Cliente)
            WHEN 'Producto'         THEN (SELECT COUNT(*) FROM Producto)
            WHEN 'FuenteDatos'      THEN (SELECT COUNT(*) FROM FuenteDatos)
            WHEN 'ComentarioSocial' THEN (SELECT COUNT(*) FROM ComentarioSocial)
            WHEN 'Encuesta'         THEN (SELECT COUNT(*) FROM Encuesta)
            WHEN 'ResenaWeb'        THEN (SELECT COUNT(*) FROM ResenaWeb)
       END AS FilasEnTabla,
       l.Estado
  FROM LogCarga l
 WHERE l.IdEjecucion = (SELECT IdEjecucion FROM LogCarga ORDER BY IdLog DESC LIMIT 1)
 ORDER BY l.IdLog;


-- ---------------------------------------------------------------------
-- 8. REGISTROS RECHAZADOS POR ARCHIVO Y MOTIVO
--    Esperado: Encuesta con ~494 rechazos por INTEGRIDAD_REFERENCIAL.
-- ---------------------------------------------------------------------
SELECT * FROM vw_RechazosPorMotivo ORDER BY Archivo, Cantidad DESC;


-- ---------------------------------------------------------------------
-- 9. CONSULTA ENTRE TABLAS (JOIN completo):
--    opiniones por categoria de producto, usando las vistas.
-- ---------------------------------------------------------------------
SELECT * FROM vw_SatisfaccionPorCategoria ORDER BY PuntajePromedio DESC;

-- Top 5 productos con mas opiniones.
SELECT IdProducto, Producto, Categoria, TotalOpiniones, Positivas, Neutras, Negativas, PuntajePromedio
  FROM vw_ResumenPorProducto
 ORDER BY TotalOpiniones DESC
 LIMIT 5;
