using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Agregado raiz del contexto Ordenes. Es de larga vida y mutable: el metodo de
/// entrega puede cambiar despues de confirmada. La unica forma de mover el estado
/// es TransicionarA, que valida contra MaquinaEstadosOrden.
/// </summary>
public sealed class Orden
{
    private readonly List<LineaOrden> _lineas = new();
    private readonly List<TransicionOrden> _transiciones = new();

    private Orden()
    {
        Id = string.Empty;
        NumeroOrden = string.Empty;
        ClienteId = string.Empty;
    }

    public Orden(
        string id,
        string numeroOrden,
        string clienteId,
        Canal canal,
        MetodoEntrega metodoEntrega,
        Moneda moneda,
        IEnumerable<LineaOrden> lineas,
        decimal impuestos,
        string? ubicacionSolicitadaId = null,
        DateTime? creadaEn = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("El identificador de la orden es obligatorio.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(numeroOrden))
        {
            throw new ArgumentException("El numero de orden es obligatorio.", nameof(numeroOrden));
        }

        if (string.IsNullOrWhiteSpace(clienteId))
        {
            throw new ArgumentException("El identificador del cliente es obligatorio.", nameof(clienteId));
        }

        var lineasMaterializadas = lineas?.ToList() ?? new List<LineaOrden>();
        if (lineasMaterializadas.Count == 0)
        {
            throw new ArgumentException("La orden requiere al menos una linea.", nameof(lineas));
        }

        if (impuestos < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(impuestos), "Los impuestos no pueden ser negativos.");
        }

        if (metodoEntrega == MetodoEntrega.RECOGIDA_TIENDA && string.IsNullOrWhiteSpace(ubicacionSolicitadaId))
        {
            throw new ArgumentException(
                "La recogida en tienda exige una ubicacion solicitada.",
                nameof(ubicacionSolicitadaId));
        }

        Id = id;
        NumeroOrden = numeroOrden;
        ClienteId = clienteId;
        Canal = canal;
        MetodoEntrega = metodoEntrega;
        Moneda = moneda;
        UbicacionSolicitadaId = ubicacionSolicitadaId;
        Impuestos = impuestos;
        Estado = EstadoOrden.CREADA;
        Secuencia = 1;
        CreadaEn = creadaEn ?? DateTime.UtcNow;

        _lineas.AddRange(lineasMaterializadas);
    }

    public string Id { get; private set; }

    public string NumeroOrden { get; private set; }

    public string ClienteId { get; private set; }

    public Canal Canal { get; private set; }

    public MetodoEntrega MetodoEntrega { get; private set; }

    public string? UbicacionSolicitadaId { get; private set; }

    public Moneda Moneda { get; private set; }

    public decimal Impuestos { get; private set; }

    public EstadoOrden Estado { get; private set; }

    /// <summary>
    /// Secuencia monotonica del agregado. Arranca en 1 con la creacion y se
    /// incrementa con cada hecho publicable. Es el campo por el que los
    /// consumidores aplican last-writer-wins.
    /// </summary>
    public int Secuencia { get; private set; }

    public DateTime CreadaEn { get; private set; }

    public IReadOnlyList<LineaOrden> Lineas => _lineas.AsReadOnly();

    public IReadOnlyList<TransicionOrden> Transiciones => _transiciones.AsReadOnly();

    public decimal Subtotal => _lineas.Sum(linea => linea.Subtotal);

    public decimal Total => Subtotal + Impuestos;

    public bool EsTerminal => MaquinaEstadosOrden.EsTerminal(Estado);

    /// <summary>
    /// Mueve la orden a un nuevo estado. Lanza si la maquina de estados no permite
    /// el movimiento. Devuelve la transicion registrada, que es lo que el
    /// orquestador convierte en evento OrdenEstadoCambiado.
    /// </summary>
    public TransicionOrden TransicionarA(
        EstadoOrden estadoNuevo,
        string? motivo = null,
        PasoSaga? pasoSaga = null,
        bool esCompensacion = false,
        DateTime? cambiadoEn = null)
    {
        if (!MaquinaEstadosOrden.EsTransicionValida(Estado, estadoNuevo))
        {
            throw new ExcepcionTransicionInvalida(Estado, estadoNuevo);
        }

        var estadoAnterior = Estado;
        Estado = estadoNuevo;
        Secuencia += 1;

        var transicion = new TransicionOrden(
            Id,
            Secuencia,
            estadoAnterior,
            estadoNuevo,
            motivo,
            pasoSaga,
            esCompensacion,
            cambiadoEn ?? DateTime.UtcNow);

        _transiciones.Add(transicion);
        return transicion;
    }

    /// <summary>
    /// Cambio de metodo de entrega posterior a la confirmacion (EV-31). No es una
    /// transicion de estado: la orden sigue donde estaba.
    /// </summary>
    public void CambiarMetodoEntrega(MetodoEntrega metodoNuevo, string? ubicacionRecogidaId)
    {
        if (EsTerminal)
        {
            throw new InvalidOperationException(
                "No se puede cambiar el metodo de entrega de una orden en estado terminal.");
        }

        if (metodoNuevo == MetodoEntrega.RECOGIDA_TIENDA && string.IsNullOrWhiteSpace(ubicacionRecogidaId))
        {
            throw new ArgumentException(
                "La recogida en tienda exige una ubicacion.",
                nameof(ubicacionRecogidaId));
        }

        MetodoEntrega = metodoNuevo;
        UbicacionSolicitadaId = metodoNuevo == MetodoEntrega.RECOGIDA_TIENDA ? ubicacionRecogidaId : null;
        Secuencia += 1;
    }
}
