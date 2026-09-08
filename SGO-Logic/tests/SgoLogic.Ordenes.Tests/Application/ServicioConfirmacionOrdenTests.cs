using System.Text.Json;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Ordenes.Api.Application.Ordenes;
using SgoLogic.Ordenes.Api.Domain;
using SgoLogic.Ordenes.Tests.Application.Dobles;
using Xunit;

namespace SgoLogic.Ordenes.Tests.Application;

public sealed class ServicioConfirmacionOrdenTests
{
    private static Orden CrearOrden() => new(
        "ORD-2026-000777",
        "000777",
        "CLI-000045",
        Canal.WEB,
        MetodoEntrega.DOMICILIO,
        Moneda.GTQ,
        new[] { new LineaOrden("SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m) },
        150.00m);

    private static Orden CrearOrdenEn(params EstadoOrden[] camino)
    {
        var orden = CrearOrden();
        foreach (var estado in camino)
        {
            orden.TransicionarA(estado);
        }

        return orden;
    }

    private static Orden CrearOrdenEntregada() => CrearOrdenEn(
        EstadoOrden.RESERVA_SOLICITADA,
        EstadoOrden.INVENTARIO_RESERVADO,
        EstadoOrden.COBRO_SOLICITADO,
        EstadoOrden.PAGO_APLICADO,
        EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL,
        EstadoOrden.CONFIRMADA,
        EstadoOrden.DESPACHO_SOLICITADO,
        EstadoOrden.EN_PREPARACION,
        EstadoOrden.EN_TRANSITO,
        EstadoOrden.ENTREGADA);

    private static (ServicioConfirmacionOrden Servicio,
        RepositorioOrdenesFalso Ordenes,
        RepositorioMensajesSalientesFalso Mensajes,
        UnidadDeTrabajoFalsa Unidad) Construir()
    {
        var ordenes = new RepositorioOrdenesFalso();
        var mensajes = new RepositorioMensajesSalientesFalso();
        var unidad = new UnidadDeTrabajoFalsa();
        var servicio = new ServicioConfirmacionOrden(ordenes, mensajes, unidad);
        return (servicio, ordenes, mensajes, unidad);
    }

    [Fact]
    public async Task Confirmar_orden_en_estado_valido_escribe_transicion_y_mensaje_saliente()
    {
        var (servicio, ordenes, mensajes, unidad) = Construir();
        var orden = CrearOrden();
        ordenes.Precargar(orden);

        var resultado = await servicio.ConfirmarOrdenAsync(orden.Id, "Cliente confirmo la compra", CancellationToken.None);

        Assert.False(resultado.YaConfirmada);
        Assert.Equal(EstadoOrden.RESERVA_SOLICITADA, orden.Estado);
        Assert.Single(orden.Transiciones);

        var mensaje = Assert.Single(mensajes.Mensajes);
        Assert.Equal("orden.estado-cambiado", mensaje.TipoEvento);
        Assert.Equal(EstadoMensajeSaliente.Pendiente, mensaje.Estado);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Confirmar_con_transicion_invalida_no_escribe_nada_y_lanza_la_excepcion()
    {
        var (servicio, ordenes, mensajes, unidad) = Construir();
        var orden = CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA, EstadoOrden.INVENTARIO_RESERVADO);
        ordenes.Precargar(orden);
        var transicionesAntes = orden.Transiciones.Count;

        await Assert.ThrowsAsync<ExcepcionTransicionInvalida>(
            () => servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None));

        Assert.Empty(mensajes.Mensajes);
        Assert.Equal(0, unidad.Confirmaciones);
        Assert.Equal(transicionesAntes, orden.Transiciones.Count);
        Assert.Equal(EstadoOrden.INVENTARIO_RESERVADO, orden.Estado);
    }

    [Fact]
    public async Task Confirmar_dos_veces_la_misma_orden_no_duplica_transicion_ni_mensaje_saliente()
    {
        var (servicio, ordenes, mensajes, unidad) = Construir();
        var orden = CrearOrden();
        ordenes.Precargar(orden);

        var primera = await servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None);
        var transicionesTrasPrimera = orden.Transiciones.Count;

        var segunda = await servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None);

