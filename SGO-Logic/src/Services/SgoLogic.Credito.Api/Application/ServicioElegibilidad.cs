using Microsoft.Extensions.Options;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

/// <summary>
/// Consulta de oferta de credito y vista "Mi credito". Solo lectura sobre el perfil
/// precalculado: la oferta es una consulta sin seguimiento, sin recalcular y sin
/// llamar a otros servicios, para que el checkout pueda preguntar en cada carga sin
/// costo. "Mi credito" agrega una segunda consulta con las compras a credito.
/// </summary>
public sealed class ServicioElegibilidad
{
    private readonly IConsultasCredito _consultas;
    private readonly PoliticaCreditoOptions _politica;
    private readonly Func<DateTime> _reloj;

    public ServicioElegibilidad(IConsultasCredito consultas, IOptions<PoliticaCreditoOptions> politica)
        : this(consultas, politica, () => DateTime.UtcNow)
    {
    }

    public ServicioElegibilidad(IConsultasCredito consultas, IOptions<PoliticaCreditoOptions> politica, Func<DateTime> reloj)
    {
        _consultas = consultas;
        _politica = politica.Value;
        _reloj = reloj;
    }

    /// <summary>
    /// Oferta para el cliente y, si viene, el monto de la compra. Quien llama valida
    /// que <paramref name="monto" /> sea mayor que 0.
    /// </summary>
    public async Task<OfertaCreditoDto> ConsultarAsync(string clienteId, decimal? monto, CancellationToken cancelacion)
    {
        var resumen = await _consultas.ObtenerResumenAsync(ServicioPerfilCrediticio.NormalizarCliente(clienteId), cancelacion);
        var ahora = _reloj();
        var situacion = Situacion(resumen, monto);
        var ofrecer = situacion.Estado == EstadosOfertaCredito.Disponible;
        DateTime? fechaPago = ofrecer
            ? DateTime.SpecifyKind(ahora.Date.AddDays(_politica.PlazoDias), DateTimeKind.Utc)
            : null;

        var mensaje = Mensaje(situacion, resumen, monto, fechaPago, ahora);

        MetricasCredito.ConsultasElegibilidadTotal.WithLabels(situacion.Estado).Inc();

        return new OfertaCreditoDto(
            situacion.Estado,
            ofrecer,
            situacion.CodigoMotivo,
            mensaje.Titulo,
            mensaje.Mensaje,
            mensaje.Recomendacion,
            situacion.Limite,
            situacion.Disponible,
            monto,
            resumen?.Moneda ?? _politica.Moneda,
            _politica.PlazoDias,
            fechaPago,
            resumen?.CalculadoEn);
    }

    /// <summary>Estado de la linea del cliente con sus factores explicados, sin puntaje.</summary>
    public async Task<MiCreditoDto> ObtenerMiCreditoAsync(string clienteId, CancellationToken cancelacion)
    {
        var resumen = await _consultas.ObtenerResumenAsync(ServicioPerfilCrediticio.NormalizarCliente(clienteId), cancelacion);
        var ahora = _reloj();
        var situacion = Situacion(resumen, monto: null);
        var mensaje = Mensaje(situacion, resumen, monto: null, fechaPago: null, ahora);

        var factores = resumen is null
            ? Array.Empty<FactorCreditoDto>()
            : FactoresCredito.Desde(resumen, _politica)
                .Select(factor => new FactorCreditoDto(
                    CatalogoMensajesCredito.NombreFactor(factor.Factor),
                    factor.Valoracion,
                    CatalogoMensajesCredito.DescripcionFactor(factor.Factor, factor.Valoracion)))
                .ToArray();

        var nivel = resumen?.Elegible == true ? resumen.Nivel : NivelCredito.NO_CALIFICA;

        var compras = (await _consultas.ListarComprasAsync(ServicioPerfilCrediticio.NormalizarCliente(clienteId), cancelacion))
            .OrderBy(compra => compra.Estado == EstadoCuentaPorCobrar.VIGENTE ? 0 : 1)
            .ThenBy(compra => compra.Estado == EstadoCuentaPorCobrar.VIGENTE ? compra.VenceEn : DateTime.MaxValue)
            .ThenByDescending(compra => compra.EmitidaEn)
            .Select(compra => new CompraCreditoDto(
                compra.NumeroOrden,
                compra.OrdenId,
                compra.MontoOriginal,
                compra.SaldoPendiente,
                compra.EmitidaEn,
                compra.VenceEn,
                compra.Estado == EstadoCuentaPorCobrar.VIGENTE ? (compra.VenceEn.Date - ahora.Date).Days : null,
                compra.Estado.ToString(),
                CatalogoMensajesCredito.EtiquetaEstadoCompra(compra.Estado)))
            .ToArray();

        return new MiCreditoDto(
            situacion.Estado,
            nivel.ToString(),
            CatalogoMensajesCredito.EtiquetaNivel(nivel),
            mensaje.Titulo,
            mensaje.Mensaje,
            mensaje.Recomendacion,
            situacion.Limite,
            situacion.SaldoUtilizado,
            situacion.Disponible,
            resumen?.Moneda ?? _politica.Moneda,
            _politica.PlazoDias,
            resumen is null ? null : Max(resumen.CalculadoEn, resumen.ActualizadoLineaEn),
            factores,
            compras);
    }

