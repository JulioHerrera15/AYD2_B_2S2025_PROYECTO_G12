using System.Text.RegularExpressions;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Contracts.Eventos.Ordenes;

namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Agregado raiz del contexto Ordenes. Es de larga vida y mutable: el metodo de
/// entrega puede cambiar despues de confirmada. La unica forma de mover el estado
/// es TransicionarA, que valida contra MaquinaEstadosOrden.
/// </summary>
public sealed partial class Orden
{
    /// <summary>
    /// Formato obligatorio del identificador de orden segun ordenes-eventos-v1:
    /// ORD-&lt;anio de 4 digitos&gt;-&lt;correlativo de 6 digitos&gt;. El id lo
    /// genera siempre el servidor; esta es la red de seguridad del dominio.
    /// </summary>
    [GeneratedRegex(@"^ORD-\d{4}-\d{6}$")]
    private static partial Regex PatronId();

    private readonly List<LineaOrden> _lineas = new();
    private readonly List<TransicionOrden> _transiciones = new();

    private Orden()
    {
        Id = string.Empty;
        NumeroOrden = string.Empty;
        ClienteId = string.Empty;
        ReceptorCui = string.Empty;
    }

    public Orden(
        string id,
        string numeroOrden,
        string clienteId,
        Canal canal,
        MetodoEntrega metodoEntrega,
        Moneda moneda,
        MedioPago medioPago,
        IEnumerable<LineaOrden> lineas,
        decimal impuestos,
        string? ubicacionSolicitadaId = null,
        DateTime? creadaEn = null,
        string receptorCui = "CF",
        DireccionEntregaData? direccionEntrega = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("El identificador de la orden es obligatorio.", nameof(id));
        }

        if (!PatronId().IsMatch(id))
        {
            throw new ArgumentException(
                "El identificador de la orden debe cumplir el patron ORD-<anio>-<correlativo de 6 digitos>.",
                nameof(id));
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

        if (string.IsNullOrWhiteSpace(receptorCui))
        {
            throw new ArgumentException("El CUI del receptor es obligatorio.", nameof(receptorCui));
        }

        ValidarDireccion(direccionEntrega);

        Id = id;
        NumeroOrden = numeroOrden;
        ClienteId = clienteId;
        Canal = canal;
        MetodoEntrega = metodoEntrega;
        Moneda = moneda;
        MedioPago = medioPago;
        UbicacionSolicitadaId = ubicacionSolicitadaId;
        DireccionEntrega = metodoEntrega == MetodoEntrega.DOMICILIO ? direccionEntrega : null;
        Impuestos = impuestos;
        Estado = EstadoOrden.CREADA;
        Secuencia = 1;
        CreadaEn = creadaEn ?? DateTime.UtcNow;
        ReceptorCui = receptorCui;

        _lineas.AddRange(lineasMaterializadas);
    }

    public string Id { get; private set; }

    public string NumeroOrden { get; private set; }

    public string ClienteId { get; private set; }

    public Canal Canal { get; private set; }

    public MetodoEntrega MetodoEntrega { get; private set; }

    public string? UbicacionSolicitadaId { get; private set; }

    /// <summary>
    /// Direccion de entrega a domicilio. Es opcional para no romper a quien crea
    /// ordenes sin ella; cuando existe viaja en OrdenCreada, DespachoSolicitado y
    /// MetodoEntregaModificado. Siempre null si el metodo es recogida en tienda.
    /// </summary>
    public DireccionEntregaData? DireccionEntrega { get; private set; }

    public Moneda Moneda { get; private set; }

    /// <summary>
    /// Medio de pago elegido al crear la orden (HU-018). Lo valida el servicio
    /// contra los medios habilitados para el canal antes de construir el agregado.
    /// </summary>
    public MedioPago MedioPago { get; private set; }

    public decimal Impuestos { get; private set; }

    public EstadoOrden Estado { get; private set; }

