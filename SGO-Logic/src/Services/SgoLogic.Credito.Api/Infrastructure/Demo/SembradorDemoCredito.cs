using System.Text.Json;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Infrastructure.Demo;

/// <summary>
/// Siembra de demo (Credito:SembrarDemo). Si historial_cliente esta vacia, aplica
/// cada registro de Datos/historial-demo.json con
/// <see cref="ServicioPerfilCrediticio.AplicarHistorialAsync" />: el perfil lo
/// calcula la politica real, el archivo solo trae el historial. ClienteDesde se
/// calcula al sembrar (ahora - diasComoCliente) y FechaCorte es ahora.
/// demo@sgo.local no esta en el archivo a proposito: es el caso SIN_HISTORIAL.
/// </summary>
public static class SembradorDemoCredito
{
    public const string RutaArchivo = "Datos/historial-demo.json";
    public const string LoteOrigen = "SEMILLA-DEMO";

    private static readonly JsonSerializerOptions OpcionesJson = new() { PropertyNameCaseInsensitive = true };

    public sealed record RegistroDemo(
        string Email,
        string CodigoClienteErp,
        int DiasComoCliente,
        int Compras12m,
        decimal Monto12m,
        int PagosPuntuales,
        int PagosTardios,
        int MaxDiasAtraso,
        int Devoluciones,
        int IncidentesGraves,
        int OtrosIncidentes);

    public static async Task<int> SembrarAsync(IServiceProvider servicios, ILogger logger, CancellationToken cancelacion)
    {
        using (var alcance = servicios.CreateScope())
        {
            var repositorio = alcance.ServiceProvider.GetRequiredService<IRepositorioCredito>();
            if (await repositorio.HayHistorialesAsync(cancelacion))
            {
                logger.LogInformation("Siembra de credito omitida: historial_cliente ya tiene datos.");
                return 0;
            }
        }

        var ruta = Path.Combine(AppContext.BaseDirectory, RutaArchivo);
        if (!File.Exists(ruta))
        {
            logger.LogWarning("Siembra de credito omitida: no se encontro {Ruta}.", ruta);
            return 0;
        }

        var registros = JsonSerializer.Deserialize<List<RegistroDemo>>(await File.ReadAllTextAsync(ruta, cancelacion), OpcionesJson)
            ?? new List<RegistroDemo>();

        var ahora = DateTime.UtcNow;
        var moneda = servicios.GetRequiredService<IOptions<PoliticaCreditoOptions>>().Value.Moneda;

        foreach (var registro in registros)
        {
            // Un alcance por cliente: cada aplicacion es su propia transaccion.
            using var alcance = servicios.CreateScope();
            var servicio = alcance.ServiceProvider.GetRequiredService<ServicioPerfilCrediticio>();

            await servicio.AplicarHistorialAsync(
                Desde(registro, ahora, moneda),
                cancelacion);
        }

        logger.LogInformation("Siembra de credito aplicada: {Cantidad} historiales de demo.", registros.Count);
        return registros.Count;
    }

    public static HistorialClienteErpActualizadoData Desde(RegistroDemo registro, DateTime ahora, string moneda) =>
        new(
            registro.Email.Trim().ToLowerInvariant(),
            registro.CodigoClienteErp,
            ahora.AddDays(-registro.DiasComoCliente),
            registro.Compras12m,
            registro.Monto12m,
            moneda,
            registro.PagosPuntuales,
            registro.PagosTardios,
            registro.MaxDiasAtraso,
            registro.Devoluciones,
            registro.IncidentesGraves,
            registro.OtrosIncidentes,
            ahora,
            LoteOrigen);
}
