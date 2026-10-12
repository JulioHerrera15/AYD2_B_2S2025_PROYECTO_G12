using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.IntegrationTests;

/// <summary>
/// Recorrido completo de la "opcion de credito" del checkout contra PostgreSQL real:
/// lo que Web pregunta antes de mostrarla (elegibilidad), lo que pasa al comprar
/// (evaluacion), lo que el cliente ve despues en "Mi credito" y lo que cambia cuando
/// abona. Ningun doble: repositorios, politica y consultas reales.
///
/// A diferencia de las otras clases de esta carpeta, aqui se recorre el flujo hacia
/// adelante, no una sola tabla, para comprobar que las tres piezas (oferta, compra,
/// vista) cuentan la misma historia con los mismos saldos.
/// </summary>
[Collection("credito-integracion")]
public sealed class OpcionCreditoPersistenciaTests
{
    private readonly CreditoFixture _fixture;

    public OpcionCreditoPersistenciaTests(CreditoFixture fixture)
    {
        _fixture = fixture;
    }

    private static string ClienteNuevo() => $"cliente-{Guid.NewGuid():N}@sgo.local";

    private static string OrdenNueva() => $"ORD-2026-{Random.Shared.Next(100000, 999999)}{Guid.NewGuid():N}"[..20];

    /// <summary>Historial del cliente1 de demo: EXCELENTE, limite 7500, elegible.</summary>
    private static HistorialClienteErpActualizadoData Excelente(string clienteId, DateTime corte) =>
        new(clienteId, "ERP-OPC", corte.AddDays(-1100), 18, 14500m, "GTQ", 18, 0, 0, 1, 0, 0, corte, "LOTE-OPC");

    /// <summary>Historial del cliente4 de demo: 3 puntuales y 7 tardias de 45 dias. No elegible.</summary>
    private static HistorialClienteErpActualizadoData MalHistorial(string clienteId, DateTime corte) =>
        new(clienteId, "ERP-OPC", corte.AddDays(-800), 10, 8000m, "GTQ", 10, 3, 7, 45, 0, 0, corte, "LOTE-OPC");

    private static async Task SembrarAsync(ServiceProvider proveedor, HistorialClienteErpActualizadoData historial)
    {
        using var alcance = proveedor.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<ServicioPerfilCrediticio>()
            .AplicarHistorialAsync(historial, CancellationToken.None);
    }

    private static async Task<OfertaCreditoDto> OfertaAsync(ServiceProvider proveedor, string clienteId, decimal? monto)
    {
        using var alcance = proveedor.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<ServicioElegibilidad>()
            .ConsultarAsync(clienteId, monto, CancellationToken.None);
    }

    private static async Task<MiCreditoDto> MiCreditoAsync(ServiceProvider proveedor, string clienteId)
    {
        using var alcance = proveedor.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<ServicioElegibilidad>()
            .ObtenerMiCreditoAsync(clienteId, CancellationToken.None);
    }

    private static async Task<ResultadoEvaluacion> EvaluarAsync(ServiceProvider proveedor, string ordenId, string clienteId, decimal monto)
    {
        using var alcance = proveedor.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<ServicioEvaluacionCredito>().EvaluarAsync(
            new EvaluacionCreditoSolicitadaData(ordenId, ordenId[^6..], clienteId, Canal.WEB, Moneda.GTQ, monto, DateTime.UtcNow),
            CancellationToken.None);
    }

    private static async Task<AbonoDto> AbonarAsync(ServiceProvider proveedor, string clienteId, Guid cuentaId, decimal monto)
    {
        using var alcance = proveedor.CreateScope();
        var resultado = await alcance.ServiceProvider.GetRequiredService<ServicioAbonos>()
            .CrearAsync(clienteId, cuentaId.ToString("N"), monto, "tok_integracion", CancellationToken.None);
        Assert.True(resultado.Exito, resultado.MensajeError);
        return resultado.Abono!;
    }

