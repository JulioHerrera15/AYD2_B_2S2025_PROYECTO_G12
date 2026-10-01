using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Inventario;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

/// <summary>
/// Pruebas del token del medio de pago en Ordenes (ADR-010). La orden recibe el token
/// que PayKlik emitio en el navegador, lo conserva solo hasta emitir CobroSolicitado y
/// lo suelta. El numero de tarjeta, el CVV y el vencimiento no entran a este servicio.
/// </summary>
public sealed class TokenMedioPagoOrdenTests
{
    private const string Token = "tok_pk_9f2a7c41d8e0b3a6c5d4e1f2";

    private static readonly MediosPagoPorCanalOptions MediosPago = new()
    {
        ["WEB"] = new() { MedioPago.TARJETA, MedioPago.TRANSFERENCIA, MedioPago.BILLETERA_DIGITAL }
    };

    private sealed class Escenario
    {
        public RepositorioOrdenesFalso Ordenes { get; } = new();

        public OutboxWriterFalso Outbox { get; } = new();

        public UnidadDeTrabajoFalsa Unidad { get; } = new();

        public ServicioConfirmacionOrden Servicio { get; }

        public ManejadorEventosInventario Inventario { get; }

        public Escenario()
        {
            Servicio = new ServicioConfirmacionOrden(
                Ordenes,
                Outbox,
                Unidad,
                new GeneradorCorrelativoOrdenFalso(),
                new PoliticaMediosPago(MediosPago),
                new ClienteCarritoFalso());

            Inventario = new ManejadorEventosInventario(
                new RegistroMensajesProcesadosFalso(),
                Ordenes,
                Outbox,
                Unidad,
                NullLogger<ManejadorEventosInventario>.Instance);
        }

        public async Task<OrdenDto> Crear(string medioPago, string? token) => await Servicio.CrearOrdenAsync(
            Solicitud(medioPago, token), CancellationToken.None);

        public Orden Orden(OrdenDto dto) => Ordenes.ObtenerPorIdAsync(dto.Id, CancellationToken.None).Result!;

        public async Task ReservarInventario(string ordenId) => await Inventario.ManejarAsync(
            new EventoInventarioRecibido(
                MessageId: Guid.NewGuid().ToString(),
                TransactionId: Guid.NewGuid().ToString(),
                TipoEvento: InventarioReservado.EventType,
                OrdenId: ordenId,
                Datos: new InventarioReservadoData(
                    "RSV-2026-000042",
                    ordenId,
                    "CD-01",
                    TipoUbicacion.CENTRO_DISTRIBUCION,
                    900,
                    new[] { new ReservaConfirmadaLineaData("SKU-LB-00871", 1) },
                    DateTime.UtcNow,
                    DateTime.UtcNow.AddSeconds(900))),
            CancellationToken.None);
    }

