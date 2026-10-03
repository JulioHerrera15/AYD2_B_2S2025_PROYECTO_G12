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
    private readonly CobranzaOptions _cobranza;
    private readonly Func<DateTime> _reloj;

    public ServicioElegibilidad(
        IConsultasCredito consultas,
        IOptions<PoliticaCreditoOptions> politica,
        IOptions<CobranzaOptions>? cobranza = null)
        : this(consultas, politica, () => DateTime.UtcNow, cobranza)
    {
    }

    public ServicioElegibilidad(
        IConsultasCredito consultas,
        IOptions<PoliticaCreditoOptions> politica,
        Func<DateTime> reloj,
        IOptions<CobranzaOptions>? cobranza = null)
    {
        _consultas = consultas;
        _politica = politica.Value;
        _cobranza = cobranza?.Value ?? new CobranzaOptions();
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

        var cliente = ServicioPerfilCrediticio.NormalizarCliente(clienteId);
        var abonosPorCuenta = (await _consultas.ListarAbonosAsync(cliente, cancelacion))
            .GroupBy(abono => abono.CuentaId)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.OrderByDescending(abono => abono.SolicitadoEn).ToList());

        var cuentas = (await _consultas.ListarComprasAsync(cliente, cancelacion))
            .OrderBy(compra => Pagable(compra) ? 0 : 1)
            .ThenBy(compra => Pagable(compra) ? compra.VenceEn : DateTime.MaxValue)
            .ThenByDescending(compra => compra.EmitidaEn)
            .ToList();

        var compras = cuentas
            .Select(compra => Compra(compra, abonosPorCuenta.GetValueOrDefault(compra.CuentaId) ?? new List<AbonoCredito>(), ahora))
            .ToArray();

        var pagables = cuentas.Where(Pagable).ToList();
        var avisos = Avisos(pagables, situacion.Estado == EstadosOfertaCredito.LineaBloqueada, ahora);

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
            compras,
            pagables.Sum(compra => compra.SaldoPendiente),
            pagables.Count == 0 ? null : pagables.Min(compra => compra.VenceEn),
            avisos);
    }

    private static bool Pagable(CompraCredito compra) =>
        compra.Estado is EstadoCuentaPorCobrar.VIGENTE or EstadoCuentaPorCobrar.VENCIDA && compra.SaldoPendiente > 0m;

    private int DiasRestantes(CompraCredito compra, DateTime ahora) => (compra.VenceEn.Date - ahora.Date).Days;

    private bool VencePronto(CompraCredito compra, DateTime ahora) =>
        compra.Estado == EstadoCuentaPorCobrar.VIGENTE && compra.SaldoPendiente > 0m
        && DiasRestantes(compra, ahora) <= _cobranza.DiasRecordatorio;

    private CompraCreditoDto Compra(CompraCredito compra, IReadOnlyList<AbonoCredito> abonos, DateTime ahora)
    {
        var enCurso = abonos.FirstOrDefault(abono => abono.EnCurso);
        return new CompraCreditoDto(
            compra.NumeroOrden,
            compra.OrdenId,
            compra.MontoOriginal,
            compra.SaldoPendiente,
            compra.EmitidaEn,
            compra.VenceEn,
            compra.Estado == EstadoCuentaPorCobrar.VIGENTE ? DiasRestantes(compra, ahora) : null,
            compra.Estado.ToString(),
            CatalogoMensajesCredito.EtiquetaEstadoCompra(compra.Estado, VencePronto(compra, ahora)),
            compra.CuentaId.ToString("N"),
            Pagable(compra) && enCurso is null,
            enCurso?.Id.ToString("N"),
            enCurso?.PuedeReintentar ?? false,
            abonos
                .Select(abono => new AbonoHistorialDto(
                    abono.Id.ToString("N"),
                    abono.Referencia,
                    abono.ResueltoEn ?? abono.SolicitadoEn,
                    abono.Monto,
                    CatalogoMensajesCredito.Tarjeta(abono.Marca, abono.UltimosCuatro),
                    abono.Estado.ToString(),
                    CatalogoMensajesCredito.EtiquetaEstadoAbono(abono.Estado)))
                .ToArray());
    }

    /// <summary>Avisos destacados: vencidas primero, luego las que vencen pronto, y la pausa de la linea.</summary>
    private IReadOnlyList<AvisoCreditoDto> Avisos(IReadOnlyList<CompraCredito> pagables, bool lineaEnPausa, DateTime ahora)
    {
        var avisos = new List<AvisoCreditoDto>();

        if (lineaEnPausa)
        {
            var pausa = CatalogoMensajesCredito.LineaBloqueada();
            avisos.Add(new AvisoCreditoDto("LINEA_EN_PAUSA", pausa.Titulo, pausa.Mensaje, pausa.Recomendacion, null));
        }

        foreach (var vencida in pagables.Where(compra => compra.Estado == EstadoCuentaPorCobrar.VENCIDA))
        {
            var mensaje = CatalogoMensajesCredito.CompraVencida(vencida.NumeroOrden, vencida.SaldoPendiente, vencida.VenceEn);
            avisos.Add(new AvisoCreditoDto("VENCIDA", mensaje.Titulo, mensaje.Mensaje, mensaje.Recomendacion, vencida.CuentaId.ToString("N")));
        }

        foreach (var proxima in pagables.Where(compra => VencePronto(compra, ahora)))
        {
            var mensaje = CatalogoMensajesCredito.CompraPorVencer(
                proxima.NumeroOrden, proxima.SaldoPendiente, proxima.VenceEn, DiasRestantes(proxima, ahora));
            avisos.Add(new AvisoCreditoDto("POR_VENCER", mensaje.Titulo, mensaje.Mensaje, mensaje.Recomendacion, proxima.CuentaId.ToString("N")));
        }

        return avisos;
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
        var limite = resumen.LimiteAprobado ?? 0m;

        // Pausa por mora: manda sobre la elegibilidad (el perfil dice DEUDA_VENCIDA).
        if (resumen.EstadoLinea == EstadoLineaCredito.BLOQUEADA)
        {
            return new SituacionCredito(
                EstadosOfertaCredito.LineaBloqueada, CodigosMotivoCredito.LineaBloqueada, limite, saldo, 0m);
        }

        if (!resumen.Elegible)
        {
            return new SituacionCredito(EstadosOfertaCredito.NoElegible, resumen.CodigoMotivo, 0m, saldo, 0m);
        }

        var disponible = LineaCredito.CalcularDisponible(limite, saldo);

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
