using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Pagos.Api.Application.Pagos;
using SgoLogic.Pagos.Api.Domain;
using SgoLogic.Pagos.Api.Infrastructure.Persistencia;
using SgoLogic.Pagos.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Pagos.Tests.Application;

public sealed class ServicioReembolsoTests
{
    private const string OrdenId = "ORD-2026-000001";
    private const string DevolucionId = "DEV-2026-000001";

    private static DevolucionRegistradaData DatosDevolucion() => new(
        DevolucionId: DevolucionId,
        OrdenOriginalId: OrdenId,
        ClienteId: "CLI-000045",
        Tipo: TipoDevolucion.DEVOLUCION,
        EsParcial: false,
        UbicacionReingresoId: "UBI-01",
        Lineas: new[]
        {
            new DevolucionLineaData("SKU-LB-00871", 1, "Producto defectuoso", null)
        },
        Moneda: "GTQ",
        MontoReembolso: "1400.00",
        PagoOriginalId: "PK-AUTH-001",
        DocumentoFiscalOriginalId: string.Empty,
        RegistradaPor: "admin@sgo.com",
        RegistradaEn: DateTime.UtcNow);

    private sealed class Escenario
    {
        private readonly string _baseDatos = $"pagos-reembolso-{Guid.NewGuid()}";

        public OutboxWriterFalso Outbox { get; } = new();

        public PagosDbContext NuevoContexto() => new(
            new DbContextOptionsBuilder<PagosDbContext>()
                .UseInMemoryDatabase(_baseDatos)
                .Options);

        public ServicioReembolso Servicio(
            PagosDbContext contexto,
            IClientePayKlik payklik) => new(
            contexto,
            payklik,
            Outbox,
            NullLogger<ServicioReembolso>.Instance);
    }

    [Fact]
    public async Task Reembolso_registrado_antes_de_invocar_al_proveedor()
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.Aprueba();

        await using (var contexto = escenario.NuevoContexto())
        {
            var intentosAlInvocar = -1;
            payklik.AntesDeResponder = () => intentosAlInvocar = contexto.ChangeTracker
                .Entries<IntentoPago>()
                .Count(entrada => entrada.Entity.Estado == EstadoIntentoPago.INICIADO);

            await escenario.Servicio(contexto, payklik)
                .ProcesarReembolsoAsync(DatosDevolucion(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();

        Assert.Equal(EstadoPago.INICIADO, pago.Estado);
        var intento = Assert.Single(pago.Intentos);
        Assert.Equal(EstadoIntentoPago.EXITOSO, intento.Estado);
        Assert.Equal("PK-REEMBOLSO-000001", intento.AutorizacionId);

        var evento = Assert.Single(escenario.Outbox.PorTipo(ReembolsoAplicado.EventType));
        Assert.Equal(ReembolsoAplicado.RoutingKey, evento.RoutingKey);
        var data = Assert.IsType<ReembolsoAplicadoData>(evento.Data);
        Assert.Equal(DevolucionId, data.DevolucionId);
        Assert.Equal(OrdenId, data.OrdenOriginalId);
        Assert.Equal(1400.00m, data.Monto);
    }

    [Fact]
    public async Task Reembolso_fallido_emite_evento()
    {
        var escenario = new Escenario();
        var payklik = ClientePayKlikFalso.Rechaza();
        payklik.ComportamientoReembolso = _ => Task.FromResult(new ResultadoPayKlik(
            null,
            EstadoRespuestaPayKlik.RECHAZADO,
            "51",
            "Fondos insuficientes para reembolso",
            null,
            null,
            DateTime.UtcNow));

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, payklik)
                .ProcesarReembolsoAsync(DatosDevolucion(), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var pago = await verificacion.Pagos.Include(p => p.Intentos).SingleAsync();

        Assert.Equal(EstadoPago.INICIADO, pago.Estado);
        var intento = Assert.Single(pago.Intentos);
        Assert.Equal(EstadoIntentoPago.RECHAZADO, intento.Estado);
        Assert.Equal("51", intento.CodigoRespuesta);

        var evento = Assert.Single(escenario.Outbox.PorTipo(ReembolsoFallido.EventType));
        Assert.Equal(ReembolsoFallido.RoutingKey, evento.RoutingKey);
        var data = Assert.IsType<ReembolsoFallidoData>(evento.Data);
        Assert.Equal(DevolucionId, data.DevolucionId);
        Assert.Equal(OrdenId, data.OrdenOriginalId);
        Assert.Equal("51", data.CodigoRespuesta);
    }
}
