using Microsoft.EntityFrameworkCore;
using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.BuildingBlocks.Outbox;
using SgoLogic.Contracts.Eventos;
using SgoLogic.Sample.Api.Domain;
using SgoLogic.Sample.Api.Infrastructure;

namespace SgoLogic.Sample.Api.Application;

public sealed class ItemEjemploService : IItemEjemploService
{
    private readonly SampleApiDbContext _contexto;
    private readonly IOutboxWriter _outboxWriter;

    public ItemEjemploService(SampleApiDbContext contexto, IOutboxWriter outboxWriter)
    {
        _contexto = contexto;
        _outboxWriter = outboxWriter;
    }

    public async Task<ItemEjemploRespuesta> CrearAsync(CrearItemEjemploComando comando, CancellationToken cancelacion)
    {
        var item = new ItemEjemplo(comando.Nombre);
        _contexto.ItemsEjemplo.Add(item);

        _outboxWriter.Encolar(
            EjemploCreadoEvento.TipoEvento,
            EjemploCreadoEvento.TipoAgregado,
            item.Id.ToString(),
            sequence: 1,
            exchange: TopologiaMensajeria.ExchangeEventos,
            routingKey: "sample-api.item-ejemplo.creado",
            data: new EjemploCreadoData(item.Nombre));

        // Misma transaccion: la entidad y el evento se confirman o se revierten juntos.
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