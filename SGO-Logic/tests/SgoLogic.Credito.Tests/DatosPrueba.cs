using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SgoLogic.Contracts.Eventos.IntegracionErp;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;
using SgoLogic.Credito.Tests.Dobles;

namespace SgoLogic.Credito.Tests;

/// <summary>
/// Reloj fijo, politica por defecto y los seis clientes de demo (tabla 2.8 del
/// requerimiento) como historial del ERP.
/// </summary>
public static class DatosPrueba
{
    public static readonly DateTime Ahora = new(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);

    public static PoliticaCreditoOptions Opciones() => new();

    public static PoliticaCredito Politica() => new(Opciones());

    /// <summary>Historial con los valores de la tabla; ClienteDesde = Ahora - dias y FechaCorte = Ahora.</summary>
    public static HistorialClienteErpActualizadoData Historial(
        string email,
        int dias,
        int compras,
        decimal monto,
        int puntuales,
        int tardios,
        int maxAtraso,
        int devoluciones,
        int graves,
        int otros,
        DateTime? fechaCorte = null) =>
        new(
            email,
            "ERP-" + email.Split('@')[0].ToUpperInvariant(),
            Ahora.AddDays(-dias),
            compras,
            monto,
            "GTQ",
            puntuales,
            tardios,
            maxAtraso,
            devoluciones,
            graves,
            otros,
            fechaCorte ?? Ahora,
            "LOTE-PRUEBA");

    public static HistorialClienteErpActualizadoData Cliente1() => Historial("cliente1@sgo.local", 1100, 18, 14500m, 18, 0, 0, 1, 0, 0);
    public static HistorialClienteErpActualizadoData Cliente2() => Historial("cliente2@sgo.local", 500, 9, 8400m, 8, 1, 6, 1, 0, 0);
    public static HistorialClienteErpActualizadoData Cliente3() => Historial("cliente3@sgo.local", 300, 5, 6000m, 4, 1, 12, 1, 0, 0);
    public static HistorialClienteErpActualizadoData Cliente4() => Historial("cliente4@sgo.local", 800, 10, 9000m, 3, 7, 45, 2, 0, 1);
    public static HistorialClienteErpActualizadoData Cliente5() => Historial("cliente5@sgo.local", 40, 2, 900m, 2, 0, 0, 0, 0, 0);
    public static HistorialClienteErpActualizadoData Cliente6() => Historial("cliente6@sgo.local", 900, 14, 11000m, 14, 0, 0, 1, 1, 0);

    /// <summary>Entidad de dominio a partir del historial, para probar la politica sin servicio.</summary>
    public static HistorialCliente Entidad(HistorialClienteErpActualizadoData datos) =>
        new(
            datos.ClienteId,
            datos.CodigoClienteErp,
            datos.ClienteDesde,
            datos.ComprasUltimos12Meses,
            datos.MontoCompradoUltimos12Meses,
            datos.Moneda,
            datos.PagosPuntuales,
            datos.PagosTardios,
            datos.MaxDiasAtraso,
            datos.Devoluciones,
            datos.IncidentesGraves,
            datos.OtrosIncidentes,
            datos.FechaCorte,
            datos.LoteOrigen,
            Ahora);

    public static ServicioPerfilCrediticio ServicioPerfil(AlmacenCreditoFalso almacen, OutboxWriterFalso outbox, Func<DateTime>? reloj = null) =>
        new(almacen, outbox, Politica(), NullLogger<ServicioPerfilCrediticio>.Instance, reloj ?? (() => Ahora));

    public static ServicioElegibilidad ServicioElegibilidad(IConsultasCredito consultas) =>
        new(consultas, Options.Create(Opciones()), () => Ahora);
}
