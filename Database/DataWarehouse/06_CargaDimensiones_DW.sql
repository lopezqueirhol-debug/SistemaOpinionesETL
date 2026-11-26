-- =====================================================================
--  DATA WAREHOUSE - SISTEMA DE ANALISIS DE OPINIONES DE CLIENTES
--  Script 6: CARGA DE DIMENSIONES (upsert por clave natural)
--
--  Reglas:
--    * La clave natural (IdClienteNK / IdProductoNK) identifica la fila.
--    * Si la clave no existe, se INSERTA y recibe una clave sustituta nueva.
--    * Si la clave existe y algun atributo cambio, se ACTUALIZA en el mismo lugar.
--      La clave sustituta NUNCA cambia, porque los hechos la referencian.
--    * Si la clave existe y nada cambio, no se toca la fila.
--    * Un lote con claves repetidas o campos vacios se rechaza completo.
--    * Todo el lote va en una transaccion: un fallo no deja cargas a medias.
-- =====================================================================

SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;
USE SistemaOpinionesDW;

DELIMITER $$

-- ---------------------------------------------------------------------
-- DIMENSION CLIENTE
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS sp_CargarDimCliente$$
CREATE PROCEDURE sp_CargarDimCliente(
    IN  p_Lote JSON,
    OUT p_Nuevos INT,
    OUT p_Modificados INT
)
BEGIN
    DECLARE v_Total INT DEFAULT 0;
    DECLARE v_Vacios INT DEFAULT 0;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        DROP TEMPORARY TABLE IF EXISTS tmp_lote_cliente;
        RESIGNAL;
    END;

    IF p_Lote IS NULL OR JSON_TYPE(p_Lote) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'sp_CargarDimCliente: el lote debe ser un arreglo JSON';
    END IF;

    DROP TEMPORARY TABLE IF EXISTS tmp_lote_cliente;
    CREATE TEMPORARY TABLE tmp_lote_cliente (
        IdClienteNK VARCHAR(10)  NOT NULL,
        Nombre VARCHAR(100) NOT NULL,
        Email VARCHAR(150) NOT NULL,
        PRIMARY KEY (IdClienteNK) -- rechaza claves repetidas dentro del lote
    ) ENGINE=InnoDB;

    -- Descomponer el JSON en filas
    INSERT INTO tmp_lote_cliente (IdClienteNK, Nombre, Email)
    SELECT j.IdClienteNK, j.Nombre, j.Email
      FROM JSON_TABLE(
           p_Lote, '$[*]'
           COLUMNS (
               IdClienteNK VARCHAR(10) PATH '$.IdClienteNK',
               Nombre VARCHAR(100) PATH '$.Nombre',
               Email VARCHAR(150) PATH '$.Email'
           )
      ) AS j;

    -- Validar campos obligatorios (un JSON con null o vacio no debe llegar a la dimension)
    SELECT COUNT(*) INTO v_Vacios
      FROM tmp_lote_cliente
     WHERE IdClienteNK IS NULL OR TRIM(IdClienteNK) = ''
        OR TRIM(Nombre) = '' OR TRIM(Email) = '';

    IF v_Vacios > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'sp_CargarDimCliente: hay filas con IdClienteNK, Nombre o Email vacios';
    END IF;

    -- Contar antes de escribir
    SELECT COUNT(*) INTO v_Total FROM tmp_lote_cliente;

    SELECT COUNT(*) INTO p_Nuevos
      FROM tmp_lote_cliente t
      LEFT JOIN DimCliente d ON d.IdClienteNK = t.IdClienteNK
     WHERE d.IdClienteKey IS NULL;

    SELECT COUNT(*) INTO p_Modificados
      FROM tmp_lote_cliente t
      JOIN DimCliente d ON d.IdClienteNK = t.IdClienteNK
     WHERE d.Nombre <> t.Nombre OR d.Email <> t.Email;

    START TRANSACTION;

    -- 1) Actualizar las existentes que cambiaron
    UPDATE DimCliente d
      JOIN tmp_lote_cliente t ON t.IdClienteNK = d.IdClienteNK
       SET d.Nombre = t.Nombre,
           d.Email  = t.Email
     WHERE d.Nombre <> t.Nombre OR d.Email <> t.Email;

    -- 2) Insertar las que no existen
    INSERT INTO DimCliente (IdClienteNK, Nombre, Email)
    SELECT t.IdClienteNK, t.Nombre, t.Email
      FROM tmp_lote_cliente t
      LEFT JOIN DimCliente d ON d.IdClienteNK = t.IdClienteNK
     WHERE d.IdClienteKey IS NULL;

    COMMIT;
    DROP TEMPORARY TABLE IF EXISTS tmp_lote_cliente;
