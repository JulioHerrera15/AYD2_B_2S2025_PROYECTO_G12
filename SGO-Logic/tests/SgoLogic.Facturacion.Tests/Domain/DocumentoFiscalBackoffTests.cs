using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Facturacion.Api.Domain;

namespace SgoLogic.Facturacion.Tests.Domain;

public sealed class DocumentoFiscalBackoffTests
{
    private static readonly DateTime Ahora = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static DocumentoFiscal NuevoDocumento() => new(
        ordenId: "ORD-2026-000123",
        transactionId: "txn-1",
        tipoDocumento: TipoDocumentoFiscal.FACTURA,
        documentoOrigenId: null,
        cuiReceptor: "2547896320101",
        nombreReceptor: "Angel David Martinez Garcia",
        direccionReceptor: "5a avenida 3-21 zona 2",
        paisReceptor: Pais.GT,
        moneda: Moneda.GTQ,
        subtotal: 1250.00m,
        impuestos: 150.00m,
        total: 1400.00m,
        serie: "A",
        correlativo: 42,
        xml: "<x/>",
        lineas: Array.Empty<DocumentoFiscalLinea>(),
        creadoEn: Ahora);

    [Fact]
    public void El_backoff_calcula_proximo_intento_en_creciente_por_intento()
    {
        var documento = NuevoDocumento();

        // Jitter fijo para que la prueba sea determinista.
        Func<double> jitterFijo = () => 0.5;
        var esperas = new List<TimeSpan>();

        for (var i = 0; i < 3; i++)
        {
            documento.ProgramarReintento(maxIntentos: 4, backoffBaseMs: 2000, ahora: Ahora, aleatorio: jitterFijo);
            Assert.NotNull(documento.ProximoIntentoEn);
            esperas.Add(documento.ProximoIntentoEn!.Value - Ahora);
        }

        Assert.Equal(3, esperas.Count);
        Assert.True(esperas[1] > esperas[0], "el segundo intento debe esperar mas que el primero");
        Assert.True(esperas[2] > esperas[1], "el tercer intento debe esperar mas que el segundo");
        Assert.All(esperas, espera => Assert.True(espera > TimeSpan.Zero));
    }

    [Fact]
    public void Al_llegar_a_MaxIntentos_el_documento_pasa_a_ALERTA_sin_proximo_intento()
    {
        var documento = NuevoDocumento();
        Func<double> jitter = () => 0.0;

        for (var i = 0; i < 4; i++)
        {
            documento.ProgramarReintento(maxIntentos: 4, backoffBaseMs: 2000, ahora: Ahora, aleatorio: jitter);
        }

        Assert.Equal(4, documento.Intentos);
        Assert.Equal(EstadoDocumentoFiscal.ALERTA, documento.Estado);
        Assert.Null(documento.ProximoIntentoEn);
    }

    [Fact]
    public void Un_reintento_no_cambia_la_serie_ni_el_correlativo()
    {
        var documento = NuevoDocumento();
        var serie = documento.Serie;
        var correlativo = documento.Correlativo;
        var numero = documento.NumeroDocumento;

        documento.ProgramarReintento(maxIntentos: 4, backoffBaseMs: 2000, ahora: Ahora, aleatorio: () => 0.3);
        documento.ProgramarReintento(maxIntentos: 4, backoffBaseMs: 2000, ahora: Ahora, aleatorio: () => 0.3);

        Assert.Equal(serie, documento.Serie);
        Assert.Equal(correlativo, documento.Correlativo);
        Assert.Equal(numero, documento.NumeroDocumento);
    }
}
