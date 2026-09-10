using Microsoft.EntityFrameworkCore;
using SgoLogic.Facturacion.Api.Application.Facturacion;

namespace SgoLogic.Facturacion.Api.Infrastructure.Persistencia;

/// <summary>
/// Implementacion de <see cref="IGeneradorCorrelativoFiscal" /> sobre la secuencia
/// de PostgreSQL <c>facturacion.documento_fiscal_correlativo</c> (creada por la
/// migracion). Cada llamada consume un valor; nextval no participa de la
/// transaccion, asi que un rollback posterior solo deja un hueco en la numeracion,
/// nunca un correlativo repetido.
/// </summary>
public sealed class GeneradorCorrelativoFiscal : IGeneradorCorrelativoFiscal
{
    private readonly FacturacionDbContext _contexto;

    public GeneradorCorrelativoFiscal(FacturacionDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<long> SiguienteAsync(CancellationToken cancelacion) =>
        await _contexto.Database
            .SqlQueryRaw<long>("SELECT nextval('facturacion.documento_fiscal_correlativo') AS \"Value\"")
            .SingleAsync(cancelacion);
}
