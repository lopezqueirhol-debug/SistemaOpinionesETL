using System.Globalization;
using SistemaOpiniones.Etl.Configuration;
using SistemaOpiniones.Etl.Domain;
using SistemaOpiniones.Etl.Validation;

namespace SistemaOpiniones.Etl.Services;

/// <summary>
/// Define, para cada CSV, como se transforma cada registro en su entidad y en que tabla se inserta.
/// El orden de la lista respeta las dependencias de claves foraneas:
///   tablas maestras (FuenteDatos, Cliente, Producto) -> tablas transaccionales.
/// </summary>
public static class LoaderCatalog
{
    // Formato de identificadores en la base de datos.
    private const char ClientePrefix = 'C';
    private const char ProductoPrefix = 'P';
    private const char FuentePrefix = 'F';
    private const char ComentarioPrefix = 'T';
    private const char ResenaPrefix = 'W';

    public static IReadOnlyList<IFileLoader> Create(EtlOptions options, FieldValidator v)
    {
        return new IFileLoader[]
        {
            // --- Tablas maestras ---
            Fuentes(v),
            Clientes(v),
            Productos(v),

            // --- Tablas transaccionales ---
            ComentariosSociales(options, v),
            ResenasWeb(options, v),
            Encuestas(options, v)
        };
    }

    // Maestras

    private static IFileLoader Fuentes(FieldValidator v) =>
        new FileLoader<FuenteDatos>(
            fileName: "fuente_datos.csv",
            tableName: "FuenteDatos",
            isMaster: true,
            expectedColumns: new[] { "IdFuente", "TipoFuente", "FechaCarga" },
            map: (r, _) => new FuenteDatos(
                IdFuente: v.Code(r["IdFuente"], FuentePrefix, 3, "IdFuente"),
                TipoFuente: v.RequiredText(r["TipoFuente"], "TipoFuente", 50),
                FechaCarga: v.Date(r["FechaCarga"], "FechaCarga")),
            keyOf: e => e.IdFuente,
            contentKeyOf: null,
            insert: (repo, e) => repo.Insertar(e));

    // clients.csv trae IdCliente numerico (1, 2, 3...) -> se transforma a 'C001', 'C002'
    private static IFileLoader Clientes(FieldValidator v) =>
        new FileLoader<Cliente>(
            fileName: "clients.csv",
            tableName: "Cliente",
            isMaster: true,
            expectedColumns: new[] { "IdCliente", "Nombre", "Email" },
            map: (r, _) => new Cliente(
                IdCliente: v.Code(r["IdCliente"], ClientePrefix, 3, "IdCliente"),
                Nombre: v.RequiredText(r["Nombre"], "Nombre", 100),
                Email: v.Email(r["Email"], "Email", 150)),
            keyOf: e => e.IdCliente,
            contentKeyOf: null,
            insert: (repo, e) => repo.Insertar(e));

    // products.csv trae IdProducto numerico (1, 2, 3...) -> se transforma a 'P001', 'P002'
    private static IFileLoader Productos(FieldValidator v) =>
        new FileLoader<Producto>(
            fileName: "products.csv",
            tableName: "Producto",
            isMaster: true,
            expectedColumns: new[] { "IdProducto", "Nombre", "Categoria" },
            map: (r, _) => new Producto(
                IdProducto: v.Code(r["IdProducto"], ProductoPrefix, 3, "IdProducto"),
                Nombre: v.RequiredText(r["Nombre"], "Nombre", 100),
                Categoria: v.RequiredText(r["Categoria"], "Categoria", 50)),
            keyOf: e => e.IdProducto,
            contentKeyOf: null,
            insert: (repo, e) => repo.Insertar(e));


    // Transaccionales


    // social_comments.csv: IdCliente opcional (para datos vacios). La columna 'Fuente' (Instagram, ...)
    // se guarda en Plataforma; el IdFuente se resuelve por TipoFuente.
    private static IFileLoader ComentariosSociales(EtlOptions options, FieldValidator v) =>
        new FileLoader<ComentarioSocial>(
            fileName: "social_comments.csv",
            tableName: "ComentarioSocial",
            isMaster: false,
            expectedColumns: new[] { "IdComment", "IdCliente", "IdProducto", "Fuente", "Fecha", "Comentario" },
            map: (r, idFuente) => new ComentarioSocial(
                IdComment: v.Code(r["IdComment"], ComentarioPrefix, 4, "IdComment"),
                IdCliente: v.OptionalCode(r["IdCliente"], ClientePrefix, 3, "IdCliente"),
                IdProducto: v.Code(r["IdProducto"], ProductoPrefix, 3, "IdProducto"),
                IdFuente: RequireFuente(idFuente),
                Fecha: v.Date(r["Fecha"], "Fecha"),
                Plataforma: v.RequiredText(r["Fuente"], "Fuente", 50),
                Comentario: v.RequiredText(r["Comentario"], "Comentario", 10000)),
            keyOf: e => e.IdComment,
            // Solo comentarios con cliente identificado: los anonimos pueden repetir texto legitimamente.
            contentKeyOf: e => e.IdCliente is null
                ? null
                : ContentKey(e.IdCliente, e.IdProducto, e.Fecha, e.Comentario),
            insert: (repo, e) => repo.Insertar(e),
            tipoFuente: RequireTipoFuente(options, "social_comments.csv"));

