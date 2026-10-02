using Microsoft.Extensions.Logging.Abstractions;
using SgoLogic.Contracts.Eventos.Despacho;
using SgoLogic.Notificaciones.Api.Application;
using SgoLogic.Notificaciones.Api.Domain;
using Xunit;

namespace SgoLogic.Notificaciones.Tests.Consumers;

/// <summary>
/// B3: el aviso de pedido listo para recoger va al email del cliente (ClienteId del
/// evento), nunca al OrdenId; un envio fallido se guarda como pendiente en lugar de
/// perderse, y el reintento exitoso lo marca enviado.
/// </summary>
public sealed class NotificacionesDespachoConsumidorTests
{
    private static readonly DateTime Ahora = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    // ── Dobles hechos a mano ──────────────────────────────────────────────────

    private sealed class MensajeriaSimulada : IMockMensajeriaClient
    {
        public bool Falla { get; set; }

        public List<(string Destinatario, string Asunto, string Cuerpo)> Enviados { get; } = new();

        public int Intentos { get; private set; }

        public Task EnviarAsync(string clienteId, string estadoOrden, string ordenId, CancellationToken ct = default) =>
            EnviarEmailAsync(clienteId, estadoOrden, ordenId, ct);

        public Task EnviarEmailAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default)
        {
            Intentos++;
            if (Falla)
            {
                throw new HttpRequestException("Proveedor de mensajeria no disponible (simulado).");
            }

            Enviados.Add((destinatario, asunto, cuerpo));
            return Task.CompletedTask;
        }
    }

    private sealed class PendientesEnMemoria : IRepositorioNotificacionesPendientes
    {
        public List<NotificacionPendiente> Todas { get; } = new();

        public int Guardados { get; private set; }

        public void Agregar(NotificacionPendiente notificacion) => Todas.Add(notificacion);

        public Task<IReadOnlyList<NotificacionPendiente>> ListarParaReintentarAsync(DateTime ahora, int maximo, CancellationToken cancelacion) =>
            Task.FromResult<IReadOnlyList<NotificacionPendiente>>(Todas
                .Where(n => n.TocaReintentar(ahora))
                .OrderBy(n => n.ProximoIntentoEn)
                .Take(maximo)
                .ToList());

        public Task GuardarCambiosAsync(CancellationToken cancelacion)
        {
            Guardados++;
            return Task.CompletedTask;
        }
    }

    private static (ServicioNotificaciones Servicio, MensajeriaSimulada Mensajeria, PendientesEnMemoria Pendientes) Construir(
        Func<DateTime>? reloj = null)
    {
        var mensajeria = new MensajeriaSimulada();
        var pendientes = new PendientesEnMemoria();
        var servicio = new ServicioNotificaciones(
            mensajeria, pendientes, NullLogger<ServicioNotificaciones>.Instance, reloj ?? (() => Ahora));
        return (servicio, mensajeria, pendientes);
    }

    private static PedidoListoParaRecogerData Listo(string? clienteId = "cliente1@sgo.local") =>
        new("DES-2026-000001", "ORD-2026-000123", "TIENDA-01-CENTRAL", Ahora, clienteId);

    // ── Destinatario ──────────────────────────────────────────────────────────

    [Fact]
    public async Task El_aviso_de_listo_para_recoger_va_al_email_del_cliente_y_no_al_OrdenId()
    {
        var (servicio, mensajeria, pendientes) = Construir();

        await servicio.NotificarListoParaRecogerAsync(Listo(), "evt-1", CancellationToken.None);

        var enviado = Assert.Single(mensajeria.Enviados);
        Assert.Equal("cliente1@sgo.local", enviado.Destinatario);
        Assert.NotEqual("ORD-2026-000123", enviado.Destinatario);
        Assert.Contains("ORD-2026-000123", enviado.Asunto);
        Assert.Contains("TIENDA-01-CENTRAL", enviado.Cuerpo);
        Assert.Empty(pendientes.Todas);
    }

    [Fact]
    public async Task Un_evento_sin_ClienteId_no_se_envia_a_nadie()
    {
        var (servicio, mensajeria, pendientes) = Construir();

        await servicio.NotificarListoParaRecogerAsync(Listo(clienteId: null), "evt-2", CancellationToken.None);

        Assert.Equal(0, mensajeria.Intentos);
        Assert.Empty(pendientes.Todas);
    }

    // ── Envio fallido: pendiente, sin relanzar ────────────────────────────────

    [Fact]
    public async Task Un_envio_fallido_se_guarda_como_pendiente_y_no_relanza()
    {
        var (servicio, mensajeria, pendientes) = Construir();
        mensajeria.Falla = true;

        var ex = await Record.ExceptionAsync(
            () => servicio.NotificarListoParaRecogerAsync(Listo(), "evt-3", CancellationToken.None));

        Assert.Null(ex);
        var pendiente = Assert.Single(pendientes.Todas);
        Assert.Equal("evt-3", pendiente.EventId);
        Assert.Equal("cliente1@sgo.local", pendiente.Destinatario);
        Assert.Equal(EstadoNotificacionPendiente.PENDIENTE, pendiente.Estado);
        Assert.Equal(0, pendiente.Intentos);
        Assert.Equal(Ahora.AddSeconds(10), pendiente.ProximoIntentoEn);
        Assert.Contains("no disponible", pendiente.UltimoError);

        // El consumidor guarda la pendiente junto con la marca de procesado; el
        // servicio no guarda por su cuenta.
        Assert.Equal(0, pendientes.Guardados);
    }

    // ── Reintento ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_reintento_exitoso_marca_la_pendiente_como_enviada()
    {
        var momento = Ahora;
        var (servicio, mensajeria, pendientes) = Construir(() => momento);
        mensajeria.Falla = true;
        await servicio.NotificarListoParaRecogerAsync(Listo(), "evt-4", CancellationToken.None);

        // Antes de los 10 s no toca reintentar.
        momento = Ahora.AddSeconds(5);
        Assert.Equal(0, await servicio.ReintentarPendientesAsync(CancellationToken.None));

        mensajeria.Falla = false;
        momento = Ahora.AddSeconds(10);
        var tratadas = await servicio.ReintentarPendientesAsync(CancellationToken.None);

        Assert.Equal(1, tratadas);
        var pendiente = Assert.Single(pendientes.Todas);
        Assert.Equal(EstadoNotificacionPendiente.ENVIADA, pendiente.Estado);
        Assert.Equal(momento, pendiente.EnviadaEn);
        Assert.Equal(1, pendiente.Intentos);
        Assert.Equal("cliente1@sgo.local", Assert.Single(mensajeria.Enviados).Destinatario);
        Assert.Equal(1, pendientes.Guardados);
    }

    [Fact]
    public async Task Los_reintentos_usan_backoff_exponencial_y_tras_seis_fallos_la_pendiente_queda_FALLIDA()
    {
        var momento = Ahora;
        var (servicio, mensajeria, pendientes) = Construir(() => momento);
        mensajeria.Falla = true;
        await servicio.NotificarListoParaRecogerAsync(Listo(), "evt-5", CancellationToken.None);
        var pendiente = Assert.Single(pendientes.Todas);

        var esperas = new List<TimeSpan>();
        for (var reintento = 1; reintento <= NotificacionPendiente.MaximoIntentos; reintento++)
        {
            esperas.Add(pendiente.ProximoIntentoEn - momento);
            momento = pendiente.ProximoIntentoEn;
            await servicio.ReintentarPendientesAsync(CancellationToken.None);
        }

        Assert.Equal(
            new[] { 10, 20, 40, 80, 160, 320 }.Select(s => TimeSpan.FromSeconds(s)),
            esperas);
        Assert.Equal(EstadoNotificacionPendiente.FALLIDA, pendiente.Estado);
        Assert.Equal(NotificacionPendiente.MaximoIntentos, pendiente.Intentos);

        // Una FALLIDA ya no se reintenta.
        momento = momento.AddHours(1);
        Assert.Equal(0, await servicio.ReintentarPendientesAsync(CancellationToken.None));
    }

    // ── Contratos ─────────────────────────────────────────────────────────────

    [Fact]
    public void Evento_PedidoListoParaRecoger_tiene_routing_key_correcta_y_ClienteId_opcional_al_final()
    {
        Assert.Equal("despacho.pedido.listo-para-recoger", PedidoListoParaRecoger.RoutingKey);
        Assert.Equal("PedidoListoParaRecoger", PedidoListoParaRecoger.EventType);

        // Un productor anterior que no manda ClienteId sigue siendo valido.
        var sinCliente = new PedidoListoParaRecogerData("DES-1", "ORD-1", "TIENDA-01-CENTRAL", Ahora);
        Assert.Null(sinCliente.ClienteId);
    }
}