    /// <summary>
    /// Secuencia monotonica del agregado. Arranca en 1 con la creacion y se
    /// incrementa con cada hecho publicable. Es el campo por el que los
    /// consumidores aplican last-writer-wins.
    /// </summary>
    public int Secuencia { get; private set; }

    public DateTime CreadaEn { get; private set; }

    /// <summary>
    /// CUI del receptor para la factura (HU-005). "CF" cuando el cliente no
    /// entrega uno al crear la orden. Se usa al construir
    /// CertificacionFiscalSolicitada; no cambia despues de creada la orden.
    /// </summary>
    public string ReceptorCui { get; private set; }

    /// <summary>
    /// Identificador real de la reserva de inventario (el reservaId que devuelve
    /// Inventario, tomado de InventarioReservado). Null hasta que Ordenes consume
    /// ese evento; mientras sea null no hay reserva conocida que liberar.
    /// </summary>
    public string? ReservaId { get; private set; }

    /// <summary>
    /// Ubicacion de origen de la reserva de inventario (el ubicacionId que devuelve
    /// Inventario en InventarioReservado). Se necesita para que, ante una devolucion
    /// (HU-014), el evento DevolucionRegistrada le diga a Inventario donde reingresar
    /// las unidades sin que tenga que volver a consultarlo. Null hasta que se
    /// registra la reserva.
    /// </summary>
    public string? UbicacionReservaId { get; private set; }

    /// <summary>
    /// Identificador real del pago aplicado (el autorizacionId que devuelve el
    /// proveedor, tomado de PagoAplicado). Reemplaza el placeholder
    /// PAG-PENDIENTE-&lt;id&gt; una vez que Ordenes consume ese evento. Null hasta
    /// entonces.
    /// </summary>
    public string? PagoId { get; private set; }

    /// <summary>Identificador interno del documento fiscal (DocumentoFiscalCertificado.documentoFiscalId).</summary>
    public string? DocumentoFiscalId { get; private set; }

    /// <summary>Serie autorizada del documento fiscal certificado.</summary>
    public string? DocumentoFiscalSerie { get; private set; }

    /// <summary>Numero correlativo del documento fiscal certificado.</summary>
    public string? DocumentoFiscalNumero { get; private set; }

    /// <summary>Numero de autorizacion que devolvio la SAT.</summary>
    public string? DocumentoFiscalNumeroAutorizacion { get; private set; }

    /// <summary>Folio que devolvio la SAT. Puede llegar vacio segun el mock.</summary>
    public string? DocumentoFiscalFolio { get; private set; }

    /// <summary>Momento en que la SAT certifico el documento (campo certificadaEn del evento).</summary>
    public DateTime? DocumentoFiscalCertificadoEn { get; private set; }

    /// <summary>
    /// Bandera que indica que la certificacion fiscal ante la SAT agoto sus
    /// reintentos y la orden queda varada en CONFIRMADA_PENDIENTE_FISCAL.
    /// Se levanta al consumir CertificacionSatFallida y se limpia al reintentar
    /// manualmente (ADR-011).
    /// </summary>
    public bool AlertaFiscal { get; private set; }

    /// <summary>Motivo por el que se levanto la alerta fiscal (ultimo codigo/motivo de la SAT).</summary>
    public string? AlertaFiscalMotivo { get; private set; }

    /// <summary>Momento en que se levanto la alerta fiscal.</summary>
    public DateTime? AlertaFiscalEn { get; private set; }

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
    public void CambiarMetodoEntrega(
        MetodoEntrega metodoNuevo,
        string? ubicacionRecogidaId,
        DireccionEntregaData? direccionEntrega = null)
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

        ValidarDireccion(direccionEntrega);

