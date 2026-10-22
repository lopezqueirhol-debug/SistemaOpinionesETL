namespace SistemaOpiniones.Etl.Domain;

/// <summary>Tabla FuenteDatos (fuente_datos.csv).</summary>
public sealed record FuenteDatos(string IdFuente, string TipoFuente, DateOnly FechaCarga);

/// <summary>Tabla Cliente (clients.csv).</summary>
public sealed record Cliente(string IdCliente, string Nombre, string Email);

/// <summary>Tabla Producto (products.csv).</summary>
public sealed record Producto(string IdProducto, string Nombre, string Categoria);

/// <summary>Tabla ComentarioSocial (social_comments.csv). IdCliente puede ser nulo.</summary>
public sealed record ComentarioSocial(
    string IdComment,
    string? IdCliente,
    string IdProducto,
    string IdFuente,
    DateOnly Fecha,
    string Plataforma,
    string Comentario);

/// <summary>Tabla Encuesta (surveys_part1.csv).</summary>
public sealed record Encuesta(
    int IdOpinion,
    string IdCliente,
    string IdProducto,
    string IdFuente,
    DateOnly Fecha,
    string Comentario,
    string Clasificacion,
    int PuntajeSatisfaccion);

/// <summary>Tabla ResenaWeb (web_reviews.csv).</summary>
public sealed record ResenaWeb(
    string IdReview,
    string IdCliente,
    string IdProducto,
    string IdFuente,
    DateOnly Fecha,
    string Comentario,
    int Rating);