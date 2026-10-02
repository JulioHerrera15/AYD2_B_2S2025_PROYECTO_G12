using System.Collections;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.BuildingBlocks.Serialization;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.PayKlik;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

/// <summary>
/// Pruebas del cobro con tokenizacion interna (ADR-010, RES-08, EV-25, A4). Cubren:
/// que un cobro tokenizado no deja numero de tarjeta ni CVV en claro en lo que Pagos
/// persiste ni en lo que publica; que los datos que PayKlik no acepta se rechazan sin
/// contar como falla del proveedor; y que un cobro con TARJETA sin token se rechaza
/// antes de llamar a PayKlik.
/// </summary>
public sealed class CobroTokenizadoTests
{
    private const string Pan = BovedaPruebas.PanVisa;
    private const string Cvv = BovedaPruebas.Cvv;
    private const int MesExpiracion = 12;

    private static readonly int AnioExpiracion = DateTime.UtcNow.Year + 3;

    private static SolicitudCobro Solicitud(
        string ordenId,
        string? token,
        MedioPago medioPago = MedioPago.TARJETA) => new(
        ordenId,
        "CLI-000045",
        Canal.WEB,
        Moneda.GTQ,
        1400.00m,
        medioPago,
        TokenMedioPago: token,
        // Ordenes no conoce ultimos cuatro ni marca: los resuelve Pagos desde el token.
        UltimosCuatro: null,
        Marca: null);

    private sealed class Escenario
    {
        private readonly string _baseDatos = $"pagos-{Guid.NewGuid()}";

        public OutboxWriterFalso Outbox { get; } = new();

        public PagosDbContext NuevoContexto() => new(
            new DbContextOptionsBuilder<PagosDbContext>()
                .UseInMemoryDatabase(_baseDatos)
                .Options);

        /// <summary>Guarda en la boveda tokens vigentes de la tarjeta de prueba y devuelve sus ids.</summary>
        public string[] SembrarTokens(params string[] ids)
        {
            using var contexto = NuevoContexto();
            contexto.TokensPago.AddRange(ids.Select(id => BovedaPruebas.TokenNuevo(id, anioExp: AnioExpiracion)));
            contexto.SaveChanges();
            return ids;
        }

        public ServicioCobro Servicio(PagosDbContext contexto, IClientePayKlik payklik, PayKlikOptions? opciones = null) => new(
            contexto,
            payklik,
            Outbox,
            Options.Create(opciones ?? new PayKlikOptions { MaxReintentos = 0, BackoffBaseMs = 0 }),
            Options.Create(new ReintentoOptions { ExpiracionMinutos = 60 }),
            BovedaPruebas.Cifrador,
            NullLogger<ServicioCobro>.Instance);
    }

    // ------------------------------------------------------------------
    // Un cobro tokenizado persiste token, ultimos cuatro y marca
    // ------------------------------------------------------------------

    [Fact]
    public async Task Un_cobro_tokenizado_persiste_token_ultimos_cuatro_y_marca_de_la_tarjeta_real()
    {
        var escenario = new Escenario();
        var token = escenario.SembrarTokens("tok_cobro_feliz")[0];
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, payklik)
                .ProcesarCobroAsync(Solicitud("ORD-2026-000001", token), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();
        var intento = Assert.Single(pago.Intentos);

        Assert.Equal(EstadoPago.APLICADO, pago.Estado);
        Assert.Equal(token, intento.TokenMedioPago);
        Assert.Equal("4242", intento.UltimosCuatro);
        Assert.Equal("VISA", intento.Marca);

        // Hacia PayKlik viajaron los datos descifrados en memoria (A4), con el
        // titular y el vencimiento que guardo el token.
        var peticion = Assert.Single(payklik.Peticiones);
        Assert.Equal(Pan, peticion.Tarjeta!.Numero);
        Assert.Equal(Cvv, peticion.Tarjeta.Cvv);
        Assert.Equal(MesExpiracion, peticion.Tarjeta.MesExp);
        Assert.Null(peticion.ReferenciaTarjeta);
        Assert.DoesNotContain(Pan, peticion.ToString());

        // El token quedo CONSUMIDO y sin datos cifrados.
        var tokenPersistido = await verificacion.TokensPago.SingleAsync(t => t.Id == token);
        Assert.Equal(EstadoTokenPago.CONSUMIDO, tokenPersistido.Estado);
        Assert.Null(tokenPersistido.DatosCifrados);

        var data = Assert.IsType<PagoAplicadoData>(Assert.Single(escenario.Outbox.PorTipo(PagoAplicado.EventType)).Data);
        Assert.Equal("4242", data.UltimosCuatro);
        Assert.Equal("VISA", data.Marca);
    }