        MetodoEntrega = metodoNuevo;
        UbicacionSolicitadaId = metodoNuevo == MetodoEntrega.RECOGIDA_TIENDA ? ubicacionRecogidaId : null;
        DireccionEntrega = metodoNuevo == MetodoEntrega.DOMICILIO ? direccionEntrega : null;
        Secuencia += 1;
    }

    private static void ValidarDireccion(DireccionEntregaData? direccion)
    {
        if (direccion is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(direccion.Departamento)
            || string.IsNullOrWhiteSpace(direccion.Municipio)
            || string.IsNullOrWhiteSpace(direccion.Direccion))
        {
            throw new ArgumentException(
                "La direccion de entrega exige departamento, municipio y direccion.",
                nameof(direccion));
        }
    }

    /// <summary>
    /// Registra el identificador real de la reserva de inventario y la ubicacion
    /// de origen. Lo llama el manejador de eventos de Inventario al recibir
    /// InventarioReservado, antes de transicionar a INVENTARIO_RESERVADO. El
    /// reservaId lo usan DespachoSolicitado y LiberacionReservaSolicitada, y la
    /// ubicacion queda disponible para que DevolucionRegistrada (HU-014) le diga a
    /// Inventario donde reingresar.
    /// </summary>
    public void RegistrarReserva(string reservaId, string ubicacionId)
    {
        if (string.IsNullOrWhiteSpace(reservaId))
        {
            throw new ArgumentException("El identificador de la reserva es obligatorio.", nameof(reservaId));
        }

        if (string.IsNullOrWhiteSpace(ubicacionId))
        {
            throw new ArgumentException("La ubicacion de la reserva es obligatoria.", nameof(ubicacionId));
        }

        ReservaId = reservaId;
        UbicacionReservaId = ubicacionId;
    }

    /// <summary>
    /// Registra el identificador real del pago aplicado (HU-004/HU-005). Lo llama
    /// el manejador de eventos de Pagos al recibir PagoAplicado, antes de
    /// transicionar a PAGO_APLICADO. Reemplaza el placeholder
    /// PAG-PENDIENTE-&lt;id&gt; que hoy usa OrdenConfirmada/OrdenCancelada.
    /// </summary>
    public void RegistrarPago(string pagoId)
    {
        if (string.IsNullOrWhiteSpace(pagoId))
        {
            throw new ArgumentException("El identificador del pago es obligatorio.", nameof(pagoId));
        }

        PagoId = pagoId;
    }

    /// <summary>
    /// Persiste el resultado de la certificacion fiscal (DocumentoFiscalCertificado)
    /// sobre la orden. Solo valido mientras la orden esta CONFIRMADA_PENDIENTE_FISCAL:
    /// lo llama <see cref="ManejadorDocumentoFiscalCertificado" />, en el mismo
    /// momento en que aplica la transicion a CONFIRMADA, nunca despues.
    /// </summary>
    public void AsignarCertificacionFiscal(
        string documentoFiscalId,
        string serie,
        string numeroDocumento,
        string numeroAutorizacion,
        string? folio,
        DateTime certificadaEn)
    {
        if (Estado != EstadoOrden.CONFIRMADA_PENDIENTE_FISCAL)
        {
            throw new InvalidOperationException(
                $"No se puede asignar la certificacion fiscal con la orden en estado {Estado}.");
        }

        DocumentoFiscalId = documentoFiscalId;
        DocumentoFiscalSerie = serie;
        DocumentoFiscalNumero = numeroDocumento;
        DocumentoFiscalNumeroAutorizacion = numeroAutorizacion;
        DocumentoFiscalFolio = folio;
        DocumentoFiscalCertificadoEn = certificadaEn;
    }

    /// <summary>
    /// Levanta la bandera de alerta fiscal cuando la SAT agoto sus reintentos.
    /// No cambia de estado: la orden se queda en CONFIRMADA_PENDIENTE_FISCAL.
    /// </summary>
    public void LevantarAlertaFiscal(string motivo, DateTime en)
    {
        AlertaFiscal = true;
        AlertaFiscalMotivo = motivo;
        AlertaFiscalEn = en;
    }

    /// <summary>
    /// Limpia la bandera de alerta fiscal despues de un reintento manual exitoso.
    /// </summary>
    public void LimpiarAlertaFiscal()
    {
        AlertaFiscal = false;
        AlertaFiscalMotivo = null;
        AlertaFiscalEn = null;
    }
}