    private static CrearOrdenRequest Solicitud(string medioPago, string? token) => new(
        ClienteId: "CLI-000045",
        Canal: Canal.WEB,
        MetodoEntrega: MetodoEntrega.DOMICILIO,
        Moneda: Moneda.GTQ,
        MedioPago: medioPago,
        Impuestos: 150.00m,
        Lineas: new[] { new CrearLineaOrdenRequest("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
        TokenMedioPago: token);

    private static string Json(object data) =>
        JsonSerializer.Serialize(data, data.GetType(), SgoJsonSerializerOptions.Default);

    // ------------------------------------------------------------------
    // Validacion: TARJETA exige token; los demas medios no
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Crear_una_orden_con_TARJETA_y_sin_token_lanza_y_no_escribe_nada(string? tokenAusente)
    {
        var escenario = new Escenario();

        var excepcion = await Assert.ThrowsAsync<ExcepcionTokenMedioPagoRequerido>(
            () => escenario.Crear("TARJETA", tokenAusente));

        Assert.Equal(MedioPago.TARJETA, excepcion.MedioPago);
        Assert.Contains("tokenMedioPago", excepcion.Message);
        Assert.Empty(escenario.Outbox.Eventos);
        Assert.Equal(0, escenario.Ordenes.LlamadasAgregar);
        Assert.Equal(0, escenario.Unidad.Confirmaciones);
    }

    [Theory]
    [InlineData("TRANSFERENCIA")]
    [InlineData("BILLETERA_DIGITAL")]
    public async Task Los_medios_sin_token_se_crean_sin_exigirlo(string medioPago)
    {
        var escenario = new Escenario();

        var dto = await escenario.Crear(medioPago, token: null);

        Assert.Equal(Enum.Parse<MedioPago>(medioPago), dto.MedioPago);
        Assert.Null(escenario.Orden(dto).TokenMedioPago);
    }

    [Theory]
    [InlineData("TRANSFERENCIA")]
    [InlineData("BILLETERA_DIGITAL")]
    public async Task Un_token_enviado_con_un_medio_que_no_es_tarjeta_se_ignora_y_no_se_guarda(string medioPago)
    {
        var escenario = new Escenario();

        var dto = await escenario.Crear(medioPago, Token);

        Assert.Null(escenario.Orden(dto).TokenMedioPago);
    }

    [Fact]
    public async Task Un_token_demasiado_largo_se_rechaza_como_solicitud_invalida()
    {
        var escenario = new Escenario();

        await Assert.ThrowsAsync<ArgumentException>(
            () => escenario.Crear("TARJETA", new string('x', Orden.LongitudMaximaTokenMedioPago + 1)));

        Assert.Empty(escenario.Outbox.Eventos);
    }

    [Theory]
    [InlineData(MedioPago.TARJETA, true)]
    [InlineData(MedioPago.TRANSFERENCIA, false)]
    [InlineData(MedioPago.BILLETERA_DIGITAL, false)]
    public void La_politica_de_medios_de_pago_sabe_cual_requiere_token(MedioPago medioPago, bool esperado)
    {
        Assert.Equal(esperado, new PoliticaMediosPago(MediosPago).RequiereToken(medioPago));
    }

    // ------------------------------------------------------------------
    // La orden conserva el token solo hasta CobroSolicitado
    // ------------------------------------------------------------------

    [Fact]
    public async Task La_orden_conserva_el_token_al_crearse_pero_no_lo_devuelve_ni_lo_publica_en_OrdenCreada()
    {
        var escenario = new Escenario();

        var dto = await escenario.Crear("TARJETA", Token);

        Assert.Equal(Token, escenario.Orden(dto).TokenMedioPago);

        // Ni la respuesta HTTP ni el evento de creacion llevan el token.
        Assert.DoesNotContain("tokenMedioPago", Json(dto), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token, Json(dto));
        var creada = Assert.Single(escenario.Outbox.PorTipo(OrdenCreada.EventType));
        Assert.DoesNotContain(Token, Json(creada.Data));
        Assert.DoesNotContain("tokenMedioPago", Json(creada.Data), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Al_reservarse_el_inventario_CobroSolicitado_lleva_el_token_y_la_orden_lo_suelta()
    {
        var escenario = new Escenario();
        var dto = await escenario.Crear("TARJETA", Token);
        await escenario.Servicio.ConfirmarOrdenAsync(dto.Id, null, CancellationToken.None);

        // Antes del cobro la orden sigue guardando el token: CobroSolicitado sale
        // despues, cuando llega InventarioReservado.
        Assert.Equal(Token, escenario.Orden(dto).TokenMedioPago);

        await escenario.ReservarInventario(dto.Id);

        var cobro = Assert.IsType<CobroSolicitadoData>(
            Assert.Single(escenario.Outbox.PorTipo(CobroSolicitado.EventType)).Data);
        Assert.Equal(Token, cobro.TokenMedioPago);
        Assert.Equal(MedioPago.TARJETA, cobro.MedioPago);
        Assert.Null(cobro.UltimosCuatro);
        Assert.Null(cobro.Marca);

        var orden = escenario.Orden(dto);
        Assert.Equal(EstadoOrden.COBRO_SOLICITADO, orden.Estado);
        Assert.Null(orden.TokenMedioPago);
    }

    [Fact]
    public async Task El_token_solo_viaja_en_CobroSolicitado_y_en_ningun_otro_evento_de_la_saga()
    {
        var escenario = new Escenario();
        var dto = await escenario.Crear("TARJETA", Token);
        await escenario.Servicio.ConfirmarOrdenAsync(dto.Id, null, CancellationToken.None);
        await escenario.ReservarInventario(dto.Id);

        Assert.NotEmpty(escenario.Outbox.Eventos);
        var conToken = escenario.Outbox.Eventos
            .Where(evento => Json(evento.Data).Contains(Token, StringComparison.Ordinal))
            .Select(evento => evento.EventType)
            .ToList();

        Assert.Equal(new[] { CobroSolicitado.EventType }, conToken);
    }

    [Theory]
    [InlineData("TRANSFERENCIA")]
    [InlineData("BILLETERA_DIGITAL")]
    public async Task Un_medio_sin_token_emite_CobroSolicitado_sin_token(string medioPago)
    {
        var escenario = new Escenario();
        var dto = await escenario.Crear(medioPago, token: null);
        await escenario.Servicio.ConfirmarOrdenAsync(dto.Id, null, CancellationToken.None);
        await escenario.ReservarInventario(dto.Id);

        var cobro = Assert.IsType<CobroSolicitadoData>(
            Assert.Single(escenario.Outbox.PorTipo(CobroSolicitado.EventType)).Data);
        Assert.Null(cobro.TokenMedioPago);
    }

    [Theory]
    [InlineData(EstadoOrden.RECHAZADA)]
    [InlineData(EstadoOrden.CANCELADA)]
    public async Task Una_orden_que_termina_antes_de_cobrarse_no_deja_el_token_guardado(EstadoOrden terminal)
    {
        var escenario = new Escenario();
        var dto = await escenario.Crear("TARJETA", Token);
        await escenario.Servicio.ConfirmarOrdenAsync(dto.Id, null, CancellationToken.None);
        Assert.Equal(Token, escenario.Orden(dto).TokenMedioPago);

        await escenario.Servicio.AplicarTransicionAsync(
            dto.Id, terminal, PasoSaga.RESERVA, "Termina antes de cobrar", true, CancellationToken.None);

        Assert.Null(escenario.Orden(dto).TokenMedioPago);
        Assert.Empty(escenario.Outbox.PorTipo(CobroSolicitado.EventType));
    }

    [Fact]
    public async Task Un_segundo_CobroSolicitado_de_la_misma_orden_sale_sin_token_porque_la_orden_ya_lo_solto()
    {
        // PAGO_PENDIENTE -> COBRO_SOLICITADO es una transicion valida. El token ya
        // cruzo hacia Pagos en el primer CobroSolicitado: la orden no lo repone.
        var escenario = new Escenario();
        var dto = await escenario.Crear("TARJETA", Token);
        await escenario.Servicio.ConfirmarOrdenAsync(dto.Id, null, CancellationToken.None);
        await escenario.ReservarInventario(dto.Id);

        await escenario.Servicio.AplicarTransicionAsync(
            dto.Id, EstadoOrden.PAGO_PENDIENTE, PasoSaga.COBRO, "Sin respuesta", false, CancellationToken.None);
        await escenario.Servicio.AplicarTransicionAsync(
            dto.Id, EstadoOrden.COBRO_SOLICITADO, PasoSaga.COBRO, "Reintento", false, CancellationToken.None);

        var cobros = escenario.Outbox.PorTipo(CobroSolicitado.EventType)
            .Select(evento => Assert.IsType<CobroSolicitadoData>(evento.Data))
            .ToList();

        Assert.Equal(2, cobros.Count);
        Assert.Equal(Token, cobros[0].TokenMedioPago);
        Assert.Null(cobros[1].TokenMedioPago);
    }

    // ------------------------------------------------------------------
    // Ningun dato de tarjeta en lo que Ordenes recibe, guarda o publica
    // ------------------------------------------------------------------

    [Fact]
    public void Ni_la_solicitud_ni_la_orden_ni_sus_eventos_tienen_campos_de_PAN_CVV_o_vencimiento()
    {
        var tipos = new[]
        {
            typeof(CrearOrdenRequest), typeof(Orden), typeof(OrdenDto),
            typeof(OrdenCreadaData), typeof(OrdenEstadoCambiadoData), typeof(CobroSolicitadoData)
        };

        foreach (var tipo in tipos)
        {
            foreach (var propiedad in tipo.GetProperties())
            {
                Assert.False(
                    EsNombreProhibido(propiedad.Name),
                    $"{tipo.Name}.{propiedad.Name} parece un dato de tarjeta y no puede entrar a Ordenes.");
            }
        }

        // Lo unico de la tarjeta que Ordenes conoce es el token.
        Assert.NotNull(typeof(CrearOrdenRequest).GetProperty(nameof(CrearOrdenRequest.TokenMedioPago)));
        Assert.NotNull(typeof(Orden).GetProperty(nameof(Orden.TokenMedioPago)));
        Assert.Null(typeof(OrdenDto).GetProperty("TokenMedioPago"));
    }

    private static readonly string[] FragmentosProhibidos =
    {
        "numerotarjeta", "cardnumber", "numerodetarjeta", "vencimiento", "expiracion", "expiry", "expirationdate"
    };

    private static readonly HashSet<string> PalabrasProhibidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "pan", "cvv", "cvc", "cvv2"
    };

    private static bool EsNombreProhibido(string nombre)
    {
        var compacto = Regex.Replace(nombre, "[^A-Za-z0-9]", string.Empty).ToLowerInvariant();
        if (FragmentosProhibidos.Any(compacto.Contains))
        {
            return true;
        }

        var palabras = Regex.Split(Regex.Replace(nombre, "([a-z0-9])([A-Z])", "$1 $2"), "[^A-Za-z0-9]+");
        return palabras.Any(PalabrasProhibidas.Contains);
    }
}
