namespace SgoLogic.Sample.Api.Application;

public sealed record CrearItemEjemploComando(string Nombre);

public sealed record ItemEjemploRespuesta(Guid Id, string Nombre, DateTime CreadoEn);

public interface IItemEjemploService
{
    Task<ItemEjemploRespuesta> CrearAsync(CrearItemEjemploComando comando, CancellationToken cancelacion);

    Task<IReadOnlyList<ItemEjemploRespuesta>> ObtenerTodosAsync(CancellationToken cancelacion);
}