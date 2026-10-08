using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Tests.Dobles;

namespace SgoLogic.Credito.Tests.Application;

public sealed class ServicioElegibilidadTests
{
    private readonly AlmacenCreditoFalso _almacen = new();

    private async Task SembrarAsync(params HistorialClienteErpActualizadoData[] historiales)
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, new OutboxWriterFalso());
        foreach (var historial in historiales)
        {
            await servicio.AplicarHistorialAsync(historial, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Cliente_elegible_sin_monto_esta_DISPONIBLE()
    {
        await SembrarAsync(DatosPrueba.Cliente1());
        var servicio = DatosPrueba.ServicioElegibilidad(_almacen);

        var oferta = await servicio.ConsultarAsync("cliente1@sgo.local", null, CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.Disponible, oferta.Estado);
        Assert.True(oferta.Ofrecer);
        Assert.Null(oferta.CodigoMotivo);
        Assert.Equal(7500m, oferta.LimiteAprobado);
        Assert.Equal(7500m, oferta.Disponible);
        Assert.Null(oferta.MontoSolicitado);
        Assert.Equal("GTQ", oferta.Moneda);
        Assert.Equal(30, oferta.PlazoDias);
        Assert.Equal(DatosPrueba.Ahora.Date.AddDays(30), oferta.FechaPagoEstimada);
        Assert.Equal("Tienes crédito disponible", oferta.Titulo);
        Assert.Contains("Q7,500.00", oferta.Mensaje);
        Assert.Equal(1, _almacen.Consultas);
    }

    [Fact]
    public async Task Con_monto_que_cabe_esta_DISPONIBLE_y_el_mensaje_trae_la_fecha_limite()
    {
        await SembrarAsync(DatosPrueba.Cliente2());
        var servicio = DatosPrueba.ServicioElegibilidad(_almacen);

        var oferta = await servicio.ConsultarAsync("cliente2@sgo.local", 3500m, CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.Disponible, oferta.Estado);
        Assert.True(oferta.Ofrecer);
        Assert.Equal(3500m, oferta.MontoSolicitado);
        Assert.Contains("1 de noviembre de 2026", oferta.Mensaje);
    }

    [Fact]
    public async Task Con_monto_mayor_al_disponible_es_CUPO_INSUFICIENTE()
    {
        await SembrarAsync(DatosPrueba.Cliente3());
        var servicio = DatosPrueba.ServicioElegibilidad(_almacen);

        var oferta = await servicio.ConsultarAsync("cliente3@sgo.local", 2000m, CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.CupoInsuficiente, oferta.Estado);
        Assert.False(oferta.Ofrecer);
        Assert.Null(oferta.FechaPagoEstimada);
        Assert.Equal(1500m, oferta.Disponible);
        Assert.Equal("Tu crédito disponible no alcanza para esta compra", oferta.Titulo);
        Assert.Contains("Q1,500.00", oferta.Mensaje);
        Assert.Contains("Q2,000.00", oferta.Mensaje);
        Assert.Contains("tarjeta", oferta.Recomendacion);
    }

    [Fact]
    public async Task Cliente_con_puntaje_bajo_es_NO_ELEGIBLE_y_el_mensaje_habla_de_su_factor_mas_debil()
    {
        await SembrarAsync(DatosPrueba.Cliente4());
        var servicio = DatosPrueba.ServicioElegibilidad(_almacen);

        var oferta = await servicio.ConsultarAsync("cliente4@sgo.local", 100m, CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.NoElegible, oferta.Estado);
        Assert.False(oferta.Ofrecer);
        Assert.Equal("PUNTAJE_INSUFICIENTE", oferta.CodigoMotivo);
        Assert.Equal(0m, oferta.LimiteAprobado);
        Assert.Equal(0m, oferta.Disponible);
        Assert.Equal("Por ahora no podemos ofrecerte crédito", oferta.Titulo);
        Assert.Contains("pagos", oferta.Mensaje);
        Assert.Contains("periódicamente", oferta.Recomendacion);
    }

    [Fact]
    public async Task Cuenta_reciente_dice_cuantos_dias_faltan()
    {
        await SembrarAsync(DatosPrueba.Cliente5());
        var servicio = DatosPrueba.ServicioElegibilidad(_almacen);

        var oferta = await servicio.ConsultarAsync("cliente5@sgo.local", null, CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.NoElegible, oferta.Estado);
        Assert.Equal("ANTIGUEDAD_INSUFICIENTE", oferta.CodigoMotivo);
        Assert.Equal("Tu cuenta es muy reciente", oferta.Titulo);
        Assert.Contains("Te faltan 50 días", oferta.Mensaje);
    }

    [Fact]
    public async Task Incidente_grave_pide_comunicarse_con_atencion_al_cliente()
    {
        await SembrarAsync(DatosPrueba.Cliente6());
        var servicio = DatosPrueba.ServicioElegibilidad(_almacen);

        var oferta = await servicio.ConsultarAsync("cliente6@sgo.local", null, CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.NoElegible, oferta.Estado);
        Assert.Equal("INCIDENTE_GRAVE", oferta.CodigoMotivo);
        Assert.Equal("Necesitamos revisar tu cuenta", oferta.Titulo);
        Assert.Contains("atención al cliente", oferta.Recomendacion);
    }

    [Fact]
    public async Task Cliente_sin_historial_es_SIN_HISTORIAL()
    {
        var servicio = DatosPrueba.ServicioElegibilidad(_almacen);

        var oferta = await servicio.ConsultarAsync("demo@sgo.local", 500m, CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.SinHistorial, oferta.Estado);
        Assert.False(oferta.Ofrecer);
        Assert.Equal("SIN_HISTORIAL", oferta.CodigoMotivo);
        Assert.Equal("Aún no podemos ofrecerte crédito", oferta.Titulo);
        Assert.Null(oferta.CalculadoEn);
    }

    [Fact]
    public async Task Mi_credito_trae_montos_nivel_y_cuatro_factores()
    {
        await SembrarAsync(DatosPrueba.Cliente2());
        var servicio = DatosPrueba.ServicioElegibilidad(_almacen);

        var credito = await servicio.ObtenerMiCreditoAsync("cliente2@sgo.local", CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.Disponible, credito.Estado);
        Assert.Equal("BUENO", credito.Nivel);
        Assert.Equal("Bueno", credito.EtiquetaNivel);
        Assert.Equal(3500m, credito.LimiteAprobado);
        Assert.Equal(0m, credito.SaldoUtilizado);
        Assert.Equal(3500m, credito.Disponible);
        Assert.Equal(
            new[] { "Pagos a tiempo", "Monto de tus compras", "Tiempo y frecuencia como cliente", "Devoluciones y reclamos" },
            credito.Factores.Select(f => f.Nombre));

        // Puntualidad 35.56/40 = 89%, monto 21/30 = 70%, tiempo 10.73/15 = 72%, devoluciones 11.30/15 = 75%.
        Assert.Equal(
            new[] { ValoracionesFactor.Favorable, ValoracionesFactor.Aceptable, ValoracionesFactor.Aceptable, ValoracionesFactor.Aceptable },
            credito.Factores.Select(f => f.Valoracion));
    }

    [Fact]
    public async Task Mi_credito_sin_historial_no_trae_factores_ni_montos()
    {
        var servicio = DatosPrueba.ServicioElegibilidad(_almacen);

        var credito = await servicio.ObtenerMiCreditoAsync("demo@sgo.local", CancellationToken.None);

        Assert.Equal(EstadosOfertaCredito.SinHistorial, credito.Estado);
        Assert.Equal("NO_CALIFICA", credito.Nivel);
        Assert.Empty(credito.Factores);
        Assert.Equal(0m, credito.LimiteAprobado);
        Assert.Null(credito.ActualizadoEn);
    }

    [Fact]
    public void Ninguno_de_los_dos_DTO_expone_el_puntaje()
    {
        foreach (var tipo in new[] { typeof(OfertaCreditoDto), typeof(MiCreditoDto), typeof(FactorCreditoDto) })
        {
            Assert.DoesNotContain(tipo.GetProperties(), p => p.Name.Contains("Puntaje", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Puntos", StringComparison.OrdinalIgnoreCase));
        }
    }
}
