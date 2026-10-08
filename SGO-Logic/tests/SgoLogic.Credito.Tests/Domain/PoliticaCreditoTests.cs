using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Tests.Domain;

public sealed class PoliticaCreditoTests
{
    private static readonly PoliticaCredito Politica = DatosPrueba.Politica();

    private static EvaluacionCredito Evaluar(HistorialClienteErpActualizadoData datos) =>
        Politica.Evaluar(DatosPrueba.Entidad(datos), DatosPrueba.Ahora);

    /// <summary>Cliente solido: 400 dias, buen monto, pagos a tiempo, sin devoluciones ni incidentes.</summary>
    private static HistorialClienteErpActualizadoData Solido(
        int dias = 400, int compras = 12, int devoluciones = 0, int graves = 0, int puntuales = 12, int tardios = 0, decimal monto = 12000m) =>
        DatosPrueba.Historial("solido@sgo.local", dias, compras, monto, puntuales, tardios, 0, devoluciones, graves, 0);

    // ── Descartes, uno por uno ──────────────────────────────────────────────────

    [Fact]
    public void Sin_historial_descarta_con_SIN_HISTORIAL_y_no_califica()
    {
        var evaluacion = Politica.Evaluar(null, DatosPrueba.Ahora);

        Assert.False(evaluacion.Elegible);
        Assert.Equal(CodigosMotivoCredito.SinHistorial, evaluacion.CodigoMotivo);
        Assert.Equal(NivelCredito.NO_CALIFICA, evaluacion.Nivel);
        Assert.Equal(0m, evaluacion.LimiteAprobado);
    }

    [Fact]
    public void Un_incidente_grave_descarta_con_INCIDENTE_GRAVE()
    {
        var evaluacion = Evaluar(Solido(graves: 1));

        Assert.False(evaluacion.Elegible);
        Assert.Equal(CodigosMotivoCredito.IncidenteGrave, evaluacion.CodigoMotivo);
        Assert.Equal(0m, evaluacion.LimiteAprobado);
    }

    [Fact]
    public void Menos_de_90_dias_descarta_con_ANTIGUEDAD_INSUFICIENTE()
    {
        var evaluacion = Evaluar(Solido(dias: 89));

        Assert.False(evaluacion.Elegible);
        Assert.Equal(CodigosMotivoCredito.AntiguedadInsuficiente, evaluacion.CodigoMotivo);
    }

    [Fact]
    public void Con_90_dias_exactos_ya_no_descarta_por_antiguedad()
    {
        var evaluacion = Evaluar(Solido(dias: 90));

        Assert.True(evaluacion.Elegible);
        Assert.Null(evaluacion.CodigoMotivo);
    }

    [Fact]
    public void Tasa_de_devolucion_mayor_a_30_por_ciento_descarta_con_DEVOLUCIONES_EXCESIVAS()
    {
        var evaluacion = Evaluar(Solido(compras: 10, devoluciones: 4));

        Assert.False(evaluacion.Elegible);
        Assert.Equal(CodigosMotivoCredito.DevolucionesExcesivas, evaluacion.CodigoMotivo);
    }

    [Fact]
    public void Tasa_de_devolucion_de_30_por_ciento_exacto_no_descarta()
    {
        var evaluacion = Evaluar(Solido(compras: 10, devoluciones: 3));

        Assert.NotEqual(CodigosMotivoCredito.DevolucionesExcesivas, evaluacion.CodigoMotivo);
    }

    [Fact]
    public void Puntaje_menor_a_60_descarta_con_PUNTAJE_INSUFICIENTE()
    {
        var evaluacion = Evaluar(DatosPrueba.Cliente4());

        Assert.False(evaluacion.Elegible);
        Assert.Equal(CodigosMotivoCredito.PuntajeInsuficiente, evaluacion.CodigoMotivo);
        Assert.True(evaluacion.Puntaje < 60);
    }

    // ── Orden de prioridad ──────────────────────────────────────────────────────

    [Fact]
    public void Incidente_grave_gana_a_antiguedad_devoluciones_y_puntaje()
    {
        // 10 dias, 5 de 10 compras devueltas, sin pagos a tiempo y con incidente grave.
        var evaluacion = Evaluar(DatosPrueba.Historial("todo@sgo.local", 10, 10, 100m, 0, 10, 60, 5, 1, 3));

        Assert.Equal(CodigosMotivoCredito.IncidenteGrave, evaluacion.CodigoMotivo);
    }

    [Fact]
    public void Antiguedad_gana_a_devoluciones_y_puntaje()
    {
        var evaluacion = Evaluar(DatosPrueba.Historial("nuevo@sgo.local", 10, 10, 100m, 0, 10, 60, 5, 0, 3));

        Assert.Equal(CodigosMotivoCredito.AntiguedadInsuficiente, evaluacion.CodigoMotivo);
    }

    [Fact]
    public void Devoluciones_gana_a_puntaje()
    {
        var evaluacion = Evaluar(DatosPrueba.Historial("devuelve@sgo.local", 400, 10, 100m, 0, 10, 60, 5, 0, 3));

        Assert.True(evaluacion.Puntaje < 60);
        Assert.Equal(CodigosMotivoCredito.DevolucionesExcesivas, evaluacion.CodigoMotivo);
    }

