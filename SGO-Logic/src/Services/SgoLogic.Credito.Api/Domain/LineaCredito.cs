namespace SgoLogic.Credito.Api.Domain;

/// <summary>
/// Linea de credito del cliente. El limite lo fija el perfil; el saldo utilizado lo
/// mueven los cargos (compra aprobada) y su anulacion, por eso al recalcular se
/// conserva. BLOQUEADA todavia no se usa. Version es el token de concurrencia que
/// impide que dos compras simultaneas superen el limite.
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

    /// <summary>
    /// Aparta <paramref name="monto" /> del disponible. Lanza si la linea no esta
    /// ACTIVA o el disponible no alcanza: quien llama lo verifica antes; esto es la
    /// red de seguridad del dominio.
    /// </summary>
    public void Cargar(decimal monto, DateTime ahora)
    {
        if (monto <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto a cargar debe ser mayor que 0.");
        }

        if (Estado != EstadoLineaCredito.ACTIVA || Disponible < monto)
        {
            throw new InvalidOperationException("La linea no tiene disponible suficiente para este cargo.");
        }

        SaldoUtilizado += monto;
        ActualizadoEn = ahora;
        Version++;
    }

    /// <summary>Devuelve <paramref name="monto" /> al disponible (anulacion de un cargo). El saldo nunca queda negativo.</summary>
    public void Liberar(decimal monto, DateTime ahora)
    {
        if (monto <= 0m)
        {
            return;
        }

        SaldoUtilizado = Math.Max(0m, SaldoUtilizado - monto);
        ActualizadoEn = ahora;
        Version++;
    }

    /// <summary>Reinicio de demo: deja el saldo utilizado en 0 sin tocar limite ni estado.</summary>
    public void ReiniciarSaldo(DateTime ahora)
    {
        SaldoUtilizado = 0m;
        ActualizadoEn = ahora;
        Version++;
    }
}
