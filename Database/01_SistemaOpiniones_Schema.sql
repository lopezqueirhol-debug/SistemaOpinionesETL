-- =====================================================================
--  SISTEMA DE ANALISIS DE OPINIONES DE CLIENTES
--
--  Contenido:
--    1. Creacion de la base de datos
--    2. Limpieza (script re-ejecutable)
--    3. Tablas maestras
--    4. Tablas transaccionales
--    5. Tablas de control del ETL (LogCarga, RegistroRechazado)
--    6. Claves foraneas, reglas de dominio e indices
--    7. Procedimientos almacenados
--    8. Vistas
-- =====================================================================

-- =====================================================================
-- 1. CREACION DE LA BASE DE DATOS
-- =====================================================================
SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE DATABASE IF NOT EXISTS SistemaOpiniones
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- Si la base ya existia con otra collation
-- Este ALTER la fuerza para que todas las tablas hereden utf8mb4_unicode_ci.
ALTER DATABASE SistemaOpiniones
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE SistemaOpiniones;


-- =====================================================================
-- 2. LIMPIEZA (orden inverso de dependencias)
-- =====================================================================
DROP VIEW IF EXISTS vw_RechazosPorMotivo;
DROP VIEW IF EXISTS vw_ResumenCargas;
DROP VIEW IF EXISTS vw_ComentariosSocialesPorPlataforma;
DROP VIEW IF EXISTS vw_ActividadPorCliente;
DROP VIEW IF EXISTS vw_SatisfaccionPorCategoria;
DROP VIEW IF EXISTS vw_ResumenPorProducto;
DROP VIEW IF EXISTS vw_OpinionesUnificadas;

DROP PROCEDURE IF EXISTS sp_OpinionesPorProducto;
DROP PROCEDURE IF EXISTS sp_ResumenCarga;
DROP PROCEDURE IF EXISTS sp_LimpiarDatos;
DROP PROCEDURE IF EXISTS sp_InsertarResenaWeb;
DROP PROCEDURE IF EXISTS sp_InsertarEncuesta;
DROP PROCEDURE IF EXISTS sp_InsertarComentarioSocial;
DROP PROCEDURE IF EXISTS sp_InsertarFuenteDatos;
DROP PROCEDURE IF EXISTS sp_InsertarProducto;
DROP PROCEDURE IF EXISTS sp_InsertarCliente;
DROP PROCEDURE IF EXISTS sp_RegistrarRechazo;
DROP PROCEDURE IF EXISTS sp_FinalizarLog;
DROP PROCEDURE IF EXISTS sp_IniciarLog;
DROP PROCEDURE IF EXISTS sp_ResolverFuentePorTipo;

DROP TABLE IF EXISTS RegistroRechazado;
DROP TABLE IF EXISTS LogCarga;
DROP TABLE IF EXISTS ResenaWeb;
DROP TABLE IF EXISTS Encuesta;
DROP TABLE IF EXISTS ComentarioSocial;
DROP TABLE IF EXISTS FuenteDatos;
DROP TABLE IF EXISTS Producto;
DROP TABLE IF EXISTS Cliente;

-- =====================================================================
-- 3. TABLAS MAESTRAS
-- =====================================================================