    [Fact]
    public void El_puntaje_se_calcula_aunque_haya_descarte()
    {
        var evaluacion = Evaluar(DatosPrueba.Cliente6());

        Assert.Equal(CodigosMotivoCredito.IncidenteGrave, evaluacion.CodigoMotivo);
        Assert.Equal(95, evaluacion.Puntaje);
        Assert.Equal(NivelCredito.NO_CALIFICA, evaluacion.Nivel);
    }

    // ── Niveles ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(59, NivelCredito.NO_CALIFICA, 0)]
    [InlineData(60, NivelCredito.REGULAR, 1500)]
    [InlineData(74, NivelCredito.REGULAR, 1500)]
    [InlineData(75, NivelCredito.BUENO, 3500)]
    [InlineData(89, NivelCredito.BUENO, 3500)]
    [InlineData(90, NivelCredito.EXCELENTE, 7500)]
    public void Limites_de_nivel(int puntaje, NivelCredito nivel, int limite)
    {
        var (nivelObtenido, limiteObtenido) = Politica.NivelPara(puntaje);

        Assert.Equal(nivel, nivelObtenido);
        Assert.Equal((decimal)limite, limiteObtenido);
    }

    // ── Clientes de la tabla de demo ────────────────────────────────────────────

    public static IEnumerable<object?[]> ClientesDemo() => new[]
    {
        new object?[] { DatosPrueba.Cliente1(), 98, NivelCredito.EXCELENTE, 7500, true, null },
        new object?[] { DatosPrueba.Cliente2(), 79, NivelCredito.BUENO, 3500, true, null },
        new object?[] { DatosPrueba.Cliente3(), 62, NivelCredito.REGULAR, 1500, true, null },
        new object?[] { DatosPrueba.Cliente4(), 54, NivelCredito.NO_CALIFICA, 0, false, CodigosMotivoCredito.PuntajeInsuficiente },
        new object?[] { DatosPrueba.Cliente5(), 59, NivelCredito.NO_CALIFICA, 0, false, CodigosMotivoCredito.AntiguedadInsuficiente },
        new object?[] { DatosPrueba.Cliente6(), 95, NivelCredito.NO_CALIFICA, 0, false, CodigosMotivoCredito.IncidenteGrave }
    };

    [Theory]
    [MemberData(nameof(ClientesDemo))]
    public void Clientes_de_demo_dan_el_resultado_esperado(
        HistorialClienteErpActualizadoData datos, int puntaje, NivelCredito nivel, int limite, bool elegible, string? motivo)
    {
        var evaluacion = Evaluar(datos);

        Assert.Equal(puntaje, evaluacion.Puntaje);
        Assert.Equal(nivel, evaluacion.Nivel);
        Assert.Equal((decimal)limite, evaluacion.LimiteAprobado);
        Assert.Equal(elegible, evaluacion.Elegible);
        Assert.Equal(motivo, evaluacion.CodigoMotivo);
        Assert.Equal("2026.10-v2", evaluacion.VersionPolitica);
    }

    [Fact]
    public void Cliente1_suma_factores_exactos()
    {
        var factores = Evaluar(DatosPrueba.Cliente1()).Factores;

        Assert.Equal(40m, factores.Puntualidad);
        Assert.Equal(30m, factores.Monto);
        Assert.Equal(8m, factores.Antiguedad);
        Assert.Equal(7m, factores.Frecuencia);
        Assert.Equal(8.15m, Math.Round(factores.Devoluciones, 2));
        Assert.Equal(5m, factores.Incidentes);
    }

    // ── Casos de borde ──────────────────────────────────────────────────────────

    [Fact]
    public void Sin_pagos_registrados_la_puntualidad_vale_20()
    {
        var evaluacion = Evaluar(DatosPrueba.Historial("sinpagos@sgo.local", 730, 12, 12000m, 0, 0, 0, 0, 0, 0));

        Assert.Equal(20m, evaluacion.Factores.Puntualidad);
        // 20 + 30 + 8 + 7 + 10 + 5 = 80.
        Assert.Equal(80, evaluacion.Puntaje);
        Assert.Equal(NivelCredito.BUENO, evaluacion.Nivel);
    }

    [Fact]
    public void Sin_compras_la_tasa_de_devolucion_es_cero()
    {
        var evaluacion = Evaluar(DatosPrueba.Historial("sincompras@sgo.local", 400, 0, 0m, 0, 0, 0, 0, 0, 0));

        Assert.Equal(10m, evaluacion.Factores.Devoluciones);
        Assert.NotEqual(CodigosMotivoCredito.DevolucionesExcesivas, evaluacion.CodigoMotivo);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 2.5)]
    [InlineData(2, 0)]
    [InlineData(5, 0)]
    public void Otros_incidentes_restan_puntos(int otros, double esperado)
    {
        var evaluacion = Evaluar(DatosPrueba.Historial("otros@sgo.local", 400, 12, 12000m, 12, 0, 0, 0, 0, otros));

        Assert.Equal((decimal)esperado, evaluacion.Factores.Incidentes);
    }

    [Fact]
    public void El_redondeo_del_puntaje_es_hacia_arriba_en_el_punto_medio()
    {
        // 20 (sin pagos) + 30 + 8 + 7 + 5 (tasa 15%) + 2.5 (un incidente) = 72.5 -> 73.
        var evaluacion = Evaluar(DatosPrueba.Historial("medio@sgo.local", 730, 20, 12000m, 0, 0, 0, 3, 0, 1));

        Assert.Equal(72.5m, Math.Round(evaluacion.Factores.Total, 4));
        Assert.Equal(73, evaluacion.Puntaje);
    }
}
