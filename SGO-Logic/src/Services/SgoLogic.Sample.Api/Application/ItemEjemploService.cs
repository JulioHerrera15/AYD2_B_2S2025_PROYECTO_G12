using Microsoft.EntityFrameworkCore;
using SgoLogic.Sample.Api.Domain;
using SgoLogic.Sample.Api.Infrastructure;

namespace SgoLogic.Sample.Api.Application;

public sealed class ItemEjemploService : IItemEjemploService
{
    private readonly SampleApiDbContext _contexto;

    public ItemEjemploService(SampleApiDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<ItemEjemploRespuesta> CrearAsync(CrearItemEjemploComando comando, CancellationToken cancelacion)
    {
        var item = new ItemEjemplo(comando.Nombre);

        _contexto.ItemsEjemplo.Add(item);
        await _contexto.SaveChangesAsync(cancelacion);

        return new ItemEjemploRespuesta(item.Id, item.Nombre, item.CreadoEn);
    }

    public async Task<IReadOnlyList<ItemEjemploRespuesta>> ObtenerTodosAsync(CancellationToken cancelacion)
    {
        return await _contexto.ItemsEjemplo
            .OrderBy(item => item.CreadoEn)
            .Select(item => new ItemEjemploRespuesta(item.Id, item.Nombre, item.CreadoEn))
            .ToListAsync(cancelacion);
    }
}