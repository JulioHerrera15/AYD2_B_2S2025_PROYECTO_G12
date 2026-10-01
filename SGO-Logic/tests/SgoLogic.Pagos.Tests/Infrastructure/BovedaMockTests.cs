using SgoLogic.Mocks.PayKlik;
using Xunit;

namespace SgoLogic.Pagos.Tests.Infrastructure;

/// <summary>
/// Boveda de tarjetas del mock de PayKlik: guarda solo metadatos, valida el CVV por
/// marca y sobrevive a un reinicio cuando tiene archivo.
/// </summary>
public sealed class BovedaMockTests
{
    private static readonly DateTime Ahora = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static TarjetaTokenizada Tokenizar(string numero, string cvv, int mes = 12, int anio = 2030) =>
        ValidadorTarjeta.Validar(new PeticionTokenizacion(numero, cvv, mes, anio, Guardar: true), Ahora).Tarjeta!;

    [Theory]
    [InlineData("4111111111111111", "123", "VISA")]
    [InlineData("5555555555554444", "123", "MASTERCARD")]
    [InlineData("378282246310005", "1234", "AMEX")]
    public void Las_tarjetas_de_prueba_se_guardan_y_su_referencia_emite_con_el_CVV_correcto(string numero, string cvv, string marca)
    {
        var boveda = new BovedaTarjetas(() => Ahora, rutaArchivo: null);
        var referencia = boveda.Guardar(Tokenizar(numero, cvv));

        var (resultado, tarjeta) = boveda.Validar(new PeticionTokenDesdeReferencia(referencia, cvv));

        Assert.StartsWith("card_pk_", referencia);
        Assert.Equal(ResultadoTokenDesdeReferencia.Emitido, resultado);
        Assert.Equal(marca, tarjeta!.Marca);
        Assert.Equal(numero[^4..], tarjeta.UltimosCuatro);
    }

    [Theory]
    [InlineData("378282246310005", "1234", "123")]
    [InlineData("4111111111111111", "123", "1234")]
    [InlineData("5555555555554444", "123", "12")]
    public void Un_CVV_con_el_largo_equivocado_para_la_marca_se_rechaza(string numero, string cvvAlTokenizar, string cvvAlUsar)
    {
        var boveda = new BovedaTarjetas(() => Ahora, rutaArchivo: null);
        var referencia = boveda.Guardar(Tokenizar(numero, cvvAlTokenizar));

        var (resultado, _) = boveda.Validar(new PeticionTokenDesdeReferencia(referencia, cvvAlUsar));

        Assert.Equal(ResultadoTokenDesdeReferencia.CvvInvalido, resultado);
    }

    [Fact]
    public void Una_referencia_inexistente_no_existe()
    {
        var boveda = new BovedaTarjetas(() => Ahora, rutaArchivo: null);

        var (resultado, tarjeta) = boveda.Validar(new PeticionTokenDesdeReferencia("card_pk_noexiste", "123"));

        Assert.Equal(ResultadoTokenDesdeReferencia.NoExiste, resultado);
        Assert.Null(tarjeta);
    }

    [Fact]
    public void Una_tarjeta_guardada_que_ya_vencio_no_emite_token()
    {
        var reloj = Ahora;
        var boveda = new BovedaTarjetas(() => reloj, rutaArchivo: null);
        var referencia = boveda.Guardar(Tokenizar("4111111111111111", "123", mes: 10, anio: 2026));

        reloj = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var (resultado, _) = boveda.Validar(new PeticionTokenDesdeReferencia(referencia, "123"));

        Assert.Equal(ResultadoTokenDesdeReferencia.Vencida, resultado);
    }

    [Fact]
    public void La_boveda_sobrevive_a_un_reinicio_y_el_archivo_no_tiene_PAN_ni_CVV()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"boveda-{Guid.NewGuid():N}.json");
        try
        {
            var referencia = new BovedaTarjetas(() => Ahora, ruta).Guardar(Tokenizar("4111111111111111", "987"));

            var reiniciada = new BovedaTarjetas(() => Ahora, ruta);
            var contenido = File.ReadAllText(ruta);

            Assert.NotNull(reiniciada.Obtener(referencia));
            Assert.DoesNotContain("4111111111111111", contenido);
            Assert.DoesNotContain("cvv", contenido, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("numero", contenido, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(ruta);
        }
    }

    [Fact]
    public void La_peticion_desde_referencia_no_imprime_el_CVV()
    {
        Assert.DoesNotContain("4321", new PeticionTokenDesdeReferencia("card_pk_x", "4321").ToString());
    }
}
