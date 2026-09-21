using System.Text.Json;
using SgoLogic.Mocks.PayKlik;
using Xunit;

namespace SgoLogic.Pagos.Tests.Infrastructure;

/// <summary>
/// Pruebas de la tokenizacion del mock de PayKlik: validacion de la tarjeta, vida y
/// uso unico del token, y que el mock solo recuerde metadatos no sensibles.
/// </summary>
public sealed class TokenizacionMockTests
{
    private static readonly DateTime Ahora = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private static PeticionTokenizacion Valida(
        string numero = "4242424242424242",
        string cvv = "123",
        int mes = 12,
        int anio = 2030) => new(numero, cvv, mes, anio);

    private static TarjetaTokenizada TarjetaValida() =>
        ValidadorTarjeta.Validar(Valida(), Ahora).Tarjeta!;

    // ------------------------------------------------------------------
    // Validacion del formato
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("4242424242424242", "VISA", "123", "4242")]
    [InlineData("4111111111111111", "VISA", "123", "1111")]
    [InlineData("4222222222222", "VISA", "123", "2222")]
    [InlineData("5555555555554444", "MASTERCARD", "123", "4444")]
    [InlineData("2223003122003222", "MASTERCARD", "123", "3222")]
    [InlineData("378282246310005", "AMEX", "1234", "0005")]
    [InlineData("4242 4242 4242 4242", "VISA", "123", "4242")]
    [InlineData("4242-4242-4242-4242", "VISA", "123", "4242")]
    public void Una_tarjeta_valida_devuelve_ultimos_cuatro_y_la_marca_deducida_del_BIN(
        string numero, string marca, string cvv, string ultimosCuatro)
    {
        var resultado = ValidadorTarjeta.Validar(Valida(numero, cvv), Ahora);

        Assert.True(resultado.EsValida);
        Assert.Equal(marca, resultado.Tarjeta!.Marca);
        Assert.Equal(ultimosCuatro, resultado.Tarjeta.UltimosCuatro);
    }

    [Theory]
    [InlineData("4242424242424241")]
    [InlineData("5555555555554445")]
    [InlineData("4012888888881882")]
    public void Un_numero_con_marca_y_longitud_validas_que_no_pasa_Luhn_se_rechaza(string numero)
    {
        var resultado = ValidadorTarjeta.Validar(Valida(numero), Ahora);

        Assert.False(resultado.EsValida);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal("numeroTarjeta", error.Campo);
        Assert.Contains("Luhn", error.Motivo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("4242abcd42424242")]
    [InlineData("424242424242")]
    [InlineData("6011111111111117")]
    public void Un_numero_vacio_con_letras_de_longitud_incorrecta_o_de_marca_no_soportada_se_rechaza(string numero)
    {
        var resultado = ValidadorTarjeta.Validar(Valida(numero), Ahora);

        Assert.False(resultado.EsValida);
        Assert.Contains(resultado.Errores, e => e.Campo == "numeroTarjeta");
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("abc")]
    [InlineData("")]
    public void Un_CVV_que_no_tiene_3_o_4_digitos_se_rechaza(string cvv)
    {
        var resultado = ValidadorTarjeta.Validar(Valida(cvv: cvv), Ahora);

        Assert.False(resultado.EsValida);
        Assert.Contains(resultado.Errores, e => e.Campo == "cvv");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    public void Un_CVV_de_3_o_4_digitos_se_acepta(string cvv)
    {
        Assert.True(ValidadorTarjeta.Validar(Valida(cvv: cvv), Ahora).EsValida);
    }

    [Theory]
    [InlineData(8, 2026)]
    [InlineData(12, 2025)]
    [InlineData(1, 2020)]
    public void Una_expiracion_pasada_se_rechaza(int mes, int anio)
    {
        var resultado = ValidadorTarjeta.Validar(Valida(mes: mes, anio: anio), Ahora);

        Assert.False(resultado.EsValida);
        Assert.Contains(resultado.Errores, e => e.Campo == "expiracion");
    }

    [Theory]
    [InlineData(9, 2026)]
    [InlineData(10, 2026)]
    [InlineData(1, 2027)]
    [InlineData(12, 30)]
    public void Una_expiracion_del_mes_actual_o_futura_se_acepta_y_el_anio_de_dos_digitos_es_2000_mas(int mes, int anio)
    {
        var resultado = ValidadorTarjeta.Validar(Valida(mes: mes, anio: anio), Ahora);

        Assert.True(resultado.EsValida);
        Assert.Equal(anio < 100 ? 2000 + anio : anio, resultado.Tarjeta!.AnioExpiracion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Un_mes_fuera_de_rango_se_rechaza(int mes)
    {
        var resultado = ValidadorTarjeta.Validar(Valida(mes: mes), Ahora);

        Assert.False(resultado.EsValida);
        Assert.Contains(resultado.Errores, e => e.Campo == "mesExpiracion");
    }

    [Fact]
    public void Faltan_todos_los_campos_y_cada_uno_se_reporta()
    {
        var resultado = ValidadorTarjeta.Validar(new PeticionTokenizacion(null, null, null, null), Ahora);

        Assert.False(resultado.EsValida);
        Assert.Contains(resultado.Errores, e => e.Campo == "numeroTarjeta");
        Assert.Contains(resultado.Errores, e => e.Campo == "cvv");
        Assert.Contains(resultado.Errores, e => e.Campo == "expiracion");
    }

    [Fact]
    public void Los_errores_de_validacion_nunca_repiten_el_numero_ni_el_CVV_recibidos()
    {
        const string numero = "4242424242424241";
        const string cvv = "98765";

        var resultado = ValidadorTarjeta.Validar(new PeticionTokenizacion(numero, cvv, 1, 2020), Ahora);

        Assert.False(resultado.EsValida);
        var texto = JsonSerializer.Serialize(resultado.Errores);
        Assert.DoesNotContain(numero, texto);
        Assert.DoesNotContain(cvv, texto);
    }

    [Fact]
    public void La_peticion_de_tokenizacion_impresa_no_filtra_el_PAN_ni_el_CVV()
    {
        var peticion = new PeticionTokenizacion("4242424242424242", "731", 12, 2030);

        var impresa = peticion.ToString();

        Assert.DoesNotContain("4242424242424242", impresa);
        Assert.DoesNotContain("731", impresa);
        Assert.DoesNotContain("2030", impresa);
    }

    // ------------------------------------------------------------------
    // Vida del token: vigencia y uso unico
    // ------------------------------------------------------------------

    [Fact]
    public void Emitir_devuelve_un_token_opaco_con_su_vigencia_y_declara_que_es_de_un_solo_uso()
    {
        var almacen = new AlmacenTokens(() => Ahora);

        var respuesta = almacen.Emitir(TarjetaValida(), TimeSpan.FromSeconds(900));

        Assert.StartsWith("tok_pk_", respuesta.Token);
        Assert.DoesNotContain("4242424242424242", respuesta.Token);
        Assert.Equal("4242", respuesta.UltimosCuatro);
        Assert.Equal("VISA", respuesta.Marca);
        Assert.Equal(12, respuesta.MesExpiracion);
        Assert.Equal(2030, respuesta.AnioExpiracion);
        Assert.Equal(Ahora, respuesta.EmitidoEn);
        Assert.Equal(Ahora.AddSeconds(900), respuesta.VenceEn);
        Assert.Equal(900, respuesta.VigenciaSegundos);
        Assert.True(respuesta.UsoUnico);
    }

    [Fact]
    public void Cada_tokenizacion_emite_un_token_distinto_aunque_sea_la_misma_tarjeta()
    {
        var almacen = new AlmacenTokens(() => Ahora);

        var primero = almacen.Emitir(TarjetaValida(), TimeSpan.FromMinutes(15));
        var segundo = almacen.Emitir(TarjetaValida(), TimeSpan.FromMinutes(15));

        Assert.NotEqual(primero.Token, segundo.Token);
        Assert.Equal(2, almacen.Cantidad);
    }

    [Fact]
    public void Un_token_vigente_se_resuelve_a_la_tarjeta_una_sola_vez()
    {
        var almacen = new AlmacenTokens(() => Ahora);
        var token = almacen.Emitir(TarjetaValida(), TimeSpan.FromMinutes(15)).Token;

        var primero = almacen.Consumir(token);
        var segundo = almacen.Consumir(token);

        Assert.Equal(ResultadoConsumoToken.Consumido, primero.Resultado);
        Assert.Equal("4242", primero.Tarjeta!.UltimosCuatro);
        Assert.Equal("VISA", primero.Tarjeta.Marca);
        Assert.Equal(ResultadoConsumoToken.YaUsado, segundo.Resultado);
        Assert.Null(segundo.Tarjeta);
    }

    [Fact]
    public void Un_token_vale_hasta_el_instante_de_su_vencimiento_y_desde_ahi_esta_vencido()
    {
        var reloj = Ahora;
        var almacen = new AlmacenTokens(() => reloj);
        var ultimoInstanteVigente = almacen.Emitir(TarjetaValida(), TimeSpan.FromSeconds(900)).Token;
        var alVencer = almacen.Emitir(TarjetaValida(), TimeSpan.FromSeconds(900)).Token;

        reloj = Ahora.AddSeconds(899);
        Assert.Equal(ResultadoConsumoToken.Consumido, almacen.Consumir(ultimoInstanteVigente).Resultado);

        reloj = Ahora.AddSeconds(900);
        var vencido = almacen.Consumir(alVencer);

        Assert.Equal(ResultadoConsumoToken.Vencido, vencido.Resultado);
        Assert.Null(vencido.Tarjeta);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tok_pk_inexistente")]
    public void Un_token_que_no_existe_o_esta_vacio_no_se_resuelve(string? token)
    {
        var almacen = new AlmacenTokens(() => Ahora);

        Assert.Equal(ResultadoConsumoToken.NoExiste, almacen.Consumir(token).Resultado);
    }

    [Fact]
    public async Task Dos_cobros_concurrentes_con_el_mismo_token_no_pueden_consumirlo_ambos()
    {
        var almacen = new AlmacenTokens(() => Ahora);
        var token = almacen.Emitir(TarjetaValida(), TimeSpan.FromMinutes(15)).Token;

        var resultados = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => almacen.Consumir(token).Resultado)));

        Assert.Equal(1, resultados.Count(r => r == ResultadoConsumoToken.Consumido));
        Assert.Equal(31, resultados.Count(r => r == ResultadoConsumoToken.YaUsado));
    }

    // ------------------------------------------------------------------
    // El mock solo recuerda metadatos no sensibles
    // ------------------------------------------------------------------

    [Fact]
    public void El_listado_de_admin_muestra_solo_token_ultimos_cuatro_marca_y_vigencia()
    {
        var reloj = Ahora;
        var almacen = new AlmacenTokens(() => reloj);
        var usado = almacen.Emitir(TarjetaValida(), TimeSpan.FromMinutes(15)).Token;
        var vigente = almacen.Emitir(TarjetaValida(), TimeSpan.FromMinutes(15)).Token;
        almacen.Consumir(usado);

        var listado = almacen.Listar();

        Assert.Equal(2, listado.Count);
        Assert.Equal("USADO", listado.Single(t => t.Token == usado).Estado);
        Assert.Equal("VIGENTE", listado.Single(t => t.Token == vigente).Estado);

        reloj = Ahora.AddMinutes(16);
        Assert.Equal("VENCIDO", almacen.Listar().Single(t => t.Token == vigente).Estado);

        Assert.Equal(
            new[] { "EmitidoEn", "Estado", "Marca", "Token", "UltimosCuatro", "VenceEn" },
            typeof(TokenAdminDto).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain("4242424242424242", JsonSerializer.Serialize(listado));
    }

    [Fact]
    public void Ni_la_tarjeta_guardada_ni_la_respuesta_de_tokenizacion_tienen_PAN_ni_CVV()
    {
        // Lo que el mock recuerda de la tarjeta: ultimos cuatro, marca y expiracion.
        Assert.Equal(
            new[] { "AnioExpiracion", "Marca", "MesExpiracion", "UltimosCuatro" },
            typeof(TarjetaTokenizada).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

        var almacen = new AlmacenTokens(() => Ahora);
        var respuesta = almacen.Emitir(TarjetaValida(), TimeSpan.FromMinutes(15));

        Assert.DoesNotContain("4242424242424242", JsonSerializer.Serialize(respuesta));

        foreach (var propiedad in typeof(RespuestaTokenizacion).GetProperties())
        {
            var nombre = propiedad.Name.ToLowerInvariant();
            Assert.DoesNotContain("cvv", nombre);
            Assert.DoesNotContain("numero", nombre);
        }
    }

    [Theory]
    [InlineData("4242424242424242", "VISA")]
    [InlineData("4000056655665556", "VISA")]
    [InlineData("5105105105105100", "MASTERCARD")]
    [InlineData("2720999999999999", "MASTERCARD")]
    [InlineData("341111111111111", "AMEX")]
    [InlineData("371449635398431", "AMEX")]
    public void La_marca_se_deduce_del_BIN(string numero, string marca)
    {
        Assert.Equal(marca, ValidadorTarjeta.DeducirMarca(numero));
    }

    [Theory]
    [InlineData("6011111111111117")]
    [InlineData("3530111333300000")]
    [InlineData("5061111111111111")]
    public void Un_BIN_de_otra_marca_no_se_deduce(string numero)
    {
        Assert.Null(ValidadorTarjeta.DeducirMarca(numero));
    }
}
