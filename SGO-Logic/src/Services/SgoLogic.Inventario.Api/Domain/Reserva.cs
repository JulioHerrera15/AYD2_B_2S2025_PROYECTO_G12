using System.Text.RegularExpressions;
using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Domain;

/// <summary>
/// Agregado raiz del contexto Inventario. Representa las unidades que Inventario
/// comprometio para una orden, en una unica ubicacion, con una marca de expiracion
/// (TTL, EV-23). Hay a lo sumo una reserva activa por <see cref="OrdenId" />: esa
/// es la clave de idempotencia del consumidor de <c>ordenes.reserva.solicitada</c>.
/// </summary>
public sealed partial class Reserva
{
    /// <summary>
    /// Formato del identificador de reserva: RSV-&lt;anio de 4 digitos&gt;-&lt;correlativo
    /// de 6 digitos&gt;. Lo genera siempre el servidor a partir de una secuencia de
    /// PostgreSQL; esta es la red de seguridad del dominio.
    /// </summary>
    [GeneratedRegex(@"^RSV-\d{4}-\d{6}$")]
    private static partial Regex PatronId();

    private readonly List<LineaReserva> _lineas = new();

    private Reserva()
    {
        Id = string.Empty;
        OrdenId = string.Empty;
        UbicacionId = string.Empty;
    }

    private Reserva(
        string id,
        string ordenId,
        string ubicacionId,
        TipoUbicacion tipoUbicacion,
        int ttlSegundos,
        IEnumerable<LineaReserva> lineas,
        DateTime creadaEn)
    {
        if (string.IsNullOrWhiteSpace(id) || !PatronId().IsMatch(id))
        {
            throw new ArgumentException(
                "El identificador de la reserva debe cumplir el patron RSV-<anio>-<correlativo de 6 digitos>.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(ordenId))
        {
            throw new ArgumentException("El identificador de la orden es obligatorio.", nameof(ordenId));
        }

        if (string.IsNullOrWhiteSpace(ubicacionId))
        {
            throw new ArgumentException("El identificador de la ubicacion es obligatorio.", nameof(ubicacionId));
        }

        if (ttlSegundos <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ttlSegundos), "El TTL de la reserva debe ser mayor que cero.");
        }

        var lineasMaterializadas = lineas?.ToList() ?? new List<LineaReserva>();
        if (lineasMaterializadas.Count == 0)
        {
            throw new ArgumentException("La reserva requiere al menos una linea.", nameof(lineas));
        }

        Id = id;
        OrdenId = ordenId;
        UbicacionId = ubicacionId;
        TipoUbicacion = tipoUbicacion;
        TtlSegundos = ttlSegundos;
        Estado = EstadoReserva.ACTIVA;
        Secuencia = 1;
        CreadaEn = creadaEn;
        ExpiraEn = creadaEn.AddSeconds(ttlSegundos);
        _lineas.AddRange(lineasMaterializadas);
    }

    /// <summary>Crea una reserva ACTIVA cuya expiracion se calcula sumando el TTL a la fecha de creacion.</summary>
    public static Reserva Crear(
        string id,
        string ordenId,
        string ubicacionId,
        TipoUbicacion tipoUbicacion,
        int ttlSegundos,
        IEnumerable<(string Sku, int Cantidad)> lineas,
        DateTime? creadaEn = null)
    {
        var lineasReserva = (lineas ?? Enumerable.Empty<(string, int)>())
            .Select(linea => new LineaReserva(linea.Sku, linea.Cantidad));

        return new Reserva(
            id,
            ordenId,
            ubicacionId,
            tipoUbicacion,
            ttlSegundos,
            lineasReserva,
            (creadaEn ?? DateTime.UtcNow).ToUniversalTime());
    }

    public string Id { get; private set; }

    public string OrdenId { get; private set; }

    public string UbicacionId { get; private set; }

    public TipoUbicacion TipoUbicacion { get; private set; }

    public int TtlSegundos { get; private set; }

    public EstadoReserva Estado { get; private set; }

    /// <summary>
    /// Secuencia monotonica por reservaId para el campo <c>sequence</c> del sobre
    /// (envelope seccion 3). Arranca en 1 con InventarioReservado; la expiracion
    /// (HU-002) y la liberacion (HU-003) emiten con 2, 3, ...
    /// </summary>
    public int Secuencia { get; private set; }

    public DateTime CreadaEn { get; private set; }

    public DateTime ExpiraEn { get; private set; }

    public IReadOnlyList<LineaReserva> Lineas => _lineas.AsReadOnly();

    public bool EstaActiva => Estado == EstadoReserva.ACTIVA;

    /// <summary>
    /// True mientras la reserva retiene unidades en <see cref="Existencia.CantidadReservada" />:
    /// ACTIVA (a la espera del cobro) o CONFIRMADA (pagada, a la espera de salir a ruta).
    /// </summary>
    public bool RetieneStock => Estado is EstadoReserva.ACTIVA or EstadoReserva.CONFIRMADA;

    /// <summary>Devuelve true si el TTL ya vencio en el momento indicado.</summary>
    public bool HaVencido(DateTime ahora) => ahora.ToUniversalTime() >= ExpiraEn;

    /// <summary>Marca la reserva como expirada por TTL (HU-002). Idempotente si ya no esta activa.</summary>
    public void MarcarExpirada()
    {
        if (Estado == EstadoReserva.ACTIVA)
        {
            Estado = EstadoReserva.EXPIRADA;
            Secuencia += 1;
        }
    }

    /// <summary>
    /// La orden fue pagada: la reserva deja de estar sujeta al TTL y retiene las
    /// unidades hasta que salgan a ruta. Solo es valida desde ACTIVA; repetirla
    /// sobre una reserva ya CONFIRMADA no hace nada.
    /// </summary>
    public void Confirmar()
    {
        if (Estado == EstadoReserva.CONFIRMADA)
        {
            return;
        }

        if (Estado != EstadoReserva.ACTIVA)
        {
            throw new InvalidOperationException(
                $"La reserva {Id} esta en {Estado} y solo puede confirmarse desde ACTIVA.");
        }

        Estado = EstadoReserva.CONFIRMADA;
    }

    /// <summary>
    /// Marca la reserva como consumida: sus unidades salieron fisicamente del
    /// almacen. Solo es valida desde ACTIVA o CONFIRMADA; repetirla sobre una
    /// reserva ya ENTREGADA no hace nada.
    /// </summary>
    public void MarcarEntregada()
    {
        if (Estado == EstadoReserva.ENTREGADA)
        {
            return;
        }

        if (!RetieneStock)
        {
            throw new InvalidOperationException(
                $"La reserva {Id} esta en {Estado} y ya no retiene unidades que entregar.");
        }

        Estado = EstadoReserva.ENTREGADA;
        Secuencia += 1;
    }

    /// <summary>
    /// Libera la reserva por compensacion de la saga (cobro fallido, cancelacion del
    /// cliente). Aplica sobre ACTIVA o CONFIRMADA; en cualquier otro estado no hace nada.
    /// </summary>
    public void Liberar()
    {
        if (RetieneStock)
        {
            Estado = EstadoReserva.LIBERADA;
            Secuencia += 1;
        }
    }
}
