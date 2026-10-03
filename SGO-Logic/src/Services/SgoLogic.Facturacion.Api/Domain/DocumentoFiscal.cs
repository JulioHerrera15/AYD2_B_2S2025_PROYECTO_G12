using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Facturacion.Api.Domain;

/// <summary>
/// Agregado raiz del contexto Facturacion. Hay un documento fiscal por orden. Lleva
/// el XML generado, la serie y el correlativo (asignados UNA sola vez al crear el
/// documento, con la secuencia de PostgreSQL), el estado de certificacion ante la
/// SAT y el backoff persistido en <see cref="ProximoIntentoEn" />. Los reintentos
/// reusan siempre la misma serie y el mismo correlativo (ADR-011, HU-005).
/// </summary>
public sealed class DocumentoFiscal
{
    private readonly List<DocumentoFiscalLinea> _lineas = new();

    private DocumentoFiscal()
    {
        OrdenId = string.Empty;
        TransactionId = string.Empty;
        CuiReceptor = string.Empty;
        NombreReceptor = string.Empty;
        DireccionReceptor = string.Empty;
        Serie = string.Empty;
        NumeroDocumento = string.Empty;
        Xml = string.Empty;
    }

    public DocumentoFiscal(
        string ordenId,
        string transactionId,
        TipoDocumentoFiscal tipoDocumento,
        string? documentoOrigenId,
        string cuiReceptor,
        string nombreReceptor,
        string direccionReceptor,
        Pais paisReceptor,
        Moneda moneda,
        decimal subtotal,
        decimal impuestos,
        decimal total,
        string serie,
        long correlativo,
        string xml,
        IEnumerable<DocumentoFiscalLinea> lineas,
        DateTime creadoEn)
    {
        if (string.IsNullOrWhiteSpace(ordenId))
        {
            throw new ArgumentException("El identificador de la orden es obligatorio.", nameof(ordenId));
        }

        if (string.IsNullOrWhiteSpace(cuiReceptor))
        {
            throw new ArgumentException("El CUI del receptor es obligatorio.", nameof(cuiReceptor));
        }

        if (string.IsNullOrWhiteSpace(serie))
        {
            throw new ArgumentException("La serie es obligatoria.", nameof(serie));
        }

        if (correlativo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(correlativo), "El correlativo debe ser mayor que cero.");
        }

        Id = Guid.NewGuid();
        OrdenId = ordenId;
        TransactionId = transactionId;
        TipoDocumento = tipoDocumento;
        DocumentoOrigenId = documentoOrigenId;
        CuiReceptor = cuiReceptor;
        NombreReceptor = nombreReceptor;
        DireccionReceptor = direccionReceptor;
        PaisReceptor = paisReceptor;
        Moneda = moneda;
        Subtotal = subtotal;
        Impuestos = impuestos;
        Total = total;
        Serie = serie;
        Correlativo = correlativo;
        NumeroDocumento = correlativo.ToString("D8");
        Xml = xml;
        Estado = EstadoDocumentoFiscal.PENDIENTE;
        Intentos = 0;
        Secuencia = 0;
        var momento = creadoEn.ToUniversalTime();
        CreadoEn = momento;
        ActualizadoEn = momento;
        // El documento nace vencido a proposito: el worker de polling lo toma en su
        // primer ciclo y lo encola hacia la SAT.
        ProximoIntentoEn = momento;