    // ------------------------------------------------------------------
    // Prueba de no persistencia: falla si alguien agrega un campo con la tarjeta
    // ------------------------------------------------------------------

    [Fact]
    public async Task Un_cobro_tokenizado_no_deja_PAN_ni_CVV_en_la_entidad_persistida_ni_en_el_payload_del_outbox()
    {
        var escenario = new Escenario();
        var token = escenario.SembrarTokens("tok_sin_pan")[0];

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClientePayKlikFalso.ApruebaComoPayKlik())
                .ProcesarCobroAsync(Solicitud("ORD-2026-000001", token), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        // Lo persistido, leido de nuevo desde la base: el pago con sus intentos y los enlaces.
        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();

        var propiedadesPersistidas = RecorrerObjeto(pago).ToList();
        Assert.NotEmpty(propiedadesPersistidas);
        VerificarSinDatosDeTarjeta("entidad persistida", propiedadesPersistidas, token);

        // Lo publicado: el payload de cada fila de outbox, serializado como lo hace el
        // escritor real, recorrido en todos sus niveles.
        Assert.NotEmpty(escenario.Outbox.Eventos);
        foreach (var evento in escenario.Outbox.Eventos)
        {
            var json = JsonSerializer.Serialize(evento.Data, evento.Data.GetType(), SgoJsonSerializerOptions.Default);
            var propiedadesPublicadas = RecorrerJson(JsonDocument.Parse(json).RootElement, "$").ToList();
            Assert.NotEmpty(propiedadesPublicadas);
            VerificarSinDatosDeTarjeta($"payload de {evento.EventType}", propiedadesPublicadas, token);
        }

        // Y la boveda transitoria: tras el cobro el token ya no conserva nada que
        // sirva para reconstruir la tarjeta.
        var tokenPersistido = await verificacion.TokensPago.SingleAsync();
        VerificarSinDatosDeTarjeta("token de la boveda", RecorrerObjeto(tokenPersistido).ToList(), token);
        Assert.Null(tokenPersistido.DatosCifrados);
    }

    [Fact]
    public void Un_token_vigente_solo_guarda_PAN_y_CVV_cifrados_nunca_en_claro()
    {
        var token = BovedaPruebas.TokenNuevo("tok_cifrado", anioExp: AnioExpiracion);

        Assert.NotNull(token.DatosCifrados);
        var crudo = Encoding.UTF8.GetString(token.DatosCifrados!);
        Assert.DoesNotContain(Pan, crudo);
        Assert.False(ContienePan(crudo));
        Assert.DoesNotContain(Pan, Convert.ToHexString(token.DatosCifrados!), StringComparison.OrdinalIgnoreCase);
        VerificarSinDatosDeTarjeta("token de la boveda", RecorrerObjeto(token).ToList(), token.Id);
    }

    [Fact]
    public async Task El_modelo_de_persistencia_de_Pagos_no_tiene_columnas_para_PAN_CVV_ni_vencimiento()
    {
        await using var contexto = new Escenario().NuevoContexto();

        var tablasRevisadas = 0;
        foreach (var entidad in contexto.Model.GetEntityTypes())
        {
            tablasRevisadas++;
            foreach (var propiedad in entidad.GetProperties())
            {
                var columna = propiedad.FindAnnotation("Relational:ColumnName")?.Value as string;

                Assert.False(
                    EsNombreProhibido(propiedad.Name),
                    $"{entidad.DisplayName()}.{propiedad.Name} parece un dato de tarjeta y no puede persistirse.");
                Assert.False(
                    columna is not null && EsNombreProhibido(columna),
                    $"La columna {columna} de {entidad.DisplayName()} parece un dato de tarjeta y no puede persistirse.");
            }
        }

        Assert.True(tablasRevisadas >= 3, "el modelo debe incluir al menos pago, intento_pago y enlace_reintento");

        // Y la entidad del intento conserva exactamente lo que ADR-010 permite guardar de la tarjeta.
        var intento = contexto.Model.FindEntityType(typeof(IntentoPago))!;
        Assert.NotNull(intento.FindProperty(nameof(IntentoPago.TokenMedioPago)));
        Assert.NotNull(intento.FindProperty(nameof(IntentoPago.UltimosCuatro)));
        Assert.NotNull(intento.FindProperty(nameof(IntentoPago.Marca)));
        Assert.Equal(4, intento.FindProperty(nameof(IntentoPago.UltimosCuatro))!.GetMaxLength());
    }

