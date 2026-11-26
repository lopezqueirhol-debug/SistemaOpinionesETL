--  En un Data Warehouse nunca debe borrarse una dimension que tiene hechos
--  asociados: se perderia el historial analitico.
-- =====================================================================

SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;
USE SistemaOpinionesDW;


-- ---------------------------------------------------------------------
-- Claves foraneas: FactEncuesta (5)
-- ---------------------------------------------------------------------
ALTER TABLE FactEncuesta
    ADD CONSTRAINT fk_factencuesta_tiempo
        FOREIGN KEY (IdTiempoKey) REFERENCES DimTiempo (IdTiempoKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factencuesta_producto
        FOREIGN KEY (IdProductoKey) REFERENCES DimProducto (IdProductoKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factencuesta_cliente
        FOREIGN KEY (IdClienteKey) REFERENCES DimCliente (IdClienteKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factencuesta_fuente
        FOREIGN KEY (IdFuenteKey) REFERENCES DimFuente (IdFuenteKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factencuesta_clasificacion
        FOREIGN KEY (IdClasificacionKey) REFERENCES DimClasificacion (IdClasificacionKey)
        ON UPDATE CASCADE ON DELETE RESTRICT;

-- ---------------------------------------------------------------------
-- Claves foraneas: FactResenaWeb (5)
-- ---------------------------------------------------------------------
ALTER TABLE FactResenaWeb
    ADD CONSTRAINT fk_factresenaweb_tiempo
        FOREIGN KEY (IdTiempoKey) REFERENCES DimTiempo (IdTiempoKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factresenaweb_producto
        FOREIGN KEY (IdProductoKey) REFERENCES DimProducto (IdProductoKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factresenaweb_cliente
        FOREIGN KEY (IdClienteKey) REFERENCES DimCliente (IdClienteKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factresenaweb_fuente
        FOREIGN KEY (IdFuenteKey) REFERENCES DimFuente (IdFuenteKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factresenaweb_clasificacion
        FOREIGN KEY (IdClasificacionKey) REFERENCES DimClasificacion (IdClasificacionKey)
        ON UPDATE CASCADE ON DELETE RESTRICT;

-- ---------------------------------------------------------------------
-- Claves foraneas: FactComentarioSocial (4, sin clasificacion)
-- ---------------------------------------------------------------------
ALTER TABLE FactComentarioSocial
    ADD CONSTRAINT fk_factcomentariosocial_tiempo
        FOREIGN KEY (IdTiempoKey) REFERENCES DimTiempo (IdTiempoKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factcomentariosocial_producto
        FOREIGN KEY (IdProductoKey) REFERENCES DimProducto (IdProductoKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factcomentariosocial_cliente
        FOREIGN KEY (IdClienteKey) REFERENCES DimCliente (IdClienteKey)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    ADD CONSTRAINT fk_factcomentariosocial_fuente
        FOREIGN KEY (IdFuenteKey) REFERENCES DimFuente (IdFuenteKey)
        ON UPDATE CASCADE ON DELETE RESTRICT;

ALTER TABLE DimTiempo
    ADD CONSTRAINT chk_dimtiempo_trimestre CHECK (Trimestre BETWEEN 1 AND 4),
    ADD CONSTRAINT chk_dimtiempo_mes CHECK (Mes BETWEEN 1 AND 12),
    ADD CONSTRAINT chk_dimtiempo_dia CHECK (Dia BETWEEN 1 AND 31),
    ADD CONSTRAINT chk_dimtiempo_finsemana CHECK (EsFinDeSemana IN (0, 1));

ALTER TABLE FactEncuesta
    ADD CONSTRAINT chk_factencuesta_puntaje CHECK (PuntajeSatisfaccion BETWEEN 1 AND 5),
    ADD CONSTRAINT chk_factencuesta_texto CHECK (CHAR_LENGTH(TRIM(Comentario)) > 0);

ALTER TABLE FactResenaWeb
    ADD CONSTRAINT chk_factresenaweb_rating CHECK (Rating BETWEEN 1 AND 5),
    ADD CONSTRAINT chk_factresenaweb_texto CHECK (CHAR_LENGTH(TRIM(Comentario)) > 0);

ALTER TABLE FactComentarioSocial
    ADD CONSTRAINT chk_factcomentariosocial_texto CHECK (CHAR_LENGTH(TRIM(Comentario)) > 0);

-- Indices de apoyo analitico
CREATE INDEX idx_factencuesta_prod_tiempo ON FactEncuesta (IdProductoKey, IdTiempoKey);
CREATE INDEX idx_factresenaweb_prod_tiempo ON FactResenaWeb (IdProductoKey, IdTiempoKey);
CREATE INDEX idx_factcomentariosocial_prod_tiempo ON FactComentarioSocial (IdProductoKey, IdTiempoKey);

CREATE INDEX idx_dimproducto_categoria ON DimProducto (Categoria);
CREATE INDEX idx_dimtiempo_anio_mes ON DimTiempo (Anio, Mes);