END$$


-- ---------------------------------------------------------------------
-- DIMENSION PRODUCTO
-- ---------------------------------------------------------------------
DROP PROCEDURE IF EXISTS sp_CargarDimProducto$$
CREATE PROCEDURE sp_CargarDimProducto(
    IN  p_Lote JSON,
    OUT p_Nuevos INT,
    OUT p_Modificados INT
)
BEGIN
    DECLARE v_Vacios INT DEFAULT 0;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        DROP TEMPORARY TABLE IF EXISTS tmp_lote_producto;
        RESIGNAL;
    END;

    IF p_Lote IS NULL OR JSON_TYPE(p_Lote) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'sp_CargarDimProducto: el lote debe ser un arreglo JSON';
    END IF;

    DROP TEMPORARY TABLE IF EXISTS tmp_lote_producto;
    CREATE TEMPORARY TABLE tmp_lote_producto (
        IdProductoNK VARCHAR(10)  NOT NULL,
        Nombre VARCHAR(100) NOT NULL,
        Categoria VARCHAR(50)  NOT NULL,
        PRIMARY KEY (IdProductoNK)
    ) ENGINE=InnoDB;

    INSERT INTO tmp_lote_producto (IdProductoNK, Nombre, Categoria)
    SELECT j.IdProductoNK, j.Nombre, j.Categoria
      FROM JSON_TABLE(
           p_Lote, '$[*]'
           COLUMNS (
               IdProductoNK VARCHAR(10) PATH '$.IdProductoNK',
               Nombre VARCHAR(100) PATH '$.Nombre',
               Categoria VARCHAR(50) PATH '$.Categoria'
           )
      ) AS j;

    SELECT COUNT(*) INTO v_Vacios
      FROM tmp_lote_producto
     WHERE IdProductoNK IS NULL OR TRIM(IdProductoNK) = ''
        OR TRIM(Nombre) = '' OR TRIM(Categoria) = '';

    IF v_Vacios > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'sp_CargarDimProducto: hay filas con IdProductoNK, Nombre o Categoria vacios';
    END IF;

    SELECT COUNT(*) INTO p_Nuevos
      FROM tmp_lote_producto t
      LEFT JOIN DimProducto d ON d.IdProductoNK = t.IdProductoNK
     WHERE d.IdProductoKey IS NULL;

    SELECT COUNT(*) INTO p_Modificados
      FROM tmp_lote_producto t
      JOIN DimProducto d ON d.IdProductoNK = t.IdProductoNK
     WHERE d.Nombre <> t.Nombre OR d.Categoria <> t.Categoria;

    START TRANSACTION;

    UPDATE DimProducto d
      JOIN tmp_lote_producto t ON t.IdProductoNK = d.IdProductoNK
       SET d.Nombre    = t.Nombre,
           d.Categoria = t.Categoria
     WHERE d.Nombre <> t.Nombre OR d.Categoria <> t.Categoria;

    INSERT INTO DimProducto (IdProductoNK, Nombre, Categoria)
    SELECT t.IdProductoNK, t.Nombre, t.Categoria
      FROM tmp_lote_producto t
      LEFT JOIN DimProducto d ON d.IdProductoNK = t.IdProductoNK
     WHERE d.IdProductoKey IS NULL;

    COMMIT;
    DROP TEMPORARY TABLE IF EXISTS tmp_lote_producto;
END$$

DELIMITER ;