    private static async Task CobradoAsync(ServiceProvider proveedor, AbonoDto abono, string clienteId)
    {
        using var alcance = proveedor.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<ServicioResultadoAbonos>().CobradoAsync(
            new AbonoCreditoCobradoData(abono.Id, abono.Referencia, clienteId, "GTQ", abono.Monto, "PK-OPC", "4242", "VISA", DateTime.UtcNow),
            CancellationToken.None);
    }

    [Fact]
    public async Task Oferta_de_credito_disponible_coincide_con_el_limite_que_luego_persiste_la_linea()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        await SembrarAsync(proveedor, Excelente(clienteId, DateTime.UtcNow));

        var oferta = await OfertaAsync(proveedor, clienteId, monto: null);

        Assert.Equal(EstadosOfertaCredito.Disponible, oferta.Estado);
        Assert.True(oferta.Ofrecer);
        Assert.Null(oferta.CodigoMotivo);
        Assert.Equal(7500m, oferta.LimiteAprobado);
        Assert.Equal(7500m, oferta.Disponible);
        Assert.Equal("GTQ", oferta.Moneda);
        Assert.Equal(30, oferta.PlazoDias);
        Assert.NotNull(oferta.FechaPagoEstimada);
        Assert.NotEmpty(oferta.Titulo);
        Assert.NotEmpty(oferta.Mensaje);

        // El monto consultado entra en la respuesta y la fecha de pago se calcula sobre el plazo.
        var conMonto = await OfertaAsync(proveedor, clienteId, 1200m);
        Assert.Equal(1200m, conMonto.MontoSolicitado);
        Assert.Equal(EstadosOfertaCredito.Disponible, conMonto.Estado);
        Assert.Equal(
            DateTime.UtcNow.Date.AddDays(30),
            conMonto.FechaPagoEstimada!.Value.Date);

