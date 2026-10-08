using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

/// <summary>
/// Cobro de abonos a credito (CobroAbonoCreditoSolicitado): mismo cliente de PayKlik y
/// boveda de tokens que las ordenes, un Pago por abono (Concepto ABONO_CREDITO), clave
/// de idempotencia estable por abono y solo eventos de abono.
/// </summary>
public sealed class CobroAbonoCreditoTests
{
    private const string Cliente = "cliente1@sgo.local";
    private const string AbonoId = "0f3c2a9d8e7b4c6a9b1d2e3f4a5b6c7d";
    private const string Referencia = "ABO-2026-000001";

    private readonly string _baseDatos = $"pagos-abono-{Guid.NewGuid()}";
    private readonly OutboxWriterFalso _outbox = new();

    private PagosDbContext Contexto() =>
        new(new DbContextOptionsBuilder<PagosDbContext>().UseInMemoryDatabase(_baseDatos).Options);

    private ServicioCobro Servicio(PagosDbContext contexto, ClientePayKlikFalso payklik) => new(
        contexto,
        payklik,
        _outbox,
        Options.Create(new PayKlikOptions { MaxReintentos = 0, BackoffBaseMs = 0 }),
        Options.Create(new ReintentoOptions { ExpiracionMinutos = 60 }),
        BovedaPruebas.Cifrador,
        NullLogger<ServicioCobro>.Instance);

    private async Task SembrarTokenAsync(string tokenId, string? clienteId = Cliente)
    {
        await using var contexto = Contexto();
        contexto.TokensPago.Add(BovedaPruebas.TokenNuevo(tokenId, clienteId: clienteId));
        await contexto.SaveChangesAsync();
    }

    private static CobroAbonoCreditoSolicitadoData Solicitud(string token, int intento = 1, decimal monto = 400m) =>
        new(AbonoId, Referencia, Cliente, "GTQ", monto, token, intento, DateTime.UtcNow);

    private async Task<Pago> CobrarAsync(ClientePayKlikFalso payklik, CobroAbonoCreditoSolicitadoData datos)
    {
        await using var contexto = Contexto();
        var pago = await Servicio(contexto, payklik).ProcesarCobroAbonoAsync(datos, CancellationToken.None);
        await contexto.SaveChangesAsync();
        return pago;
    }

    private static readonly string[] EventosDeOrden =
    {
        PagoAplicado.EventType, PagoRechazado.EventType, PagoPendiente.EventType, IntentoPagoFallido.EventType
    };

    [Fact]
    public async Task Abono_aprobado_emite_AbonoCreditoCobrado_con_la_tarjeta()
    {
        await SembrarTokenAsync("tok_abono_1");
        var payklik = ClientePayKlikFalso.Aprueba("PK-ABONO-1");

        var pago = await CobrarAsync(payklik, Solicitud("tok_abono_1"));

        Assert.Equal(EstadoPago.APLICADO, pago.Estado);
        Assert.Equal(ConceptoPago.ABONO_CREDITO, pago.Concepto);
        Assert.Equal(Referencia, pago.OrdenId);
        Assert.Single(payklik.Peticiones);
        Assert.Equal(Referencia, payklik.Peticiones[0].Referencia);

        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(AbonoCreditoCobrado.EventType, evento.EventType);
        Assert.Equal(AbonoCreditoCobrado.RoutingKey, evento.RoutingKey);
        Assert.Equal(AbonoId, evento.AggregateId);
        var datos = Assert.IsType<AbonoCreditoCobradoData>(evento.Data);
        Assert.Equal("PK-ABONO-1", datos.AutorizacionId);
        Assert.Equal(400m, datos.Monto);
        Assert.Equal("4242", datos.UltimosCuatro);
    }

    [Fact]
    public async Task Abono_rechazado_por_el_proveedor_emite_AbonoCreditoRechazado_con_su_codigo()
    {
        await SembrarTokenAsync("tok_abono_2");

        var pago = await CobrarAsync(ClientePayKlikFalso.Rechaza(), Solicitud("tok_abono_2"));

        Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        var datos = Assert.IsType<AbonoCreditoRechazadoData>(Assert.Single(_outbox.Eventos).Data);
        Assert.Equal("51", datos.CodigoRespuesta);
    }

    [Theory]
    [MemberData(nameof(FallasTecnicas))]
    public async Task Sin_respuesta_util_el_abono_queda_pendiente_sin_enlace(ClientePayKlikFalso payklik)
    {
        await SembrarTokenAsync("tok_abono_3");

        var pago = await CobrarAsync(payklik, Solicitud("tok_abono_3"));

        Assert.Equal(EstadoPago.PENDIENTE, pago.Estado);
        Assert.Equal(AbonoCreditoPendiente.EventType, Assert.Single(_outbox.Eventos).EventType);
        await using var contexto = Contexto();
        Assert.Empty(await contexto.EnlacesReintento.ToListAsync());
    }

    public static IEnumerable<object[]> FallasTecnicas() => new[]
    {
        new object[] { ClientePayKlikFalso.Timeout() },
        new object[] { ClientePayKlikFalso.NoDisponible() }
    };

