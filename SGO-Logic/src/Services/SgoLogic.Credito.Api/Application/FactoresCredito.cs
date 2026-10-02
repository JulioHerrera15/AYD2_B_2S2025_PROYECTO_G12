using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Api.Application;

/// <summary>Los cuatro factores que ve el cliente; agrupan los seis de la politica.</summary>
public enum FactorCliente
{
    PagosATiempo,
    MontoCompras,
    TiempoYFrecuencia,
    DevolucionesYReclamos
}

/// <summary>Puntos obtenidos sobre el maximo posible de un factor del cliente.</summary>
public sealed record FactorEvaluado(FactorCliente Factor, decimal Puntos, decimal Maximo)
{
    public decimal Proporcion => Maximo <= 0m ? 1m : Math.Clamp(Puntos / Maximo, 0m, 1m);

    /// <summary>80% o mas FAVORABLE, 50% o mas ACEPTABLE, menos A_MEJORAR.</summary>
    public string Valoracion => Proporcion switch
    {
        >= 0.8m => ValoracionesFactor.Favorable,
        >= 0.5m => ValoracionesFactor.Aceptable,
        _ => ValoracionesFactor.AMejorar
    };
}

/// <summary>
/// Agrupa los factores de la politica en los cuatro que se explican al cliente:
/// puntualidad; monto; antiguedad + frecuencia; devoluciones + incidentes menores.
/// </summary>
public static class FactoresCredito
{
    public static IReadOnlyList<FactorEvaluado> Desde(ResumenCredito resumen, PoliticaCreditoOptions opciones) =>
        new[]
        {
            new FactorEvaluado(FactorCliente.PagosATiempo, resumen.PuntosPuntualidad, opciones.PuntosPuntualidad),
            new FactorEvaluado(FactorCliente.MontoCompras, resumen.PuntosMonto, opciones.PuntosMonto),
            new FactorEvaluado(
                FactorCliente.TiempoYFrecuencia,
                resumen.PuntosAntiguedad + resumen.PuntosFrecuencia,
                opciones.PuntosAntiguedad + opciones.PuntosFrecuencia),
            new FactorEvaluado(
                FactorCliente.DevolucionesYReclamos,
                resumen.PuntosDevoluciones + resumen.PuntosIncidentes,
                opciones.PuntosDevoluciones + opciones.PuntosIncidentes)
        };

    /// <summary>El factor con menor proporcion de puntos; ante empate, el primero de la lista.</summary>
    public static FactorCliente MasDebil(IReadOnlyList<FactorEvaluado> factores)
    {
        var masDebil = factores[0];
        foreach (var factor in factores)
        {
            if (factor.Proporcion < masDebil.Proporcion)
            {
                masDebil = factor;
            }
        }

        return masDebil.Factor;
    }
}