        Assert.False(primera.YaConfirmada);
        Assert.True(segunda.YaConfirmada);
        Assert.Equal(transicionesTrasPrimera, orden.Transiciones.Count);
        Assert.Single(mensajes.Mensajes);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task La_secuencia_de_la_transicion_de_confirmacion_es_la_siguiente_sin_huecos()
    {
        var (servicio, ordenes, _, _) = Construir();
        var orden = CrearOrden();
        var secuenciaAntes = orden.Secuencia;
        ordenes.Precargar(orden);

        await servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None);

        var transicionConfirmacion = orden.Transiciones
            .OrderBy(transicion => transicion.Secuencia)
            .Last();

        Assert.Equal(secuenciaAntes + 1, transicionConfirmacion.Secuencia);
        Assert.Equal(orden.Secuencia, transicionConfirmacion.Secuencia);

        var secuencias = orden.Transiciones
            .Select(transicion => transicion.Secuencia)
            .OrderBy(secuencia => secuencia)
            .ToArray();

        Assert.Equal(Enumerable.Range(2, secuencias.Length).ToArray(), secuencias);
    }

    [Fact]
    public async Task El_evento_del_outbox_lleva_el_mismo_agregado_id_que_la_orden()
    {
        var (servicio, ordenes, mensajes, _) = Construir();
        var orden = CrearOrden();
        ordenes.Precargar(orden);

        await servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None);

        var mensaje = Assert.Single(mensajes.Mensajes);
        Assert.Equal(orden.Id, mensaje.AgregadoId);
        Assert.Equal(orden.Secuencia, mensaje.Secuencia);

        using var documento = JsonDocument.Parse(mensaje.Payload);
        var raiz = documento.RootElement;
        Assert.Equal(orden.Id, raiz.GetProperty("aggregateId").GetString());
        Assert.Equal("Orden", raiz.GetProperty("aggregateType").GetString());
        Assert.Equal(orden.Secuencia, raiz.GetProperty("sequence").GetInt32());
        Assert.Equal(orden.Id, raiz.GetProperty("data").GetProperty("ordenId").GetString());
    }

    [Fact]
    public async Task Confirmar_una_orden_recien_creada_tiene_exito()
    {
        var (servicio, ordenes, mensajes, unidad) = Construir();
        var orden = CrearOrden();
        Assert.Equal(EstadoOrden.CREADA, orden.Estado);
        ordenes.Precargar(orden);

        var resultado = await servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None);

        Assert.False(resultado.YaConfirmada);
        Assert.Equal(EstadoOrden.RESERVA_SOLICITADA, resultado.Orden.Estado);
        Assert.Equal(EstadoOrden.RESERVA_SOLICITADA, orden.Estado);
        Assert.Single(mensajes.Mensajes);
        Assert.Equal(1, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Confirmar_una_orden_ya_entregada_lanza_ExcepcionTransicionInvalida()
    {
        var (servicio, ordenes, mensajes, unidad) = Construir();
        var orden = CrearOrdenEntregada();
        ordenes.Precargar(orden);
        var transicionesAntes = orden.Transiciones.Count;

        await Assert.ThrowsAsync<ExcepcionTransicionInvalida>(
            () => servicio.ConfirmarOrdenAsync(orden.Id, null, CancellationToken.None));

        Assert.Equal(EstadoOrden.ENTREGADA, orden.Estado);
        Assert.Equal(transicionesAntes, orden.Transiciones.Count);
        Assert.Empty(mensajes.Mensajes);
        Assert.Equal(0, unidad.Confirmaciones);
    }

    [Fact]
    public async Task Aplicar_transicion_generica_avanza_la_maquina_de_estados_y_deja_evento()
    {
        var (servicio, ordenes, mensajes, unidad) = Construir();
        var orden = CrearOrdenEn(EstadoOrden.RESERVA_SOLICITADA);
        ordenes.Precargar(orden);

        var resultado = await servicio.AplicarTransicionAsync(
            orden.Id,
            EstadoOrden.INVENTARIO_RESERVADO,
            PasoSaga.RESERVA,
            motivo: null,
            esCompensacion: false,
            CancellationToken.None);

        Assert.False(resultado.SinCambios);
        Assert.Equal(EstadoOrden.INVENTARIO_RESERVADO, orden.Estado);
        var mensaje = Assert.Single(mensajes.Mensajes);
        Assert.Equal(orden.Id, mensaje.AgregadoId);
        Assert.Equal(orden.Secuencia, mensaje.Secuencia);
        Assert.Equal(1, unidad.Confirmaciones);
    }
}
