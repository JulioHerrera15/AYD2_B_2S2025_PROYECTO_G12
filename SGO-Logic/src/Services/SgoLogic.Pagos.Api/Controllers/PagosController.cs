using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Application.Tokens;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;

namespace SgoLogic.Pagos.Api.Controllers;

[ApiController]
[Route("api/pagos")]
public sealed class PagosController : ControllerBase
{
    private readonly PagosDbContext _contexto;
    private readonly ServicioCobro _servicioCobro;
    private readonly ServicioTokensPago _tokens;
    private readonly DisyuntorPayKlik _disyuntor;

    public PagosController(
        PagosDbContext contexto,
        ServicioCobro servicioCobro,
        ServicioTokensPago tokens,
        DisyuntorPayKlik disyuntor)
    {
        _contexto = contexto;
        _servicioCobro = servicioCobro;
        _tokens = tokens;
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
            .FirstOrDefaultAsync(p => p.OrdenId == ordenId && p.Concepto == ConceptoPago.ORDEN, cancelacion);

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
            .FirstOrDefaultAsync(p => p.OrdenId == ordenId && p.Concepto == ConceptoPago.ORDEN, cancelacion);

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
    /// Devuelve 404 si no existe. Devuelve los metadatos, la bandera EstaVigente y
    /// que datos de la tarjeta tiene que volver a escribir el cliente (DatosRequeridos).
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
            .FirstOrDefaultAsync(p => p.OrdenId == enlace.OrdenId && p.Concepto == ConceptoPago.ORDEN, cancelacion);

        var ultimoIntento = pago?.Intentos.OrderByDescending(i => i.IntentoNumero).FirstOrDefault();

        var ahora = DateTime.UtcNow;
        var ordenYaPagada = pago?.Estado == EstadoPago.APLICADO;
        var expiro = enlace.ExpiraEn <= ahora;

        var estaVigente = !ordenYaPagada && !expiro && (pago?.Estado == EstadoPago.PENDIENTE || !enlace.Usado);
        var usado = ordenYaPagada || (enlace.Usado && pago?.Estado != EstadoPago.PENDIENTE);

        var datosRequeridos = pago is null || !estaVigente || pago.MedioPago != MedioPago.TARJETA
            ? DatosRequeridosReintento.Ninguno
            : await DatosRequeridosAsync(ultimoIntento?.TokenMedioPago, pago.ClienteId, cancelacion);

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
            ClienteId: pago?.ClienteId,
            DatosRequeridos: datosRequeridos);

