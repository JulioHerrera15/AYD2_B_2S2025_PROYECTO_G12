namespace SgoLogic.Inventario.Api.Application.Inventario;

/// <summary>
/// Pagina de resultados de una consulta de lectura. <see cref="Total" /> es el
/// conteo completo sin paginar, para que el cliente sepa cuanto falta.
/// </summary>
public sealed record PaginaDto<T>(
    IReadOnlyList<T> Elementos,
    int Pagina,
    int Tamano,
    int Total);
