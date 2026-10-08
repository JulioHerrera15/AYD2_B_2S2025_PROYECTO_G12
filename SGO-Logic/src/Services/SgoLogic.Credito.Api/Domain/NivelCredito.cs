namespace SgoLogic.Credito.Api.Domain;

/// <summary>Nivel del perfil. Define el limite aprobado; NO_CALIFICA siempre va con limite 0.</summary>
public enum NivelCredito
{
    EXCELENTE,
    BUENO,
    REGULAR,
    NO_CALIFICA
}

/// <summary>Estado de la linea de credito. BLOQUEADA es la pausa por una cuenta vencida.</summary>
public enum EstadoLineaCredito
{
    ACTIVA,
    INACTIVA,
    BLOQUEADA
}

/// <summary>Codigos de motivo de descarte, en el orden en que la politica los evalua.</summary>
public static class CodigosMotivoCredito
{
    public const string SinHistorial = "SIN_HISTORIAL";

    /// <summary>v2: el cliente tiene una cuenta a credito vencida sin pagar. Se evalua justo despues de SIN_HISTORIAL.</summary>
    public const string DeudaVencida = "DEUDA_VENCIDA";
    public const string IncidenteGrave = "INCIDENTE_GRAVE";
    public const string AntiguedadInsuficiente = "ANTIGUEDAD_INSUFICIENTE";
    public const string DevolucionesExcesivas = "DEVOLUCIONES_EXCESIVAS";
    public const string PuntajeInsuficiente = "PUNTAJE_INSUFICIENTE";

    // Motivos que solo aparecen al evaluar una compra concreta (bloque 2).
    public const string CupoInsuficiente = "CUPO_INSUFICIENTE";
    public const string LineaBloqueada = "LINEA_BLOQUEADA";
    public const string OrdenCancelada = "ORDEN_CANCELADA";
}
