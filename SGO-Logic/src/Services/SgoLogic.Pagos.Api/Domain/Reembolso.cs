using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Pagos.Api.Domain;

/// <summary>Que se reembolsa con un <see cref="Reembolso" />.</summary>
public enum ConceptoReembolso
{
    /// <summary>Lo cobrado por una orden que se cancelo despues de pagarse. ReferenciaId = ordenId.</summary>
    CANCELACION_ORDEN,

    /// <summary>Lo cobrado (o parte) en un abono a credito. ReferenciaId = reembolsoId que manda Credito.</summary>
    ABONO_CREDITO
}

/// <summary>PENDIENTE espera al worker (primer intento o reintento); APLICADO y FALLIDO son finales.</summary>
public enum EstadoReembolso
{
    PENDIENTE,
    APLICADO,
    FALLIDO
}

/// <summary>
/// Reembolso a la tarjeta que no se pierde: se registra PENDIENTE en la misma
/// transaccion que el evento que lo origina y un worker lo lleva al proveedor. La clave
/// de idempotencia hacia PayKlik es <see cref="Id" />, estable entre intentos, asi que
/// un reintento nunca devuelve dos veces. Ante falla tecnica se reprograma con backoff
/// hasta agotar los intentos; un rechazo del proveedor es final.
///
/// No es la devolucion de ordenes con tarjeta (DevolucionRegistrada), que sigue en
/// <c>ServicioReembolso</c> sin cambios.
/// </summary>
public sealed class Reembolso
{
    public const int LargoMaximoCodigo = 60;
    public const int LargoMaximoMotivo = 300;

    private Reembolso()
    {
    }

    public Reembolso(
        ConceptoReembolso concepto,
        string referenciaId,
        Guid pagoId,
        string clienteId,
        decimal monto,
        Moneda moneda,
        DateTime ahora,
        string? abonoId = null)
    {
        if (string.IsNullOrWhiteSpace(referenciaId))
        {
            throw new ArgumentException("La referencia del reembolso es obligatoria.", nameof(referenciaId));
        }

        if (monto <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto del reembolso debe ser mayor que cero.");
        }

        Id = Guid.NewGuid();
        Concepto = concepto;
        ReferenciaId = referenciaId;
        PagoId = pagoId;
        ClienteId = clienteId;
        AbonoId = abonoId;
        Monto = monto;
        Moneda = moneda;
        Estado = EstadoReembolso.PENDIENTE;
        Intentos = 0;
        CreadoEn = ahora;
        ProximoIntentoEn = ahora;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public ConceptoReembolso Concepto { get; private set; }

    /// <summary>OrdenId (CANCELACION_ORDEN) o reembolsoId de Credito (ABONO_CREDITO); unica por concepto.</summary>
    public string ReferenciaId { get; private set; } = string.Empty;

    /// <summary>Pago APLICADO cuyo cobro se devuelve.</summary>
    public Guid PagoId { get; private set; }

    public string ClienteId { get; private set; } = string.Empty;

    /// <summary>Solo ABONO_CREDITO: abono de Credito, para el evento de resultado.</summary>
    public string? AbonoId { get; private set; }

    public decimal Monto { get; private set; }
    public Moneda Moneda { get; private set; }
    public EstadoReembolso Estado { get; private set; }

    /// <summary>Llamadas al proveedor ya hechas.</summary>
    public int Intentos { get; private set; }

    /// <summary>Cuando el worker puede volver a intentarlo. Null en estado final.</summary>
    public DateTime? ProximoIntentoEn { get; private set; }

    public string? AutorizacionId { get; private set; }
    public string? CodigoRespuesta { get; private set; }

    /// <summary>Ultimo motivo de falla (tecnica o rechazo), para el evento de fallo.</summary>
    public string? MotivoFalla { get; private set; }

    public DateTime CreadoEn { get; private set; }
    public DateTime? ResueltoEn { get; private set; }

    /// <summary>Concurrencia optimista: dos workers no resuelven el mismo reembolso.</summary>
    public int Version { get; private set; }

    public bool EsFinal => Estado != EstadoReembolso.PENDIENTE;

    public void MarcarAplicado(string autorizacionId, string codigoRespuesta, DateTime ahora)
    {
        Intentos++;
        Estado = EstadoReembolso.APLICADO;
        AutorizacionId = Recortar(autorizacionId, LargoMaximoCodigo);
        CodigoRespuesta = Recortar(codigoRespuesta, LargoMaximoCodigo);
        MotivoFalla = null;
        ProximoIntentoEn = null;
        ResueltoEn = ahora;
        Version++;
    }

    /// <summary>Rechazo del proveedor (o falla sin reintento posible): final.</summary>
    public void MarcarFallido(string codigoRespuesta, string motivo, DateTime ahora, bool contarIntento = true)
    {
        if (contarIntento)
        {
            Intentos++;
        }

        Estado = EstadoReembolso.FALLIDO;
        CodigoRespuesta = Recortar(codigoRespuesta, LargoMaximoCodigo);
        MotivoFalla = Recortar(motivo, LargoMaximoMotivo);
        ProximoIntentoEn = null;
        ResueltoEn = ahora;
        Version++;
    }

    /// <summary>
    /// Falla tecnica (timeout, 5xx, red, circuito abierto). Cuenta el intento y, si aun
    /// quedan, reprograma con backoff exponencial base * 2^(intentos-1) topado en
    /// <paramref name="backoffMaximo" />. Al agotar <paramref name="maxIntentos" /> queda
    /// FALLIDO. Devuelve true si quedo FALLIDO.
    /// </summary>
    public bool RegistrarFallaTecnica(
        string codigo,
        string motivo,
        DateTime ahora,
        int maxIntentos,
        TimeSpan backoffBase,
        TimeSpan backoffMaximo)
    {
        Intentos++;
        CodigoRespuesta = Recortar(codigo, LargoMaximoCodigo);
        MotivoFalla = Recortar(motivo, LargoMaximoMotivo);
        Version++;

        if (Intentos >= maxIntentos)
        {
            Estado = EstadoReembolso.FALLIDO;
            ProximoIntentoEn = null;
            ResueltoEn = ahora;
            return true;
        }

        var espera = TimeSpan.FromTicks(Math.Min(
            backoffMaximo.Ticks,
            (long)(backoffBase.Ticks * Math.Pow(2, Intentos - 1))));
        ProximoIntentoEn = ahora.Add(espera);
        return false;
    }

    private static string? Recortar(string? valor, int largo) =>
        valor is not null && valor.Length > largo ? valor[..largo] : valor;
}
