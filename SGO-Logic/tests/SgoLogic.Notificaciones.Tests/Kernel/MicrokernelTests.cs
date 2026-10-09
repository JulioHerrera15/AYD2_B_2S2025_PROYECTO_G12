using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Domain;
using SgoLogic.Notificaciones.Api.Kernel.Abstractions;
using SgoLogic.Notificaciones.Api.Kernel.Core;
using SgoLogic.Notificaciones.Api.Kernel.Plugins;
using Xunit;

namespace SgoLogic.Notificaciones.Tests.Kernel;

public sealed class MicrokernelTests
{
    private sealed class RepositorioPendientesMemoria : IRepositorioNotificacionesPendientes
    {
        public List<NotificacionPendiente> Agregadas { get; } = new();

        public void Agregar(NotificacionPendiente notificacion)
        {
            Agregadas.Add(notificacion);
        }

        public Task<IReadOnlyList<NotificacionPendiente>> ListarParaReintentarAsync(DateTime ahora, int maximo, CancellationToken cancelacion) =>
            Task.FromResult<IReadOnlyList<NotificacionPendiente>>(Agregadas.Where(p => p.TocaReintentar(ahora)).Take(maximo).ToList());

        public Task GuardarCambiosAsync(CancellationToken cancelacion) => Task.CompletedTask;
    }

    private sealed class MockMensajeriaSimulada : IMockMensajeriaClient
    {
        public bool FallarEmail { get; set; }
        public bool FallarSms { get; set; }
        public int EmailsEnviados { get; private set; }
        public int SmsEnviados { get; private set; }
        public int PushEnviados { get; private set; }
        public int WebhooksEnviados { get; private set; }

        public Task EnviarAsync(string clienteId, string estadoOrden, string ordenId, CancellationToken ct = default) =>
            EnviarEmailAsync(clienteId, $"Orden {ordenId}", $"Estado {estadoOrden}", ct);

        public Task EnviarEmailAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default)
        {
            EmailsEnviados++;
            if (FallarEmail) throw new HttpRequestException("Fallo simulado en Email");
            return Task.CompletedTask;
        }

        public Task EnviarSmsAsync(string destinatario, string cuerpo, CancellationToken ct = default)
        {
            SmsEnviados++;
            if (FallarSms) throw new HttpRequestException("Fallo simulado en SMS");
            return Task.CompletedTask;
        }