-- Cliente <- clients.csv
CREATE TABLE Cliente (
    IdCliente VARCHAR(10)  NOT NULL,
    Nombre VARCHAR(100) NOT NULL,
    Email VARCHAR(150) NOT NULL,
    PRIMARY KEY (IdCliente)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Producto <- products.csv
CREATE TABLE Producto (
    IdProducto VARCHAR(10)  NOT NULL,
    Nombre VARCHAR(100) NOT NULL,
    Categoria VARCHAR(50) NOT NULL,
    PRIMARY KEY (IdProducto)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- FuenteDatos <- fuente_datos.csv
CREATE TABLE FuenteDatos (
    IdFuente VARCHAR(10) NOT NULL,
    TipoFuente VARCHAR(50) NOT NULL,
    FechaCarga DATE NOT NULL,
    PRIMARY KEY (IdFuente)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =====================================================================
-- 4. TABLAS TRANSACCIONALES
-- =====================================================================

-- ComentarioSocial <- social_comments.csv
-- IdCliente admite NULL (comentarios de usuarios no identificados).
-- IdFuente se resuelve por TipoFuente = 'Red Social'.
-- Plataforma conserva el valor original del CSV (Instagram, Facebook, Twitter).
CREATE TABLE ComentarioSocial (
    IdComment VARCHAR(10) NOT NULL,
    IdCliente VARCHAR(10) NULL,
    IdProducto VARCHAR(10) NOT NULL,
    IdFuente VARCHAR(10) NOT NULL,
    Fecha DATE        NOT NULL,
    Plataforma VARCHAR(50) NOT NULL,
    Comentario TEXT        NOT NULL,
    PRIMARY KEY (IdComment)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Encuesta <- surveys_part1.csv
-- IdFuente se resuelve por TipoFuente = 'CSV'.
CREATE TABLE Encuesta (
    IdOpinion INT NOT NULL,
    IdCliente VARCHAR(10) NOT NULL,
    IdProducto VARCHAR(10) NOT NULL,
    IdFuente VARCHAR(10) NOT NULL,
    Fecha DATE NOT NULL,
    Comentario TEXT NOT NULL,
    Clasificacion ENUM('Positiva','Neutra','Negativa') NOT NULL,
    PuntajeSatisfaccion  INT NOT NULL,
    PRIMARY KEY (IdOpinion)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ResenaWeb <- web_reviews.csv
-- IdFuente se resuelve por TipoFuente = 'Web'.
CREATE TABLE ResenaWeb (
    IdReview VARCHAR(10) NOT NULL,
    IdCliente VARCHAR(10) NOT NULL,
    IdProducto VARCHAR(10) NOT NULL,
    IdFuente VARCHAR(10) NOT NULL,
    Fecha DATE NOT NULL,
    Comentario TEXT NOT NULL,
    Rating TINYINT NOT NULL,
    PRIMARY KEY (IdReview)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =====================================================================
-- 5. TABLAS DE CONTROL DEL ETL
-- =====================================================================

-- Una fila por archivo procesado en cada ejecucion del ETL.
CREATE TABLE LogCarga (
    IdLog BIGINT NOT NULL AUTO_INCREMENT,
    IdEjecucion CHAR(36) NOT NULL,          -- GUID de la corrida
    Archivo VARCHAR(100) NOT NULL,
    TablaDestino VARCHAR(50) NOT NULL,
    IdFuenteAsignada VARCHAR(10) NULL,              -- fuente resuelta por tipo
    FechaInicio DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FechaFin DATETIME NULL,
    Procesados INT NOT NULL DEFAULT 0,
    Insertados INT NOT NULL DEFAULT 0,
    Rechazados INT NOT NULL DEFAULT 0,
    Estado ENUM('EnProceso','Completado','CompletadoConRechazos','Error')
        NOT NULL DEFAULT 'EnProceso',
    Mensaje TEXT NULL,
    PRIMARY KEY (IdLog)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Detalle de cada registro rechazado y su motivo.
CREATE TABLE RegistroRechazado (
    IdRechazo BIGINT NOT NULL AUTO_INCREMENT,
    IdLog BIGINT NOT NULL,
    Archivo VARCHAR(100) NOT NULL,
    NumeroFila INT NOT NULL, -- fila en el CSV (sin contar encabezado)
    Motivo ENUM('CAMPO_OBLIGATORIO','FORMATO','TIPO_DATO','DUPLICADO',
               'INTEGRIDAD_REFERENCIAL','REGLA_NEGOCIO','ERROR_BD') NOT NULL,
    Detalle VARCHAR(500) NULL,
    DatosOriginales TEXT NULL,
    FechaRegistro DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (IdRechazo)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- =====================================================================
-- 6. CLAVES FORANEAS, REGLAS DE DOMINIO E INDICES
-- =====================================================================

-- ---------- Claves foraneas: ComentarioSocial ----------
ALTER TABLE ComentarioSocial
    ADD CONSTRAINT fk_comentariosocial_cliente
        FOREIGN KEY (IdCliente) REFERENCES Cliente(IdCliente)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_comentariosocial_producto
        FOREIGN KEY (IdProducto) REFERENCES Producto(IdProducto)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_comentariosocial_fuente
        FOREIGN KEY (IdFuente) REFERENCES FuenteDatos(IdFuente)
        ON UPDATE CASCADE ON DELETE RESTRICT;

-- ---------- Claves foraneas: Encuesta ----------
ALTER TABLE Encuesta
    ADD CONSTRAINT fk_encuesta_cliente
        FOREIGN KEY (IdCliente) REFERENCES Cliente(IdCliente)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_encuesta_producto
        FOREIGN KEY (IdProducto) REFERENCES Producto(IdProducto)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_encuesta_fuente
        FOREIGN KEY (IdFuente) REFERENCES FuenteDatos(IdFuente)
        ON UPDATE CASCADE ON DELETE RESTRICT;

-- ---------- Claves foraneas: ResenaWeb ----------
ALTER TABLE ResenaWeb
    ADD CONSTRAINT fk_resenaweb_cliente
        FOREIGN KEY (IdCliente)  REFERENCES Cliente(IdCliente)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_resenaweb_producto
        FOREIGN KEY (IdProducto) REFERENCES Producto(IdProducto)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_resenaweb_fuente
        FOREIGN KEY (IdFuente)   REFERENCES FuenteDatos(IdFuente)
        ON UPDATE CASCADE ON DELETE RESTRICT;

-- ---------- Claves foraneas: tablas de control ----------
-- Si se elimina una fuente, el historial de cargas se conserva (SET NULL).
ALTER TABLE LogCarga
    ADD CONSTRAINT fk_logcarga_fuente
        FOREIGN KEY (IdFuenteAsignada) REFERENCES FuenteDatos(IdFuente)
        ON UPDATE CASCADE ON DELETE SET NULL;

ALTER TABLE RegistroRechazado
    ADD CONSTRAINT fk_registrorechazado_log
        FOREIGN KEY (IdLog) REFERENCES LogCarga(IdLog)
        ON UPDATE CASCADE ON DELETE CASCADE;

-- ---------- Unicidad ----------
ALTER TABLE Cliente
    ADD CONSTRAINT uq_cliente_email UNIQUE (Email);

-- ---------- Reglas de dominio (CHECK) ----------
ALTER TABLE Cliente
    ADD CONSTRAINT chk_cliente_nombre CHECK (CHAR_LENGTH(TRIM(Nombre)) > 0),
    ADD CONSTRAINT chk_cliente_email  CHECK (Email LIKE '_%@_%._%' AND Email NOT LIKE '% %');

ALTER TABLE Producto
    ADD CONSTRAINT chk_producto_nombre CHECK (CHAR_LENGTH(TRIM(Nombre)) > 0),
    ADD CONSTRAINT chk_producto_categoria CHECK (CHAR_LENGTH(TRIM(Categoria)) > 0);

ALTER TABLE FuenteDatos
    ADD CONSTRAINT chk_fuente_tipo CHECK (CHAR_LENGTH(TRIM(TipoFuente)) > 0);

ALTER TABLE ComentarioSocial
    ADD CONSTRAINT chk_comentariosocial_texto CHECK (CHAR_LENGTH(TRIM(Comentario)) > 0),
    ADD CONSTRAINT chk_comentariosocial_plat  CHECK (CHAR_LENGTH(TRIM(Plataforma)) > 0);

ALTER TABLE Encuesta
    ADD CONSTRAINT chk_encuesta_texto CHECK (CHAR_LENGTH(TRIM(Comentario)) > 0),
    ADD CONSTRAINT chk_encuesta_puntaje CHECK (PuntajeSatisfaccion BETWEEN 1 AND 5),
    -- Consistencia: 1-2 Negativa | 3 Neutra | 4-5 Positiva
    ADD CONSTRAINT chk_encuesta_coherencia CHECK (
           (Clasificacion = 'Negativa' AND PuntajeSatisfaccion <= 2)
        OR (Clasificacion = 'Neutra'   AND PuntajeSatisfaccion = 3)
        OR (Clasificacion = 'Positiva' AND PuntajeSatisfaccion >= 4));

ALTER TABLE ResenaWeb
    ADD CONSTRAINT chk_resenaweb_texto  CHECK (CHAR_LENGTH(TRIM(Comentario)) > 0),
    ADD CONSTRAINT chk_resenaweb_rating CHECK (Rating BETWEEN 1 AND 5);

ALTER TABLE LogCarga
    ADD CONSTRAINT chk_logcarga_conteos CHECK (
           Procesados >= 0 AND Insertados >= 0 AND Rechazados >= 0
       AND Insertados + Rechazados <= Procesados);

-- ---------- Indices de apoyo (las FK ya generan los suyos) ----------
CREATE INDEX idx_fuente_tipo ON FuenteDatos (TipoFuente, IdFuente);
CREATE INDEX idx_producto_categoria ON Producto (Categoria);
CREATE INDEX idx_comentario_fecha ON ComentarioSocial (Fecha);
CREATE INDEX idx_encuesta_fecha ON Encuesta (Fecha);
CREATE INDEX idx_resena_fecha ON ResenaWeb (Fecha);
CREATE INDEX idx_log_ejecucion ON LogCarga (IdEjecucion);

-- =====================================================================
-- 7. PROCEDIMIENTOS ALMACENADOS
-- =====================================================================
-- Convencion de los procedimientos sp_Insertar*:
--   p_Resultado: 'OK' | 'DUPLICADO' | 'INTEGRIDAD_REFERENCIAL' | 'REGLA_NEGOCIO' | 'ERROR_BD'
--   (coinciden con los valores del ENUM RegistroRechazado.Motivo)
--   p_Detalle  : descripcion del rechazo (NULL si OK)
-- =====================================================================

DELIMITER $$

-- ---------------------------------------------------------------------
-- Resuelve el IdFuente a usar segun el TipoFuente: toma el menor IdFuente
-- de ese tipo (determinista). Si no existe ninguno, lanza una excepcion.
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_ResolverFuentePorTipo(
    IN  p_TipoFuente VARCHAR(50),
    OUT p_IdFuente VARCHAR(10)
)
BEGIN
    SET p_IdFuente = NULL;

    SELECT IdFuente INTO p_IdFuente
      FROM FuenteDatos
     WHERE TipoFuente = p_TipoFuente
     ORDER BY IdFuente
     LIMIT 1;

    IF p_IdFuente IS NULL THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'No existe ninguna fuente en FuenteDatos para el tipo solicitado';
    END IF;
END$$

-- ---------------------------------------------------------------------
-- Control de carga: inicio y cierre de un archivo
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_IniciarLog(
    IN  p_IdEjecucion CHAR(36),
    IN  p_Archivo VARCHAR(100),
    IN  p_TablaDestino VARCHAR(50),
    IN  p_IdFuenteAsignada VARCHAR(10),
    OUT p_IdLog BIGINT
)
BEGIN
    INSERT INTO LogCarga (IdEjecucion, Archivo, TablaDestino, IdFuenteAsignada)
    VALUES (p_IdEjecucion, p_Archivo, p_TablaDestino, p_IdFuenteAsignada);

    SET p_IdLog = LAST_INSERT_ID();
END$$

CREATE PROCEDURE sp_FinalizarLog(
    IN p_IdLog BIGINT,
    IN p_Procesados INT,
    IN p_Insertados INT,
    IN p_Rechazados INT,
    IN p_Estado VARCHAR(25),
    IN p_Mensaje TEXT
)
BEGIN
    UPDATE LogCarga
       SET FechaFin = CURRENT_TIMESTAMP,
           Procesados = p_Procesados,
           Insertados = p_Insertados,
           Rechazados = p_Rechazados,
           Estado = p_Estado,
           Mensaje = p_Mensaje
     WHERE IdLog = p_IdLog;
END$$

CREATE PROCEDURE sp_RegistrarRechazo(
    IN p_IdLog BIGINT,
    IN p_Archivo VARCHAR(100),
    IN p_NumeroFila INT,
    IN p_Motivo VARCHAR(30),
    IN p_Detalle VARCHAR(500),
    IN p_DatosOriginales TEXT
)
BEGIN
    INSERT INTO RegistroRechazado (IdLog, Archivo, NumeroFila, Motivo, Detalle, DatosOriginales)
    VALUES (p_IdLog, p_Archivo, p_NumeroFila, p_Motivo, p_Detalle, p_DatosOriginales);
END$$

-- ---------------------------------------------------------------------
-- INSERTAR CLIENTE
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_InsertarCliente(
    IN p_IdCliente VARCHAR(10),
    IN p_Nombre VARCHAR(100),
    IN p_Email VARCHAR(150),
    OUT p_Resultado VARCHAR(30),
    OUT p_Detalle VARCHAR(255)
)
BEGIN
    DECLARE v_msg TEXT DEFAULT NULL;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        SET p_Resultado = 'ERROR_BD';
        SET p_Detalle = LEFT(v_msg, 255);
    END;

    SET p_Resultado = 'OK';
    SET p_Detalle = NULL;

    IF EXISTS (SELECT 1 FROM Cliente WHERE IdCliente = p_IdCliente) THEN
        SET p_Resultado = 'DUPLICADO';
        SET p_Detalle = CONCAT('IdCliente ya existe: ', p_IdCliente);
    ELSEIF EXISTS (SELECT 1 FROM Cliente WHERE Email = p_Email) THEN
        SET p_Resultado = 'DUPLICADO';
        SET p_Detalle = CONCAT('Email ya registrado: ', p_Email);
    ELSE
        INSERT INTO Cliente (IdCliente, Nombre, Email)
        VALUES (p_IdCliente, p_Nombre, p_Email);
    END IF;
END$$

-- ---------------------------------------------------------------------
-- INSERTAR PRODUCTO
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_InsertarProducto(
    IN  p_IdProducto VARCHAR(10),
    IN  p_Nombre VARCHAR(100),
    IN  p_Categoria VARCHAR(50),
    OUT p_Resultado VARCHAR(30),
    OUT p_Detalle VARCHAR(255)
)
BEGIN
    DECLARE v_msg TEXT DEFAULT NULL;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        SET p_Resultado = 'ERROR_BD';
        SET p_Detalle = LEFT(v_msg, 255);
    END;

    SET p_Resultado = 'OK';
    SET p_Detalle = NULL;

    IF EXISTS (SELECT 1 FROM Producto WHERE IdProducto = p_IdProducto) THEN
        SET p_Resultado = 'DUPLICADO';
        SET p_Detalle = CONCAT('IdProducto ya existe: ', p_IdProducto);
    ELSE
        INSERT INTO Producto (IdProducto, Nombre, Categoria)
        VALUES (p_IdProducto, p_Nombre, p_Categoria);
    END IF;
END$$

-- ---------------------------------------------------------------------
-- INSERTAR FUENTE DE DATOS
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_InsertarFuenteDatos(
    IN  p_IdFuente VARCHAR(10),
    IN  p_TipoFuente VARCHAR(50),
    IN  p_FechaCarga DATE,
    OUT p_Resultado VARCHAR(30),
    OUT p_Detalle VARCHAR(255)
)
BEGIN
    DECLARE v_msg TEXT DEFAULT NULL;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        SET p_Resultado = 'ERROR_BD';
        SET p_Detalle = LEFT(v_msg, 255);
    END;

    SET p_Resultado = 'OK';
    SET p_Detalle = NULL;

    IF EXISTS (SELECT 1 FROM FuenteDatos WHERE IdFuente = p_IdFuente) THEN
        SET p_Resultado = 'DUPLICADO';
        SET p_Detalle = CONCAT('IdFuente ya existe: ', p_IdFuente);
    ELSE
        INSERT INTO FuenteDatos (IdFuente, TipoFuente, FechaCarga)
        VALUES (p_IdFuente, p_TipoFuente, p_FechaCarga);
    END IF;
END$$

-- ---------------------------------------------------------------------
-- INSERTAR COMENTARIO SOCIAL (IdCliente puede ser NULL)
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_InsertarComentarioSocial(
    IN  p_IdComment VARCHAR(10),
    IN  p_IdCliente VARCHAR(10),
    IN  p_IdProducto VARCHAR(10),
    IN  p_IdFuente VARCHAR(10),
    IN  p_Fecha DATE,
    IN  p_Plataforma VARCHAR(50),
    IN  p_Comentario TEXT,
    OUT p_Resultado VARCHAR(30),
    OUT p_Detalle VARCHAR(255)
)
BEGIN
    DECLARE v_msg TEXT DEFAULT NULL;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        SET p_Resultado = 'ERROR_BD';
        SET p_Detalle = LEFT(v_msg, 255);
    END;

    SET p_Resultado = 'OK';
    SET p_Detalle = NULL;

    IF EXISTS (SELECT 1 FROM ComentarioSocial WHERE IdComment = p_IdComment) THEN
        SET p_Resultado = 'DUPLICADO';
        SET p_Detalle = CONCAT('IdComment ya existe: ', p_IdComment);
    ELSEIF p_IdCliente IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM Cliente WHERE IdCliente = p_IdCliente) THEN
        SET p_Resultado = 'INTEGRIDAD_REFERENCIAL';
        SET p_Detalle = CONCAT('Cliente inexistente: ', p_IdCliente);
    ELSEIF NOT EXISTS (SELECT 1 FROM Producto WHERE IdProducto = p_IdProducto) THEN
        SET p_Resultado = 'INTEGRIDAD_REFERENCIAL';
        SET p_Detalle = CONCAT('Producto inexistente: ', p_IdProducto);
    ELSEIF NOT EXISTS (SELECT 1 FROM FuenteDatos WHERE IdFuente = p_IdFuente) THEN
        SET p_Resultado = 'INTEGRIDAD_REFERENCIAL';
        SET p_Detalle = CONCAT('Fuente inexistente: ', p_IdFuente);
    ELSE
        INSERT INTO ComentarioSocial
               (IdComment, IdCliente, IdProducto, IdFuente, Fecha, Plataforma, Comentario)
        VALUES (p_IdComment, p_IdCliente, p_IdProducto, p_IdFuente, p_Fecha, p_Plataforma, p_Comentario);
    END IF;
END$$

-- ---------------------------------------------------------------------
-- INSERTAR ENCUESTA
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_InsertarEncuesta(
    IN  p_IdOpinion INT,
    IN  p_IdCliente VARCHAR(10),
    IN  p_IdProducto VARCHAR(10),
    IN  p_IdFuente VARCHAR(10),
    IN  p_Fecha DATE,
    IN  p_Comentario TEXT,
    IN  p_Clasificacion VARCHAR(10),
    IN  p_Puntaje INT,
    OUT p_Resultado VARCHAR(30),
    OUT p_Detalle VARCHAR(255)
)
BEGIN
    DECLARE v_msg TEXT DEFAULT NULL;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        SET p_Resultado = 'ERROR_BD';
        SET p_Detalle = LEFT(v_msg, 255);
    END;

    SET p_Resultado = 'OK';
    SET p_Detalle = NULL;

    IF EXISTS (SELECT 1 FROM Encuesta WHERE IdOpinion = p_IdOpinion) THEN
        SET p_Resultado = 'DUPLICADO';
        SET p_Detalle = CONCAT('IdOpinion ya existe: ', p_IdOpinion);
    ELSEIF p_Puntaje NOT BETWEEN 1 AND 5 THEN
        SET p_Resultado = 'REGLA_NEGOCIO';
        SET p_Detalle = CONCAT('Puntaje fuera de rango 1-5: ', p_Puntaje);
    ELSEIF NOT ((p_Clasificacion = 'Negativa' AND p_Puntaje <= 2)
             OR (p_Clasificacion = 'Neutra' AND p_Puntaje = 3)
             OR (p_Clasificacion = 'Positiva' AND p_Puntaje >= 4)) THEN
        SET p_Resultado = 'REGLA_NEGOCIO';
        SET p_Detalle = CONCAT('Clasificacion ', p_Clasificacion, ' incoherente con puntaje ', p_Puntaje);
    ELSEIF NOT EXISTS (SELECT 1 FROM Cliente WHERE IdCliente = p_IdCliente) THEN
        SET p_Resultado = 'INTEGRIDAD_REFERENCIAL';
        SET p_Detalle = CONCAT('Cliente inexistente: ', p_IdCliente);
    ELSEIF NOT EXISTS (SELECT 1 FROM Producto WHERE IdProducto = p_IdProducto) THEN
        SET p_Resultado = 'INTEGRIDAD_REFERENCIAL';
        SET p_Detalle = CONCAT('Producto inexistente: ', p_IdProducto);
    ELSEIF NOT EXISTS (SELECT 1 FROM FuenteDatos WHERE IdFuente = p_IdFuente) THEN
        SET p_Resultado = 'INTEGRIDAD_REFERENCIAL';
        SET p_Detalle = CONCAT('Fuente inexistente: ', p_IdFuente);
    ELSE
        INSERT INTO Encuesta
               (IdOpinion, IdCliente, IdProducto, IdFuente, Fecha, Comentario,
                Clasificacion, PuntajeSatisfaccion)
        VALUES (p_IdOpinion, p_IdCliente, p_IdProducto, p_IdFuente, p_Fecha, p_Comentario,
                p_Clasificacion, p_Puntaje);
    END IF;
END$$

-- ---------------------------------------------------------------------
-- INSERTAR RESEÑA WEB
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_InsertarResenaWeb(
    IN  p_IdReview VARCHAR(10),
    IN  p_IdCliente VARCHAR(10),
    IN  p_IdProducto VARCHAR(10),
    IN  p_IdFuente VARCHAR(10),
    IN  p_Fecha DATE,
    IN  p_Comentario TEXT,
    IN  p_Rating INT,
    OUT p_Resultado VARCHAR(30),
    OUT p_Detalle VARCHAR(255)
)
BEGIN
    DECLARE v_msg TEXT DEFAULT NULL;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        GET DIAGNOSTICS CONDITION 1 v_msg = MESSAGE_TEXT;
        SET p_Resultado = 'ERROR_BD';
        SET p_Detalle = LEFT(v_msg, 255);
    END;

    SET p_Resultado = 'OK';
    SET p_Detalle = NULL;

    IF EXISTS (SELECT 1 FROM ResenaWeb WHERE IdReview = p_IdReview) THEN
        SET p_Resultado = 'DUPLICADO';
        SET p_Detalle = CONCAT('IdReview ya existe: ', p_IdReview);
    ELSEIF p_Rating NOT BETWEEN 1 AND 5 THEN
        SET p_Resultado = 'REGLA_NEGOCIO';
        SET p_Detalle = CONCAT('Rating fuera de rango 1-5: ', p_Rating);
    ELSEIF NOT EXISTS (SELECT 1 FROM Cliente WHERE IdCliente = p_IdCliente) THEN
        SET p_Resultado = 'INTEGRIDAD_REFERENCIAL';
        SET p_Detalle = CONCAT('Cliente inexistente: ', p_IdCliente);
    ELSEIF NOT EXISTS (SELECT 1 FROM Producto WHERE IdProducto = p_IdProducto) THEN
        SET p_Resultado = 'INTEGRIDAD_REFERENCIAL';
        SET p_Detalle = CONCAT('Producto inexistente: ', p_IdProducto);
    ELSEIF NOT EXISTS (SELECT 1 FROM FuenteDatos WHERE IdFuente = p_IdFuente) THEN
        SET p_Resultado = 'INTEGRIDAD_REFERENCIAL';
        SET p_Detalle = CONCAT('Fuente inexistente: ', p_IdFuente);
    ELSE
        INSERT INTO ResenaWeb
               (IdReview, IdCliente, IdProducto, IdFuente, Fecha, Comentario, Rating)
        VALUES (p_IdReview, p_IdCliente, p_IdProducto, p_IdFuente, p_Fecha, p_Comentario, p_Rating);
    END IF;
END$$

-- ---------------------------------------------------------------------
-- LIMPIAR DATOS (para re-ejecutar el ETL desde cero)
-- p_BorrarLogs = tambien vacia LogCarga y RegistroRechazado.
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_LimpiarDatos(IN p_BorrarLogs TINYINT)
BEGIN
    DECLARE v_safe INT DEFAULT @@SQL_SAFE_UPDATES;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        SET SQL_SAFE_UPDATES = v_safe;
        RESIGNAL;
    END;

    SET SQL_SAFE_UPDATES = 0;
    START TRANSACTION;

    DELETE FROM ResenaWeb;
    DELETE FROM Encuesta;
    DELETE FROM ComentarioSocial;
    DELETE FROM FuenteDatos;
    DELETE FROM Producto;
    DELETE FROM Cliente;

    IF p_BorrarLogs = 1 THEN
        DELETE FROM RegistroRechazado;
        DELETE FROM LogCarga;
    END IF;

    COMMIT;
    SET SQL_SAFE_UPDATES = v_safe;
END$$

-- ---------------------------------------------------------------------
-- RESUMEN DE UNA EJECUCION (NULL = la ultima)
-- Devuelve: detalle por archivo y totales.
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_ResumenCarga(IN p_IdEjecucion CHAR(36))
BEGIN
    DECLARE v_id CHAR(36);

    SET v_id = p_IdEjecucion;
    IF v_id IS NULL THEN
        SELECT IdEjecucion INTO v_id FROM LogCarga ORDER BY IdLog DESC LIMIT 1;
    END IF;

    SELECT Archivo, TablaDestino, IdFuenteAsignada,
           Procesados, Insertados, Rechazados, Estado,
           TIMESTAMPDIFF(SECOND, FechaInicio, FechaFin) AS DuracionSeg
      FROM LogCarga
     WHERE IdEjecucion = v_id
     ORDER BY IdLog;

    SELECT v_id                       AS IdEjecucion,
           COALESCE(SUM(Procesados),0) AS TotalProcesados,
           COALESCE(SUM(Insertados),0) AS TotalInsertados,
           COALESCE(SUM(Rechazados),0) AS TotalRechazados
      FROM LogCarga
     WHERE IdEjecucion = v_id;
END$$

-- ---------------------------------------------------------------------
-- TODAS LAS OPINIONES DE UN PRODUCTO (encuestas + resenas + sociales)
-- ---------------------------------------------------------------------
CREATE PROCEDURE sp_OpinionesPorProducto(IN p_IdProducto VARCHAR(10))
BEGIN
    SELECT *
      FROM vw_OpinionesUnificadas
     WHERE IdProducto = p_IdProducto
     ORDER BY Fecha DESC;
END$$

DELIMITER ;


-- =====================================================================
-- 8. VISTAS
-- =====================================================================

-- Une las tres fuentes de opinion en un formato comun.
--  * Encuesta.
--  * ResenaWeb.
--  * ComentarioSocial.
--
-- NOTA: todas las columnas de texto llevan COLLATE utf8mb4_unicode_ci explicito.
CREATE VIEW vw_OpinionesUnificadas AS
SELECT 'Encuesta' COLLATE utf8mb4_unicode_ci AS Origen,
       CAST(e.IdOpinion AS CHAR(10) CHARACTER SET utf8mb4)
            COLLATE utf8mb4_unicode_ci AS IdRegistro,
       e.IdCliente, e.IdProducto, e.IdFuente, e.Fecha, e.Comentario,
       'Encuesta' COLLATE utf8mb4_unicode_ci AS Plataforma,
       CAST(e.Clasificacion AS CHAR(10) CHARACTER SET utf8mb4)
            COLLATE utf8mb4_unicode_ci AS Clasificacion,
       e.PuntajeSatisfaccion AS Puntaje
  FROM Encuesta e
UNION ALL
SELECT 'ResenaWeb' COLLATE utf8mb4_unicode_ci,
       r.IdReview,
       r.IdCliente, r.IdProducto, r.IdFuente, r.Fecha, r.Comentario,
       'Web' COLLATE utf8mb4_unicode_ci,
       (CASE WHEN r.Rating >= 4 THEN 'Positiva'
             WHEN r.Rating  = 3 THEN 'Neutra'
             ELSE 'Negativa' END) COLLATE utf8mb4_unicode_ci,
       r.Rating
  FROM ResenaWeb r
UNION ALL
SELECT 'ComentarioSocial' COLLATE utf8mb4_unicode_ci,
       c.IdComment,
       c.IdCliente, c.IdProducto, c.IdFuente, c.Fecha, c.Comentario,
       c.Plataforma,
       CAST(NULL AS CHAR(10) CHARACTER SET utf8mb4) COLLATE utf8mb4_unicode_ci,
       NULL
  FROM ComentarioSocial c;

-- Resumen de opiniones por producto.
CREATE VIEW vw_ResumenPorProducto AS
SELECT p.IdProducto,
       p.Nombre AS Producto,
       p.Categoria,
       COUNT(o.IdRegistro) AS TotalOpiniones,
       COALESCE(SUM(o.Clasificacion = 'Positiva'), 0) AS Positivas,
       COALESCE(SUM(o.Clasificacion = 'Neutra'), 0) AS Neutras,
       COALESCE(SUM(o.Clasificacion = 'Negativa'), 0) AS Negativas,
       ROUND(AVG(o.Puntaje), 2) AS PuntajePromedio
  FROM Producto p
  LEFT JOIN vw_OpinionesUnificadas o ON o.IdProducto = p.IdProducto
 GROUP BY p.IdProducto, p.Nombre, p.Categoria;

-- Satisfaccion promedio por categoria de producto.
CREATE VIEW vw_SatisfaccionPorCategoria AS
SELECT p.Categoria,
       COUNT(o.IdRegistro) AS TotalOpiniones,
       ROUND(AVG(o.Puntaje), 2) AS PuntajePromedio,
       COALESCE(SUM(o.Clasificacion = 'Positiva'), 0) AS Positivas,
       COALESCE(SUM(o.Clasificacion = 'Negativa'), 0) AS Negativas
  FROM Producto p
  JOIN vw_OpinionesUnificadas o ON o.IdProducto = p.IdProducto
 GROUP BY p.Categoria;

-- Actividad de cada cliente por tipo de opinion.
CREATE VIEW vw_ActividadPorCliente AS
SELECT c.IdCliente,
       c.Nombre,
       c.Email,
       COALESCE(SUM(o.Origen = 'Encuesta'), 0) AS Encuestas,
       COALESCE(SUM(o.Origen = 'ResenaWeb'), 0) AS Resenas,
       COALESCE(SUM(o.Origen = 'ComentarioSocial'), 0) AS ComentariosSociales,
       COUNT(o.IdRegistro) AS TotalOpiniones
  FROM Cliente c
  LEFT JOIN vw_OpinionesUnificadas o ON o.IdCliente = c.IdCliente
 GROUP BY c.IdCliente, c.Nombre, c.Email;

-- Comentarios sociales por plataforma (identificados vs anonimos).
CREATE VIEW vw_ComentariosSocialesPorPlataforma AS
SELECT Plataforma,
       COUNT(*) AS TotalComentarios,
       SUM(IdCliente IS NOT NULL) AS ConCliente,
       SUM(IdCliente IS NULL) AS Anonimos,
       MIN(Fecha) AS PrimerComentario,
       MAX(Fecha) AS UltimoComentario
  FROM ComentarioSocial
 GROUP BY Plataforma;

-- Historial de cargas del ETL.
CREATE VIEW vw_ResumenCargas AS
SELECT l.IdLog,
       l.IdEjecucion,
       l.Archivo,
       l.TablaDestino,
       l.IdFuenteAsignada,
       f.TipoFuente AS TipoFuenteAsignada,
       l.Procesados, l.Insertados, l.Rechazados,
       l.Estado,
       l.FechaInicio, l.FechaFin,
       TIMESTAMPDIFF(SECOND, l.FechaInicio, l.FechaFin) AS DuracionSeg
  FROM LogCarga l
  LEFT JOIN FuenteDatos f ON f.IdFuente = l.IdFuenteAsignada;

-- Rechazos agrupados por archivo y motivo.
CREATE VIEW vw_RechazosPorMotivo AS
SELECT Archivo, Motivo, COUNT(*) AS Cantidad
  FROM RegistroRechazado
 GROUP BY Archivo, Motivo;

-- =====================================================================
-- FIN DEL SCRIPT
-- =====================================================================