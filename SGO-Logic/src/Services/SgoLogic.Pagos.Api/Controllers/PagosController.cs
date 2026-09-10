using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Controllers;

[ApiController]
[Route("api/pagos")]
public sealed class PagosController : ControllerBase
{
    private readonly PagosDbContext _contexto;
    private readonly ServicioCobro _servicioCobro;

    public PagosController(PagosDbContext contexto, ServicioCobro servicioCobro)
    {
        _contexto = contexto;
        _servicioCobro = servicioCobro;
    }

    /// <summary>Devuelve el pago de una orden con todos sus intentos.</summary>
    [HttpGet("ordenes/{ordenId}")]
    public async Task<ActionResult<PagoDto>> ObtenerPorOrden(string ordenId, CancellationToken cancelacion)
    {
        var pago = await _contexto.Pagos
            .AsNoTracking()
            .Include(p => p.Intentos)
            .FirstOrDefaultAsync(p => p.OrdenId == ordenId, cancelacion);

        return pago is null
            ? Problema(StatusCodes.Status404NotFound, "Pago no encontrado",
                $"No hay un pago para la orden {ordenId}.", "pago_no_encontrado")
            : Ok(PagoDto.Desde(pago));
    }

    /// <summary>Devuelve solo el historial de intentos de cobro de una orden.</summary>
    [HttpGet("intentos/{ordenId}")]
    public async Task<ActionResult<IReadOnlyList<IntentoPagoDto>>> ObtenerIntentos(
        string ordenId,
        CancellationToken cancelacion)
    {
        var pago = await _contexto.Pagos
            .AsNoTracking()
            .Include(p => p.Intentos)
            .FirstOrDefaultAsync(p => p.OrdenId == ordenId, cancelacion);

        return pago is null
            ? Problema(StatusCodes.Status404NotFound, "Pago no encontrado",
                $"No hay un pago para la orden {ordenId}.", "pago_no_encontrado")
            : Ok(pago.Intentos
                .OrderBy(intento => intento.IntentoNumero)
                .Select(IntentoPagoDto.Desde)
                .ToList());
    }

    /// <summary>
    /// Dispara un intento nuevo usando el enlace de reintento. Marca el enlace como
    /// usado. 404 si no existe, 410 si ya se uso o expiro.
    /// </summary>
    [HttpPost("reintentos/{token}")]
    public async Task<ActionResult<PagoDto>> Reintentar(string token, CancellationToken cancelacion)
    {
        var enlace = await _contexto.EnlacesReintento
            .FirstOrDefaultAsync(e => e.Token == token, cancelacion);

        if (enlace is null)
        {
            return Problema(StatusCodes.Status404NotFound, "Enlace de reintento no encontrado",
                $"No existe el enlace de reintento '{token}'.", "enlace_no_encontrado");
        }

        if (!enlace.EstaVigente(DateTime.UtcNow))
        {
            return Problema(StatusCodes.Status410Gone, "Enlace de reintento no vigente",
                "El enlace de reintento ya se uso o expiro.", "enlace_no_vigente");
        }

        var pago = await _contexto.Pagos
            .Include(p => p.Intentos)
            .FirstOrDefaultAsync(p => p.OrdenId == enlace.OrdenId, cancelacion);

        if (pago is null)
        {
            return Problema(StatusCodes.Status404NotFound, "Pago no encontrado",
                $"No hay un pago para la orden {enlace.OrdenId}.", "pago_no_encontrado");
        }

        enlace.MarcarUsado();

        var ultimoIntento = pago.Intentos.OrderByDescending(intento => intento.IntentoNumero).FirstOrDefault();
        var solicitud = new SolicitudCobro(
            pago.OrdenId,
            pago.ClienteId,
            pago.Canal,
            pago.Moneda,
            pago.Monto,
            pago.MedioPago,
            ultimoIntento?.TokenMedioPago,
            ultimoIntento?.UltimosCuatro,
            ultimoIntento?.Marca);

        await _servicioCobro.ProcesarCobroAsync(solicitud, cancelacion);
        await _contexto.SaveChangesAsync(cancelacion);

        return Ok(PagoDto.Desde(pago));
    }

    private ObjectResult Problema(int status, string titulo, string detalle, string codigo)
    {
        var problema = new ProblemDetails
        {
            Title = titulo,
            Detail = detalle,
            Status = status
        };
        problema.Extensions["codigo"] = codigo;
        return StatusCode(status, problema);
    }
}
