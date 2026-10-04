using SgoLogic.BuildingBlocks.Messaging;
using SgoLogic.Contracts.Eventos.Credito;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Tests.Dobles;

namespace SgoLogic.Credito.Tests.Application;

public sealed class ServicioPerfilCrediticioTests
{
    private readonly AlmacenCreditoFalso _almacen = new();
    private readonly OutboxWriterFalso _outbox = new();

    [Fact]
    public async Task Aplicar_un_historial_nuevo_guarda_historial_perfil_y_linea_y_encola_el_evento()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);

        var resultado = await servicio.AplicarHistorialAsync(DatosPrueba.Cliente1(), CancellationToken.None);

        Assert.True(resultado.Aplicado);
        Assert.True(resultado.HuboCambios);
        Assert.Equal(98, resultado.Puntaje);
        Assert.Equal("EXCELENTE", resultado.Nivel);
        Assert.Equal(7500m, resultado.LimiteAprobado);

        Assert.Single(_almacen.Historiales);
        var perfil = Assert.Single(_almacen.Perfiles);
        var linea = Assert.Single(_almacen.Lineas);
        Assert.Equal(EstadoLineaCredito.ACTIVA, linea.Estado);
        Assert.Equal(7500m, linea.LimiteAprobado);
        Assert.Equal(0m, linea.SaldoUtilizado);
        Assert.Equal(7500m, linea.Disponible);
        Assert.Equal(1, _almacen.Guardados);

        var evento = Assert.Single(_outbox.Eventos);
        Assert.Equal(PerfilCrediticioActualizado.EventType, evento.EventType);
        Assert.Equal(PerfilCrediticioActualizado.RoutingKey, evento.RoutingKey);
        Assert.Equal(PerfilCrediticioActualizado.AggregateType, evento.AggregateType);
        Assert.Equal(TopologiaMensajeria.ExchangeEventos, evento.Exchange);
        Assert.Equal("cliente1@sgo.local", evento.AggregateId);
        Assert.Equal(1, evento.Sequence);

        var data = Assert.IsType<PerfilCrediticioActualizadoData>(evento.Data);
        Assert.Equal(98, data.Puntaje);
        Assert.Equal("EXCELENTE", data.Nivel);
        Assert.True(data.Elegible);
        Assert.Null(data.CodigoMotivo);
        Assert.Equal(7500m, data.LimiteAprobado);
        Assert.Equal("GTQ", data.Moneda);
        Assert.Equal("2026.10-v2", data.VersionPolitica);
        Assert.Equal(perfil.FechaCorteHistorial, data.FechaCorteHistorial);
    }

    [Fact]
    public async Task El_cliente_se_normaliza_a_minusculas()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);

        await servicio.AplicarHistorialAsync(
            DatosPrueba.Cliente1() with { ClienteId = "  Cliente1@SGO.local " }, CancellationToken.None);

        Assert.Equal("cliente1@sgo.local", Assert.Single(_almacen.Perfiles).ClienteId);
    }

    [Fact]
    public async Task Aplicar_dos_veces_el_mismo_historial_es_idempotente()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);

        await servicio.AplicarHistorialAsync(DatosPrueba.Cliente2(), CancellationToken.None);
        var segundo = await servicio.AplicarHistorialAsync(DatosPrueba.Cliente2(), CancellationToken.None);

        Assert.False(segundo.Aplicado);
        Assert.False(segundo.HuboCambios);
        Assert.Equal(79, segundo.Puntaje);
        Assert.Single(_almacen.Historiales);
        Assert.Single(_almacen.Perfiles);
        Assert.Single(_almacen.Lineas);
        Assert.Single(_outbox.Eventos);
        Assert.Equal(1, _almacen.Guardados);
    }

    [Fact]
    public async Task Un_corte_que_solo_difiere_por_debajo_del_microsegundo_se_ignora_y_no_emite_evento()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);
        await servicio.AplicarHistorialAsync(
            DatosPrueba.Cliente1() with { FechaCorte = DatosPrueba.Ahora.AddTicks(2) }, CancellationToken.None);

        // PostgreSQL guardaria ambos cortes como el mismo microsegundo.
        var mismoMicrosegundo = DatosPrueba.Cliente4() with
        {
            ClienteId = "cliente1@sgo.local",
            FechaCorte = DatosPrueba.Ahora.AddTicks(9)
        };
        var resultado = await servicio.AplicarHistorialAsync(mismoMicrosegundo, CancellationToken.None);

        Assert.False(resultado.Aplicado);
        Assert.False(resultado.HuboCambios);
        Assert.Equal(18, Assert.Single(_almacen.Historiales).ComprasUltimos12Meses);
        Assert.Single(_outbox.Eventos);
        Assert.Equal(1, _almacen.Guardados);
    }

    [Fact]
    public async Task Un_corte_un_microsegundo_posterior_si_se_aplica()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);
        var primerCorte = DatosPrueba.Ahora.AddTicks(3);
        await servicio.AplicarHistorialAsync(
            DatosPrueba.Cliente1() with { FechaCorte = primerCorte }, CancellationToken.None);

        var siguiente = DatosPrueba.Cliente4() with
        {
            ClienteId = "cliente1@sgo.local",
            FechaCorte = primerCorte.AddTicks(TimeSpan.TicksPerMicrosecond)
        };
        var resultado = await servicio.AplicarHistorialAsync(siguiente, CancellationToken.None);

        Assert.True(resultado.Aplicado);
        Assert.True(resultado.HuboCambios);
        Assert.Equal(DatosPrueba.Ahora.AddTicks(TimeSpan.TicksPerMicrosecond), Assert.Single(_almacen.Historiales).FechaCorte);
        Assert.Equal(2, _outbox.Eventos.Count);
        Assert.Equal(2, _almacen.Guardados);
    }

    [Fact]
    public async Task Un_historial_con_fecha_de_corte_vieja_se_ignora()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);
        await servicio.AplicarHistorialAsync(DatosPrueba.Cliente1(), CancellationToken.None);

        // Mismo cliente con historial malo pero de un corte anterior.
        var viejo = DatosPrueba.Cliente4() with
        {
            ClienteId = "cliente1@sgo.local",
            FechaCorte = DatosPrueba.Ahora.AddDays(-1)
        };
        var resultado = await servicio.AplicarHistorialAsync(viejo, CancellationToken.None);

        Assert.False(resultado.Aplicado);
        Assert.Equal("EXCELENTE", resultado.Nivel);
        Assert.Equal(18, Assert.Single(_almacen.Historiales).ComprasUltimos12Meses);
        Assert.Equal(98, Assert.Single(_almacen.Perfiles).Puntaje);
        Assert.Single(_outbox.Eventos);
    }

    [Fact]
    public async Task Un_corte_nuevo_con_el_mismo_resultado_no_emite_evento()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);
        await servicio.AplicarHistorialAsync(DatosPrueba.Cliente1(), CancellationToken.None);

        var mismoResultado = DatosPrueba.Cliente1() with { FechaCorte = DatosPrueba.Ahora.AddHours(1) };
        var resultado = await servicio.AplicarHistorialAsync(mismoResultado, CancellationToken.None);

        Assert.True(resultado.Aplicado);
        Assert.False(resultado.HuboCambios);
        Assert.Single(_outbox.Eventos);
        Assert.Equal(2, _almacen.Guardados);
        Assert.Equal(DatosPrueba.Ahora.AddHours(1), Assert.Single(_almacen.Perfiles).FechaCorteHistorial);
    }

    [Fact]
    public async Task Un_cambio_de_nivel_emite_un_evento_nuevo_con_la_secuencia_siguiente()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);
        await servicio.AplicarHistorialAsync(DatosPrueba.Cliente3(), CancellationToken.None);

        var mejorado = DatosPrueba.Cliente1() with
        {
            ClienteId = "cliente3@sgo.local",
            FechaCorte = DatosPrueba.Ahora.AddDays(1)
        };
        var resultado = await servicio.AplicarHistorialAsync(mejorado, CancellationToken.None);

        Assert.True(resultado.HuboCambios);
        Assert.Equal(2, _outbox.Eventos.Count);
        Assert.Equal(new[] { 1, 2 }, _outbox.Eventos.Select(e => e.Sequence));
        Assert.Equal("EXCELENTE", Assert.IsType<PerfilCrediticioActualizadoData>(_outbox.Eventos[1].Data).Nivel);
        Assert.Equal(7500m, Assert.Single(_almacen.Lineas).LimiteAprobado);
    }

    [Fact]
    public async Task Si_deja_de_ser_elegible_la_linea_queda_INACTIVA_con_limite_cero()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);
        await servicio.AplicarHistorialAsync(DatosPrueba.Cliente1(), CancellationToken.None);

        var conIncidente = DatosPrueba.Cliente1() with { IncidentesGraves = 1, FechaCorte = DatosPrueba.Ahora.AddDays(1) };
        await servicio.AplicarHistorialAsync(conIncidente, CancellationToken.None);

        var linea = Assert.Single(_almacen.Lineas);
        Assert.Equal(EstadoLineaCredito.INACTIVA, linea.Estado);
        Assert.Equal(0m, linea.LimiteAprobado);
        Assert.Equal(0m, linea.Disponible);

        var data = Assert.IsType<PerfilCrediticioActualizadoData>(_outbox.Eventos[^1].Data);
        Assert.False(data.Elegible);
        Assert.Equal(CodigosMotivoCredito.IncidenteGrave, data.CodigoMotivo);
        Assert.Equal("NO_CALIFICA", data.Nivel);
    }

    [Fact]
    public async Task Cliente_no_elegible_desde_el_inicio_tiene_linea_INACTIVA_y_emite_evento()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);

        var resultado = await servicio.AplicarHistorialAsync(DatosPrueba.Cliente5(), CancellationToken.None);

        Assert.False(resultado.Elegible);
        Assert.Equal(CodigosMotivoCredito.AntiguedadInsuficiente, resultado.CodigoMotivo);
        Assert.Equal(EstadoLineaCredito.INACTIVA, Assert.Single(_almacen.Lineas).Estado);
        Assert.Single(_outbox.Eventos);
    }

    [Fact]
    public async Task Sin_confirmar_no_guarda_y_deja_todo_pendiente_para_quien_llama()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);

        await servicio.AplicarHistorialAsync(DatosPrueba.Cliente1(), CancellationToken.None, confirmar: false);

        Assert.Equal(0, _almacen.Guardados);
        Assert.Single(_almacen.Perfiles);
        Assert.Single(_outbox.Eventos);
    }

    [Fact]
    public async Task Un_historial_sin_cliente_o_con_negativos_se_rechaza()
    {
        var servicio = DatosPrueba.ServicioPerfil(_almacen, _outbox);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            servicio.AplicarHistorialAsync(DatosPrueba.Cliente1() with { ClienteId = " " }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            servicio.AplicarHistorialAsync(DatosPrueba.Cliente1() with { Devoluciones = -1 }, CancellationToken.None));

        Assert.Empty(_almacen.Perfiles);
        Assert.Empty(_outbox.Eventos);
    }
}
