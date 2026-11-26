--    DimTiempo, DimProducto, DimCliente, DimFuente, DimClasificacion

SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;
USE SistemaOpinionesDW;


-- ---------------------------------------------------------------------
-- DimTiempo
-- IdTiempoKey NO es autoincremental: se calcula como AAAAMMDD.
-- Permite filtrar por rango.
-- ---------------------------------------------------------------------
CREATE TABLE DimTiempo (
    IdTiempoKey INT NOT NULL,
    Fecha DATE NOT NULL,
    Anio SMALLINT NOT NULL,
    Trimestre TINYINT NOT NULL,
    Mes TINYINT NOT NULL,
    NombreMes VARCHAR(15) NOT NULL,
    Dia TINYINT NOT NULL,
    DiaSemana VARCHAR(15) NOT NULL,
    EsFinDeSemana TINYINT(1) NOT NULL,
    PRIMARY KEY (IdTiempoKey),
    UNIQUE KEY uq_dimtiempo_fecha (Fecha)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ---------------------------------------------------------------------
-- DimProducto  <-  products.csv
-- ---------------------------------------------------------------------
CREATE TABLE DimProducto (
    IdProductoKey INT NOT NULL AUTO_INCREMENT,
    IdProductoNK VARCHAR(10) NOT NULL,
    Nombre VARCHAR(100) NOT NULL,
    Categoria VARCHAR(50)  NOT NULL,
    PRIMARY KEY (IdProductoKey),
    UNIQUE KEY uq_dimproducto_nk (IdProductoNK)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ---------------------------------------------------------------------
-- DimCliente  <-  clients.csv
-- ---------------------------------------------------------------------
CREATE TABLE DimCliente (
    IdClienteKey INT NOT NULL AUTO_INCREMENT,
    IdClienteNK VARCHAR(10) NOT NULL,
    Nombre VARCHAR(100) NOT NULL,
    Email VARCHAR(150) NOT NULL,
    PRIMARY KEY (IdClienteKey),
    UNIQUE KEY uq_dimcliente_nk (IdClienteNK)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ---------------------------------------------------------------------
-- DimFuente
-- Representa el CANAL de la opinion
-- ---------------------------------------------------------------------
CREATE TABLE DimFuente (
    IdFuenteKey INT NOT NULL AUTO_INCREMENT,
    TipoFuente VARCHAR(50) NOT NULL,
    Plataforma VARCHAR(50) NOT NULL,
    PRIMARY KEY (IdFuenteKey),
    UNIQUE KEY uq_dimfuente_tipo_plataforma (TipoFuente, Plataforma)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO DimFuente (TipoFuente, Plataforma) VALUES
    ('Encuesta', 'EncuestaInterna'),
    ('Web', 'Web'),
    ('Red Social', 'Instagram'),
    ('Red Social', 'Facebook'),
    ('Red Social', 'Twitter');


-- ---------------------------------------------------------------------
-- DimClasificacion
-- Catalogo fijo de sentimiento.
-- ---------------------------------------------------------------------
CREATE TABLE DimClasificacion (
    IdClasificacionKey TINYINT NOT NULL,
    Clasificacion VARCHAR(10) NOT NULL,
    PRIMARY KEY (IdClasificacionKey),
    UNIQUE KEY uq_dimclasificacion_nombre (Clasificacion)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO DimClasificacion (IdClasificacionKey, Clasificacion) VALUES
    (1, 'Positiva'),
    (2, 'Neutra'),
    (3, 'Negativa');

-- CARGA DE DimTiempo
SET SESSION cte_max_recursion_depth = 5000;

INSERT INTO DimTiempo
    (IdTiempoKey, Fecha, Anio, Trimestre, Mes, NombreMes, Dia, DiaSemana, EsFinDeSemana)
WITH RECURSIVE calendario (Fecha) AS (
    SELECT DATE('2024-01-01')
    UNION ALL
    SELECT DATE_ADD(Fecha, INTERVAL 1 DAY) FROM calendario WHERE Fecha < DATE('2026-12-31')
)
SELECT YEAR(Fecha) * 10000 + MONTH(Fecha) * 100 + DAY(Fecha),
       Fecha,
       YEAR(Fecha),
       QUARTER(Fecha),
       MONTH(Fecha),
       ELT(MONTH(Fecha), 'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
                         'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'),
       DAY(Fecha),
       ELT(DAYOFWEEK(Fecha), 'Domingo', 'Lunes', 'Martes', 'Miércoles',
                             'Jueves', 'Viernes', 'Sábado'),
       IF(DAYOFWEEK(Fecha) IN (1, 7), 1, 0)
  FROM calendario;