        _lineas.AddRange(lineas);
    }

    public Guid Id { get; private set; }

    public string OrdenId { get; private set; }

    /// <summary>transactionId del envelope de la certificacion solicitada. Se hereda en los eventos que emite Facturacion.</summary>
    public string TransactionId { get; private set; }

    public TipoDocumentoFiscal TipoDocumento { get; private set; }

    public string? DocumentoOrigenId { get; private set; }

    public string CuiReceptor { get; private set; }

    public string NombreReceptor { get; private set; }

    public string DireccionReceptor { get; private set; }

    public Pais PaisReceptor { get; private set; }

    public Moneda Moneda { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal Impuestos { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>Serie autorizada. Se asigna una sola vez al crear el documento y no cambia en los reintentos.</summary>
    public string Serie { get; private set; }

    /// <summary>Correlativo de la secuencia de PostgreSQL. Se asigna una sola vez y no cambia en los reintentos.</summary>
    public long Correlativo { get; private set; }

    /// <summary>Correlativo formateado a ocho digitos (el numero visible del documento).</summary>
    public string NumeroDocumento { get; private set; }

    public string Xml { get; private set; }

    public EstadoDocumentoFiscal Estado { get; private set; }

    /// <summary>Intentos de certificacion contra la SAT que ya fallaron. Al llegar a MaxIntentos el documento pasa a ALERTA.</summary>
    public int Intentos { get; private set; }

    /// <summary>Backoff persistido: momento a partir del cual el worker de polling puede reencolar el documento. Null si esta certificado o en alerta.</summary>
    public DateTime? ProximoIntentoEn { get; private set; }

    /// <summary>Folio devuelto por la SAT al certificar.</summary>
    public string? Folio { get; private set; }

    /// <summary>Numero de autorizacion devuelto por la SAT al certificar.</summary>
    public string? NumeroAutorizacion { get; private set; }

    public string? UltimoCodigo { get; private set; }

    public string? UltimoMotivo { get; private set; }

    /// <summary>Secuencia monotonica por documento, para el campo sequence del envelope. Empieza en 0.</summary>
    public int Secuencia { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ActualizadoEn { get; private set; }

    public DateTime? CertificadoEn { get; private set; }

    /// <summary>
    /// Solo FACTURA: la orden se cancelo. Si la factura ya estaba certificada se genero en
    /// ese momento su nota de credito por el total; si no, se genera en cuanto se certifique.
    /// </summary>
    public DateTime? CancelacionOrdenEn { get; private set; }

    /// <summary>Solo FACTURA: nota de credito por cancelacion ya generada (idempotencia).</summary>
    public Guid? NotaCreditoCancelacionId { get; private set; }

    /// <summary>La orden de esta factura se cancelo y aun falta su nota de credito.</summary>
    public bool RequiereNotaCreditoCancelacion =>
        TipoDocumento == TipoDocumentoFiscal.FACTURA && CancelacionOrdenEn is not null && NotaCreditoCancelacionId is null;

    public IReadOnlyList<DocumentoFiscalLinea> Lineas => _lineas.AsReadOnly();

    /// <summary>Reserva el siguiente numero de secuencia para un evento que se va a encolar.</summary>
    public int SiguienteSecuencia()
    {
        Secuencia += 1;
        return Secuencia;
    }

    /// <summary>
    /// El worker de polling reencola el documento hacia la SAT: empuja
    /// <see cref="ProximoIntentoEn" /> por <paramref name="margen" /> para no volver
    /// a tomarlo en el proximo ciclo mientras el envio esta en curso. El enviador
    /// sobrescribe con el backoff real si el intento falla, o lo deja en null si
    /// certifica.
    /// </summary>
    public void MarcarReencolado(DateTime ahora, TimeSpan margen)
    {
        var momento = ahora.ToUniversalTime();
        ProximoIntentoEn = momento.Add(margen);
        ActualizadoEn = momento;
    }

    public void RegistrarUltimoError(string codigo, string motivo)
    {
        UltimoCodigo = codigo;
        UltimoMotivo = motivo;
    }

    /// <summary>
    /// Programa el siguiente reintento con backoff exponencial y jitter (jitter
    /// equitativo: mitad fija, mitad aleatoria), persistido en
    /// <see cref="ProximoIntentoEn" />. Al llegar a <paramref name="maxIntentos" />
    /// el documento pasa al estado terminal ALERTA y deja de reintentarse.
    /// </summary>
    /// <param name="aleatorio">Fuente de aleatoriedad en [0,1). Inyectable para pruebas deterministas.</param>
    public void ProgramarReintento(int maxIntentos, int backoffBaseMs, DateTime ahora, Func<double> aleatorio)
    {
        var momento = ahora.ToUniversalTime();
        Intentos += 1;
        ActualizadoEn = momento;

        if (Intentos >= maxIntentos)
        {
            Estado = EstadoDocumentoFiscal.ALERTA;
            ProximoIntentoEn = null;
            return;
        }

        var tope = backoffBaseMs * Math.Pow(2, Intentos - 1);
        var mitad = tope / 2;
        var espera = mitad + (aleatorio() * mitad);
        ProximoIntentoEn = momento.AddMilliseconds(espera);
    }

    public void MarcarCertificado(string folio, string numeroAutorizacion, DateTime ahora)
    {
        var momento = ahora.ToUniversalTime();
        Estado = EstadoDocumentoFiscal.CERTIFICADO;
        Folio = folio;
        NumeroAutorizacion = numeroAutorizacion;
        ProximoIntentoEn = null;
        CertificadoEn = momento;
        ActualizadoEn = momento;
    }

    /// <summary>Registra que la orden de esta factura se cancelo. Devuelve false si ya estaba registrado.</summary>
    public bool MarcarCancelacionOrden(DateTime ahora)
    {
        if (CancelacionOrdenEn is not null)
        {
            return false;
        }

        var momento = ahora.ToUniversalTime();
        CancelacionOrdenEn = momento;
        ActualizadoEn = momento;
        return true;
    }

    /// <summary>Asocia la nota de credito generada por la cancelacion de la orden.</summary>
    public void RegistrarNotaCreditoCancelacion(Guid notaCreditoId, DateTime ahora)
    {
        NotaCreditoCancelacionId = notaCreditoId;
        ActualizadoEn = ahora.ToUniversalTime();
    }

    /// <summary>
    /// Resetea un documento que estaba en ALERTA (agoto reintentos) a PENDIENTE
    /// para que el worker de polling lo tome de nuevo. Lo llama
    /// <see cref="Application.Facturacion.ServicioFacturacion.CrearDocumentoAsync" />
    /// cuando recibe un segundo CertificacionFiscalSolicitada (reintento manual
    /// desde Ordenes). No toca serie ni correlativo.
    /// </summary>
    public void ReiniciarDesdeAlerta(DateTime ahora)
    {
        var momento = ahora.ToUniversalTime();
        Estado = EstadoDocumentoFiscal.PENDIENTE;
        Intentos = 0;
        ProximoIntentoEn = momento;
        UltimoCodigo = null;
        UltimoMotivo = null;
        ActualizadoEn = momento;
    }
}
