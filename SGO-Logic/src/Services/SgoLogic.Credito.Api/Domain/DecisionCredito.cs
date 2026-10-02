namespace SgoLogic.Credito.Api.Domain;

public enum ResultadoDecisionCredito
{
    APROBADA,
    RECHAZADA,

    /// <summary>La orden se cancelo antes de evaluarse: la evaluacion tardia se rechaza sin apartar cupo.</summary>
    ANULADA_SIN_CARGO
}

/// <summary>
/// Sustento auditable de cada solicitud de credito: con que perfil, limite y
/// disponible se decidio y por que. Una por orden (indice unico), lo que hace
/// idempotente la evaluacion.
/// </summary>
public sealed class DecisionCredito
{
    private DecisionCredito()
    {
    }

    private DecisionCredito(
        string ordenId,
        string clienteId,
        decimal monto,
        ResultadoDecisionCredito resultado,
        string? codigoMotivo,
        int puntaje,
        NivelCredito nivel,
        decimal limiteAprobado,
        decimal disponibleAntes,
        string versionPolitica,
        DateTime decididoEn)
    {
        Id = Guid.NewGuid();
        OrdenId = ordenId;
        ClienteId = clienteId;
        Monto = monto;
        Resultado = resultado;
        CodigoMotivo = codigoMotivo;
        Puntaje = puntaje;
        Nivel = nivel;
        LimiteAprobado = limiteAprobado;
        DisponibleAntes = disponibleAntes;
        VersionPolitica = versionPolitica;
        DecididoEn = decididoEn;
    }

    public Guid Id { get; private set; }
    public string OrdenId { get; private set; } = string.Empty;
    public string ClienteId { get; private set; } = string.Empty;
    public decimal Monto { get; private set; }
    public ResultadoDecisionCredito Resultado { get; private set; }
    public string? CodigoMotivo { get; private set; }
    public int Puntaje { get; private set; }
    public NivelCredito Nivel { get; private set; }
    public decimal LimiteAprobado { get; private set; }
    public decimal DisponibleAntes { get; private set; }
    public string VersionPolitica { get; private set; } = string.Empty;
    public DateTime DecididoEn { get; private set; }

    public static DecisionCredito Aprobada(
        string ordenId, string clienteId, decimal monto, PerfilCrediticio perfil, LineaCredito linea, decimal disponibleAntes, DateTime ahora) =>
        new(ordenId, clienteId, monto, ResultadoDecisionCredito.APROBADA, null,
            perfil.Puntaje, perfil.Nivel, linea.LimiteAprobado, disponibleAntes, perfil.VersionPolitica, ahora);

    public static DecisionCredito Rechazada(
        string ordenId, string clienteId, decimal monto, string codigoMotivo, PerfilCrediticio? perfil,
        LineaCredito? linea, string versionPolitica, DateTime ahora) =>
        new(ordenId, clienteId, monto, ResultadoDecisionCredito.RECHAZADA, codigoMotivo,
            perfil?.Puntaje ?? 0, perfil?.Nivel ?? NivelCredito.NO_CALIFICA, linea?.LimiteAprobado ?? 0m,
            linea?.Disponible ?? 0m, perfil?.VersionPolitica ?? versionPolitica, ahora);

    /// <summary>La orden se cancelo sin que Credito hubiera recibido su evaluacion.</summary>
    public static DecisionCredito AnuladaSinCargo(string ordenId, string clienteId, string versionPolitica, DateTime ahora) =>
        new(ordenId, clienteId, 0m, ResultadoDecisionCredito.ANULADA_SIN_CARGO, CodigosMotivoCredito.OrdenCancelada,
            0, NivelCredito.NO_CALIFICA, 0m, 0m, versionPolitica, ahora);

    /// <summary>
    /// Una evaluacion llego despues de la cancelacion: la decision ANULADA_SIN_CARGO
    /// pasa a RECHAZADA con ORDEN_CANCELADA y el monto pedido, sin tocar la linea.
    /// </summary>
    public void RechazarPorOrdenCancelada(decimal monto, DateTime ahora)
    {
        Resultado = ResultadoDecisionCredito.RECHAZADA;
        CodigoMotivo = CodigosMotivoCredito.OrdenCancelada;
        Monto = monto;
        DecididoEn = ahora;
    }
}
