namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Asiento del ledger de movimientos fisicos de una existencia (HU-016, EV-07).
/// Es append-only: nace de un cambio ya aplicado a <see cref="Existencia.CantidadFisica" />
/// y nunca se edita ni se borra, para que el historial de entradas y salidas de una
/// ubicacion sea reconstruible.
///
/// Deliberadamente NO registra reservas ni liberaciones: esas mueven la cantidad
/// reservada, no la fisica, y su rastro vive en el agregado <see cref="Reserva" />.
/// Tampoco lleva token de concurrencia porque solo se inserta; el bloqueo optimista
/// de ADR-005 protege la existencia, que es la fila que si se actualiza.
/// </summary>
public sealed class MovimientoExistencia
{
    private MovimientoExistencia()
    {
        Sku = string.Empty;
        UbicacionId = string.Empty;
    }

    /// <summary>
    /// Registra el movimiento que explica un cambio de cantidad fisica.
    /// <paramref name="delta" /> es la diferencia con signo respecto de la cantidad
    /// anterior: su signo decide el <see cref="Tipo" /> y su valor absoluto queda en
    /// <see cref="Cantidad" />. Un delta de cero no es un movimiento y se rechaza;
    /// quien llama debe omitir el asiento en ese caso.
    /// </summary>
    public MovimientoExistencia(
        string sku,
        string ubicacionId,
        int delta,
        int cantidadFisicaResultante,
        MotivoMovimiento motivo,
        DateTime registradoEn)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("El SKU es obligatorio.", nameof(sku));
        }

        if (string.IsNullOrWhiteSpace(ubicacionId))
        {
            throw new ArgumentException("El identificador de la ubicacion es obligatorio.", nameof(ubicacionId));
        }

        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "Un movimiento tiene que cambiar la cantidad fisica: el delta no puede ser cero.");
        }

        if (cantidadFisicaResultante < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cantidadFisicaResultante),
                "La cantidad fisica resultante no puede ser negativa.");
        }

        Id = Guid.NewGuid();
        Sku = sku;
        UbicacionId = ubicacionId;
        Tipo = delta > 0 ? TipoMovimiento.ENTRADA : TipoMovimiento.SALIDA;
        Cantidad = Math.Abs(delta);
        CantidadFisicaResultante = cantidadFisicaResultante;
        Motivo = motivo;
        RegistradoEn = registradoEn.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; }

    public string UbicacionId { get; private set; }

    public TipoMovimiento Tipo { get; private set; }

    /// <summary>Unidades que se movieron, siempre positivo. El sentido lo da <see cref="Tipo" />.</summary>
    public int Cantidad { get; private set; }

    /// <summary>Cantidad fisica con la que quedo la existencia despues del movimiento.</summary>
    public int CantidadFisicaResultante { get; private set; }

    public MotivoMovimiento Motivo { get; private set; }

    public DateTime RegistradoEn { get; private set; }
}