        // Y el limite ofrecido es exactamente el que queda en la linea de credito.
        await EvaluarAsync(proveedor, OrdenNueva(), clienteId, 1200m);
        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var linea = await contexto.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteId);
        Assert.Equal(oferta.LimiteAprobado, linea.LimiteAprobado);
        Assert.Equal(6300m, linea.Disponible);
    }

    [Fact]
    public async Task Un_cliente_sin_historial_nunca_ve_la_opcion_de_credito()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);

        var oferta = await OfertaAsync(proveedor, ClienteNuevo(), 500m);

        Assert.Equal(EstadosOfertaCredito.SinHistorial, oferta.Estado);
        Assert.False(oferta.Ofrecer);
        Assert.Equal(CodigosMotivoCredito.SinHistorial, oferta.CodigoMotivo);
        Assert.Equal(0m, oferta.LimiteAprobado);
        Assert.Equal(0m, oferta.Disponible);
        Assert.Null(oferta.FechaPagoEstimada);

        var miCredito = await MiCreditoAsync(proveedor, "cliente-inexistente@sgo.local");
        Assert.Equal(EstadosOfertaCredito.SinHistorial, miCredito.Estado);
        Assert.Equal(NivelCredito.NO_CALIFICA.ToString(), miCredito.Nivel);
        Assert.Empty(miCredito.Compras);
        Assert.Empty(miCredito.Factores);
        Assert.Equal(0m, miCredito.LimiteAprobado);
        Assert.Equal(0m, miCredito.TotalPorPagar);
        Assert.Null(miCredito.ProximoVencimiento);
    }

    [Fact]
    public async Task Un_cliente_no_elegible_por_mala_historial_ve_la_opcion_bloqueada_con_motivo()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        await SembrarAsync(proveedor, MalHistorial(clienteId, DateTime.UtcNow));

        var oferta = await OfertaAsync(proveedor, clienteId, 200m);

        Assert.Equal(EstadosOfertaCredito.NoElegible, oferta.Estado);
        Assert.False(oferta.Ofrecer);
        Assert.False(string.IsNullOrWhiteSpace(oferta.CodigoMotivo));
        Assert.Equal(0m, oferta.Disponible);

        // Ni siquiera un monto de Q1 habilita la opcion: la causa no es el cupo.
        var montoMinimo = await OfertaAsync(proveedor, clienteId, 1m);
        Assert.Equal(EstadosOfertaCredito.NoElegible, montoMinimo.Estado);
        Assert.False(montoMinimo.Ofrecer);
    }

    [Fact]
    public async Task La_opcion_se_deshabilita_al_superar_el_cupo_pero_la_linea_sobrevive()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        var ordenId = OrdenNueva();
        await SembrarAsync(proveedor, Excelente(clienteId, DateTime.UtcNow));

        Assert.Equal(ResultadoEvaluacion.Aprobada, await EvaluarAsync(proveedor, ordenId, clienteId, 7000m));

        // Quedan 500 de 7500: la compra entra, la siguiente no.
        var dentro = await OfertaAsync(proveedor, clienteId, 400m);
        Assert.Equal(EstadosOfertaCredito.Disponible, dentro.Estado);
        Assert.True(dentro.Ofrecer);
        Assert.Equal(500m, dentro.Disponible);

        var fuera = await OfertaAsync(proveedor, clienteId, 600m);
        Assert.Equal(EstadosOfertaCredito.CupoInsuficiente, fuera.Estado);
        Assert.False(fuera.Ofrecer);
        Assert.Equal(500m, fuera.Disponible);
        // El limite aprobado no baja al comprar: lo que se agota es el disponible.
        Assert.Equal(7500m, fuera.LimiteAprobado);

        // El monto parcial si se compra y descuenta el cupo.
        Assert.Equal(ResultadoEvaluacion.Aprobada, await EvaluarAsync(proveedor, OrdenNueva(), clienteId, 400m));
        Assert.Equal(100m, (await OfertaAsync(proveedor, clienteId, 200m)).Disponible);
    }

    [Fact]
    public async Task Mi_credito_refleja_la_compra_y_el_abono_con_los_saldos_de_la_base()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteId = ClienteNuevo();
        var ordenId = OrdenNueva();
        await SembrarAsync(proveedor, Excelente(clienteId, DateTime.UtcNow));
        Assert.Equal(ResultadoEvaluacion.Aprobada, await EvaluarAsync(proveedor, ordenId, clienteId, 2000m));

        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var cuenta = await contexto.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.OrdenId == ordenId);

        var antesDelAbono = await MiCreditoAsync(proveedor, clienteId);
        Assert.Equal(EstadosOfertaCredito.Disponible, antesDelAbono.Estado);
        Assert.Equal(7500m, antesDelAbono.LimiteAprobado);
        Assert.Equal(2000m, antesDelAbono.SaldoUtilizado);
        Assert.Equal(5500m, antesDelAbono.Disponible);
        Assert.Equal(2000m, antesDelAbono.TotalPorPagar);
        Assert.Equal(30, antesDelAbono.PlazoDias);
        Assert.NotEmpty(antesDelAbono.Factores);

        var compra = Assert.Single(antesDelAbono.Compras);
        Assert.Equal(ordenId, compra.OrdenId);
        Assert.Equal(2000m, compra.SaldoPendiente);
        Assert.Equal(30, compra.DiasRestantes);
        Assert.True(compra.PuedePagar);
        Assert.Empty(compra.Abonos!);
        Assert.Empty(compra.Movimientos!);

        // Abono parcial: la compra sigue pagable y con el historial del abono.
        var abono = await AbonarAsync(proveedor, clienteId, cuenta.Id, 500m);
        await CobradoAsync(proveedor, abono, clienteId);

        var despuesDelAbono = await MiCreditoAsync(proveedor, clienteId);
        Assert.Equal(1500m, despuesDelAbono.SaldoUtilizado);
        Assert.Equal(6000m, despuesDelAbono.Disponible);
        Assert.Equal(1500m, despuesDelAbono.TotalPorPagar);

        var compraConAbono = Assert.Single(despuesDelAbono.Compras);
        Assert.Equal(1500m, compraConAbono.SaldoPendiente);
        Assert.True(compraConAbono.PuedePagar);
        var historial = Assert.Single(compraConAbono.Abonos!);
        Assert.Equal(abono.Id, historial.AbonoId);
        Assert.Equal(abono.Referencia, historial.Referencia);
        Assert.Equal(EstadoAbonoCredito.APLICADO.ToString(), historial.Estado);

        // Abono final: la cuenta queda PAGADA y sale de "Total por pagar" y de los avisos.
        var resto = await AbonarAsync(proveedor, clienteId, cuenta.Id, 1500m);
        await CobradoAsync(proveedor, resto, clienteId);

        var pagada = await MiCreditoAsync(proveedor, clienteId);
        Assert.Equal(0m, pagada.SaldoUtilizado);
        Assert.Equal(7500m, pagada.Disponible);
        Assert.Equal(0m, pagada.TotalPorPagar);
        Assert.Null(pagada.ProximoVencimiento);
        Assert.True(pagada.Avisos is null or { Count: 0 });

        var compraPagada = Assert.Single(pagada.Compras);
        Assert.Equal(EstadoCuentaPorCobrar.PAGADA.ToString(), compraPagada.Estado);
        Assert.Null(compraPagada.DiasRestantes);
        Assert.False(compraPagada.PuedePagar);
        Assert.Equal(2, compraPagada.Abonos!.Count);
    }

    [Fact]
    public async Task Un_cliente_ve_solo_sus_compras_y_el_saldo_de_su_propia_linea()
    {
        await using var proveedor = CreditoServicios.CrearProveedor(_fixture.CadenaConexion);
        var clienteA = ClienteNuevo();
        var clienteB = ClienteNuevo();
        var ordenA = OrdenNueva();
        var ordenB = OrdenNueva();

        await SembrarAsync(proveedor, Excelente(clienteA, DateTime.UtcNow));
        await SembrarAsync(proveedor, Excelente(clienteB, DateTime.UtcNow));
        await EvaluarAsync(proveedor, ordenA, clienteA, 3000m);
        await EvaluarAsync(proveedor, ordenB, clienteB, 1200m);

        var vistaA = await MiCreditoAsync(proveedor, clienteA);
        var vistaB = await MiCreditoAsync(proveedor, clienteB);

        Assert.Equal(3000m, vistaA.SaldoUtilizado);
        Assert.Equal(1200m, vistaB.SaldoUtilizado);
        Assert.Equal(ordenA, Assert.Single(vistaA.Compras).OrdenId);
        Assert.Equal(ordenB, Assert.Single(vistaB.Compras).OrdenId);

        // Y la cuenta del otro cliente es invisible para el servicio de abonos.
        await using var contexto = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var cuentaB = await contexto.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.OrdenId == ordenB);

        using var alcance = proveedor.CreateScope();
        var cruzado = await alcance.ServiceProvider.GetRequiredService<ServicioAbonos>()
            .CrearAsync(clienteA, cuentaB.Id.ToString("N"), 100m, "tok_integracion", CancellationToken.None);

        Assert.False(cruzado.Exito);
        Assert.Equal(ErroresAbono.CuentaNoEncontrada, cruzado.CodigoError);

        // El intento fallido no dejo nada escrito: el saldo del otro cliente esta intacto.
        await using var verificacion = CreditoServicios.CrearDbContext(_fixture.CadenaConexion);
        var lineaB = await verificacion.Lineas.AsNoTracking().SingleAsync(l => l.ClienteId == clienteB);
        Assert.Equal(1200m, lineaB.SaldoUtilizado);
        Assert.Equal(1200m, (await verificacion.CuentasPorCobrar.AsNoTracking().SingleAsync(c => c.Id == cuentaB.Id)).SaldoPendiente);
        Assert.False(await verificacion.Abonos.AnyAsync(a => a.CuentaId == cuentaB.Id));
    }
}