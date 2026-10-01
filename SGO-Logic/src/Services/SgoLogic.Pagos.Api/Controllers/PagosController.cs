using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Controllers;

[ApiController]
[Route("api/pagos")]
public sealed class PagosController : ControllerBase
{
    private readonly PagosDbContext _contexto;
    private readonly ServicioCobro _servicioCobro;
    private readonly DisyuntorPayKlik _disyuntor;

    public PagosController(PagosDbContext contexto, ServicioCobro servicioCobro, DisyuntorPayKlik disyuntor)
    {
        _contexto = contexto;
        _servicioCobro = servicioCobro;
        _disyuntor = disyuntor;
    }

    /// <summary>
    /// Estado del disyuntor hacia PayKlik para la demo: estado del circuito
    /// (CERRADO, ABIERTO o SEMIABIERTO), fallos recientes en la ventana y la hora
    /// de la ultima apertura.
    /// </summary>
    [HttpGet("salud-proveedor")]
    public ActionResult<SaludProveedorDto> SaludProveedor() => Ok(_disyuntor.Salud());

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
    /// Consulta los detalles de un enlace de reintento sin consumirlo.
    /// Devuelve 404 si no existe. Devuelve los metadatos y bandera EstaVigente.
    /// </summary>
    [HttpGet("reintentos/{token}")]
    public async Task<ActionResult<EnlaceReintentoInfoDto>> ObtenerInfoReintento(string token, CancellationToken cancelacion)
    {
        var enlace = await _contexto.EnlacesReintento
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Token == token, cancelacion);

        if (enlace is null)
        {
            return Problema(StatusCodes.Status404NotFound, "Enlace de reintento no encontrado",
                $"No existe el enlace de reintento '{token}'.", "enlace_no_encontrado");
        }

        var pago = await _contexto.Pagos
            .AsNoTracking()
            .Include(p => p.Intentos)
            .FirstOrDefaultAsync(p => p.OrdenId == enlace.OrdenId, cancelacion);

        var ultimoIntento = pago?.Intentos.OrderByDescending(i => i.IntentoNumero).FirstOrDefault();

        var ahora = DateTime.UtcNow;
        var ordenYaPagada = pago?.Estado == EstadoPago.APLICADO;
        var expiro = enlace.ExpiraEn <= ahora;

        var estaVigente = !ordenYaPagada && !expiro && (pago?.Estado == EstadoPago.PENDIENTE || !enlace.Usado);
        var usado = ordenYaPagada || (enlace.Usado && pago?.Estado != EstadoPago.PENDIENTE);

        var info = new EnlaceReintentoInfoDto(
            Token: enlace.Token,
            OrdenId: enlace.OrdenId,
            Monto: pago?.Monto ?? 0m,
            Moneda: pago?.Moneda.ToString() ?? "GTQ",
            EstaVigente: estaVigente,
            Usado: usado,
            ExpiraEn: enlace.ExpiraEn,
            EstadoPago: pago?.Estado.ToString() ?? "PENDIENTE",
            UltimosCuatro: ultimoIntento?.UltimosCuatro,
            Marca: ultimoIntento?.Marca,
            ClienteId: pago?.ClienteId);

        return Ok(info);
    }

    /// <summary>
    /// Dispara un intento nuevo usando el enlace de reintento.
    /// Si el cobro se aplica con exito, marca los enlaces de la orden como usados.
    /// Si la orden ya estaba pagada, retorna el pago sin duplicar el cobro.
    /// 404 si no existe, 410 si expiro o si la orden ya no admite reintentos.
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

        var ahora = DateTime.UtcNow;
        if (enlace.ExpiraEn <= ahora)
        {
            return Problema(StatusCodes.Status410Gone, "Enlace de reintento no vigente",
                "El enlace de reintento ha expirado.", "enlace_no_vigente");
        }

        var pago = await _contexto.Pagos
            .Include(p => p.Intentos)
            .FirstOrDefaultAsync(p => p.OrdenId == enlace.OrdenId, cancelacion);

        if (pago is null)
        {
            return Problema(StatusCodes.Status404NotFound, "Pago no encontrado",
                $"No hay un pago para la orden {enlace.OrdenId}.", "pago_no_encontrado");
        }

        if (pago.Estado == EstadoPago.APLICADO)
        {
            enlace.MarcarUsado();
            await _contexto.SaveChangesAsync(cancelacion);
            return Ok(PagoDto.Desde(pago));
        }

        if (enlace.Usado && pago.Estado != EstadoPago.PENDIENTE)
        {
            return Problema(StatusCodes.Status410Gone, "Enlace de reintento no vigente",
                "El enlace de reintento ya se uso.", "enlace_no_vigente");
        }

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

        if (pago.Estado == EstadoPago.APLICADO)
        {
            var enlacesOrden = await _contexto.EnlacesReintento
                .Where(e => e.OrdenId == pago.OrdenId)
                .ToListAsync(cancelacion);

            foreach (var e in enlacesOrden)
            {
                e.MarcarUsado();
            }
        }
        else if (pago.Estado == EstadoPago.RECHAZADO)
        {
            enlace.MarcarUsado();
        }

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