    private sealed record SituacionCredito(
        string Estado,
        string? CodigoMotivo,
        decimal Limite,
        decimal SaldoUtilizado,
        decimal Disponible);

    private static SituacionCredito Situacion(ResumenCredito? resumen, decimal? monto)
    {
        if (resumen is null)
        {
            return new SituacionCredito(EstadosOfertaCredito.SinHistorial, CodigosMotivoCredito.SinHistorial, 0m, 0m, 0m);
        }

        var saldo = resumen.SaldoUtilizado ?? 0m;

        if (!resumen.Elegible)
        {
            return new SituacionCredito(EstadosOfertaCredito.NoElegible, resumen.CodigoMotivo, 0m, saldo, 0m);
        }

        var limite = resumen.LimiteAprobado ?? 0m;
        var disponible = LineaCredito.CalcularDisponible(limite, saldo);

        if (resumen.EstadoLinea == EstadoLineaCredito.BLOQUEADA)
        {
            return new SituacionCredito(EstadosOfertaCredito.LineaBloqueada, null, limite, saldo, disponible);
        }

        if (resumen.EstadoLinea != EstadoLineaCredito.ACTIVA)
        {
            return new SituacionCredito(EstadosOfertaCredito.NoElegible, resumen.CodigoMotivo, 0m, saldo, 0m);
        }

        if (monto is not null && disponible < monto.Value)
        {
            return new SituacionCredito(EstadosOfertaCredito.CupoInsuficiente, null, limite, saldo, disponible);
        }

        return new SituacionCredito(EstadosOfertaCredito.Disponible, null, limite, saldo, disponible);
    }

    private MensajeCredito Mensaje(
        SituacionCredito situacion,
        ResumenCredito? resumen,
        decimal? monto,
        DateTime? fechaPago,
        DateTime ahora)
    {
        switch (situacion.Estado)
        {
            case EstadosOfertaCredito.Disponible:
                return CatalogoMensajesCredito.Disponible(situacion.Disponible, situacion.Limite, _politica.PlazoDias, monto, fechaPago);

            case EstadosOfertaCredito.CupoInsuficiente:
                return CatalogoMensajesCredito.CupoInsuficiente(situacion.Disponible, monto ?? 0m);

            case EstadosOfertaCredito.SinHistorial:
                return CatalogoMensajesCredito.SinHistorial();

            case EstadosOfertaCredito.LineaBloqueada:
                return CatalogoMensajesCredito.LineaBloqueada();

            default:
                int? diasFaltantes = resumen?.ClienteDesde is { } desde
                    ? Math.Max(0, _politica.AntiguedadMinimaDias - Math.Max(0, (int)Math.Floor((ahora - desde).TotalDays)))
                    : null;

                FactorCliente? masDebil = resumen is null
                    ? null
                    : FactoresCredito.MasDebil(FactoresCredito.Desde(resumen, _politica));

                return CatalogoMensajesCredito.NoElegible(
                    situacion.CodigoMotivo, _politica.AntiguedadMinimaDias, diasFaltantes, masDebil);
        }
    }

    private static DateTime Max(DateTime a, DateTime? b) => b is { } valor && valor > a ? valor : a;
}
