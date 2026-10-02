using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Despacho.Api.Domain;

/// <summary>
/// Agregado raiz del contexto Despacho (HU-011, HU-012, HU-019, HU-020). Sabe desde
/// donde sale un pedido, si lleva guia y en que punto de la entrega va.
///
/// Despacho NO decide la ubicacion por su cuenta: la pregunta a Inventario, porque
/// ahi es donde realmente se descontaron las unidades. Reimplementar aqui la
/// seleccion de ubicacion duplicaria una decision ya tomada y comprometida
/// fisicamente, y las dos copias derivarian.
/// </summary>
public sealed class Despacho
{
    private readonly List<LineaDespacho> _lineas = new();

    private Despacho()
    {
        Id = string.Empty;
        OrdenId = string.Empty;
        ReservaId = string.Empty;
    }

    private Despacho(
        string id,
        string ordenId,
        string reservaId,
        MetodoEntrega metodoEntrega,
        string? ubicacionRecogidaId,
        string? direccionEntrega,
        IEnumerable<LineaDespacho> lineas,
        DateTime creadoEn)
    {
        Id = id;
        OrdenId = ordenId;
        ReservaId = reservaId;
        MetodoEntrega = metodoEntrega;
        UbicacionRecogidaId = ubicacionRecogidaId;
        DireccionEntrega = direccionEntrega;
        Estado = EstadoDespacho.ORIGEN_PENDIENTE;
        Secuencia = 1;
        CreadoEn = creadoEn.ToUniversalTime();
        _lineas.AddRange(lineas);
    }

    /// <summary>Formato <c>DES-anio-correlativo de 6 digitos</c>, igual que Orden y Reserva.</summary>
    public string Id { get; private set; }

    public string OrdenId { get; private set; }

    public string ReservaId { get; private set; }

    /// <summary>
    /// Email del cliente de la orden, tal como llega en DespachoSolicitado. Solo se usa
    /// para que PedidoListoParaRecoger diga a quien avisar. Nulo en despachos creados
    /// antes de que el evento lo trajera.
    /// </summary>
    public string? ClienteId { get; private set; }

    public MetodoEntrega MetodoEntrega { get; private set; }

    /// <summary>Nulo hasta que se determina el origen.</summary>
    public string? UbicacionOrigenId { get; private set; }

    public TipoUbicacion? TipoUbicacionOrigen { get; private set; }

    /// <summary>Tienda donde el cliente recoge, si eligio esa opcion (HU-020).</summary>
    public string? UbicacionRecogidaId { get; private set; }

    public string? DireccionEntrega { get; private set; }

    public EstadoDespacho Estado { get; private set; }

    public string? GuiaId { get; private set; }

    public string? TrackingUrl { get; private set; }

    public string? Transportista { get; private set; }

    /// <summary>Ultimo detalle informado por el transportista (HU-012).</summary>
    public string? DetalleEstado { get; private set; }

