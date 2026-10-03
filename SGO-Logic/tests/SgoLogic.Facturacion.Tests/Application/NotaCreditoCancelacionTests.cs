using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Facturacion;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Facturacion.Api.Application.Facturacion;
using SgoLogic.Facturacion.Api.Domain;
using SgoLogic.Facturacion.Api.Infrastructure.Persistencia;
using SgoLogic.Facturacion.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Facturacion.Tests.Application;

/// <summary>
/// Nota de credito por cancelacion de la orden (bloque 4): con la factura certificada se
/// emite en el momento; con la certificacion en curso, en cuanto la SAT la certifique.
/// </summary>
public sealed class NotaCreditoCancelacionTests
{
    private const string OrdenId = "ORD-2026-000321";
    private const string TransactionId = "txn-orden-321";
    private static readonly DateTime T0 = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _baseDatos = $"facturacion-cancelacion-{Guid.NewGuid()}";
    private readonly OutboxWriterFalso _outbox = new();
    private readonly GeneradorCorrelativoFiscalFalso _correlativo = new(inicial: 900);

    private FacturacionDbContext Contexto() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(_baseDatos).Options);

    private ServicioFacturacion Servicio(FacturacionDbContext contexto, IClienteSat? sat = null) => new(
        contexto,
        sat ?? ClienteSatFalso.Certifica(),
        _outbox,
        _correlativo,
        new GeneradorXmlFiscal(),
        Options.Create(new SatOptions { Serie = "A", MaxIntentos = 4, BackoffBaseMs = 0 }),
        NullLogger<ServicioFacturacion>.Instance);

    private static CertificacionFiscalSolicitadaData SolicitudFactura() => new(
        OrdenId,
        TipoDocumentoFiscal.FACTURA,
        DocumentoOrigenId: null,
        Receptor: new ReceptorFiscalData("CF", "Consumidor Final", "Ciudad", Pais.GT),
        Moneda: Moneda.GTQ,
        Subtotal: 2678.57m,
        Impuestos: 321.43m,
        Total: 3000.00m,
        Lineas: new[]
        {
            new CertificacionFiscalLineaData("SKU-A", "Laptop", 1, 2500.00m, 267.86m),
            new CertificacionFiscalLineaData("SKU-B", "Mouse", 2, 250.00m, 53.57m)
        },
        SolicitadaEn: T0);

    private static OrdenCanceladaData Cancelacion(MedioPago medio = MedioPago.TARJETA) => new(
        OrdenId, "000321", "cliente1@sgo.local", null, true, MotivoCancelacion.CLIENTE, "Cancelada por el cliente",
        medio != MedioPago.CREDITO, null, null, Moneda.GTQ, 3000m, T0.AddHours(1), medio);

    private async Task<Guid> CrearFacturaAsync(bool certificar)
    {
        Guid id;
        await using (var contexto = Contexto())
        {
            var factura = await Servicio(contexto).CrearDocumentoAsync(SolicitudFactura(), TransactionId, T0, CancellationToken.None);
            await contexto.SaveChangesAsync();
            id = factura.Id;
        }

        if (certificar)
        {
            await CertificarAsync(id);
        }

        return id;
    }

    private async Task CertificarAsync(Guid documentoId)
    {
        await using var contexto = Contexto();
        await Servicio(contexto).ProcesarEnvioAsync(documentoId, 1, T0.AddMinutes(5), CancellationToken.None);
        await contexto.SaveChangesAsync();
    }

    private async Task<DocumentoFiscal?> CancelarAsync(OrdenCanceladaData datos)
    {
        await using var contexto = Contexto();
        var nota = await Servicio(contexto).ProcesarCancelacionOrdenAsync(datos, T0.AddHours(1), CancellationToken.None);
        await contexto.SaveChangesAsync();
        return nota;
    }

    private async Task<List<DocumentoFiscal>> DocumentosAsync()
    {
        await using var contexto = Contexto();
        return await contexto.DocumentosFiscales.AsNoTracking().Include(d => d.Lineas).ToListAsync();
    }

    [Theory]
    [InlineData(MedioPago.TARJETA)]
    [InlineData(MedioPago.CREDITO)]
    public async Task Con_la_factura_certificada_se_emite_la_nota_de_credito_por_el_total(MedioPago medio)
    {
        var facturaId = await CrearFacturaAsync(certificar: true);

        var nota = await CancelarAsync(Cancelacion(medio));

        Assert.NotNull(nota);
        var documentos = await DocumentosAsync();
        var factura = documentos.Single(d => d.TipoDocumento == TipoDocumentoFiscal.FACTURA);
        var nc = documentos.Single(d => d.TipoDocumento == TipoDocumentoFiscal.NOTA_CREDITO);
        Assert.Equal(facturaId.ToString(), nc.DocumentoOrigenId);
        Assert.Equal(OrdenId, nc.OrdenId);
        Assert.Equal(3000.00m, nc.Total);
        Assert.Equal(EstadoDocumentoFiscal.PENDIENTE, nc.Estado);
        Assert.Equal(new[] { ("SKU-A", 1), ("SKU-B", 2) }, nc.Lineas.OrderBy(l => l.NumeroLinea).Select(l => (l.Sku, l.Cantidad)));
        Assert.Equal(nc.Id, factura.NotaCreditoCancelacionId);
        Assert.NotNull(factura.CancelacionOrdenEn);
    }

    [Fact]
    public async Task Con_la_factura_pendiente_se_marca_y_la_nota_sale_al_certificarse()
    {
        var facturaId = await CrearFacturaAsync(certificar: false);

        var nota = await CancelarAsync(Cancelacion());

        Assert.Null(nota);
        var marcada = Assert.Single(await DocumentosAsync());
        Assert.NotNull(marcada.CancelacionOrdenEn);
        Assert.Null(marcada.NotaCreditoCancelacionId);

        await CertificarAsync(facturaId);

        var documentos = await DocumentosAsync();
        var factura = documentos.Single(d => d.TipoDocumento == TipoDocumentoFiscal.FACTURA);
        var nc = documentos.Single(d => d.TipoDocumento == TipoDocumentoFiscal.NOTA_CREDITO);
        Assert.Equal(EstadoDocumentoFiscal.CERTIFICADO, factura.Estado);
        Assert.Equal(nc.Id, factura.NotaCreditoCancelacionId);
        Assert.Equal(3000.00m, nc.Total);
        Assert.Equal(facturaId.ToString(), nc.DocumentoOrigenId);
        // La factura se certifico (y lo supo Ordenes) antes de emitir su nota.
        Assert.Single(_outbox.Eventos, e => e.EventType == DocumentoFiscalCertificado.EventType);
    }

    [Fact]
    public async Task Sin_documento_fiscal_no_hace_nada()
    {
        var nota = await CancelarAsync(Cancelacion());

        Assert.Null(nota);
        Assert.Empty(await DocumentosAsync());
        Assert.Empty(_outbox.Eventos);
    }

    [Fact]
    public async Task Cancelacion_repetida_no_emite_una_segunda_nota()
    {
        await CrearFacturaAsync(certificar: true);

        await CancelarAsync(Cancelacion());
        var segunda = await CancelarAsync(Cancelacion());

        Assert.Null(segunda);
        Assert.Single(await DocumentosAsync(), d => d.TipoDocumento == TipoDocumentoFiscal.NOTA_CREDITO);
    }

    [Fact]
    public async Task Certificar_la_nota_de_credito_no_genera_otra_nota()
    {
        await CrearFacturaAsync(certificar: true);
        await CancelarAsync(Cancelacion());
        var nc = (await DocumentosAsync()).Single(d => d.TipoDocumento == TipoDocumentoFiscal.NOTA_CREDITO);

        await CertificarAsync(nc.Id);

        var documentos = await DocumentosAsync();
        Assert.Equal(2, documentos.Count);
        Assert.Equal(EstadoDocumentoFiscal.CERTIFICADO, documentos.Single(d => d.Id == nc.Id).Estado);
        var certificadoNc = _outbox.Eventos
            .Where(e => e.EventType == DocumentoFiscalCertificado.EventType)
            .Select(e => (DocumentoFiscalCertificadoData)e.Data)
            .Single(d => d.DocumentoFiscalId == nc.Id.ToString());
        Assert.Equal(TipoDocumentoFiscal.NOTA_CREDITO, certificadoNc.TipoDocumento);
        Assert.Equal(OrdenId, certificadoNc.OrdenId);
    }
}
