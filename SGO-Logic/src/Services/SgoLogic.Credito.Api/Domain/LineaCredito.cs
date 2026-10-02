namespace SgoLogic.Credito.Api.Domain;

/// <summary>
/// Linea de credito del cliente. El limite lo fija el perfil; el saldo utilizado lo
/// moveran los cargos y abonos de un bloque posterior, por eso al recalcular se
/// conserva. En este bloque SaldoUtilizado queda en 0 y BLOQUEADA no se usa.
/// </summary>
public sealed class LineaCredito
{
    private LineaCredito()
    {
    }

    public LineaCredito(string clienteId, string moneda, DateTime ahora)
    {
        ClienteId = clienteId;
        Moneda = moneda;
        Estado = EstadoLineaCredito.INACTIVA;
        ActualizadoEn = ahora;
        Version = 1;
    }

    public string ClienteId { get; private set; } = string.Empty;
    public decimal LimiteAprobado { get; private set; }
    public decimal SaldoUtilizado { get; private set; }
    public string Moneda { get; private set; } = string.Empty;
    public EstadoLineaCredito Estado { get; private set; }
    public DateTime ActualizadoEn { get; private set; }

    /// <summary>Concurrencia optimista.</summary>
    public int Version { get; private set; }

    /// <summary>LimiteAprobado - SaldoUtilizado, nunca menor que 0.</summary>
    public decimal Disponible => CalcularDisponible(LimiteAprobado, SaldoUtilizado);

    public static decimal CalcularDisponible(decimal limite, decimal saldoUtilizado) =>
        Math.Max(0m, limite - saldoUtilizado);

    /// <summary>
    /// Ajusta la linea al resultado del perfil. Elegible: ACTIVA con el limite nuevo.
    /// No elegible: INACTIVA con limite 0. El saldo utilizado no se toca, y una linea
    /// BLOQUEADA sigue bloqueada aunque cambie el limite.
    /// </summary>
    public void Recalcular(bool elegible, decimal limite, string moneda, DateTime ahora)
    {
        LimiteAprobado = elegible ? limite : 0m;
        Moneda = moneda;

        if (Estado != EstadoLineaCredito.BLOQUEADA)
        {
            Estado = elegible ? EstadoLineaCredito.ACTIVA : EstadoLineaCredito.INACTIVA;
        }

        ActualizadoEn = ahora;
        Version++;
    }
}
