SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE DATABASE IF NOT EXISTS SistemaOpinionesDW
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

ALTER DATABASE SistemaOpinionesDW
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE SistemaOpinionesDW;

-- ---------------------------------------------------------------------
-- Limpieza: vistas
-- ---------------------------------------------------------------------
DROP VIEW IF EXISTS vw_ComparativaCanales;
DROP VIEW IF EXISTS vw_TendenciaTrimestral;
DROP VIEW IF EXISTS vw_TendenciaGeneral;
DROP VIEW IF EXISTS vw_ActividadPorCliente;
DROP VIEW IF EXISTS vw_SatisfaccionProductoMensual;
DROP VIEW IF EXISTS vw_ResumenPorProducto;
DROP VIEW IF EXISTS vw_DistribucionClasificacion;
DROP VIEW IF EXISTS vw_ComentariosPorFuente;
DROP VIEW IF EXISTS vw_ResumenGeneral;
DROP VIEW IF EXISTS vw_OpinionesUnificadas;
DROP VIEW IF EXISTS vw_ResenasTendenciaDiaria;
DROP VIEW IF EXISTS vw_ResenasTendencia;
DROP VIEW IF EXISTS vw_ResenasPorProducto;
DROP VIEW IF EXISTS vw_ResenasResumen;

-- ---------------------------------------------------------------------
-- Limpieza: tablas de hechos antes que dimensiones
-- ---------------------------------------------------------------------
DROP TABLE IF EXISTS FactComentarioSocial;
DROP TABLE IF EXISTS FactResenaWeb;
DROP TABLE IF EXISTS FactEncuesta;

DROP TABLE IF EXISTS DimClasificacion;
DROP TABLE IF EXISTS DimFuente;
DROP TABLE IF EXISTS DimCliente;
DROP TABLE IF EXISTS DimProducto;
DROP TABLE IF EXISTS DimTiempo;