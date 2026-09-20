using Microsoft.EntityFrameworkCore;
using SgoLogic.Despacho.Api.Application.Despacho;

namespace SgoLogic.Despacho.Api.Infrastructure.Persistencia;

/// <summary>
/// Correlativo sobre la secuencia <c>despacho.despacho_correlativo</c>. nextval no
/// participa de la transaccion: un rollback deja un hueco, nunca un id repetido.
/// </summary>
public sealed class GeneradorCorrelativoDespacho : IGeneradorCorrelativoDespacho
{
    private readonly DespachoDbContext _contexto;

    public GeneradorCorrelativoDespacho(DespachoDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<long> SiguienteAsync(CancellationToken cancelacion) =>
        await _contexto.Database
            .SqlQueryRaw<long>("SELECT nextval('despacho.despacho_correlativo') AS \"Value\"")
            .SingleAsync(cancelacion);
}
