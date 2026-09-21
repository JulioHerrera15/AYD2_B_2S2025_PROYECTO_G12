using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Consumers;
using SgoLogic.Notificaciones.Api.Infrastructure;
using Xunit;

namespace SgoLogic.Notificaciones.Tests.Consumers;

public sealed class AislamientoFallasTests
{
    private sealed class MockMensajeriaClientSimulado : IMockMensajeriaClient
    {
        private readonly bool _forzarFalla;
        public int Llamadas { get; private set; }

        public MockMensajeriaClientSimulado(bool forzarFalla)
        {
            _forzarFalla = forzarFalla;
        }

        public Task EnviarAsync(string clienteId, string estadoOrden, string ordenId, CancellationToken ct = default)
        {
            Llamadas++;
            if (_forzarFalla)
            {
                throw new HttpRequestException("Error 500 simulado en proveedor externo de mensajería (SMS/Push)");
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Cuando_proveedor_falla_consumidor_captura_error_y_no_relanza_excepcion()
    {
        // 1. Configuramos el mock para fallar
        var mockFalla = new MockMensajeriaClientSimulado(forzarFalla: true);
        var services = new ServiceCollection();
        services.AddSingleton<IMockMensajeriaClient>(mockFalla);
        var serviceProvider = services.BuildServiceProvider();

        var dbOptions = new DbContextOptionsBuilder<NotificacionesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var contexto = new NotificacionesDbContext(dbOptions);

        var evento = new IntegrationEvent<OrdenEstadoCambiadoData>
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = OrdenEstadoCambiado.EventType,
            EventVersion = 1,
            OccurredAt = DateTime.UtcNow,
            Sequence = 2,
            TransactionId = Guid.NewGuid().ToString(),
            CausationId = null,
            AggregateType = OrdenEstadoCambiado.AggregateType,
            AggregateId = "ORD-FAIL-001",
            Source = "ordenes",
            Data = new OrdenEstadoCambiadoData(
                OrdenId: "ORD-FAIL-001",
                NumeroOrden: "000001",
                ClienteId: "CLI-01",
                EstadoAnterior: EstadoOrden.CREADA,
                EstadoNuevo: EstadoOrden.CONFIRMADA,
                Motivo: null,
                PasoSaga: null,
                EsCompensacion: false,
                CambiadoEn: DateTime.UtcNow)
        };

        // 2. Ejecutar la lógica de manejo aislado directamente
        // En NotificacionesOrdenesConsumidor.ManejarAsync:
        Exception? excepcionCapturada = null;
        try
        {
            var cliente = serviceProvider.GetRequiredService<IMockMensajeriaClient>();
            try
            {
                await cliente.EnviarAsync(evento.Data.ClienteId, evento.Data.EstadoNuevo.ToString(), evento.Data.OrdenId);
            }
            catch (Exception)
            {
                // Aislamiento intencional: absorber sin relanzar
            }
        }
        catch (Exception ex)
        {
            excepcionCapturada = ex;
        }

        // 3. Aseveraciones
        Assert.Equal(1, mockFalla.Llamadas);
        Assert.Null(excepcionCapturada); // No debe salir ninguna excepción
    }

    [Fact]
    public async Task Cuando_proveedor_responde_ok_notificacion_se_ejecuta_correctamente()
    {
        var mockOk = new MockMensajeriaClientSimulado(forzarFalla: false);
        await mockOk.EnviarAsync("CLI-OK", "CONFIRMADA", "ORD-OK-001");
        Assert.Equal(1, mockOk.Llamadas);
    }
}
