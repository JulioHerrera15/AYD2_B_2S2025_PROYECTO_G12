using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Facturacion.Api.Application.Facturacion;
using SgoLogic.Facturacion.Api.Domain;
using SgoLogic.Facturacion.Api.Infrastructure.Persistencia;
using SgoLogic.Facturacion.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Facturacion.Tests.Application;

public sealed class NotaCreditoTests
{
    private const string OrdenId = "ORD-2026-000123";
    private const string DevolucionId = "DEV-2026-000001";
    private const string TransactionIdDevolucion = "txn-devolucion-001";
    private static readonly DateTime T0 = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static CertificacionFiscalSolicitadaData SolicitudFactura() => new(
        OrdenId,
        TipoDocumentoFiscal.FACTURA,
        DocumentoOrigenId: null,
        Receptor: new ReceptorFiscalData(
            "2547896320101", "Angel David Martinez Garcia", "5a avenida 3-21 zona 2", Pais.GT),
        Moneda: Moneda.GTQ,
        Subtotal: 1250.00m,
        Impuestos: 150.00m,
        Total: 1400.00m,
        Lineas: new[]
        {
            new CertificacionFiscalLineaData("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m, 150.00m)
        },
        SolicitadaEn: T0);

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
        RegistradaEn: T0);

    private sealed class Escenario
    {
        private readonly string _baseDatos = $"facturacion-nc-{Guid.NewGuid()}";

        public OutboxWriterFalso Outbox { get; } = new();

        public GeneradorCorrelativoFiscalFalso Correlativo { get; } = new(inicial: 600);

        public FacturacionDbContext NuevoContexto() => new(
            new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(_baseDatos)
                .Options);

        public ServicioFacturacion Servicio(
            FacturacionDbContext contexto,
            IClienteSat sat) => new(
            contexto,
            sat,
            Outbox,
            Correlativo,
            new GeneradorXmlFiscal(),
            Options.Create(new SatOptions { Serie = "A", MaxIntentos = 4, BackoffBaseMs = 0 }),
            NullLogger<ServicioFacturacion>.Instance);
    }

    [Fact]
    public async Task Nota_de_credito_se_genera_ligada_a_la_venta_original()
    {
        var escenario = new Escenario();
        Guid facturaId;

        await using (var contexto = escenario.NuevoContexto())
        {
            var factura = await escenario.Servicio(contexto, ClienteSatFalso.Certifica())
                .CrearDocumentoAsync(SolicitudFactura(), "txn-factura-1", T0, CancellationToken.None);
            await contexto.SaveChangesAsync();
            facturaId = factura.Id;
        }

        await using (var contexto = escenario.NuevoContexto())
        {
            var nc = await escenario.Servicio(contexto, ClienteSatFalso.Certifica())
                .CrearNotaCreditoAsync(DatosDevolucion(), TransactionIdDevolucion, T0.AddMinutes(1), CancellationToken.None);
            await contexto.SaveChangesAsync();

            Assert.Equal(TipoDocumentoFiscal.NOTA_CREDITO, nc.TipoDocumento);
            Assert.Equal(facturaId.ToString(), nc.DocumentoOrigenId);
            Assert.Equal("A", nc.Serie);
            Assert.Equal(1400.00m, nc.Total);
        }

        await using var verificacion = escenario.NuevoContexto();
        var documentos = await verificacion.DocumentosFiscales.Include(d => d.Lineas).ToListAsync();
        Assert.Equal(2, documentos.Count);

        var facturaDoc = documentos.Single(d => d.TipoDocumento == TipoDocumentoFiscal.FACTURA);
        var ncDoc = documentos.Single(d => d.TipoDocumento == TipoDocumentoFiscal.NOTA_CREDITO);

        Assert.Equal(facturaDoc.Id.ToString(), ncDoc.DocumentoOrigenId);
        Assert.Equal(facturaDoc.Serie, ncDoc.Serie);
        Assert.Single(ncDoc.Lineas);
        Assert.Equal("SKU-LB-00871", ncDoc.Lineas[0].Sku);
        Assert.Equal(1, ncDoc.Lineas[0].Cantidad);
    }
}
