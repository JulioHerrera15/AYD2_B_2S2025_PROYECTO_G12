using SgoLogic.Mocks.SAT;

namespace SgoLogic.Facturacion.Tests.Application;

/// <summary>
/// El mock de la SAT respeta la idempotencia: la misma clave devuelve la misma
/// respuesta sin volver a "certificar".
/// </summary>
public sealed class AlmacenIdempotenciaSatTests
{
    private static RespuestaCertificacion NuevaRespuesta() => new(
        Folio: $"FOL-{Guid.NewGuid():N}"[..12],
        NumeroAutorizacion: Guid.NewGuid().ToString("D"),
        Estado: "CERTIFICADO",
        CodigoRespuesta: "00",
        Mensaje: "Documento certificado",
        CertificadoEn: DateTime.UtcNow);

    [Fact]
    public void La_misma_clave_devuelve_la_misma_respuesta_y_no_vuelve_a_generar()
    {
        var almacen = new AlmacenIdempotencia();
        var generaciones = 0;

        var primera = almacen.ObtenerOAgregar("documento-fiscal-1", () =>
        {
            generaciones++;
            return NuevaRespuesta();
        });

        var segunda = almacen.ObtenerOAgregar("documento-fiscal-1", () =>
        {
            generaciones++;
            return NuevaRespuesta();
        });

        Assert.Same(primera, segunda);
        Assert.Equal(1, generaciones);
        Assert.Equal(1, almacen.Cantidad);
        Assert.Contains("documento-fiscal-1", almacen.Claves);
    }

    [Fact]
    public void Claves_distintas_generan_respuestas_distintas()
    {
        var almacen = new AlmacenIdempotencia();

        var uno = almacen.ObtenerOAgregar("documento-fiscal-1", NuevaRespuesta);
        var dos = almacen.ObtenerOAgregar("documento-fiscal-2", NuevaRespuesta);

        Assert.NotEqual(uno.NumeroAutorizacion, dos.NumeroAutorizacion);
        Assert.Equal(2, almacen.Cantidad);
        Assert.True(almacen.TryObtener("documento-fiscal-1", out var recuperada));
        Assert.Same(uno, recuperada);
    }
}