    [Fact]
    public void Los_datos_del_evento_de_solicitud_de_cobro_y_de_resultado_no_tienen_campos_de_tarjeta()
    {
        // PeticionCargoPayKlik queda fuera a proposito: desde A4 lleva los datos de la
        // tarjeta hacia PayKlik, descifrados solo en memoria durante el cobro, y su
        // ToString no los imprime (ver la prueba del cobro feliz).
        var tipos = new[]
        {
            typeof(SgoLogic.Contracts.Eventos.Ordenes.CobroSolicitadoData),
            typeof(PagoAplicadoData), typeof(PagoRechazadoData), typeof(PagoPendienteData),
            typeof(SolicitudCobro), typeof(ResultadoPayKlik),
            typeof(Pago), typeof(IntentoPago), typeof(EnlaceReintento), typeof(TokenPago)
        };

        foreach (var tipo in tipos)
        {
            foreach (var propiedad in tipo.GetProperties())
            {
                Assert.False(
                    EsNombreProhibido(propiedad.Name),
                    $"{tipo.Name}.{propiedad.Name} parece un dato de tarjeta y no puede viajar ni persistirse.");
            }
        }
    }

    [Fact]
    public void El_detector_de_la_prueba_reconoce_un_PAN_y_un_CVV_escondidos_bajo_cualquier_nombre()
    {
        // Sin esto, la prueba de no persistencia podria pasar porque el detector no detecta nada.
        Assert.True(ContienePan(Pan));
        Assert.True(ContienePan("tarjeta " + Pan + " vence pronto"));
        Assert.True(ContienePan("4242 4242 4242 4242"));
        Assert.True(ContienePan("4242-4242-4242-4242"));
        Assert.False(ContienePan("4242"));
        Assert.False(ContienePan("PK-TEST-000001"));
        Assert.False(ContienePan("tok_pk_0123456789abcdef01234567"));
        Assert.True(EsNombreProhibido("NumeroTarjeta"));
        Assert.True(EsNombreProhibido("cvv"));
        Assert.True(EsNombreProhibido("PanTarjeta"));
        Assert.True(EsNombreProhibido("FechaVencimiento"));
        Assert.False(EsNombreProhibido("UltimosCuatro"));
        Assert.False(EsNombreProhibido("TokenMedioPago"));
        Assert.False(EsNombreProhibido("Company"));
    }

