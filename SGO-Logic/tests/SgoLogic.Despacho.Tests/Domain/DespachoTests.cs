using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Despacho.Api.Domain;
using Xunit;

namespace SgoLogic.Despacho.Tests.Domain;

/// <summary>
/// HU-011, HU-012 y HU-020 vistos desde el agregado: que transiciones son legales y
/// cuales no pueden ocurrir por mucho que lo pida un mensaje tardio.
/// </summary>
public sealed class DespachoTests
{
    private static Despacho.Api.Domain.Despacho Crear(
        MetodoEntrega metodo = MetodoEntrega.DOMICILIO,
        string? ubicacionRecogida = null) =>
        Despacho.Api.Domain.Despacho.Crear(
            "DES-2026-000001",
            "ORD-2026-000001",
            "RSV-2026-000001",
            metodo,
            ubicacionRecogida,
            metodo == MetodoEntrega.DOMICILIO ? "zona 10" : null,
            new[] { ("SKU-A", "lampara", 2) });

    [Fact]
    public void Un_despacho_nace_con_origen_pendiente_y_secuencia_uno()
    {
        var despacho = Crear();

        Assert.Equal(EstadoDespacho.ORIGEN_PENDIENTE, despacho.Estado);
        Assert.Equal(1, despacho.Secuencia);
        Assert.Null(despacho.UbicacionOrigenId);
    }

    [Fact]
    public void Un_despacho_sin_lineas_no_existe()
    {
        Assert.Throws<ArgumentException>(() => Despacho.Api.Domain.Despacho.Crear(
            "DES-2026-000002", "ORD-X", "RSV-X", MetodoEntrega.DOMICILIO, null, "z1",
            Array.Empty<(string, string, int)>()));
    }

    [Fact]
    public void Determinar_origen_a_domicilio_deja_la_guia_solicitada()
    {
        var despacho = Crear();

        despacho.DeterminarOrigen("CD-01", TipoUbicacion.CENTRO_DISTRIBUCION);

        Assert.Equal(EstadoDespacho.GUIA_SOLICITADA, despacho.Estado);
        Assert.Equal("CD-01", despacho.UbicacionOrigenId);
        Assert.True(despacho.RequiereGuia);
    }

    [Fact]
    public void Determinar_origen_en_recogida_no_solicita_guia()
    {
        // HU-020: si el cliente recoge, el despacho ya esta donde tiene que estar.
        var despacho = Crear(MetodoEntrega.RECOGIDA_TIENDA, "TDA-07");

        despacho.DeterminarOrigen("TDA-07", TipoUbicacion.TIENDA);

        Assert.Equal(EstadoDespacho.RECOGIDA_TIENDA, despacho.Estado);
        Assert.False(despacho.RequiereGuia);
    }

    [Fact]
    public void Asignar_guia_guarda_el_rastreo()
    {
        var despacho = Crear();
        despacho.DeterminarOrigen("CD-01", TipoUbicacion.CENTRO_DISTRIBUCION);

        despacho.AsignarGuia("LE-1", "https://rastreo/1", "LogiExpress");

        Assert.Equal(EstadoDespacho.GUIA_ASIGNADA, despacho.Estado);
        Assert.Equal("LE-1", despacho.GuiaId);
        Assert.Equal("https://rastreo/1", despacho.TrackingUrl);
    }

    [Fact]
    public void No_se_puede_asignar_guia_sin_haber_determinado_el_origen()
    {
        var despacho = Crear();

        Assert.Throws<ExcepcionTransicionDespachoInvalida>(
            () => despacho.AsignarGuia("LE-1", "https://rastreo/1", "LogiExpress"));
    }

    [Fact]
    public void Una_guia_pendiente_puede_asignarse_despues()
    {
        // El criterio de HU-011: el despacho sigue vivo tras el timeout, y la guia
        // se consigue mas tarde sin rehacer nada.
        var despacho = Crear();
        despacho.DeterminarOrigen("CD-01", TipoUbicacion.CENTRO_DISTRIBUCION);
        despacho.MarcarGuiaPendiente();

        Assert.Equal(EstadoDespacho.GUIA_PENDIENTE, despacho.Estado);

        despacho.AsignarGuia("LE-9", "https://rastreo/9", "LogiExpress");

        Assert.Equal(EstadoDespacho.GUIA_ASIGNADA, despacho.Estado);
    }

