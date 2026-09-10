using SgoLogic.Mocks.PayKlik;
using Xunit;

namespace SgoLogic.Pagos.Tests.Infrastructure;

/// <summary>
/// Pruebas del almacen de idempotencia del mock de PayKlik: ante una clave
/// repetida se devuelve la misma respuesta sin volver a procesar el cargo.
/// </summary>
public sealed class AlmacenIdempotenciaTests
{
    private static RespuestaCargo NuevaRespuesta(string autorizacionId) => new(
        AutorizacionId: autorizacionId,
        Estado: "APROBADO",
        CodigoRespuesta: "00",
        Mensaje: "Cargo aprobado",
        UltimosCuatro: "4242",
        Marca: "VISA",
        ProcesadoEn: DateTime.UtcNow);

    [Fact]
    public void Dos_llamadas_con_la_misma_clave_devuelven_la_misma_respuesta_sin_reprocesar()
    {
        var almacen = new AlmacenIdempotencia();
        var original = NuevaRespuesta("PK-1");

        var primera = almacen.ObtenerOAgregar("clave-pago-1", () => original);

        var vecesGenerada = 0;
        var segunda = almacen.ObtenerOAgregar("clave-pago-1", () =>
        {
            vecesGenerada++;
            return NuevaRespuesta("PK-2");
        });

        Assert.Same(primera, segunda);
        Assert.Same(original, segunda);
        Assert.Equal(0, vecesGenerada);
        Assert.Equal(1, almacen.Cantidad);
    }

    [Fact]
    public void Claves_distintas_guardan_respuestas_distintas()
    {
        var almacen = new AlmacenIdempotencia();

        var a = almacen.ObtenerOAgregar("clave-a", () => NuevaRespuesta("PK-A"));
        var b = almacen.ObtenerOAgregar("clave-b", () => NuevaRespuesta("PK-B"));

        Assert.NotSame(a, b);
        Assert.Equal(2, almacen.Cantidad);
        Assert.True(almacen.TryObtener("clave-a", out var recuperada));
        Assert.Same(a, recuperada);
    }
}