    // ------------------------------------------------------------------
    // Datos no aceptados por PayKlik: rechazo distinguible, sin reintento, sin disyuntor
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(CodigosToken.TarjetaInvalida)]
    [InlineData(CodigosToken.CvvInvalido)]
    [InlineData(CodigosToken.TarjetaVencida)]
    public async Task Datos_que_PayKlik_no_acepta_rechazan_el_pago_con_su_codigo_sin_reintentar_y_sin_generar_enlace(string codigo)
    {
        var escenario = new Escenario();
        var token = escenario.SembrarTokens("tok_pk_x")[0];
        var payklik = ClientePayKlikFalso.TokenNoValido(codigo);

        await using (var contexto = escenario.NuevoContexto())
        {
            // MaxReintentos = 2: un medio de pago rechazado igual no debe reintentarse.
            await escenario.Servicio(contexto, payklik, new PayKlikOptions { MaxReintentos = 2, BackoffBaseMs = 0 })
                .ProcesarCobroAsync(Solicitud("ORD-2026-000001", token), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        Assert.Single(payklik.Peticiones);

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();
        var intento = Assert.Single(pago.Intentos);

        Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        Assert.Equal(EstadoIntentoPago.RECHAZADO, intento.Estado);
        Assert.Equal(codigo, intento.CodigoRespuesta);
        Assert.Empty(await verificacion.EnlacesReintento.ToListAsync());

        var evento = Assert.Single(escenario.Outbox.PorTipo(PagoRechazado.EventType));
        Assert.Equal(codigo, Assert.IsType<PagoRechazadoData>(evento.Data).CodigoRespuesta);
        Assert.Empty(escenario.Outbox.PorTipo(PagoPendiente.EventType));
    }

    [Fact]
    public async Task Un_rechazo_por_fondos_y_una_tarjeta_invalida_se_distinguen_por_el_codigo_de_respuesta()
    {
        var escenario = new Escenario();
        escenario.SembrarTokens("tok_pk_a", "tok_pk_b");

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClientePayKlikFalso.Rechaza())
                .ProcesarCobroAsync(Solicitud("ORD-2026-000001", "tok_pk_a"), CancellationToken.None);
            await escenario.Servicio(contexto, ClientePayKlikFalso.TokenNoValido(CodigosToken.TarjetaInvalida))
                .ProcesarCobroAsync(Solicitud("ORD-2026-000002", "tok_pk_b"), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        var codigos = escenario.Outbox.PorTipo(PagoRechazado.EventType)
            .Select(e => Assert.IsType<PagoRechazadoData>(e.Data))
            .ToDictionary(d => d.OrdenId, d => d.CodigoRespuesta);

        Assert.Equal("51", codigos["ORD-2026-000001"]);
        Assert.Equal(CodigosToken.TarjetaInvalida, codigos["ORD-2026-000002"]);
    }

    [Fact]
    public async Task Un_token_ya_usado_no_se_puede_cobrar_en_otra_orden()
    {
        var escenario = new Escenario();
        var token = escenario.SembrarTokens("tok_una_vez")[0];
        var payklik = ClientePayKlikFalso.ApruebaComoPayKlik();

        await using (var contexto = escenario.NuevoContexto())
        {
            var servicio = escenario.Servicio(contexto, payklik);
            var primero = await servicio.ProcesarCobroAsync(Solicitud("ORD-2026-000001", token), CancellationToken.None);
            var segundo = await servicio.ProcesarCobroAsync(Solicitud("ORD-2026-000002", token), CancellationToken.None);
            await contexto.SaveChangesAsync();

            Assert.Equal(EstadoPago.APLICADO, primero.Estado);
            Assert.Equal(EstadoPago.RECHAZADO, segundo.Estado);
            Assert.Equal(CodigosToken.YaUsado, Assert.Single(segundo.Intentos).CodigoRespuesta);
        }

        // El segundo cobro se rechazo en Pagos, sin volver a llamar a PayKlik.
        Assert.Single(payklik.Peticiones);
    }

    [Fact]
    public async Task Una_tarjeta_que_PayKlik_rechaza_no_abre_el_disyuntor_ni_cuenta_como_falla_del_proveedor()
    {
        var disyuntor = NuevoDisyuntor();
        var manejador = new ManejadorHttpFalso(_ => RespuestaTokenNoValido(CodigosToken.TarjetaInvalida));
        var escenario = new Escenario();
        escenario.SembrarTokens(Enumerable.Range(1, 10).Select(i => $"tok_invalido_{i}").ToArray());

        await using var contexto = escenario.NuevoContexto();
        var servicio = escenario.Servicio(
            contexto,
            ClienteReal(manejador, disyuntor),
            new PayKlikOptions { MaxReintentos = 1, BackoffBaseMs = 0 });

        // Diez cobros con token invalido: muy por encima del minimo de llamadas (3) que
        // abre el circuito cuando lo que falla es el proveedor.
        for (var i = 1; i <= 10; i++)
        {
            var pago = await servicio.ProcesarCobroAsync(
                Solicitud($"ORD-2026-{i:D6}", $"tok_invalido_{i}"), CancellationToken.None);
            Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        }

        await contexto.SaveChangesAsync();

        var salud = disyuntor.Salud();
        Assert.Equal("CERRADO", salud.Estado);
        Assert.Equal(0, salud.FallosRecientes);
        Assert.Null(salud.UltimaApertura);

        // Una llamada por cobro: ningun reintento tras el 422.
        Assert.Equal(10, manejador.Llamadas);
    }

    [Fact]
    public async Task Control_las_caidas_del_proveedor_si_abren_el_disyuntor_con_el_mismo_montaje()
    {
        // Mismo montaje que la prueba anterior, pero el proveedor responde 503. Demuestra
        // que la prueba de arriba tiene dientes: si la tarjeta rechazada contara como falla,
        // el circuito estaria abierto.
        var disyuntor = NuevoDisyuntor();
        var manejador = new ManejadorHttpFalso(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var escenario = new Escenario();
        escenario.SembrarTokens("tok_caida_1", "tok_caida_2", "tok_caida_3");

        await using var contexto = escenario.NuevoContexto();
        var servicio = escenario.Servicio(
            contexto,
            ClienteReal(manejador, disyuntor),
            new PayKlikOptions { MaxReintentos = 1, BackoffBaseMs = 0 });

        for (var i = 1; i <= 3; i++)
        {
            await servicio.ProcesarCobroAsync(Solicitud($"ORD-2026-{i:D6}", $"tok_caida_{i}"), CancellationToken.None);
        }

        Assert.Equal("ABIERTO", disyuntor.Salud().Estado);
    }

    // ------------------------------------------------------------------
    // TARJETA sin token se rechaza antes de llamar a PayKlik
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Un_cobro_con_TARJETA_y_sin_token_se_rechaza_sin_llamar_a_PayKlik(string? tokenAusente)
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.Aprueba();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, payklik, new PayKlikOptions { MaxReintentos = 1, BackoffBaseMs = 0 })
                .ProcesarCobroAsync(Solicitud("ORD-2026-000001", tokenAusente), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        Assert.Empty(payklik.Peticiones);

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();
        var intento = Assert.Single(pago.Intentos);

        Assert.Equal(EstadoPago.RECHAZADO, pago.Estado);
        Assert.Equal(EstadoIntentoPago.RECHAZADO, intento.Estado);
        Assert.Equal(CodigosToken.Requerido, intento.CodigoRespuesta);
        Assert.Contains("token", intento.MensajeRespuesta, StringComparison.OrdinalIgnoreCase);
        Assert.Null(intento.TokenMedioPago);
        Assert.Empty(await verificacion.EnlacesReintento.ToListAsync());

        var evento = Assert.Single(escenario.Outbox.PorTipo(PagoRechazado.EventType));
        Assert.Equal(CodigosToken.Requerido, Assert.IsType<PagoRechazadoData>(evento.Data).CodigoRespuesta);
    }

    [Theory]
    [InlineData(MedioPago.TRANSFERENCIA)]
    [InlineData(MedioPago.BILLETERA_DIGITAL)]
    public async Task Un_medio_de_pago_que_no_es_tarjeta_no_exige_token(MedioPago medioPago)
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.Aprueba();

        await using var contexto = escenario.NuevoContexto();
        var pago = await escenario.Servicio(contexto, payklik)
            .ProcesarCobroAsync(Solicitud("ORD-2026-000001", token: null, medioPago), CancellationToken.None);

        Assert.Equal(EstadoPago.APLICADO, pago.Estado);
        Assert.Single(payklik.Peticiones);
    }

    // ------------------------------------------------------------------
    // Utilidades: recorrido de objetos y JSON, y detector de datos de tarjeta
    // ------------------------------------------------------------------

    private sealed record Hoja(string Ruta, string Nombre, string? Valor);

    private static DisyuntorPayKlik NuevoDisyuntor() => new(Options.Create(new PayKlikOptions
    {
        VentanaSegundos = 30,
        MinimoLlamadas = 3,
        RatioFallo = 0.5,
        AperturaSegundos = 30
    }));

    private static IClientePayKlik ClienteReal(ManejadorHttpFalso manejador, DisyuntorPayKlik disyuntor) =>
        new ClientePayKlikConDisyuntor(
            new ClientePayKlikHttp(new HttpClient(manejador) { BaseAddress = new Uri("http://payklik.test") }),
            disyuntor);

    private static HttpResponseMessage RespuestaTokenNoValido(string codigo) => new(HttpStatusCode.UnprocessableEntity)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                title = "Token de medio de pago no valido",
                detail = "El token de medio de pago no existe.",
                status = 422,
                codigo
            }),
            Encoding.UTF8,
            "application/problem+json")
    };

    private static IEnumerable<Hoja> RecorrerObjeto(object? objeto, string ruta = "$", HashSet<object>? visitados = null)
    {
        visitados ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

        if (objeto is null)
        {
            yield break;
        }

        var tipo = objeto.GetType();
        if (objeto is string || tipo.IsPrimitive || tipo.IsEnum || objeto is decimal || objeto is Guid ||
            objeto is DateTime || objeto is DateTimeOffset || objeto is TimeSpan)
        {
            yield return new Hoja(ruta, ruta[(ruta.LastIndexOf('.') + 1)..], Convert.ToString(objeto, System.Globalization.CultureInfo.InvariantCulture));
            yield break;
        }

        if (!visitados.Add(objeto))
        {
            yield break;
        }

        if (objeto is IEnumerable coleccion)
        {
            var indice = 0;
            foreach (var elemento in coleccion)
            {
                foreach (var hoja in RecorrerObjeto(elemento, $"{ruta}[{indice++}]", visitados))
                {
                    yield return hoja;
                }
            }

            yield break;
        }

        foreach (var propiedad in tipo.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (propiedad.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var valor = propiedad.GetValue(objeto);
            var rutaPropiedad = $"{ruta}.{propiedad.Name}";

            if (valor is null)
            {
                yield return new Hoja(rutaPropiedad, propiedad.Name, null);
                continue;
            }

            foreach (var hoja in RecorrerObjeto(valor, rutaPropiedad, visitados))
            {
                yield return hoja;
            }
        }
    }

    private static IEnumerable<Hoja> RecorrerJson(JsonElement elemento, string ruta)
    {
        switch (elemento.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var propiedad in elemento.EnumerateObject())
                {
                    var rutaPropiedad = $"{ruta}.{propiedad.Name}";
                    if (propiedad.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        // El nombre de un objeto o arreglo tambien cuenta: no debe ser de tarjeta.
                        yield return new Hoja(rutaPropiedad, propiedad.Name, null);
                    }

                    foreach (var hoja in RecorrerJson(propiedad.Value, rutaPropiedad))
                    {
                        yield return hoja;
                    }
                }

                break;

            case JsonValueKind.Array:
                var indice = 0;
                foreach (var item in elemento.EnumerateArray())
                {
                    foreach (var hoja in RecorrerJson(item, $"{ruta}[{indice++}]"))
                    {
                        yield return hoja;
                    }
                }

                break;

            default:
                yield return new Hoja(ruta, ruta[(ruta.LastIndexOf('.') + 1)..], elemento.ToString());
                break;
        }
    }

    private static void VerificarSinDatosDeTarjeta(string donde, IReadOnlyList<Hoja> hojas, string tokenLegitimo)
    {
        foreach (var hoja in hojas)
        {
            Assert.False(
                EsNombreProhibido(hoja.Nombre),
                $"En {donde}, el campo {hoja.Ruta} parece un dato de tarjeta y no puede persistirse ni publicarse.");

            if (hoja.Valor is null || hoja.Valor == tokenLegitimo)
            {
                continue;
            }

            Assert.False(
                ContienePan(hoja.Valor) || hoja.Valor.Contains(Pan, StringComparison.Ordinal),
                $"En {donde}, el campo {hoja.Ruta} contiene un numero de tarjeta.");
            Assert.False(
                hoja.Valor == Cvv,
                $"En {donde}, el campo {hoja.Ruta} contiene el CVV.");
            Assert.False(
                hoja.Valor == $"{MesExpiracion}/{AnioExpiracion}" ||
                hoja.Valor == $"{MesExpiracion:D2}/{AnioExpiracion % 100:D2}" ||
                hoja.Valor == $"{MesExpiracion:D2}/{AnioExpiracion}",
                $"En {donde}, el campo {hoja.Ruta} contiene la fecha de vencimiento de la tarjeta.");
        }
    }

    private static readonly string[] FragmentosProhibidos =
    {
        "numerotarjeta", "cardnumber", "numerodetarjeta", "vencimiento", "expiracion", "expiry", "expirationdate"
    };

    private static readonly HashSet<string> PalabrasProhibidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "pan", "cvv", "cvc", "cvv2"
    };

    /// <summary>
    /// Un nombre de campo o columna es de tarjeta si contiene una palabra como PAN o
    /// CVV (separando camelCase y guiones bajos, para no marcar "Company") o un
    /// fragmento como "numerotarjeta" o "vencimiento".
    /// </summary>
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

    // Una corrida de 13 a 19 digitos (con espacios o guiones como separador), sin letras ni
    // digitos pegados a los lados, que ademas pase Luhn: el formato de un PAN.
    private static readonly Regex PosiblePan = new(@"(?<![0-9A-Za-z])\d(?:[ -]?\d){12,18}(?![0-9A-Za-z])", RegexOptions.Compiled);

    private static bool ContienePan(string valor) =>
        PosiblePan.Matches(valor)
            .Select(m => m.Value.Replace(" ", string.Empty).Replace("-", string.Empty))
            .Any(ValidadorTarjeta.PasaLuhn);
}
