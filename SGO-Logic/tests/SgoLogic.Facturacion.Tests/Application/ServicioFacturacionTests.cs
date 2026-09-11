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

namespace SgoLogic.Facturacion.Tests.Application;

public sealed class ServicioFacturacionTests
{
    private const string OrdenId = "ORD-2026-000123";
    private static readonly DateTime T0 = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static CertificacionFiscalSolicitadaData Solicitud(string ordenId = OrdenId) => new(
        ordenId,
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

    private sealed class Escenario
    {
        private readonly string _baseDatos = $"facturacion-{Guid.NewGuid()}";

        public OutboxWriterFalso Outbox { get; } = new();

        public GeneradorCorrelativoFiscalFalso Correlativo { get; } = new(inicial: 500);

        public FacturacionDbContext NuevoContexto() => new(
            new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(_baseDatos)
                .Options);

        public ServicioFacturacion Servicio(
            FacturacionDbContext contexto,
            IClienteSat sat,
            SatOptions? opciones = null) => new(
            contexto,
            sat,
            Outbox,
            Correlativo,
            new GeneradorXmlFiscal(),
            Options.Create(opciones ?? new SatOptions { Serie = "A", MaxIntentos = 4, BackoffBaseMs = 0 }),
            NullLogger<ServicioFacturacion>.Instance);
    }

    [Fact]
    public async Task CrearDocumento_genera_el_xml_con_el_cui_y_la_serie_y_asigna_correlativo_una_vez()
    {
        var escenario = new Escenario();

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClienteSatFalso.Certifica())
                .CrearDocumentoAsync(Solicitud(), "txn-1", T0, CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var documento = await verificacion.DocumentosFiscales.Include(d => d.Lineas).SingleAsync();

        Assert.Equal("A", documento.Serie);
        Assert.Equal(500, documento.Correlativo);
        Assert.Equal("00000500", documento.NumeroDocumento);
        Assert.Equal(EstadoDocumentoFiscal.PENDIENTE, documento.Estado);
        Assert.Equal(0, documento.Intentos);
        Assert.NotNull(documento.ProximoIntentoEn);
        Assert.Equal("txn-1", documento.TransactionId);
        Assert.Contains("2547896320101", documento.Xml);
        Assert.Contains("<Serie>A</Serie>", documento.Xml);
        Assert.Contains("<Numero>00000500</Numero>", documento.Xml);
        Assert.Equal(1, escenario.Correlativo.Llamadas);
    }

    [Fact]
    public async Task Es_idempotente_por_orden_no_crea_un_segundo_documento_ni_pide_otro_correlativo()
    {
        var escenario = new Escenario();

        await using (var contexto = escenario.NuevoContexto())
        {
            var servicio = escenario.Servicio(contexto, ClienteSatFalso.Certifica());
            await servicio.CrearDocumentoAsync(Solicitud(), "txn-1", T0, CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using (var contexto = escenario.NuevoContexto())
        {
            var servicio = escenario.Servicio(contexto, ClienteSatFalso.Certifica());
            await servicio.CrearDocumentoAsync(Solicitud(), "txn-2", T0.AddMinutes(1), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        Assert.Equal(1, await verificacion.DocumentosFiscales.CountAsync());
        Assert.Equal(1, escenario.Correlativo.Llamadas);
    }

    [Fact]
    public async Task Un_reintento_reusa_la_misma_serie_y_el_mismo_correlativo()
    {
        var escenario = new Escenario();
        Guid documentoId;
        string serie;
        long correlativo;
        string numero;

        await using (var contexto = escenario.NuevoContexto())
        {
            var documento = await escenario.Servicio(contexto, ClienteSatFalso.Rechaza())
                .CrearDocumentoAsync(Solicitud(), "txn-1", T0, CancellationToken.None);
            await contexto.SaveChangesAsync();

            documentoId = documento.Id;
            serie = documento.Serie;
            correlativo = documento.Correlativo;
            numero = documento.NumeroDocumento;
        }

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClienteSatFalso.Rechaza())
                .ProcesarEnvioAsync(documentoId, intento: 1, T0.AddSeconds(10), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClienteSatFalso.Rechaza())
                .ProcesarEnvioAsync(documentoId, intento: 2, T0.AddSeconds(30), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var actualizado = await verificacion.DocumentosFiscales.SingleAsync();

        Assert.Equal(serie, actualizado.Serie);
        Assert.Equal(correlativo, actualizado.Correlativo);
        Assert.Equal(numero, actualizado.NumeroDocumento);
        Assert.Equal(2, actualizado.Intentos);
        Assert.Equal(1, escenario.Correlativo.Llamadas);
    }

    [Fact]
    public async Task Certificado_marca_el_documento_y_encola_DocumentoFiscalCertificado()
    {
        var escenario = new Escenario();
        Guid documentoId;

        await using (var contexto = escenario.NuevoContexto())
        {
            var documento = await escenario.Servicio(contexto, ClienteSatFalso.Certifica("AUTZ-77", "FOL-77"))
                .CrearDocumentoAsync(Solicitud(), "txn-1", T0, CancellationToken.None);
            await contexto.SaveChangesAsync();
            documentoId = documento.Id;
        }

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClienteSatFalso.Certifica("AUTZ-77", "FOL-77"))
                .ProcesarEnvioAsync(documentoId, intento: 1, T0.AddSeconds(10), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var documentoCertificado = await verificacion.DocumentosFiscales.SingleAsync();

        Assert.Equal(EstadoDocumentoFiscal.CERTIFICADO, documentoCertificado.Estado);
        Assert.Equal("AUTZ-77", documentoCertificado.NumeroAutorizacion);
        Assert.Equal("FOL-77", documentoCertificado.Folio);
        Assert.Null(documentoCertificado.ProximoIntentoEn);
        Assert.NotNull(documentoCertificado.CertificadoEn);

        var evento = Assert.Single(escenario.Outbox.PorTipo(DocumentoFiscalCertificado.EventType));
        Assert.Equal(DocumentoFiscalCertificado.RoutingKey, evento.RoutingKey);
        Assert.Equal(TopologiaExchangeEventos, evento.Exchange);
        var data = Assert.IsType<DocumentoFiscalCertificadoData>(evento.Data);
        Assert.Equal(OrdenId, data.OrdenId);
        Assert.Equal("AUTZ-77", data.NumeroAutorizacion);
        Assert.Equal("A", data.Serie);
        Assert.Equal(1400.00m, data.Total);
    }

    [Fact]
    public async Task Al_agotar_MaxIntentos_el_documento_queda_en_ALERTA_y_encola_CertificacionSatFallida()
    {
        var escenario = new Escenario();
        var opciones = new SatOptions { Serie = "A", MaxIntentos = 4, BackoffBaseMs = 0 };
        Guid documentoId;

        await using (var contexto = escenario.NuevoContexto())
        {
            var documento = await escenario.Servicio(contexto, ClienteSatFalso.NoDisponible(), opciones)
                .CrearDocumentoAsync(Solicitud(), "txn-1", T0, CancellationToken.None);
            await contexto.SaveChangesAsync();
            documentoId = documento.Id;
        }

        for (var intento = 1; intento <= 4; intento++)
        {
            await using var contexto = escenario.NuevoContexto();
            await escenario.Servicio(contexto, ClienteSatFalso.NoDisponible(), opciones)
                .ProcesarEnvioAsync(documentoId, intento, T0.AddMinutes(intento), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var documentoAlerta = await verificacion.DocumentosFiscales.SingleAsync();

        Assert.Equal(EstadoDocumentoFiscal.ALERTA, documentoAlerta.Estado);
        Assert.Equal(4, documentoAlerta.Intentos);
        Assert.Null(documentoAlerta.ProximoIntentoEn);
        Assert.Empty(escenario.Outbox.PorTipo(DocumentoFiscalCertificado.EventType));

        var evento = Assert.Single(escenario.Outbox.PorTipo(CertificacionSatFallida.EventType));
        var data = Assert.IsType<CertificacionSatFallidaData>(evento.Data);
        Assert.Equal(4, data.Intentos);
        Assert.Equal(OrdenId, data.OrdenId);
        Assert.NotEmpty(data.UltimoCodigo);
    }

    [Fact]
    public async Task Los_reintentos_usan_la_misma_clave_de_idempotencia_el_id_del_documento()
    {
        var escenario = new Escenario();
        var sat = ClienteSatFalso.RechazaLuegoCertifica(rechazos: 1);
        Guid documentoId;

        await using (var contexto = escenario.NuevoContexto())
        {
            var documento = await escenario.Servicio(contexto, sat)
                .CrearDocumentoAsync(Solicitud(), "txn-1", T0, CancellationToken.None);
            await contexto.SaveChangesAsync();
            documentoId = documento.Id;
        }

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, sat)
                .ProcesarEnvioAsync(documentoId, intento: 1, T0.AddSeconds(10), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, sat)
                .ProcesarEnvioAsync(documentoId, intento: 2, T0.AddSeconds(30), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        Assert.Equal(2, sat.ClavesIdempotencia.Count);
        Assert.All(sat.ClavesIdempotencia, clave => Assert.Equal(documentoId.ToString(), clave));
    }

    [Fact]
    public async Task Dos_envios_con_la_misma_clave_de_idempotencia_devuelven_la_misma_respuesta()
    {
        var sat = ClienteSatFalso.CertificaIdempotente();
        var peticion = new PeticionCertificacionSat("A", "00000500", "2547896320101", "<x/>", "1400.00", Moneda.GTQ);

        var primera = await sat.CertificarAsync(peticion, "documento-fiscal-1", CancellationToken.None);
        var segunda = await sat.CertificarAsync(peticion, "documento-fiscal-1", CancellationToken.None);

        Assert.Equal(primera, segunda);
        Assert.Equal(primera.Folio, segunda.Folio);
        Assert.Equal(primera.NumeroAutorizacion, segunda.NumeroAutorizacion);
        Assert.Equal(1, sat.CertificacionesReales);
    }

    [Fact]
    public async Task Reintento_manual_resetea_documento_de_ALERTA_a_PENDIENTE()
    {
        var escenario = new Escenario();
        var opciones = new SatOptions { Serie = "A", MaxIntentos = 2, BackoffBaseMs = 0 };
        Guid documentoId;

        await using (var contexto = escenario.NuevoContexto())
        {
            var documento = await escenario.Servicio(contexto, ClienteSatFalso.NoDisponible(), opciones)
                .CrearDocumentoAsync(Solicitud(), "txn-1", T0, CancellationToken.None);
            await contexto.SaveChangesAsync();
            documentoId = documento.Id;
        }

        for (var intento = 1; intento <= 2; intento++)
        {
            await using var contexto = escenario.NuevoContexto();
            await escenario.Servicio(contexto, ClienteSatFalso.NoDisponible(), opciones)
                .ProcesarEnvioAsync(documentoId, intento, T0.AddMinutes(intento), CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using (var contexto = escenario.NuevoContexto())
        {
            var doc = await contexto.DocumentosFiscales.SingleAsync();
            Assert.Equal(EstadoDocumentoFiscal.ALERTA, doc.Estado);
            Assert.Null(doc.ProximoIntentoEn);
        }

        var ahora = T0.AddMinutes(10);
        await using (var contexto = escenario.NuevoContexto())
        {
            await escenario.Servicio(contexto, ClienteSatFalso.NoDisponible(), opciones)
                .CrearDocumentoAsync(Solicitud(), "txn-2", ahora, CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var verificacion = escenario.NuevoContexto();
        var reiniciado = await verificacion.DocumentosFiscales.SingleAsync();

        Assert.Equal(EstadoDocumentoFiscal.PENDIENTE, reiniciado.Estado);
        Assert.Equal(0, reiniciado.Intentos);
        Assert.NotNull(reiniciado.ProximoIntentoEn);
        Assert.Null(reiniciado.UltimoCodigo);
        Assert.Null(reiniciado.UltimoMotivo);
    }

    private const string TopologiaExchangeEventos = "sgo.eventos";
}
