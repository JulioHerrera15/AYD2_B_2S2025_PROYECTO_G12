using Microsoft.Extensions.Options;

namespace SgoLogic.Credito.Api.Domain;

/// <summary>Puntos obtenidos en cada factor del puntaje, sin redondear el total.</summary>
public sealed record PuntosFactores(
    decimal Puntualidad,
    decimal Monto,
    decimal Antiguedad,
    decimal Frecuencia,
    decimal Devoluciones,
    decimal Incidentes)
{
    public static readonly PuntosFactores Ninguno = new(0m, 0m, 0m, 0m, 0m, 0m);

    public decimal Total => Puntualidad + Monto + Antiguedad + Frecuencia + Devoluciones + Incidentes;
}

/// <summary>
/// Comportamiento de pago del cliente con su propio credito (no el del ERP): cuentas
/// pagadas a tiempo, pagadas tarde y vencidas sin pagar. Entra a la politica v2.
/// </summary>
public sealed record ComportamientoPropio(int PagadasATiempo, int PagadasTarde, int VencidasSinPagar)
{
    public static readonly ComportamientoPropio Ninguno = new(0, 0, 0);
}

/// <summary>Resultado de aplicar la politica a un historial.</summary>
public sealed record EvaluacionCredito(
    int Puntaje,
    NivelCredito Nivel,
    bool Elegible,
    string? CodigoMotivo,
    decimal LimiteAprobado,
    PuntosFactores Factores,
    string VersionPolitica);

/// <summary>
/// Politica de elegibilidad para "Compra ahora, paga en 30 dias". Servicio de
/// dominio puro: no toca datos ni reloj, recibe el historial y el instante de
/// evaluacion.
///
/// El puntaje se calcula siempre, aunque haya descarte. Los descartes se evaluan en
/// orden fijo y el primero que aplica define el motivo: SIN_HISTORIAL,
/// DEUDA_VENCIDA, INCIDENTE_GRAVE, ANTIGUEDAD_INSUFICIENTE, DEVOLUCIONES_EXCESIVAS y
/// PUNTAJE_INSUFICIENTE. Un cliente no elegible queda NO_CALIFICA con limite 0
/// aunque su puntaje sea alto.
///
/// Version 2026.10-v2: suma el comportamiento propio. Las cuentas pagadas cuentan
/// como pagos puntuales o tardios junto con los del ERP, y una cuenta vencida sin
/// pagar descarta (DEUDA_VENCIDA). Con comportamiento propio en cero el resultado es
/// identico al de la v1.
/// </summary>
public sealed class PoliticaCredito
{
    private readonly PoliticaCreditoOptions _opciones;

    public PoliticaCredito(IOptions<PoliticaCreditoOptions> opciones)
        : this(opciones.Value)
    {
    }

    public PoliticaCredito(PoliticaCreditoOptions opciones)
    {
        _opciones = opciones;
    }

    public PoliticaCreditoOptions Opciones => _opciones;

    public EvaluacionCredito Evaluar(HistorialCliente? historial, DateTime ahora) =>
        Evaluar(historial, ComportamientoPropio.Ninguno, ahora);

    public EvaluacionCredito Evaluar(HistorialCliente? historial, ComportamientoPropio comportamiento, DateTime ahora)
    {
        ArgumentNullException.ThrowIfNull(comportamiento);

        if (historial is null)
        {
            return new EvaluacionCredito(
                0, NivelCredito.NO_CALIFICA, false, CodigosMotivoCredito.SinHistorial, 0m,
                PuntosFactores.Ninguno, _opciones.VersionPolitica);
        }

        var factores = CalcularFactores(historial, comportamiento, ahora);
        var puntaje = (int)Math.Round(factores.Total, 0, MidpointRounding.AwayFromZero);
        var motivo = comportamiento.VencidasSinPagar > 0
            ? CodigosMotivoCredito.DeudaVencida
            : MotivoDescarte(historial, ahora, puntaje);

        if (motivo is not null)
        {
            return new EvaluacionCredito(
                puntaje, NivelCredito.NO_CALIFICA, false, motivo, 0m, factores, _opciones.VersionPolitica);
        }

        var (nivel, limite) = NivelPara(puntaje);
        return new EvaluacionCredito(puntaje, nivel, true, null, limite, factores, _opciones.VersionPolitica);
    }

    /// <summary>Nivel y limite que corresponden a un puntaje, sin considerar descartes.</summary>
    public (NivelCredito Nivel, decimal Limite) NivelPara(int puntaje)
    {
        if (puntaje >= _opciones.UmbralExcelente)
        {
            return (NivelCredito.EXCELENTE, _opciones.LimiteExcelente);
        }

        if (puntaje >= _opciones.UmbralBueno)
        {
            return (NivelCredito.BUENO, _opciones.LimiteBueno);
        }

        if (puntaje >= _opciones.PuntajeMinimo)
        {
            return (NivelCredito.REGULAR, _opciones.LimiteRegular);
        }

        return (NivelCredito.NO_CALIFICA, 0m);
    }

    public PuntosFactores CalcularFactores(HistorialCliente historial, DateTime ahora) =>
        CalcularFactores(historial, ComportamientoPropio.Ninguno, ahora);

    public PuntosFactores CalcularFactores(HistorialCliente historial, ComportamientoPropio comportamiento, DateTime ahora)
    {
        var puntuales = historial.PagosPuntuales + comportamiento.PagadasATiempo;
        var tardios = historial.PagosTardios + comportamiento.PagadasTarde;
        var pagos = puntuales + tardios;
        var puntualidad = pagos <= 0
            ? _opciones.PuntosPuntualidadSinPagos
            : _opciones.PuntosPuntualidad * puntuales / pagos;

        var monto = _opciones.PuntosMonto * Proporcion(historial.MontoCompradoUltimos12Meses, _opciones.MontoReferencia);
        var antiguedad = _opciones.PuntosAntiguedad * Proporcion(historial.DiasComoCliente(ahora), _opciones.DiasAntiguedadReferencia);
        var frecuencia = _opciones.PuntosFrecuencia * Proporcion(historial.ComprasUltimos12Meses, _opciones.ComprasReferencia);
        var devoluciones = _opciones.PuntosDevoluciones * (1m - Proporcion(historial.TasaDevolucion, _opciones.TasaDevolucionMaxima));

        var incidentes = historial.OtrosIncidentes switch
        {
            <= 0 => _opciones.PuntosIncidentes,
            1 => _opciones.PuntosUnIncidente,
            _ => 0m
        };

        return new PuntosFactores(puntualidad, monto, antiguedad, frecuencia, devoluciones, incidentes);
    }

    private string? MotivoDescarte(HistorialCliente historial, DateTime ahora, int puntaje)
    {
        if (historial.IncidentesGraves > 0)
        {
            return CodigosMotivoCredito.IncidenteGrave;
        }

        if (historial.DiasComoCliente(ahora) < _opciones.AntiguedadMinimaDias)
        {
            return CodigosMotivoCredito.AntiguedadInsuficiente;
        }

        if (historial.TasaDevolucion > _opciones.TasaDevolucionMaxima)
        {
            return CodigosMotivoCredito.DevolucionesExcesivas;
        }

        if (puntaje < _opciones.PuntajeMinimo)
        {
            return CodigosMotivoCredito.PuntajeInsuficiente;
        }

        return null;
    }

    /// <summary>valor / referencia acotado a [0, 1].</summary>
    private static decimal Proporcion(decimal valor, decimal referencia) =>
        referencia <= 0m ? 1m : Math.Clamp(valor / referencia, 0m, 1m);
}