    /// <summary>Secuencia monotonica del agregado para el envelope de eventos. Empieza en 1.</summary>
    public int Secuencia { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime? EntregadoEn { get; private set; }

    /// <summary>Token de concurrencia (ADR-005).</summary>
    public int Version { get; private set; }

    public IReadOnlyList<LineaDespacho> Lineas => _lineas.AsReadOnly();

    public bool RequiereGuia => MetodoEntrega == MetodoEntrega.DOMICILIO;

    public static Despacho Crear(
        string id,
        string ordenId,
        string reservaId,
        MetodoEntrega metodoEntrega,
        string? ubicacionRecogidaId,
        string? direccionEntrega,
        IEnumerable<(string Sku, string Descripcion, int Cantidad)> lineas,
        DateTime? creadoEn = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("El identificador del despacho es obligatorio.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(ordenId))
        {
            throw new ArgumentException("El identificador de la orden es obligatorio.", nameof(ordenId));
        }

        var materializadas = lineas.Select(l => new LineaDespacho(l.Sku, l.Descripcion, l.Cantidad)).ToList();

        if (materializadas.Count == 0)
        {
            throw new ArgumentException("Un despacho necesita al menos una linea.", nameof(lineas));
        }

        return new Despacho(
            id,
            ordenId,
            reservaId,
            metodoEntrega,
            ubicacionRecogidaId,
            direccionEntrega,
            materializadas,
            creadoEn ?? DateTime.UtcNow);
    }

    /// <summary>Guarda el cliente de la orden (null o vacio lo deja sin cliente).</summary>
    public void RegistrarCliente(string? clienteId)
    {
        var limpio = clienteId?.Trim();
        ClienteId = string.IsNullOrEmpty(limpio) ? null : limpio;
    }

    /// <summary>
    /// Fija desde donde sale el pedido. En recogida en tienda el despacho queda
    /// listo (no hay guia que pedir); en domicilio queda a la espera de LogiExpress.
    /// </summary>
    public void DeterminarOrigen(string ubicacionId, TipoUbicacion tipoUbicacion)
    {
        if (string.IsNullOrWhiteSpace(ubicacionId))
        {
            throw new ArgumentException("La ubicacion de origen es obligatoria.", nameof(ubicacionId));
        }

        UbicacionOrigenId = ubicacionId;
        TipoUbicacionOrigen = tipoUbicacion;
        Estado = RequiereGuia ? EstadoDespacho.GUIA_SOLICITADA : EstadoDespacho.RECOGIDA_TIENDA;
        Avanzar();
    }

    public void AsignarGuia(string guiaId, string trackingUrl, string transportista)
    {
        if (Estado != EstadoDespacho.GUIA_SOLICITADA && Estado != EstadoDespacho.GUIA_PENDIENTE)
        {
            throw new ExcepcionTransicionDespachoInvalida(Estado, EstadoDespacho.GUIA_ASIGNADA);
        }

        GuiaId = guiaId;
        TrackingUrl = trackingUrl;
        Transportista = transportista;
        Estado = EstadoDespacho.GUIA_ASIGNADA;
        Avanzar();
    }

    /// <summary>
    /// LogiExpress no respondio. No es un error que aborte nada: el pedido se prepara
    /// y sale igual, y la guia se pide despues (criterio de aceptacion de HU-011).
    /// </summary>
    public void MarcarGuiaPendiente()
    {
        if (Estado != EstadoDespacho.GUIA_SOLICITADA)
        {
            throw new ExcepcionTransicionDespachoInvalida(Estado, EstadoDespacho.GUIA_PENDIENTE);
        }

        Estado = EstadoDespacho.GUIA_PENDIENTE;
        Avanzar();
    }

    /// <summary>
    /// Aplica un estado de entrega informado por LogiExpress (HU-012). Devuelve false
    /// si el estado ya era ese, para que quien llama no emita un evento repetido.
    /// </summary>
    public bool ActualizarEstadoEntrega(EstadoDespacho nuevoEstado, string? detalle, DateTime ocurridoEn)
    {
        // Un despacho anulado por cancelacion de la orden no admite ninguna
        // transicion: ni siquiera repetir ANULADA por esta via.
        if (Estado == EstadoDespacho.ANULADA)
        {
            throw new ExcepcionDespachoCancelado(OrdenId);
        }

        if (Estado == nuevoEstado)
        {
            return false;
        }

        if (!TransicionesEntrega.TryGetValue(Estado, out var permitidos) || !permitidos.Contains(nuevoEstado))
        {
            throw new ExcepcionTransicionDespachoInvalida(Estado, nuevoEstado);
        }

        Estado = nuevoEstado;
        DetalleEstado = detalle;

        if (nuevoEstado == EstadoDespacho.ENTREGADA)
        {
            EntregadoEn = ocurridoEn.ToUniversalTime();
        }

        Avanzar();
        return true;
    }

    /// <summary>
    /// El cliente cambio de opinion despues de confirmada la compra (HU-011). Al pasar
    /// a domicilio el despacho vuelve a pedir guia desde cero; al pasar a recogida se
    /// abandona la guia en curso, y quien llama se encarga de anularla en LogiExpress.
    /// </summary>
    public void ReasignarMetodoEntrega(
        MetodoEntrega nuevoMetodo,
        string? ubicacionRecogidaId,
        string? direccionEntrega)
    {
        if (Estado is EstadoDespacho.ENTREGADA or EstadoDespacho.ANULADA)
        {
            throw new ExcepcionTransicionDespachoInvalida(Estado, EstadoDespacho.ORIGEN_PENDIENTE);
        }

        MetodoEntrega = nuevoMetodo;
        UbicacionRecogidaId = ubicacionRecogidaId;
        DireccionEntrega = direccionEntrega;

        if (nuevoMetodo == MetodoEntrega.RECOGIDA_TIENDA)
        {
            GuiaId = null;
            TrackingUrl = null;
            Transportista = null;
            Estado = EstadoDespacho.RECOGIDA_TIENDA;
        }
        else
        {
            // Vuelve a origen pendiente: hay que resolver de nuevo de donde sale y
            // pedir una guia nueva.
            Estado = EstadoDespacho.ORIGEN_PENDIENTE;
        }

        Avanzar();
    }

    /// <summary>
    /// La orden se cancelo: el despacho se anula (estado cancelado) para no dejar
    /// guias huerfanas. Solo antes de salir a ruta; un despacho EN_TRANSITO,
    /// LISTA_PARA_RECOGER o ENTREGADA ya saco las unidades y no se toca.
    /// </summary>
    public bool Anular()
    {
        if (Estado is EstadoDespacho.EN_TRANSITO
            or EstadoDespacho.LISTA_PARA_RECOGER
            or EstadoDespacho.ENTREGADA
            or EstadoDespacho.ANULADA)
        {
            return false;
        }

        Estado = EstadoDespacho.ANULADA;
        Avanzar();
        return true;
    }

    private void Avanzar()
    {
        Secuencia += 1;
        Version += 1;
    }

    /// <summary>
    /// Transiciones validas de entrega. Deliberadamente no hay camino de vuelta desde
    /// ENTREGADA ni ANULADA: un estado tardio del transportista no debe revivir un
    /// despacho ya cerrado.
    /// </summary>
    private static readonly IReadOnlyDictionary<EstadoDespacho, IReadOnlySet<EstadoDespacho>> TransicionesEntrega =
        new Dictionary<EstadoDespacho, IReadOnlySet<EstadoDespacho>>
        {
            [EstadoDespacho.RECOGIDA_TIENDA] = Conjunto(
                EstadoDespacho.EN_PREPARACION, EstadoDespacho.LISTA_PARA_RECOGER,
                EstadoDespacho.ENTREGADA, EstadoDespacho.INCIDENCIA),

            [EstadoDespacho.GUIA_SOLICITADA] = Conjunto(
                EstadoDespacho.EN_PREPARACION, EstadoDespacho.INCIDENCIA),

            // ENTREGADA es alcanzable directo: un transportista puede reportar solo
            // el estado final sin haber informado los intermedios, y perder esa
            // entrega por rigidez seria peor que aceptar el salto.
            [EstadoDespacho.GUIA_ASIGNADA] = Conjunto(
                EstadoDespacho.EN_PREPARACION, EstadoDespacho.EN_TRANSITO,
                EstadoDespacho.ENTREGADA, EstadoDespacho.INCIDENCIA),

            [EstadoDespacho.GUIA_PENDIENTE] = Conjunto(
                EstadoDespacho.EN_PREPARACION, EstadoDespacho.EN_TRANSITO,
                EstadoDespacho.ENTREGADA, EstadoDespacho.INCIDENCIA),

            [EstadoDespacho.EN_PREPARACION] = Conjunto(
                EstadoDespacho.EN_TRANSITO, EstadoDespacho.LISTA_PARA_RECOGER,
                EstadoDespacho.ENTREGADA, EstadoDespacho.INCIDENCIA),

            [EstadoDespacho.EN_TRANSITO] = Conjunto(
                EstadoDespacho.ENTREGADA, EstadoDespacho.INCIDENCIA),

            [EstadoDespacho.LISTA_PARA_RECOGER] = Conjunto(
                EstadoDespacho.ENTREGADA, EstadoDespacho.INCIDENCIA),

            // Una incidencia se puede resolver y el pedido seguir su camino.
            [EstadoDespacho.INCIDENCIA] = Conjunto(
                EstadoDespacho.EN_PREPARACION, EstadoDespacho.EN_TRANSITO,
                EstadoDespacho.LISTA_PARA_RECOGER, EstadoDespacho.ENTREGADA),

            [EstadoDespacho.ORIGEN_PENDIENTE] = Conjunto(EstadoDespacho.INCIDENCIA),
            [EstadoDespacho.ENTREGADA] = Conjunto(),
            [EstadoDespacho.ANULADA] = Conjunto()
        };

    private static IReadOnlySet<EstadoDespacho> Conjunto(params EstadoDespacho[] estados) => estados.ToHashSet();
}
