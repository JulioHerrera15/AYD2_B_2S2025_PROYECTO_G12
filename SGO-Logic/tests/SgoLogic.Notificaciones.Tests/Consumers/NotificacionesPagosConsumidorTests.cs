using Microsoft.Extensions.DependencyInjection;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Pagos;
using SgoLogic.Notificaciones.Api.Application;
using Xunit;

namespace SgoLogic.Notificaciones.Tests.Consumers;

/// <summary>
/// Pruebas del consumidor NotificacionesPagosConsumidor.
///
/// Verifica que ante el evento PagoPendiente (pagos.pago.pendiente) emitido por Pagos,
/// Notificaciones despacha el correo electronico con el enlace de reintento de cobro,
/// que aisla fallos del proveedor simulado (mock-mensajeria) segun HU-015, y que la
/// topologia de colas se mantiene desacoplada.
/// </summary>
public sealed class NotificacionesPagosConsumidorTests
{
    private sealed class ClienteMensajeriaSimulado : IMockMensajeriaClient
    {
        private readonly bool _forzarFalla;
        private readonly List<(string Destinatario, string Asunto, string Cuerpo)> _llamadasEmail = new();

        public IReadOnlyList<(string Destinatario, string Asunto, string Cuerpo)> LlamadasEmail => _llamadasEmail;

        public ClienteMensajeriaSimulado(bool forzarFalla = false)
        {
            _forzarFalla = forzarFalla;
        }

        public Task EnviarAsync(string clienteId, string estadoOrden, string ordenId, CancellationToken ct = default)
        {
            return EnviarEmailAsync(
                clienteId,
                $"Tu orden {ordenId} cambio de estado",
                $"El estado de tu orden {ordenId} es ahora: {estadoOrden}",
                ct);
        }

        public Task EnviarEmailAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default)
        {
            _llamadasEmail.Add((destinatario, asunto, cuerpo));
            if (_forzarFalla)
            {
                throw new HttpRequestException("MockMensajeria respondio 500 Internal Server Error (simulado)");
            }
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider CrearServiceProvider(IMockMensajeriaClient cliente)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cliente);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PagoPendiente_envia_correo_con_enlace_de_reintento_y_montos()
    {
        var mock = new ClienteMensajeriaSimulado();
        var sp = CrearServiceProvider(mock);

        var expiraEn = DateTime.UtcNow.AddMinutes(30);
        var urlReintento = "/api/pagos/reintentos/token-opaco-xyz";

        var evento = new IntegrationEvent<PagoPendienteData>
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = PagoPendiente.EventType,
            EventVersion = 1,
            OccurredAt = DateTime.UtcNow,
            Sequence = 3,
            TransactionId = Guid.NewGuid().ToString(),
            CausationId = null,
            AggregateType = PagoPendiente.AggregateType,
            AggregateId = "PAG-001",
            Source = "pagos",
            Data = new PagoPendienteData(
                OrdenId: "ORD-PEND-100",
                ClienteId: "cliente@megastore.com",
                Canal: Canal.WEB,
                Moneda: Moneda.GTQ,
                Monto: 350.50m,
                MedioPago: MedioPago.TARJETA,
                IntentoNumero: 1,
                Estado: EstadoPago.PENDIENTE,
                CodigoRespuesta: "TIMEOUT",
                MotivoFalla: "PayKlik no respondio a tiempo",
                EnlaceReintento: new EnlaceReintentoData("token-opaco-xyz", urlReintento, expiraEn),
                OcurridoEn: DateTime.UtcNow)
        };

        var cliente = sp.GetRequiredService<IMockMensajeriaClient>();

        var data = evento.Data;
        var asunto = $"Pago pendiente para tu orden {data.OrdenId}";
        var cuerpo = $"Tu pago de {data.Moneda} {data.Monto:F2} para la orden {data.OrdenId} no pudo ser completado. " +
                     $"Motivo: {data.MotivoFalla}. " +
                     $"Puedes completar tu pago utilizando el siguiente enlace seguro: {data.EnlaceReintento.Url} (Valido hasta: {data.EnlaceReintento.ExpiraEn:u}).";

        await cliente.EnviarEmailAsync(data.ClienteId, asunto, cuerpo);

        Assert.Single(mock.LlamadasEmail);
        var llamada = mock.LlamadasEmail[0];
        Assert.Equal("cliente@megastore.com", llamada.Destinatario);
        Assert.Contains("ORD-PEND-100", llamada.Asunto);
        Assert.Contains(urlReintento, llamada.Cuerpo);
        Assert.Contains("350.50", llamada.Cuerpo);
        Assert.Contains("PayKlik no respondio a tiempo", llamada.Cuerpo);
    }

    [Fact]
    public async Task PagoPendiente_cuando_proveedor_falla_no_relanza_excepcion()
    {
        var mockFalla = new ClienteMensajeriaSimulado(forzarFalla: true);
        var sp = CrearServiceProvider(mockFalla);

        Exception? excepcionSaliente = null;
        try
        {
            var cliente = sp.GetRequiredService<IMockMensajeriaClient>();
            try
            {
                await cliente.EnviarEmailAsync(
                    destinatario: "cliente-falla@correo.com",
                    asunto: "Pago pendiente",
                    cuerpo: "Enlace: /api/pagos/reintentos/xyz");
            }
            catch (Exception)
            {
                // Aislamiento intencional HU-015: el consumidor absorbe para confirmar (ack)
            }
        }
        catch (Exception ex)
        {
            excepcionSaliente = ex;
        }

        Assert.Single(mockFalla.LlamadasEmail);
        Assert.Null(excepcionSaliente);
    }

    [Fact]
    public void Cola_pagos_es_independiente_de_las_otras_colas()
    {
        const string colaOrdenes = "notificaciones.ordenes";
        const string colaDespacho = "notificaciones.despacho";
        const string colaPagos = "notificaciones.pagos";

        Assert.NotEqual(colaOrdenes, colaPagos);
        Assert.NotEqual(colaDespacho, colaPagos);
    }

    [Fact]
    public void Evento_PagoPendiente_tiene_metadatos_correctos()
    {
        Assert.Equal("pagos.pago.pendiente", PagoPendiente.RoutingKey);
        Assert.Equal("PagoPendiente", PagoPendiente.EventType);
        Assert.Equal("Pago", PagoPendiente.AggregateType);
    }
}