        public Task EnviarPushAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default)
        {
            PushEnviados++;
            return Task.CompletedTask;
        }

        public Task EnviarWebhookAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default)
        {
            WebhooksEnviados++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Registro_de_plugins_ordena_por_prioridad_y_resuelve_por_canal()
    {
        var registry = new PluginRegistry();
        var mock = new MockMensajeriaSimulada();

        var email = new EmailNotificacionPlugin(mock, NullLogger<EmailNotificacionPlugin>.Instance);
        var sms = new SmsNotificacionPlugin(mock, NullLogger<SmsNotificacionPlugin>.Instance);
        var push = new PushNotificacionPlugin(mock, NullLogger<PushNotificacionPlugin>.Instance);

        registry.Registrar(push);
        registry.Registrar(email);
        registry.Registrar(sms);

        var todos = registry.ObtenerTodos();
        Assert.Equal(3, todos.Count);
        // Mayor prioridad primero: Email (100) > SMS (80) > Push (70)
        Assert.Equal("EMAIL", todos[0].Metadata.Canal);
        Assert.Equal("SMS", todos[1].Metadata.Canal);
        Assert.Equal("PUSH", todos[2].Metadata.Canal);

        var pluginsSms = registry.ObtenerPorCanal("SMS");
        Assert.Single(pluginsSms);
        Assert.Equal("SMS", pluginsSms[0].Metadata.Canal);
    }

    [Fact]
    public async Task Despacho_exitoso_a_canal_especifico_ejecuta_plugin_y_retorna_ok()
    {
        var registry = new PluginRegistry();
        var mock = new MockMensajeriaSimulada();
        var repo = new RepositorioPendientesMemoria();

        registry.Registrar(new EmailNotificacionPlugin(mock, NullLogger<EmailNotificacionPlugin>.Instance));
        registry.Registrar(new SmsNotificacionPlugin(mock, NullLogger<SmsNotificacionPlugin>.Instance));

        var kernel = new NotificacionesKernel(registry, repo, NullLogger<NotificacionesKernel>.Instance);

        var solicitud = new SolicitudNotificacion(
            EventId: "EVT-001",
            Canal: "EMAIL",
            Destinatario: "cliente@sgo.local",
            Asunto: "Bienvenida",
            Cuerpo: "Gracias por registrarte"
        );

        var resultado = await kernel.DespacharAsync(solicitud);

        Assert.True(resultado.TodoExitoso);
        Assert.Equal(1, resultado.PluginsEjecutados);
        Assert.Equal(0, resultado.FallosRegistrados);
        Assert.Equal(1, mock.EmailsEnviados);
        Assert.Equal(0, mock.SmsEnviados);
        Assert.Empty(repo.Agregadas);
    }

    [Fact]
    public async Task Aislamiento_de_fallas_cuando_un_plugin_falla_no_rompe_el_kernel_y_encola_pendiente()
    {
        var registry = new PluginRegistry();
        var mock = new MockMensajeriaSimulada { FallarEmail = true };
        var repo = new RepositorioPendientesMemoria();

        registry.Registrar(new EmailNotificacionPlugin(mock, NullLogger<EmailNotificacionPlugin>.Instance));
        var kernel = new NotificacionesKernel(registry, repo, NullLogger<NotificacionesKernel>.Instance);

        var solicitud = new SolicitudNotificacion(
            EventId: "EVT-FAIL-01",
            Canal: "EMAIL",
            Destinatario: "cliente@sgo.local",
            Asunto: "Alerta de Orden",
            Cuerpo: "Tu orden ha cambiado"
        );

        var resultado = await kernel.DespacharAsync(solicitud);

        Assert.False(resultado.TodoExitoso);
        Assert.Equal(1, resultado.FallosRegistrados);
        Assert.Single(repo.Agregadas);
        Assert.Equal("EVT-FAIL-01", repo.Agregadas[0].EventId);
        Assert.Equal("EMAIL", repo.Agregadas[0].Canal);
    }

    [Fact]
    public async Task Desactivar_plugin_dinamicamente_evita_su_ejecucion()
    {
        var registry = new PluginRegistry();
        var mock = new MockMensajeriaSimulada();
        var repo = new RepositorioPendientesMemoria();

        var smsPlugin = new SmsNotificacionPlugin(mock, NullLogger<SmsNotificacionPlugin>.Instance);
        registry.Registrar(smsPlugin);

        // Desactivamos el plugin dinámicamente
        smsPlugin.ConfigurarHabilitado(false);

        var kernel = new NotificacionesKernel(registry, repo, NullLogger<NotificacionesKernel>.Instance);

        var solicitud = new SolicitudNotificacion(
            EventId: "EVT-DIS-01",
            Canal: "SMS",
            Destinatario: "+50255551234",
            Asunto: "PIN",
            Cuerpo: "Tu PIN es 123456"
        );

        var resultado = await kernel.DespacharAsync(solicitud);

        Assert.False(resultado.TodoExitoso);
        Assert.Equal(0, resultado.PluginsEjecutados);
        Assert.Equal(0, mock.SmsEnviados);
    }

    [Fact]
    public async Task ServicioNotificaciones_despacha_a_traves_del_kernel_correctamente()
    {
        var registry = new PluginRegistry();
        var mock = new MockMensajeriaSimulada();
        var repo = new RepositorioPendientesMemoria();

        registry.Registrar(new EmailNotificacionPlugin(mock, NullLogger<EmailNotificacionPlugin>.Instance));
        var kernel = new NotificacionesKernel(registry, repo, NullLogger<NotificacionesKernel>.Instance);

        var servicio = new ServicioNotificaciones(
            mock,
            repo,
            NullLogger<ServicioNotificaciones>.Instance,
            kernel
        );

        var data = new OrdenEstadoCambiadoData(
            OrdenId: "ORD-KERNEL-01",
            NumeroOrden: "000099",
            ClienteId: "usuario@sgo.local",
            EstadoAnterior: EstadoOrden.CREADA,
            EstadoNuevo: EstadoOrden.CONFIRMADA,
            Motivo: null,
            PasoSaga: null,
            EsCompensacion: false,
            CambiadoEn: DateTime.UtcNow
        );

        await servicio.NotificarEstadoOrdenAsync(data, "EVT-SAGA-99", CancellationToken.None);

        Assert.Equal(1, mock.EmailsEnviados);
        Assert.Empty(repo.Agregadas);
    }
}