        return Ok(info);
    }

    /// <summary>
    /// Dispara un intento nuevo usando el enlace de reintento.
    /// Si el cobro se aplica con exito, marca los enlaces de la orden como usados.
    /// Si la orden ya estaba pagada, retorna el pago sin duplicar el cobro.
    /// 404 si no existe, 410 si expiro o si la orden ya no admite reintentos.
    ///
    /// Con TARJETA reusa el token del ultimo intento mientras siga vigente. Si vencio,
    /// responde 422 (codigo datos_tarjeta_requeridos) sin cobrar ni rechazar el pago,
    /// hasta que el cuerpo traiga un token nuevo o el CVV de la tarjeta guardada.
    /// </summary>
    [HttpPost("reintentos/{token}")]
    public async Task<ActionResult<PagoDto>> Reintentar(
        string token,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReintentoPagoRequest? solicitudReintento,
        CancellationToken cancelacion)
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
            .FirstOrDefaultAsync(p => p.OrdenId == enlace.OrdenId && p.Concepto == ConceptoPago.ORDEN, cancelacion);

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
        var tokenMedioPago = ultimoIntento?.TokenMedioPago;

        if (pago.MedioPago == MedioPago.TARJETA)
        {
            var (tokenElegido, rechazo) = await ElegirTokenReintentoAsync(
                tokenMedioPago, pago.ClienteId, solicitudReintento, cancelacion);

            if (rechazo is not null)
            {
                return rechazo;
            }

            tokenMedioPago = tokenElegido;
        }

        var solicitud = new SolicitudCobro(
            pago.OrdenId,
            pago.ClienteId,
            pago.Canal,
            pago.Moneda,
            pago.Monto,
            pago.MedioPago,
            tokenMedioPago,
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

    /// <summary>
    /// Token con el que se reintenta: el del cuerpo, uno nuevo desde la tarjeta
    /// guardada del token anterior (si el cuerpo trae el CVV) o el del ultimo intento
    /// si sigue vigente. Si no hay ninguno utilizable devuelve el 422 que le dice a la
    /// Web que datos pedir.
    /// </summary>
    private async Task<(string? Token, ActionResult? Rechazo)> ElegirTokenReintentoAsync(
        string? tokenAnterior,
        string clienteId,
        ReintentoPagoRequest? solicitud,
        CancellationToken cancelacion)
    {
        if (!string.IsNullOrWhiteSpace(solicitud?.TokenMedioPago))
        {
            return (solicitud.TokenMedioPago.Trim(), null);
        }

        if (!string.IsNullOrWhiteSpace(solicitud?.Cvv))
        {
            var anterior = string.IsNullOrWhiteSpace(tokenAnterior)
                ? null
                : await _contexto.TokensPago.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tokenAnterior, cancelacion);

            if (anterior?.ReferenciaTarjetaGuardadaId is not Guid tarjetaId)
            {
                return (null, DatosRequeridos(DatosRequeridosReintento.Tarjeta,
                    "Ingresa de nuevo los datos completos de la tarjeta."));
            }

            var nuevo = await _tokens.TokenizarTarjetaGuardadaAsync(clienteId, tarjetaId, solicitud.Cvv, cancelacion);

            return nuevo.Estado switch
            {
                EstadoTokenTarjetaGuardada.Emitido => (nuevo.Token!.Token, null),
                EstadoTokenTarjetaGuardada.CvvInvalido => (null, UnprocessableEntity(new
                {
                    codigo = "datos_tarjeta_invalidos",
                    errores = new Dictionary<string, string> { ["cvv"] = nuevo.Mensaje! }
                })),
                _ => (null, DatosRequeridos(DatosRequeridosReintento.Tarjeta,
                    nuevo.Mensaje ?? "La tarjeta guardada ya no se puede usar; ingresa otra tarjeta."))
            };
        }

        var diagnostico = await _servicioCobro.DiagnosticarTokenAsync(tokenAnterior, clienteId, cancelacion);
        if (diagnostico is null)
        {
            return (tokenAnterior, null);
        }

        var requeridos = await DatosRequeridosAsync(tokenAnterior, clienteId, cancelacion);
        return (null, DatosRequeridos(requeridos,
            requeridos == DatosRequeridosReintento.Cvv
                ? "La validacion de tu tarjeta vencio. Escribe de nuevo el codigo de seguridad."
                : "La validacion de tu tarjeta vencio. Ingresa de nuevo los datos de la tarjeta."));
    }

    /// <summary>
    /// NINGUNO si el token sigue vigente; CVV si vencio pero era de una tarjeta
    /// guardada que sigue activa; TARJETA en cualquier otro caso.
    /// </summary>
    private async Task<string> DatosRequeridosAsync(string? tokenId, string clienteId, CancellationToken cancelacion)
    {
        if (await _servicioCobro.DiagnosticarTokenAsync(tokenId, clienteId, cancelacion) is null)
        {
            return DatosRequeridosReintento.Ninguno;
        }

        var token = string.IsNullOrWhiteSpace(tokenId)
            ? null
            : await _contexto.TokensPago.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tokenId, cancelacion);

        if (token?.ReferenciaTarjetaGuardadaId is Guid tarjetaId
            && await _contexto.TarjetasGuardadas.AsNoTracking().AnyAsync(t => t.Id == tarjetaId && t.Activa, cancelacion))
        {
            return DatosRequeridosReintento.Cvv;
        }

        return DatosRequeridosReintento.Tarjeta;
    }

    private ObjectResult DatosRequeridos(string datosRequeridos, string detalle)
    {
        var problema = new ProblemDetails
        {
            Title = "Datos de tarjeta requeridos",
            Detail = detalle,
            Status = StatusCodes.Status422UnprocessableEntity
        };
        problema.Extensions["codigo"] = "datos_tarjeta_requeridos";
        problema.Extensions["datosRequeridos"] = datosRequeridos;
        return StatusCode(StatusCodes.Status422UnprocessableEntity, problema);
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