    [Fact]
    public void El_avance_de_entrega_sigue_el_orden_esperado()
    {
        var despacho = Crear();
        despacho.DeterminarOrigen("CD-01", TipoUbicacion.CENTRO_DISTRIBUCION);
        despacho.AsignarGuia("LE-1", "u", "LogiExpress");

        Assert.True(despacho.ActualizarEstadoEntrega(EstadoDespacho.EN_PREPARACION, null, DateTime.UtcNow));
        Assert.True(despacho.ActualizarEstadoEntrega(EstadoDespacho.EN_TRANSITO, null, DateTime.UtcNow));
        Assert.True(despacho.ActualizarEstadoEntrega(EstadoDespacho.ENTREGADA, "recibido", DateTime.UtcNow));

        Assert.Equal(EstadoDespacho.ENTREGADA, despacho.Estado);
        Assert.NotNull(despacho.EntregadoEn);
    }

    [Fact]
    public void Un_estado_tardio_no_revive_un_despacho_entregado()
    {
        // Es el caso que protege de verdad: LogiExpress reenvia un EN_TRANSITO viejo
        // despues de que el cliente ya recibio el pedido.
        var despacho = Crear();
        despacho.DeterminarOrigen("CD-01", TipoUbicacion.CENTRO_DISTRIBUCION);
        despacho.AsignarGuia("LE-1", "u", "LogiExpress");
        despacho.ActualizarEstadoEntrega(EstadoDespacho.ENTREGADA, null, DateTime.UtcNow);

        Assert.Throws<ExcepcionTransicionDespachoInvalida>(
            () => despacho.ActualizarEstadoEntrega(EstadoDespacho.EN_TRANSITO, null, DateTime.UtcNow));

        Assert.Equal(EstadoDespacho.ENTREGADA, despacho.Estado);
    }

    [Fact]
    public void Repetir_el_mismo_estado_no_cambia_nada_y_lo_dice()
    {
        var despacho = Crear();
        despacho.DeterminarOrigen("CD-01", TipoUbicacion.CENTRO_DISTRIBUCION);
        despacho.AsignarGuia("LE-1", "u", "LogiExpress");
        despacho.ActualizarEstadoEntrega(EstadoDespacho.EN_TRANSITO, null, DateTime.UtcNow);

        var secuenciaAntes = despacho.Secuencia;

        // false = no hubo cambio, y por eso quien llama no debe emitir evento.
        Assert.False(despacho.ActualizarEstadoEntrega(EstadoDespacho.EN_TRANSITO, null, DateTime.UtcNow));
        Assert.Equal(secuenciaAntes, despacho.Secuencia);
    }

    [Fact]
    public void Pasar_a_recogida_descarta_la_guia_en_curso()
    {
        var despacho = Crear();
        despacho.DeterminarOrigen("CD-01", TipoUbicacion.CENTRO_DISTRIBUCION);
        despacho.AsignarGuia("LE-1", "u", "LogiExpress");

        despacho.ReasignarMetodoEntrega(MetodoEntrega.RECOGIDA_TIENDA, "TDA-07", null);

        Assert.Equal(EstadoDespacho.RECOGIDA_TIENDA, despacho.Estado);
        Assert.Null(despacho.GuiaId);
        Assert.Equal("TDA-07", despacho.UbicacionRecogidaId);
    }

    [Fact]
    public void Pasar_a_domicilio_vuelve_a_dejar_el_origen_pendiente()
    {
        var despacho = Crear(MetodoEntrega.RECOGIDA_TIENDA, "TDA-07");
        despacho.DeterminarOrigen("TDA-07", TipoUbicacion.TIENDA);

        despacho.ReasignarMetodoEntrega(MetodoEntrega.DOMICILIO, null, "zona 9");

        // Hay que resolver de nuevo de donde sale y pedir guia nueva.
        Assert.Equal(EstadoDespacho.ORIGEN_PENDIENTE, despacho.Estado);
        Assert.True(despacho.RequiereGuia);
    }

    [Fact]
    public void Un_despacho_entregado_ya_no_se_reasigna()
    {
        var despacho = Crear();
        despacho.DeterminarOrigen("CD-01", TipoUbicacion.CENTRO_DISTRIBUCION);
        despacho.AsignarGuia("LE-1", "u", "LogiExpress");
        despacho.ActualizarEstadoEntrega(EstadoDespacho.ENTREGADA, null, DateTime.UtcNow);

        Assert.Throws<ExcepcionTransicionDespachoInvalida>(
            () => despacho.ReasignarMetodoEntrega(MetodoEntrega.RECOGIDA_TIENDA, "TDA-07", null));
    }

    [Fact]
    public void Anular_un_despacho_entregado_no_hace_nada()
    {
        var despacho = Crear();
        despacho.DeterminarOrigen("CD-01", TipoUbicacion.CENTRO_DISTRIBUCION);
        despacho.AsignarGuia("LE-1", "u", "LogiExpress");
        despacho.ActualizarEstadoEntrega(EstadoDespacho.ENTREGADA, null, DateTime.UtcNow);

        Assert.False(despacho.Anular());
        Assert.Equal(EstadoDespacho.ENTREGADA, despacho.Estado);
    }
}
