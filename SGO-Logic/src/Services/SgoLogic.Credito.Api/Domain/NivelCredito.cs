namespace SgoLogic.Credito.Api.Domain;

/// <summary>Nivel del perfil. Define el limite aprobado; NO_CALIFICA siempre va con limite 0.</summary>
public enum NivelCredito
{
    EXCELENTE,
    BUENO,
    REGULAR,
    NO_CALIFICA
}

/// <summary>Estado de la linea de credito. BLOQUEADA queda reservada para un bloque posterior.</summary>
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
    public const string IncidenteGrave = "INCIDENTE_GRAVE";
    public const string AntiguedadInsuficiente = "ANTIGUEDAD_INSUFICIENTE";
    public const string DevolucionesExcesivas = "DEVOLUCIONES_EXCESIVAS";
    public const string PuntajeInsuficiente = "PUNTAJE_INSUFICIENTE";
}