    // web_reviews.csv: no trae fuente; el IdFuente se resuelve por TipoFuente.
    private static IFileLoader ResenasWeb(EtlOptions options, FieldValidator v) =>
        new FileLoader<ResenaWeb>(
            fileName: "web_reviews.csv",
            tableName: "ResenaWeb",
            isMaster: false,
            expectedColumns: new[] { "IdReview", "IdCliente", "IdProducto", "Fecha", "Comentario", "Rating" },
            map: (r, idFuente) => new ResenaWeb(
                IdReview: v.Code(r["IdReview"], ResenaPrefix, 4, "IdReview"),
                IdCliente: v.Code(r["IdCliente"], ClientePrefix, 3, "IdCliente"),
                IdProducto: v.Code(r["IdProducto"], ProductoPrefix, 3, "IdProducto"),
                IdFuente: RequireFuente(idFuente),
                Fecha: v.Date(r["Fecha"], "Fecha"),
                Comentario: v.RequiredText(r["Comentario"], "Comentario", 10000),
                Rating: v.IntegerInRange(r["Rating"], "Rating", 1, 5)),
            keyOf: e => e.IdReview,
            contentKeyOf: e => ContentKey(e.IdCliente, e.IdProducto, e.Fecha, e.Comentario),
            insert: (repo, e) => repo.Insertar(e),
            tipoFuente: RequireTipoFuente(options, "web_reviews.csv"));

    // surveys_part1.csv: IdCliente/IdProducto vienen como numeros (8537, 366...) -> 'C8537', 'P366'.
    // Los que no existan en las tablas maestras se rechazan por integridad referencial.
    private static IFileLoader Encuestas(EtlOptions options, FieldValidator v) =>
        new FileLoader<Encuesta>(
            fileName: "surveys_part1.csv",
            tableName: "Encuesta",
            isMaster: false,
            expectedColumns: new[]
            {
                "IdOpinion", "IdCliente", "IdProducto", "Fecha", "Comentario",
                "Clasificacion", "PuntajeSatisfaccion", "Fuente"
            },
            map: (r, idFuente) =>
            {
                var clasificacion = v.Classification(r["Clasificacion"], "Clasificacion");
                var puntaje = v.IntegerInRange(r["PuntajeSatisfaccion"], "PuntajeSatisfaccion", 1, 5);
                FieldValidator.EnsureClassificationMatchesScore(clasificacion, puntaje);

                return new Encuesta(
                    IdOpinion: v.PositiveInteger(r["IdOpinion"], "IdOpinion"),
                    IdCliente: v.Code(r["IdCliente"], ClientePrefix, 3, "IdCliente"),
                    IdProducto: v.Code(r["IdProducto"], ProductoPrefix, 3, "IdProducto"),
                    IdFuente: RequireFuente(idFuente),
                    Fecha: v.Date(r["Fecha"], "Fecha"),
                    Comentario: v.RequiredText(r["Comentario"], "Comentario", 10000),
                    Clasificacion: clasificacion,
                    PuntajeSatisfaccion: puntaje);
            },
            keyOf: e => e.IdOpinion.ToString(CultureInfo.InvariantCulture),
            contentKeyOf: e => ContentKey(e.IdCliente, e.IdProducto, e.Fecha, e.Comentario),
            insert: (repo, e) => repo.Insertar(e),
            tipoFuente: RequireTipoFuente(options, "surveys_part1.csv"));

    // Helpers

    private static string ContentKey(string idCliente, string idProducto, DateOnly fecha, string comentario) =>
        $"{idCliente}|{idProducto}|{fecha:yyyy-MM-dd}|{comentario}";

    private static string RequireFuente(string? idFuente) =>
        idFuente ?? throw new EtlException("No se resolvio el IdFuente para el archivo.");

    private static string RequireTipoFuente(EtlOptions options, string fileName)
    {
        if (options.TipoFuentePorArchivo.TryGetValue(fileName, out var tipo) && !string.IsNullOrWhiteSpace(tipo))
            return tipo.Trim();

        throw new ConfigurationException(
            $"Falta Etl:TipoFuentePorArchivo:\"{fileName}\" en appsettings.json " +
            "(tipo de FuenteDatos que corresponde a este archivo, p. ej. \"Web\").");
    }
}