namespace SgoLogic.Credito.Api.Domain;

/// <summary>
/// Perfil crediticio precalculado de un cliente: el resultado de la ultima
/// evaluacion de la politica. La consulta de elegibilidad solo lee esta fila (y la
/// linea), nunca recalcula.
/// </summary>
public sealed class PerfilCrediticio
{
    private PerfilCrediticio()
    {
    }

    public PerfilCrediticio(string clienteId, EvaluacionCredito evaluacion, DateTime fechaCorteHistorial, DateTime calculadoEn)
    {
        ClienteId = clienteId;
        Asignar(evaluacion, fechaCorteHistorial, calculadoEn);
        Version = 1;
    }

    public string ClienteId { get; private set; } = string.Empty;
    public int Puntaje { get; private set; }
    public NivelCredito Nivel { get; private set; }
    public bool Elegible { get; private set; }
    public string? CodigoMotivo { get; private set; }

    /// <summary>Limite que dio la politica; la linea de credito lo copia.</summary>
    public decimal LimiteAprobado { get; private set; }

    public decimal PuntosPuntualidad { get; private set; }
    public decimal PuntosMonto { get; private set; }
    public decimal PuntosAntiguedad { get; private set; }
    public decimal PuntosFrecuencia { get; private set; }
    public decimal PuntosDevoluciones { get; private set; }
    public decimal PuntosIncidentes { get; private set; }

    public string VersionPolitica { get; private set; } = string.Empty;
    public DateTime FechaCorteHistorial { get; private set; }
    public DateTime CalculadoEn { get; private set; }

    /// <summary>Secuencia del agregado para el envelope: sube solo cuando se emite PerfilCrediticioActualizado.</summary>
    public int Secuencia { get; private set; }

    /// <summary>Concurrencia optimista: sube en cada recalculo.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Aplica una evaluacion nueva. Devuelve true si cambio algo que el cliente o
    /// los demas contextos ven: puntaje, nivel, elegibilidad, motivo o limite.
    /// </summary>
    public bool Recalcular(EvaluacionCredito evaluacion, DateTime fechaCorteHistorial, DateTime calculadoEn)
    {
        var cambio = Puntaje != evaluacion.Puntaje
            || Nivel != evaluacion.Nivel
            || Elegible != evaluacion.Elegible
            || !string.Equals(CodigoMotivo, evaluacion.CodigoMotivo, StringComparison.Ordinal)
            || LimiteAprobado != evaluacion.LimiteAprobado;

        Asignar(evaluacion, fechaCorteHistorial, calculadoEn);
        Version++;
        return cambio;
    }

    /// <summary>Siguiente valor de secuencia para el evento que se va a encolar.</summary>
    public int SiguienteSecuencia() => ++Secuencia;

    private void Asignar(EvaluacionCredito evaluacion, DateTime fechaCorteHistorial, DateTime calculadoEn)
    {
        Puntaje = evaluacion.Puntaje;
        Nivel = evaluacion.Nivel;
        Elegible = evaluacion.Elegible;
        CodigoMotivo = evaluacion.CodigoMotivo;
        LimiteAprobado = evaluacion.LimiteAprobado;

        // Dos decimales para guardar; el puntaje ya se calculo con los valores exactos.
        PuntosPuntualidad = Math.Round(evaluacion.Factores.Puntualidad, 2, MidpointRounding.AwayFromZero);
        PuntosMonto = Math.Round(evaluacion.Factores.Monto, 2, MidpointRounding.AwayFromZero);
        PuntosAntiguedad = Math.Round(evaluacion.Factores.Antiguedad, 2, MidpointRounding.AwayFromZero);
        PuntosFrecuencia = Math.Round(evaluacion.Factores.Frecuencia, 2, MidpointRounding.AwayFromZero);
        PuntosDevoluciones = Math.Round(evaluacion.Factores.Devoluciones, 2, MidpointRounding.AwayFromZero);
        PuntosIncidentes = Math.Round(evaluacion.Factores.Incidentes, 2, MidpointRounding.AwayFromZero);

        VersionPolitica = evaluacion.VersionPolitica;
        FechaCorteHistorial = fechaCorteHistorial;
        CalculadoEn = calculadoEn;
    }
}
