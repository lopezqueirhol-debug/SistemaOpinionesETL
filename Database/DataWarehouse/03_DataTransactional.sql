-- Modelo de constelacion de hechos: una tabla de hechos por fuente de opinion
--    Fuente            Puntaje   Clasificacion   Cliente
--    Encuesta          1-5       explicita       obligatorio
--    ResenaWeb         Rating    derivada        obligatorio
--    ComentarioSocial  no tiene  no tiene        opcional (anonimos)
-- =====================================================================

SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci;
USE SistemaOpinionesDW;


-- ---------------------------------------------------------------------
-- FactEncuesta  <-  surveys_part1.csv
-- ---------------------------------------------------------------------
CREATE TABLE FactEncuesta (
    IdEncuestaKey INT NOT NULL AUTO_INCREMENT,
    IdOpinionNK INT NOT NULL,
    IdTiempoKey INT NOT NULL,
    IdProductoKey INT NOT NULL,
    IdClienteKey INT NOT NULL,
    IdFuenteKey INT NOT NULL,
    IdClasificacionKey TINYINT NOT NULL,
    PuntajeSatisfaccion TINYINT NOT NULL,
    Comentario TEXT NOT NULL,
    PRIMARY KEY (IdEncuestaKey),
    UNIQUE KEY uq_factencuesta_nk (IdOpinionNK)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ---------------------------------------------------------------------
-- FactResenaWeb  <-  web_reviews.csv
-- IdClasificacionKey se DERIVA del Rating al cargar:
--   4-5 Positiva (1) | 3 Neutra (2) | 1-2 Negativa (3)
-- ---------------------------------------------------------------------
CREATE TABLE FactResenaWeb (
    IdResenaKey INT NOT NULL AUTO_INCREMENT,
    IdReviewNK VARCHAR(10) NOT NULL,
    IdTiempoKey INT NOT NULL,
    IdProductoKey INT NOT NULL,
    IdClienteKey INT NOT NULL,
    IdFuenteKey INT NOT NULL,
    IdClasificacionKey TINYINT NOT NULL,
    Rating TINYINT NOT NULL,
    Comentario TEXT NOT NULL,
    PRIMARY KEY (IdResenaKey),
    UNIQUE KEY uq_factresenaweb_nk (IdReviewNK)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ---------------------------------------------------------------------
-- FactComentarioSocial  <-  social_comments.csv
-- Sin clasificacion ni puntaje
-- IdClienteKey admite NULL (comentarios de usuarios no identificados).
-- ---------------------------------------------------------------------
CREATE TABLE FactComentarioSocial (
    IdComentarioKey INT NOT NULL AUTO_INCREMENT,
    IdCommentNK VARCHAR(10) NOT NULL,
    IdTiempoKey INT NOT NULL,
    IdProductoKey INT NOT NULL,
    IdClienteKey INT NULL,
    IdFuenteKey INT NOT NULL,
    Comentario TEXT NOT NULL,
    PRIMARY KEY (IdComentarioKey),
    UNIQUE KEY uq_factcomentariosocial_nk (IdCommentNK)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;