    [Fact]
    public async Task El_reintento_del_mismo_abono_usa_el_mismo_pago_y_la_misma_clave_de_idempotencia()
    {
        await SembrarTokenAsync("tok_abono_4");
        await SembrarTokenAsync("tok_abono_5");
        var caido = ClientePayKlikFalso.NoDisponible();
        var arriba = ClientePayKlikFalso.Aprueba();

        var primero = await CobrarAsync(caido, Solicitud("tok_abono_4"));
        var segundo = await CobrarAsync(arriba, Solicitud("tok_abono_5", intento: 2));

        Assert.Equal(primero.Id, segundo.Id);
        Assert.Equal(EstadoPago.APLICADO, segundo.Estado);
        Assert.Equal(primero.Id.ToString(), Assert.Single(caido.ClavesIdempotencia));
        Assert.Equal(primero.Id.ToString(), Assert.Single(arriba.ClavesIdempotencia));

        await using var contexto = Contexto();
        var pago = await contexto.Pagos.Include(p => p.Intentos).SingleAsync();
        Assert.Equal(2, pago.Intentos.Count);
        Assert.Single(pago.Intentos, i => i.Estado == EstadoIntentoPago.EXITOSO);
    }

    [Fact]
    public async Task Un_abono_ya_cobrado_no_vuelve_a_llamar_al_proveedor()
    {
        await SembrarTokenAsync("tok_abono_6");
        await SembrarTokenAsync("tok_abono_7");
        await CobrarAsync(ClientePayKlikFalso.Aprueba("PK-UNA-VEZ"), Solicitud("tok_abono_6"));
        _outbox.Eventos.Clear();
        var otra = ClientePayKlikFalso.Aprueba("PK-OTRA");

        await CobrarAsync(otra, Solicitud("tok_abono_7", intento: 2));

        Assert.Empty(otra.Peticiones);
        var datos = Assert.IsType<AbonoCreditoCobradoData>(Assert.Single(_outbox.Eventos).Data);
        Assert.Equal("PK-UNA-VEZ", datos.AutorizacionId);
    }

    [Fact]
    public async Task Un_token_de_otro_cliente_se_rechaza_sin_llamar_al_proveedor()
    {
        await SembrarTokenAsync("tok_ajeno", clienteId: "cliente2@sgo.local");
        var payklik = ClientePayKlikFalso.Aprueba();

        var pago = await CobrarAsync(payklik, Solicitud("tok_ajeno"));

        Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        Assert.Empty(payklik.Peticiones);
        var datos = Assert.IsType<AbonoCreditoRechazadoData>(Assert.Single(_outbox.Eventos).Data);
        Assert.Equal(CodigosToken.Invalido, datos.CodigoRespuesta);
    }

    [Fact]
    public async Task Un_abono_nunca_emite_eventos_de_orden()
    {
        await SembrarTokenAsync("tok_a");
        await SembrarTokenAsync("tok_b");
        await SembrarTokenAsync("tok_c");

        await CobrarAsync(ClientePayKlikFalso.Aprueba(), Solicitud("tok_a"));
        await CobrarAsync(ClientePayKlikFalso.Rechaza(), new CobroAbonoCreditoSolicitadoData(
            "1f3c2a9d8e7b4c6a9b1d2e3f4a5b6c7d", "ABO-2026-000002", Cliente, "GTQ", 100m, "tok_b", 1, DateTime.UtcNow));
        await CobrarAsync(ClientePayKlikFalso.NoDisponible(), new CobroAbonoCreditoSolicitadoData(
            "2f3c2a9d8e7b4c6a9b1d2e3f4a5b6c7d", "ABO-2026-000003", Cliente, "GTQ", 100m, "tok_c", 1, DateTime.UtcNow));

        Assert.Equal(3, _outbox.Eventos.Count);
        Assert.DoesNotContain(_outbox.Eventos, e => EventosDeOrden.Contains(e.EventType));
    }

    [Fact]
    public async Task Las_consultas_de_pagos_por_orden_no_ven_los_abonos()
    {
        await SembrarTokenAsync("tok_d");
        var pagoAbono = await CobrarAsync(ClientePayKlikFalso.Aprueba(), Solicitud("tok_d"));

        // El cobro de una orden con el mismo identificador no reutiliza el pago del abono.
        await using var contexto = Contexto();
        contexto.TokensPago.Add(BovedaPruebas.TokenNuevo("tok_orden", clienteId: Cliente));
        await contexto.SaveChangesAsync();
        var pagoOrden = await Servicio(contexto, ClientePayKlikFalso.Aprueba()).ProcesarCobroAsync(
            new SolicitudCobro(Referencia, Cliente, Canal.WEB, Moneda.GTQ, 400m, MedioPago.TARJETA, "tok_orden", null, null),
            CancellationToken.None);

        Assert.Equal(ConceptoPago.ORDEN, pagoOrden.Concepto);
        Assert.NotEqual(pagoAbono.Id, pagoOrden.Id);
    }
}
