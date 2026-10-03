using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using SgoLogic.BuildingBlocks.Serialization;

namespace SgoLogic.Web.Services;

public sealed record RegistroModel(string Email, string Contrasena, string NombreCompleto);
public sealed record VerificarModel(string Email, string Codigo);
public sealed record LoginModel(string Email, string Contrasena);
public sealed record AuthResult(
    string Token,
    Guid UsuarioId,
    string Email,
    string NombreCompleto,
    DateTime ExpiraEn,
    [property: JsonPropertyName("rol")] string? Rol = "CLIENTE");

public sealed record UsuarioDto(
    Guid Id,
    string Email,
    string NombreCompleto,
    string Rol,
    bool EmailVerificado,
    DateTime CreadoEn);

public sealed record ItemCarritoModel(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("descripcion")] string Descripcion,
    [property: JsonPropertyName("cantidad")] int Cantidad,
    [property: JsonPropertyName("precioUnitario")] decimal PrecioUnitario,
    [property: JsonPropertyName("ubicacionId")] string? UbicacionId = null,
    [property: JsonPropertyName("nombreUbicacion")] string? NombreUbicacion = null,
    [property: JsonPropertyName("expiraEn")] DateTime? ExpiraEn = null,
    [property: JsonPropertyName("estadoReserva")] string? EstadoReserva = null,
    [property: JsonPropertyName("enOtraSucursal")] bool? EnOtraSucursal = null)
{
    [JsonIgnore]
    public decimal Subtotal => Cantidad * PrecioUnitario;

    /// <summary>La retencion de stock de este item ya vencio segun Inventario.</summary>
    [JsonIgnore]
    public bool ReservaVencida => string.Equals(EstadoReserva, "EXPIRADA", StringComparison.OrdinalIgnoreCase);
}

/// <param name="CarritoIdReserva">
/// Identificador con el que Inventario retiene el stock del carrito (cart:{usuarioId}
/// o guest:{sesionId}). Es el que viaja como carritoId al crear la orden.
/// </param>
public sealed record CarritoModel(
    [property: JsonPropertyName("clienteOSesionId")] string? ClienteOSesionId = null,
    [property: JsonPropertyName("items")] List<ItemCarritoModel>? Items = null,
    [property: JsonPropertyName("total")] decimal Total = 0m,
    [property: JsonPropertyName("reservaExpiraEn")] DateTime? ReservaExpiraEn = null,
    [property: JsonPropertyName("carritoId")] string? CarritoIdReserva = null)
{
    [JsonIgnore]
    public string CarritoId => CarritoIdReserva ?? ClienteOSesionId ?? string.Empty;

    [JsonIgnore]
    public bool TieneReservasVencidas => Items?.Any(item => item.ReservaVencida) ?? false;
}

public sealed record AgregarItemModel(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    string? UbicacionPreferidaId = null);

/// <summary>
/// Resultado de una operacion sobre el carrito. Si Inventario rechazo por falta de
/// stock (409) o no respondio (503), <see cref="Mensaje" /> trae el texto para el
/// cliente y <see cref="SucursalCercana" /> la tienda donde si hay, si Inventario la sugirio.
/// </summary>
public sealed record ResultadoCarritoModel(
    bool Exito,
    int Status,
    string? Mensaje = null,
    SucursalCercanaModel? SucursalCercana = null);

// ── Sucursales (Inventario) ─────────────────────────────────────────────────
public sealed record SucursalModel(
    string Id,
    string Nombre,
    string TipoUbicacion,
    string Departamento,
    string Municipio,
    double? Latitud,
    double? Longitud,
    bool Activa)
{
    [JsonIgnore]
    public bool EsTienda => string.Equals(TipoUbicacion, "TIENDA", StringComparison.OrdinalIgnoreCase);
}

public sealed record SucursalCercanaModel(
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("desde")] string? Desde,
    [property: JsonPropertyName("ubicacionId")] string UbicacionId,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("departamento")] string? Departamento,
    [property: JsonPropertyName("municipio")] string? Municipio,
    [property: JsonPropertyName("disponible")] int Disponible,
    [property: JsonPropertyName("distanciaKm")] double? DistanciaKm);

public sealed record LineaSimulacionModel(string Sku, int Cantidad);

/// <summary>Producto del catalogo de Inventario (GET /api/inventario/productos). Su precio es el que vale.</summary>
public sealed record ProductoInventarioModel(
    string Sku,
    string Nombre,
    string Categoria,
    decimal Precio,
    string Icono,
    bool Activo,
    string? Descripcion = null,
    string? Etiqueta = null);

// ── Tarjetas guardadas (Pagos) ──────────────────────────────────────────────
/// <summary>
/// Tarjeta guardada del cliente. Solo metadatos para mostrarla: Pagos nunca expone
/// la referencia de PayKlik, y la Web no conserva el numero completo ni el CVV.
/// </summary>
public sealed record TarjetaGuardadaModel(
    Guid Id,
    string Marca,
    string Ultimos4,
    int MesExp,
    int AnioExp,
    string Titular,
    string Enmascarado,
    bool Vencida,
    DateTime CreadaEn,
    DateTime? UltimoUsoEn)
{
    [JsonIgnore]
    public string VenceTexto => $"{MesExp:00}/{AnioExp % 100:00}";

    [JsonIgnore]
    public bool EsAmex => string.Equals(Marca, "AMEX", StringComparison.OrdinalIgnoreCase);

    /// <summary>Clase de Font Awesome del logo de la marca.</summary>
    [JsonIgnore]
    public string IconoMarca => Marca.ToUpperInvariant() switch
    {
        "VISA" => "fa-brands fa-cc-visa",
        "MASTERCARD" => "fa-brands fa-cc-mastercard",
        "AMEX" => "fa-brands fa-cc-amex",
        _ => "fa-solid fa-credit-card"
    };
}

/// <summary>
/// Cuerpo de POST /api/pagos/tokens. Lleva PAN y CVV hacia Pagos (no a PayKlik):
/// <see cref="ToString" /> no los imprime y la Web nunca lo registra.
/// </summary>
// ── Mi credito (Credito) ────────────────────────────────────────────────────
/// <summary>Un factor del perfil explicado al cliente. Valoracion: FAVORABLE, ACEPTABLE o A_MEJORAR.</summary>
public sealed record FactorCreditoModel(string Nombre, string Valoracion, string Descripcion)
{
    /// <summary>Texto de la valoracion para mostrar; la vista nunca imprime el codigo.</summary>
    [JsonIgnore]
    public string ValoracionTexto => Valoracion switch
    {
        "FAVORABLE" => "Favorable",
        "ACEPTABLE" => "Aceptable",
        _ => "Por mejorar"
    };
}

/// <summary>Respuesta de GET /api/credito/mi-credito. No trae puntaje.</summary>
public sealed record MiCreditoModel(
    string Estado,
    string Nivel,
    string EtiquetaNivel,
    string Titulo,
    string Mensaje,
    string Recomendacion,
    decimal LimiteAprobado,
    decimal SaldoUtilizado,
    decimal Disponible,
    string Moneda,
    int PlazoDias,
    DateTime? ActualizadoEn,
    List<FactorCreditoModel>? Factores,
    List<CompraCreditoModel>? Compras = null,
    decimal TotalPorPagar = 0m,
    DateTime? ProximoVencimiento = null,
    List<AvisoCreditoModel>? Avisos = null)
{
    /// <summary>La linea esta en pausa por una compra vencida.</summary>
    [JsonIgnore]
    public bool EnPausa => string.Equals(Estado, "LINEA_BLOQUEADA", StringComparison.OrdinalIgnoreCase);

    /// <summary>El cliente tiene una linea activa con limite: la vista muestra montos.</summary>
    [JsonIgnore]
    public bool TieneCredito =>
        (Estado is "DISPONIBLE" or "CUPO_INSUFICIENTE") && LimiteAprobado > 0m;

    /// <summary>Porcentaje del limite ya utilizado, de 0 a 100.</summary>
    [JsonIgnore]
    public int PorcentajeUtilizado =>
        LimiteAprobado <= 0m ? 0 : (int)Math.Clamp(Math.Round(SaldoUtilizado * 100m / LimiteAprobado), 0m, 100m);
}

public enum EstadoConsultaCredito
{
    Ok,
    SesionInvalida,
    NoDisponible
}

/// <summary>
/// Resultado de consultar Mi credito. Nunca lanza: distingue datos correctos, sesion
/// no valida (401) y servicio no disponible (timeout, error de red o 5xx).
/// </summary>
public sealed record ResultadoMiCreditoModel(EstadoConsultaCredito Estado, MiCreditoModel? Credito = null);

/// <summary>Una compra a credito. EtiquetaEstado es lo que se muestra; Estado es el codigo.</summary>
public sealed record CompraCreditoModel(
    string NumeroOrden,
    string OrdenId,
    decimal Monto,
    decimal SaldoPendiente,
    DateTime EmitidaEn,
    DateTime VenceEn,
    int? DiasRestantes,
    string Estado,
    string EtiquetaEstado,
    string? CuentaId = null,
    bool PuedePagar = false,
    string? AbonoEnCursoId = null,
    bool AbonoEnCursoPuedeReintentar = false,
    List<AbonoHistorialModel>? Abonos = null)
{
    [JsonIgnore]
    public bool EsVigente => string.Equals(Estado, "VIGENTE", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool EsVencida => string.Equals(Estado, "VENCIDA", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Aviso destacado de Mi credito. Tipo solo decide el estilo; nunca se muestra.</summary>
public sealed record AvisoCreditoModel(string Tipo, string Titulo, string Mensaje, string Recomendacion, string? CuentaId);

/// <summary>Un pago de una compra a credito para el historial.</summary>
public sealed record AbonoHistorialModel(
    string AbonoId,
    string Referencia,
    DateTime Fecha,
    decimal Monto,
    string? Tarjeta,
    string Estado,
    string EtiquetaEstado);

/// <summary>Estado de un abono (GET/POST /api/credito/abonos). Titulo y Mensaje son para el cliente.</summary>
public sealed record AbonoCreditoModel(
    string Id,
    string Referencia,
    string CuentaId,
    string Estado,
    bool EnCurso,
    string Titulo,
    string Mensaje,
    string Recomendacion,
    decimal Monto,
    string Moneda,
    decimal SaldoPendienteCuenta,
    bool PuedeReintentar);

/// <summary>
/// Resultado de una operacion de abono. Nunca lanza: Abono con datos, o Status y un
/// mensaje para el cliente (el detail del backend o uno generico si no respondio).
/// </summary>
public sealed record ResultadoAbonoCreditoModel(AbonoCreditoModel? Abono, int Status, string? Mensaje)
{
    public const string MensajeNoDisponible =
        "En este momento no podemos procesar tu pago. Inténtalo de nuevo en unos minutos.";
}

/// <summary>Respuesta de GET /api/credito/elegibilidad. No trae puntaje.</summary>
public sealed record OfertaCreditoModel(
    string Estado,
    bool Ofrecer,
    string? CodigoMotivo,
    string Titulo,
    string Mensaje,
    string Recomendacion,
    decimal LimiteAprobado,
    decimal Disponible,
    decimal? MontoSolicitado,
    string Moneda,
    int PlazoDias,
    DateTime? FechaPagoEstimada,
    DateTime? CalculadoEn);

/// <summary>
/// Resultado de consultar la oferta de credito. Nunca lanza: datos, sin sesion (401)
/// o servicio no disponible (timeout, red, 5xx o cualquier otra respuesta).
/// </summary>
public sealed record ResultadoOfertaCreditoModel(EstadoConsultaCredito Estado, OfertaCreditoModel? Oferta = null);

/// <summary>Linea de una orden tal como la devuelve Ordenes (GET /api/ordenes/{id}).</summary>
public sealed record LineaOrdenModel(string Sku, string Descripcion, int Cantidad, decimal PrecioUnitario);

/// <summary>Lo que la Web necesita de una orden de Ordenes para rearmar el carrito.</summary>
public sealed record OrdenDetalleModel(
    string Id,
    string NumeroOrden,
    string ClienteId,
    string Estado,
    string MedioPago,
    List<LineaOrdenModel>? Lineas);

/// <summary>Resultado de crear una orden con el codigo HTTP, para distinguir 401 y 403.</summary>
public sealed record ResultadoCrearOrdenModel(bool Exito, int Status, OrdenCreadaModel? Orden, string? Error);

public sealed record SolicitudTokenPagoModel(
    string? Numero,
    int? MesExp,
    int? AnioExp,
    string? Cvv,
    string? Titular,
    bool Guardar)
{
    public override string ToString() => nameof(SolicitudTokenPagoModel) + " { datos de tarjeta omitidos }";
}

/// <summary>Token de la boveda de Pagos: lo que el checkout manda como tokenMedioPago.</summary>
public sealed record TokenPagoModel(string Token, string Marca, string Ultimos4, DateTime ExpiraEn);

/// <summary>Desenlace de tokenizar: el token, o el status con errores por campo (422) o un mensaje.</summary>
public sealed record ResultadoTokenPagoModel(
    TokenPagoModel? Token,
    int Status,
    Dictionary<string, string>? Errores,
    string? Mensaje)
{
    public static ResultadoTokenPagoModel Falla(int status, string mensaje) => new(null, status, null, mensaje);
}

public sealed record SimularReservaModel(
    string? CarritoId,
    string MetodoEntrega,
    string? UbicacionPreferidaId,
    string? UbicacionReferenciaId,
    List<LineaSimulacionModel> Lineas);

public sealed record SimulacionReservaModel(
    [property: JsonPropertyName("factible")] bool Factible,
    [property: JsonPropertyName("ubicacionId")] string? UbicacionId,
    [property: JsonPropertyName("nombreUbicacion")] string? NombreUbicacion,
    [property: JsonPropertyName("tipoUbicacion")] string? TipoUbicacion,
    [property: JsonPropertyName("esOtraSucursal")] bool EsOtraSucursal,
    [property: JsonPropertyName("motivo")] string? Motivo);

public sealed record IncidenciaModel(Guid Id, string OrdenId, string Descripcion, string Estado, string? Resolucion, DateTime CreadaEn);
public sealed record OrdenEstadoModel(
    string OrdenId,
    string? NumeroOrden,
    string? Estado,
    string? Canal,
    string? MetodoEntrega,
    decimal? Total,
    string? Moneda,
    string? PagoId,
    string? DocumentoFiscalId,
    DateTime? CreadaEn,
    DateTime? ActualizadaEn,
    List<IncidenciaModel>? Incidencias,
    string? MotivoUltimoCambio = null,
    string? ClienteId = null,
    string? UbicacionOrigenId = null,
    string? SerieDocumentoFiscal = null,
    string? NumeroDocumentoFiscal = null,
    string? EstadoAnterior = null);

public sealed record CrearIncidenciaModel(string OrdenId, string Descripcion);

// ── Modelos de Órdenes y Checkout ───────────────────────────────────────────
public sealed record LineaCrearOrdenModel(string Sku, string Descripcion, int Cantidad, decimal PrecioUnitario);

public sealed record DireccionEntregaModel(
    [property: JsonPropertyName("pais")] string Pais,
    [property: JsonPropertyName("departamento")] string Departamento,
    [property: JsonPropertyName("municipio")] string Municipio,
    [property: JsonPropertyName("direccion")] string Direccion,
    [property: JsonPropertyName("referencia")] string? Referencia = null);

public sealed record CrearOrdenModel(
    string ClienteId,
    string Canal,
    string MetodoEntrega,
    string Moneda,
    string MedioPago,
    decimal Impuestos,
    List<LineaCrearOrdenModel> Lineas,
    string? UbicacionSolicitadaId = null,
    string? ReceptorCui = null,
    DireccionEntregaModel? DireccionEntrega = null,
    string? TokenMedioPago = null,
    string? CarritoId = null);

public sealed record OrdenCreadaModel(
    string Id,
    string NumeroOrden,
    string ClienteId,
    string Canal,
    string MetodoEntrega,
    string Estado,
    decimal Total,
    DateTime CreadaEn);

public sealed record ConfirmarOrdenResponse(
    OrdenCreadaModel Orden,
    bool YaConfirmada);

public sealed record OrdenHistorialResumenModel(
    string OrdenId,
    string NumeroOrden,
    string Estado,
    decimal Total,
    DateTime ActualizadaEn,
    string? ClienteId = null,
    DateTime? CreadaEn = null);

/// <summary>
/// Documento fiscal (FACTURA) de una orden tal como lo expone Facturacion en
/// GET /api/facturacion/documentos/{ordenId}. Estado es PENDIENTE, CERTIFICADO o ALERTA.
/// </summary>
public sealed record FacturaModel(
    string Serie,
    string NumeroDocumento,
    string Xml,
    string? NumeroAutorizacion = null,
    string? Folio = null,
    DateTime? CertificadoEn = null,
    string? Estado = null,
    string? NombreReceptor = null,
    decimal Subtotal = 0m,
    decimal Impuestos = 0m,
    decimal Total = 0m,
    List<FacturaLineaModel>? Lineas = null,
    string? CuiReceptor = null,
    string? Moneda = null)
{
    [JsonIgnore]
    public bool EstaCertificada => string.Equals(Estado, "CERTIFICADO", StringComparison.OrdinalIgnoreCase);
}

public sealed record FacturaLineaModel(
    int NumeroLinea,
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Impuesto)
{
    [JsonIgnore]
    public decimal Subtotal => Cantidad * PrecioUnitario;
}

// ── Informe de ventas conciliado (Integracion ERP) ───────────────────────────
public sealed record InformeVentasModel(DateTime GeneradoEn, int VentasPendientes, decimal MontoPendiente, List<LoteInformeModel> Lotes);
public sealed record LoteInformeModel(
    string LoteId, long Secuencia, DateTime FechaGeneracion, string Formato, string Estado,
    int CantidadRegistros, decimal MontoTotal, int Intentos, int? CodigoHttp, DateTime? EnviadoEn, DateTime? ConfirmadoEn,
    ConciliacionInformeModel? Conciliacion, List<RegistroInformeModel> Registros);
public sealed record ConciliacionInformeModel(DateTime Fecha, int RegistrosErp, decimal MontoErp, bool Conciliada, string? Diferencias, DateTime CreadaEn);
public sealed record RegistroInformeModel(string OrdenId, string Canal, string Tienda, decimal Monto, DateTime Fecha);
public sealed record EventoAuditoriaModel(string EventId, string EventType, DateTime OccurredAt, string TransactionId, string AggregateId, string Source, DateTime FechaRecepcion);
public sealed record PaginaAuditoriaModel(List<EventoAuditoriaModel> Items, int Total);

public sealed record HistorialClienteModel(
    string ClienteId,
    int Pagina,
    int Tamano,
    int TotalRegistros,
    List<OrdenHistorialResumenModel> Ordenes);

// ── Modelos de Reintento de Pago ─────────────────────────────────────────────
public sealed record EnlaceReintentoInfoModel(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("ordenId")] string OrdenId,
    [property: JsonPropertyName("monto")] decimal Monto,
    [property: JsonPropertyName("moneda")] string Moneda,
    [property: JsonPropertyName("estaVigente")] bool EstaVigente,
    [property: JsonPropertyName("usado")] bool Usado,
    [property: JsonPropertyName("expiraEn")] DateTime ExpiraEn,
    [property: JsonPropertyName("estadoPago")] string EstadoPago,
    [property: JsonPropertyName("ultimosCuatro")] string? UltimosCuatro,
    [property: JsonPropertyName("marca")] string? Marca,
    [property: JsonPropertyName("clienteId")] string? ClienteId,
    [property: JsonPropertyName("datosRequeridos")] string? DatosRequeridos = "NINGUNO")
{
    /// <summary>El token del ultimo intento vencio: hay que volver a escribir la tarjeta o el CVV.</summary>
    [JsonIgnore]
    public bool PideDatos => DatosRequeridos is "CVV" or "TARJETA";
}

/// <summary>
/// Desenlace de POST /api/pagos/reintentos/{token}. Si Pagos pide datos de tarjeta
/// (422 datos_tarjeta_requeridos) <see cref="DatosRequeridos" /> dice cuales; si los
/// datos enviados no son validos vienen en <see cref="Errores" />.
/// </summary>
public sealed record ResultadoReintentoModel(
    bool Exito,
    PagoReintentoResultadoModel? Pago,
    string? Error,
    string? DatosRequeridos = null,
    Dictionary<string, string>? Errores = null);

public sealed record IntentoPagoResultadoModel(
    [property: JsonPropertyName("intentoNumero")] int IntentoNumero,
    [property: JsonPropertyName("codigoRespuesta")] string? CodigoRespuesta,
    [property: JsonPropertyName("mensajeRespuesta")] string? MensajeRespuesta);

public sealed record PagoReintentoResultadoModel(
    [property: JsonPropertyName("ordenId")] string? OrdenId,
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("monto")] decimal? Monto,
    [property: JsonPropertyName("moneda")] string? Moneda,
    [property: JsonPropertyName("intentos")] List<IntentoPagoResultadoModel>? Intentos = null);

// ── Modelos de Despacho ─────────────────────────────────────────────────────
public sealed record DespachoOrdenModel(
    [property: JsonPropertyName("id")] string? DespachoId,
    string OrdenId,
    string Estado,
    string MetodoEntrega,
    string? GuiaId,
    string? TrackingUrl,
    string? Transportista,
    string? UbicacionOrigenId,
    [property: JsonPropertyName("creadoEn")] DateTime? CreadoEn = null,
    [property: JsonPropertyName("entregadoEn")] DateTime? EntregadoEn = null,
    [property: JsonPropertyName("detalleEstado")] string? DetalleEstado = null);

public sealed record DespachoPreparacionDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("ordenId")] string OrdenId,
    [property: JsonPropertyName("ubicacionOrigenId")] string? UbicacionOrigenId,
    [property: JsonPropertyName("metodoEntrega")] string MetodoEntrega,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("guiaId")] string? GuiaId,
    [property: JsonPropertyName("creadoEn")] DateTime? CreadoEn)
{
    [JsonPropertyName("despachoId")]
    public string DespachoId => !string.IsNullOrWhiteSpace(Id) ? Id : (!string.IsNullOrWhiteSpace(OrdenId) ? OrdenId : string.Empty);
}

// ── Modelos de Inventario ───────────────────────────────────────────────────
public sealed record ExistenciaSkuDto(
    string Sku,
    string UbicacionId,
    int CantidadFisica,
    int CantidadReservada,
    [property: JsonPropertyName("disponible")] int Disponible,
    string? TipoUbicacion = null,
    int Version = 0)
{
    [JsonPropertyName("cantidadDisponible")]
    public int CantidadDisponible => Disponible;
}

public sealed record UbicacionResumenDto(
    string UbicacionId,
    string TipoUbicacion,
    [property: JsonPropertyName("cantidadDeSkus")] int TotalSkus,
    [property: JsonPropertyName("cantidadTotalFisica")] int CantidadFisicaTotal,
    [property: JsonPropertyName("cantidadTotalReservada")] int CantidadReservadaTotal,
    [property: JsonPropertyName("totalDisponible")] int CantidadDisponibleTotal);

public sealed record ExistenciaUbicacionDto(
    string Sku,
    string UbicacionId,
    int CantidadFisica,
    int CantidadReservada,
    [property: JsonPropertyName("disponible")] int Disponible,
    string? TipoUbicacion = null,
    int Version = 0)
{
    [JsonPropertyName("cantidadDisponible")]
    public int CantidadDisponible => Disponible;
}

/// <summary>
/// Cliente tipado hacia el API Gateway. No modifica DefaultRequestHeaders: los
/// encabezados de la sesion (Authorization y X-Session-Id) los agrega
/// <see cref="EncabezadosSesionHandler" /> a cada mensaje, asi que es seguro llamarlo
/// en paralelo (Task.WhenAll).
/// </summary>
public sealed class GatewayClient
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;

    private const string ClaveCacheSucursales = "inventario:sucursales";
    private const string ClaveCacheProductos = "inventario:productos";
    private static readonly TimeSpan VigenciaCacheSucursales = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VigenciaCacheProductos = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LimiteMiCredito = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LimiteOfertaCredito = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
        Converters = { new JsonStringEnumConverter(), new DecimalComoTextoConverter() }
    };

    public GatewayClient(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    private static async Task<string> ParseProblemDetailsOrStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Credenciales incorrectas o sesión no autorizada.",
                    System.Net.HttpStatusCode.Forbidden => "No tienes permisos para realizar esta acción.",
                    System.Net.HttpStatusCode.NotFound => "El recurso solicitado no fue encontrado.",
                    System.Net.HttpStatusCode.Conflict => "Conflicto con un recurso existente.",
                    System.Net.HttpStatusCode.BadRequest => "Solicitud inválida. Verifica los datos ingresados.",
                    _ => $"Error en la operación (código {(int)response.StatusCode})."
                };
            }

            var trimmed = raw.Trim();
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                // 1. Si viene detail (RFC 7807 ProblemDetails standard)
                if (root.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                {
                    var detail = detailProp.GetString();
                    if (!string.IsNullOrWhiteSpace(detail))
                        return detail;
                }

                // 2. Si vienen errores de validación (HttpValidationProblemDetails)
                if (root.TryGetProperty("errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Object)
                {
                    var errorList = new List<string>();
                    foreach (var prop in errorsProp.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var errItem in prop.Value.EnumerateArray())
                            {
                                var s = errItem.GetString();
                                if (!string.IsNullOrWhiteSpace(s))
                                    errorList.Add(s);
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var s = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                                errorList.Add(s);
                        }
                    }
                    if (errorList.Count > 0)
                        return string.Join(" ", errorList);
                }

                // 3. Si viene message
                if (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                {
                    var msg = msgProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                        return msg;
                }

                // 4. Si viene title
                if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                {
                    var title = titleProp.GetString();
                    if (!string.IsNullOrWhiteSpace(title) && !title.Equals("One or more validation errors occurred.", StringComparison.OrdinalIgnoreCase))
                        return title;
                }
            }

            return trimmed;
        }
        catch
        {
            return response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Credenciales incorrectas o sesión no autorizada."
                : $"Error inesperado en la comunicación (código {(int)response.StatusCode}).";
        }
    }

    // ── Auth ───────────────────────────────────────────────────────────────────
    public async Task<(bool Exito, string? Error)> RegistrarAsync(RegistroModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/registro", model, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, string? Error)> VerificarAsync(VerificarModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/verificar", model, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, string? Error)> ReenviarCodigoAsync(string email, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/reenviar-codigo", new { Email = email }, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, AuthResult? Result, string? Error)> LoginAsync(LoginModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/login", model, ct);
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<AuthResult>(JsonOptions, ct);
            return (true, data, null);
        }
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, null, err);
    }

    public async Task<(bool Exito, string? Error)> AsignarRolAsync(string email, string rol, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/auth/asignar-rol", new { Email = email, Rol = rol }, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<UsuarioDto?> ObtenerUsuarioPorEmailAsync(string email, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/auth/usuario?email={Uri.EscapeDataString(email)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<UsuarioDto>(JsonOptions, ct);
    }

    public async Task<List<UsuarioDto>> ListarUsuariosAsync(CancellationToken ct = default)
    {
        var res = await _http.GetAsync("/api/auth/usuarios", ct);
        if (!res.IsSuccessStatusCode) return new List<UsuarioDto>();
        return await res.Content.ReadFromJsonAsync<List<UsuarioDto>>(JsonOptions, ct) ?? new List<UsuarioDto>();
    }

    // ── Carrito ────────────────────────────────────────────────────────────────
    public async Task<CarritoModel?> ObtenerCarritoAsync(CancellationToken ct = default)
    {
        var res = await _http.GetAsync("/api/carrito", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<CarritoModel>(JsonOptions, ct);
    }

    public async Task<CarritoModel?> ObtenerCarritoPorIdAsync(string sesionOClienteId, CancellationToken ct = default)
    {
        // El carrito que se consulta es el del identificador indicado, no el de quien
        // navega: sin el JWT de la sesion, Auth resuelve por X-Session-Id.
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/carrito");
        req.Headers.Add(EncabezadosSesionHandler.EncabezadoSesion, sesionOClienteId);
        req.Options.Set(EncabezadosSesionHandler.SinSesion, true);
        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<CarritoModel>(JsonOptions, ct);
    }

    public async Task<ResultadoCarritoModel> AgregarItemCarritoAsync(AgregarItemModel model, CancellationToken ct = default)
    {
        return await EjecutarCarritoAsync(() => _http.PostAsJsonAsync("/api/carrito/items", model, ct), ct);
    }

    public async Task<ResultadoCarritoModel> ActualizarCantidadItemCarritoAsync(
        string sku,
        int cantidad,
        string? ubicacionPreferidaId = null,
        CancellationToken ct = default)
    {
        return await EjecutarCarritoAsync(
            () => _http.PutAsJsonAsync(
                $"/api/carrito/items/{Uri.EscapeDataString(sku)}",
                new { Cantidad = cantidad, UbicacionPreferidaId = ubicacionPreferidaId },
                ct),
            ct);
    }

    public async Task<ResultadoCarritoModel> EliminarItemCarritoAsync(string sku, CancellationToken ct = default)
    {
        return await EjecutarCarritoAsync(() => _http.DeleteAsync($"/api/carrito/items/{Uri.EscapeDataString(sku)}", ct), ct);
    }

    /// <summary>Pide a Auth que reactive o extienda la retencion de stock de todo el carrito.</summary>
    public async Task<(ResultadoCarritoModel Resultado, CarritoModel? Carrito)> RenovarReservaCarritoAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await _http.PostAsync("/api/carrito/reserva/renovar", null, ct);
            if (res.IsSuccessStatusCode)
            {
                var carrito = await res.Content.ReadFromJsonAsync<CarritoModel>(JsonOptions, ct);
                return (new ResultadoCarritoModel(true, (int)res.StatusCode), carrito);
            }

            return (await LeerRechazoCarritoAsync(res, ct), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (SinRespuestaCarrito(), null);
        }
    }

    private static async Task<ResultadoCarritoModel> EjecutarCarritoAsync(Func<Task<HttpResponseMessage>> llamada, CancellationToken ct)
    {
        try
        {
            var res = await llamada();
            return res.IsSuccessStatusCode
                ? new ResultadoCarritoModel(true, (int)res.StatusCode)
                : await LeerRechazoCarritoAsync(res, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return SinRespuestaCarrito();
        }
    }

    private static ResultadoCarritoModel SinRespuestaCarrito() =>
        new(false, StatusCodes.Status503ServiceUnavailable,
            "No pudimos comunicarnos con el servicio de carrito. Intenta de nuevo en unos momentos.");

    /// <summary>
    /// Lee el cuerpo de un rechazo del carrito. El 409 de falta de stock viaja tal
    /// cual lo arma Inventario: {sku, disponible, mensaje, sucursalCercana?}.
    /// </summary>
    private static async Task<ResultadoCarritoModel> LeerRechazoCarritoAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var status = (int)res.StatusCode;
        var raw = await res.Content.ReadAsStringAsync(ct);

        string? mensaje = null;
        SucursalCercanaModel? cercana = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(raw) && raw.TrimStart().StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(raw);
                var raiz = doc.RootElement;

                if (raiz.TryGetProperty("mensaje", out var mensajeProp) && mensajeProp.ValueKind == JsonValueKind.String)
                {
                    mensaje = mensajeProp.GetString();
                }
                else if (raiz.TryGetProperty("detail", out var detalleProp) && detalleProp.ValueKind == JsonValueKind.String)
                {
                    mensaje = detalleProp.GetString();
                }

                if (raiz.TryGetProperty("sucursalCercana", out var cercanaProp) && cercanaProp.ValueKind == JsonValueKind.Object)
                {
                    cercana = cercanaProp.Deserialize<SucursalCercanaModel>(JsonOptions);
                }
            }
        }
        catch (JsonException)
        {
            // Cuerpo no JSON: se usa el mensaje generico de abajo.
        }

        mensaje ??= status switch
        {
            StatusCodes.Status409Conflict => "No hay unidades suficientes de este producto.",
            StatusCodes.Status503ServiceUnavailable => "El inventario no esta disponible en este momento. Tu carrito no se modifico.",
            _ => $"No se pudo actualizar el carrito (codigo {status})."
        };

        return new ResultadoCarritoModel(false, status, mensaje, cercana);
    }

    /// <summary>
    /// Asocia el carrito de invitado a la cuenta. <paramref name="token" /> es el JWT
    /// recien emitido en el login, cuando la cookie todavia no viaja en la peticion;
    /// sin el, el handler usa el de la sesion.
    /// </summary>
    public async Task<bool> AsociarCarritoAsync(string guestSessionId, string? token = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/carrito/asociar")
        {
            Content = JsonContent.Create(new { SesionId = guestSessionId })
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    // ── Estado ─────────────────────────────────────────────────────────────────
    public async Task<OrdenEstadoModel?> ObtenerEstadoOrdenAsync(string ordenId, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/estado/ordenes/{ordenId}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<OrdenEstadoModel>(JsonOptions, ct);
    }

    public async Task<HistorialClienteModel?> ObtenerHistorialClienteAsync(string clienteId, int pagina = 1, int tamano = 20, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/estado/clientes/{clienteId}/ordenes?pagina={pagina}&tamano={tamano}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<HistorialClienteModel>(JsonOptions, ct);
    }

    // ── Facturacion ────────────────────────────────────────────────────────────
    public async Task<FacturaModel?> ObtenerFacturaAsync(string ordenId, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/facturacion/documentos/{ordenId}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<FacturaModel>(JsonOptions, ct);
    }

    // ── Informe de ventas / ERP ────────────────────────────────────────────────
    public async Task<InformeVentasModel?> ObtenerInformeVentasAsync(DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        var filtros = new List<string>();
        if (desde.HasValue) filtros.Add($"desde={desde:yyyy-MM-dd}");
        if (hasta.HasValue) filtros.Add($"hasta={hasta:yyyy-MM-dd}");
        var res = await _http.GetAsync($"/api/erp/informe?{string.Join('&', filtros)}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<InformeVentasModel>(JsonOptions, ct);
    }

    public async Task<(bool Exito, string? Error)> EjecutarConciliacionAsync(CancellationToken ct = default)
    {
        var res = await _http.PostAsync("/api/erp/conciliar", null, ct);
        return res.IsSuccessStatusCode ? (true, null) : (false, await ParseProblemDetailsOrStringAsync(res, ct));
    }

    public async Task<List<EventoAuditoriaModel>> ObtenerAuditoriaAgregadoAsync(string aggregateId, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/auditoria/agregados/{Uri.EscapeDataString(aggregateId)}?tamano=50", ct);
        if (!res.IsSuccessStatusCode) return new();
        return (await res.Content.ReadFromJsonAsync<PaginaAuditoriaModel>(JsonOptions, ct))?.Items ?? new();
    }

    // ── Incidencias ────────────────────────────────────────────────────────────
    public async Task<(bool Exito, IncidenciaModel? Incidencia, string? Error)> CrearIncidenciaAsync(CrearIncidenciaModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/incidencias", model, ct);
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<IncidenciaModel>(JsonOptions, ct);
            return (true, data, null);
        }
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, null, err);
    }

    // ── Órdenes y Checkout ─────────────────────────────────────────────────────
    public async Task<(bool Exito, OrdenCreadaModel? Orden, string? Error)> CrearOrdenAsync(CrearOrdenModel model, CancellationToken ct = default)
    {
        var resultado = await CrearOrdenConEstadoAsync(model, ct);
        return (resultado.Exito, resultado.Orden, resultado.Error);
    }

    /// <summary>Igual que <see cref="CrearOrdenAsync" />, pero conserva el codigo HTTP (401/403 en ordenes a credito).</summary>
    public async Task<ResultadoCrearOrdenModel> CrearOrdenConEstadoAsync(CrearOrdenModel model, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/api/ordenes", model, JsonOptions, ct);
        if (res.IsSuccessStatusCode)
        {
            var orden = await res.Content.ReadFromJsonAsync<OrdenCreadaModel>(JsonOptions, ct);
            return new ResultadoCrearOrdenModel(true, (int)res.StatusCode, orden, null);
        }
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return new ResultadoCrearOrdenModel(false, (int)res.StatusCode, null, err);
    }

    /// <summary>Orden de Ordenes con sus lineas (GET /api/ordenes/{id}); null si no responde.</summary>
    public async Task<OrdenDetalleModel?> ObtenerOrdenAsync(string ordenId, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.GetAsync($"/api/ordenes/{Uri.EscapeDataString(ordenId)}", ct);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadFromJsonAsync<OrdenDetalleModel>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public async Task<(bool Exito, OrdenCreadaModel? Orden, string? Error)> ConfirmarOrdenAsync(string ordenId, string? motivo = null, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync($"/api/ordenes/{ordenId}/confirmar", new { Motivo = motivo }, JsonOptions, ct);
        if (res.IsSuccessStatusCode)
        {
            var respuesta = await res.Content.ReadFromJsonAsync<ConfirmarOrdenResponse>(JsonOptions, ct);
            return (true, respuesta?.Orden, null);
        }
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, null, err);
    }

    /// <summary>
    /// Cancelacion de la orden por su cliente via POST /api/ordenes/{id}/cancelar.
    /// Ordenes valida que <paramref name="clienteId" /> sea el dueno (403) y que el
    /// estado lo admita (409); el detalle del ProblemDetails vuelve como error.
    /// </summary>
    public async Task<(bool Exito, string? Error)> CancelarOrdenAsync(string ordenId, string clienteId, string? motivo = null, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync(
            $"/api/ordenes/{Uri.EscapeDataString(ordenId)}/cancelar",
            new { ClienteId = clienteId, Motivo = motivo },
            JsonOptions,
            ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    // ── Despacho ────────────────────────────────────────────────────────────────
    /// <summary>
    /// Consulta el estado real de despacho de una orden via GET /api/despacho/{ordenId}.
    /// Retorna null si el despacho todavía no existe para la orden (saga aún en etapas tempranas).
    /// </summary>
    public async Task<DespachoOrdenModel?> ObtenerDespachoOrdenAsync(string ordenId, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/despacho/{ordenId}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<DespachoOrdenModel>(JsonOptions, ct);
    }

    public async Task<List<DespachoPreparacionDto>?> ObtenerColaPreparacionAsync(string ubicacionId, string? estado = null, CancellationToken ct = default)
    {
        var url = $"/api/despacho/ubicaciones/{ubicacionId}/preparacion" + (!string.IsNullOrWhiteSpace(estado) ? $"?estado={estado}" : "");
        var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<DespachoPreparacionDto>>(JsonOptions, ct);
    }

    public async Task<(bool Exito, string? Error)> ActualizarEstadoDespachoAsync(string ordenId, string estado, string? detalle = null, CancellationToken ct = default)
    {
        var payload = new { Estado = estado, Detalle = detalle, OcurridoEn = DateTime.UtcNow };
        var res = await _http.PostAsJsonAsync($"/api/despacho/{ordenId}/estados", payload, JsonOptions, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, string? Error)> ReasignarMetodoEntregaDespachoAsync(string ordenId, string nuevoMetodo, CancellationToken ct = default)
    {
        var res = await _http.PutAsJsonAsync($"/api/despacho/{ordenId}/metodo-entrega", new { MetodoEntrega = nuevoMetodo }, JsonOptions, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    // ── Inventario ─────────────────────────────────────────────────────────────
    public async Task<List<ExistenciaSkuDto>?> ObtenerExistenciasSkuAsync(string sku, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/inventario/existencias/{sku}", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<ExistenciaSkuDto>>(JsonOptions, ct);
    }

    // ── Tarjetas guardadas y tokenizacion ──────────────────────────────────────
    // Pagos toma el cliente del JWT de la sesion (EncabezadosSesionHandler), nunca de
    // un parametro. Estas llamadas no registran nada: el PAN y el CVV solo viajan en
    // el cuerpo hacia Pagos, que los cifra en su boveda transitoria.

    /// <summary>Tarjetas activas del cliente; lista vacia si no hay sesion o Pagos no responde.</summary>
    public async Task<IReadOnlyList<TarjetaGuardadaModel>> ListarTarjetasAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await _http.GetAsync("/api/pagos/tarjetas", ct);
            if (!res.IsSuccessStatusCode) return Array.Empty<TarjetaGuardadaModel>();
            return await res.Content.ReadFromJsonAsync<List<TarjetaGuardadaModel>>(JsonOptions, ct)
                ?? new List<TarjetaGuardadaModel>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Array.Empty<TarjetaGuardadaModel>();
        }
    }

    public async Task<(bool Exito, string? Error)> EliminarTarjetaAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.DeleteAsync($"/api/pagos/tarjetas/{id}", ct);
            return res.IsSuccessStatusCode ? (true, null) : (false, await ParseProblemDetailsOrStringAsync(res, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, "No pudimos comunicarnos con el servicio de pagos.");
        }
    }

    // Credito toma el cliente del JWT de la sesion; la Web no manda ningun identificador.
    /// <summary>
    /// Oferta de credito para el total de la compra (GET /api/credito/elegibilidad)
    /// con limite propio de 1 segundo: el checkout no espera a Credito. No lanza.
    /// </summary>
    public async Task<ResultadoOfertaCreditoModel> ConsultarOfertaCreditoAsync(decimal monto, CancellationToken ct = default)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(LimiteOfertaCredito);

        try
        {
            var montoTexto = monto.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            using var res = await _http.GetAsync($"/api/credito/elegibilidad?monto={montoTexto}", limite.Token);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new ResultadoOfertaCreditoModel(EstadoConsultaCredito.SesionInvalida);
            }

            if (!res.IsSuccessStatusCode)
            {
                return new ResultadoOfertaCreditoModel(EstadoConsultaCredito.NoDisponible);
            }

            var oferta = await res.Content.ReadFromJsonAsync<OfertaCreditoModel>(JsonOptions, limite.Token);
            return oferta is null
                ? new ResultadoOfertaCreditoModel(EstadoConsultaCredito.NoDisponible)
                : new ResultadoOfertaCreditoModel(EstadoConsultaCredito.Ok, oferta);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return new ResultadoOfertaCreditoModel(EstadoConsultaCredito.NoDisponible);
        }
    }

    /// <summary>Crea un abono (POST /api/credito/abonos). 202 con el abono; si no, Status y mensaje.</summary>
    public Task<ResultadoAbonoCreditoModel> CrearAbonoCreditoAsync(string cuentaId, decimal monto, string token, CancellationToken ct = default) =>
        EnviarAbonoAsync(() => _http.PostAsJsonAsync("/api/credito/abonos", new { cuentaId, monto, tokenMedioPago = token }, JsonOptions, ct), ct);

    /// <summary>Reintenta un abono pendiente con un token nuevo (mismo abono).</summary>
    public Task<ResultadoAbonoCreditoModel> ReintentarAbonoCreditoAsync(string abonoId, string token, CancellationToken ct = default) =>
        EnviarAbonoAsync(() => _http.PostAsJsonAsync(
            $"/api/credito/abonos/{Uri.EscapeDataString(abonoId)}/reintentar", new { tokenMedioPago = token }, JsonOptions, ct), ct);

    public Task<ResultadoAbonoCreditoModel> ObtenerAbonoCreditoAsync(string abonoId, CancellationToken ct = default) =>
        EnviarAbonoAsync(() => _http.GetAsync($"/api/credito/abonos/{Uri.EscapeDataString(abonoId)}", ct), ct);

    private static async Task<ResultadoAbonoCreditoModel> EnviarAbonoAsync(Func<Task<HttpResponseMessage>> llamada, CancellationToken ct)
    {
        try
        {
            using var res = await llamada();
            if (res.IsSuccessStatusCode)
            {
                var abono = await res.Content.ReadFromJsonAsync<AbonoCreditoModel>(JsonOptions, ct);
                return abono is null
                    ? new ResultadoAbonoCreditoModel(null, StatusCodes.Status502BadGateway, ResultadoAbonoCreditoModel.MensajeNoDisponible)
                    : new ResultadoAbonoCreditoModel(abono, (int)res.StatusCode, null);
            }

            var status = (int)res.StatusCode;
            var mensaje = status is >= 400 and < 500
                ? await ParseProblemDetailsOrStringAsync(res, ct)
                : ResultadoAbonoCreditoModel.MensajeNoDisponible;
            return new ResultadoAbonoCreditoModel(null, status, mensaje);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return new ResultadoAbonoCreditoModel(null, StatusCodes.Status503ServiceUnavailable, ResultadoAbonoCreditoModel.MensajeNoDisponible);
        }
    }

    /// <summary>
    /// Mi credito (GET /api/credito/mi-credito) con limite propio de 2 segundos, para
    /// que una caida de Credito no frene la pagina. No lanza excepciones.
    /// </summary>
    public async Task<ResultadoMiCreditoModel> ObtenerMiCreditoAsync(CancellationToken ct = default)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(LimiteMiCredito);

        try
        {
            using var res = await _http.GetAsync("/api/credito/mi-credito", limite.Token);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new ResultadoMiCreditoModel(EstadoConsultaCredito.SesionInvalida);
            }

            if (!res.IsSuccessStatusCode)
            {
                return new ResultadoMiCreditoModel(EstadoConsultaCredito.NoDisponible);
            }

            var credito = await res.Content.ReadFromJsonAsync<MiCreditoModel>(JsonOptions, limite.Token);
            return credito is null
                ? new ResultadoMiCreditoModel(EstadoConsultaCredito.NoDisponible)
                : new ResultadoMiCreditoModel(EstadoConsultaCredito.Ok, credito);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return new ResultadoMiCreditoModel(EstadoConsultaCredito.NoDisponible);
        }
    }

    /// <summary>
    /// Tokeniza una tarjeta nueva en la boveda de Pagos (POST /api/pagos/tokens). Pagos
    /// la valida (autoridad) y, con sesion y guardar=true, la guarda al cobrar. 201 con
    /// token; 422 con errores por campo; cualquier otra cosa, mensaje generico. El
    /// cuerpo lleva el PAN y el CVV: no se registra.
    /// </summary>
    public Task<ResultadoTokenPagoModel> TokenizarTarjetaAsync(SolicitudTokenPagoModel solicitud, CancellationToken ct = default) =>
        EnviarTokenizacionAsync(() => _http.PostAsJsonAsync("/api/pagos/tokens", solicitud, ct), ct);

    /// <summary>
    /// Token de la boveda de Pagos para pagar con una tarjeta guardada, con el CVV que
    /// escribio el cliente. 201 con token; 404 si no es suya; 422 con errores por campo.
    /// </summary>
    public Task<ResultadoTokenPagoModel> ObtenerTokenTarjetaAsync(Guid id, string cvv, CancellationToken ct = default) =>
        EnviarTokenizacionAsync(() => _http.PostAsJsonAsync($"/api/pagos/tarjetas/{id}/token", new { cvv }, ct), ct);

    private static async Task<ResultadoTokenPagoModel> EnviarTokenizacionAsync(
        Func<Task<HttpResponseMessage>> llamada,
        CancellationToken ct)
    {
        try
        {
            using var res = await llamada();
            if (res.IsSuccessStatusCode)
            {
                var token = await res.Content.ReadFromJsonAsync<TokenPagoModel>(JsonOptions, ct);
                return token is null
                    ? ResultadoTokenPagoModel.Falla(StatusCodes.Status502BadGateway, "Respuesta vacia del servicio de pagos.")
                    : new ResultadoTokenPagoModel(token, (int)res.StatusCode, null, null);
            }

            var status = (int)res.StatusCode;
            if (status == StatusCodes.Status422UnprocessableEntity)
            {
                var errores = await LeerErroresPorCampoAsync(res, ct);
                return new ResultadoTokenPagoModel(null, status, errores, "Revisa los datos de la tarjeta.");
            }

            return ResultadoTokenPagoModel.Falla(status, await ParseProblemDetailsOrStringAsync(res, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ResultadoTokenPagoModel.Falla(
                StatusCodes.Status503ServiceUnavailable, "No pudimos comunicarnos con el servicio de pagos.");
        }
    }

    /// <summary>Lee {errores:{campo:mensaje}} de un 422; diccionario vacio si el cuerpo no lo trae.</summary>
    private static async Task<Dictionary<string, string>> LeerErroresPorCampoAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var errores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var documento = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (documento.RootElement.TryGetProperty("errores", out var porCampo) && porCampo.ValueKind == JsonValueKind.Object)
            {
                foreach (var campo in porCampo.EnumerateObject())
                {
                    if (campo.Value.ValueKind == JsonValueKind.String)
                    {
                        errores[campo.Name] = campo.Value.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Cuerpo no JSON: sin errores por campo.
        }

        return errores;
    }

    /// <summary>
    /// Catalogo de sucursales activas de Inventario (GET /api/inventario/sucursales).
    /// Se guarda 5 minutos en memoria de la Web: cambia rara vez y lo piden el
    /// catalogo, el checkout y el rastreo en cada carga. Si Inventario no responde
    /// devuelve una lista vacia y no la cachea.
    /// </summary>
    public async Task<IReadOnlyList<SucursalModel>> ObtenerSucursalesAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(ClaveCacheSucursales, out IReadOnlyList<SucursalModel>? enCache) && enCache is not null)
        {
            return enCache;
        }

        try
        {
            var res = await _http.GetAsync("/api/inventario/sucursales", ct);
            if (!res.IsSuccessStatusCode)
            {
                return Array.Empty<SucursalModel>();
            }

            var sucursales = await res.Content.ReadFromJsonAsync<List<SucursalModel>>(JsonOptions, ct)
                ?? new List<SucursalModel>();

            _cache.Set(ClaveCacheSucursales, (IReadOnlyList<SucursalModel>)sucursales, VigenciaCacheSucursales);
            return sucursales;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Array.Empty<SucursalModel>();
        }
    }

    /// <summary>
    /// Catalogo de productos activos de Inventario (GET /api/inventario/productos), la
    /// fuente del precio. Se guarda 5 minutos en memoria de la Web. Si Inventario no
    /// responde devuelve una lista vacia y no la cachea.
    /// </summary>
    public async Task<IReadOnlyList<ProductoInventarioModel>> ObtenerProductosAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(ClaveCacheProductos, out IReadOnlyList<ProductoInventarioModel>? enCache) && enCache is not null)
        {
            return enCache;
        }

        try
        {
            var res = await _http.GetAsync("/api/inventario/productos", ct);
            if (!res.IsSuccessStatusCode)
            {
                return Array.Empty<ProductoInventarioModel>();
            }

            var productos = await res.Content.ReadFromJsonAsync<List<ProductoInventarioModel>>(JsonOptions, ct)
                ?? new List<ProductoInventarioModel>();

            _cache.Set(ClaveCacheProductos, (IReadOnlyList<ProductoInventarioModel>)productos, VigenciaCacheProductos);
            return productos;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Array.Empty<ProductoInventarioModel>();
        }
    }

    /// <summary>Tienda mas cercana a <paramref name="desde" /> con stock del SKU, o null si ninguna tiene (404).</summary>
    public async Task<SucursalCercanaModel?> ObtenerSucursalCercanaAsync(string sku, string? desde, int cantidad = 1, CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/inventario/existencias/{Uri.EscapeDataString(sku)}/cercana?cantidad={cantidad}"
                      + (string.IsNullOrWhiteSpace(desde) ? string.Empty : $"&desde={Uri.EscapeDataString(desde)}");
            var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadFromJsonAsync<SucursalCercanaModel>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Simula sin reservar desde donde saldria el pedido (POST /api/inventario/reservas/simular).
    /// Null si Inventario no respondio.
    /// </summary>
    public async Task<SimulacionReservaModel?> SimularReservaAsync(SimularReservaModel model, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("/api/inventario/reservas/simular", model, ct);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadFromJsonAsync<SimulacionReservaModel>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public async Task<List<UbicacionResumenDto>?> ObtenerUbicacionesInventarioAsync(CancellationToken ct = default)
    {
        var res = await _http.GetAsync("/api/inventario/ubicaciones", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<UbicacionResumenDto>>(JsonOptions, ct);
    }

    public async Task<List<ExistenciaUbicacionDto>?> ObtenerExistenciasPorUbicacionAsync(string ubicacionId, CancellationToken ct = default)
    {
        var res = await _http.GetAsync($"/api/inventario/ubicaciones/{ubicacionId}/existencias", ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<ExistenciaUbicacionDto>>(JsonOptions, ct);
    }

    // ── Incidencias (Listar / Resolver) ────────────────────────────────────────
    public async Task<List<IncidenciaModel>?> ListarIncidenciasAsync(string? ordenId = null, string? estado = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(ordenId)) query.Add($"ordenId={Uri.EscapeDataString(ordenId)}");
        if (!string.IsNullOrWhiteSpace(estado)) query.Add($"estado={Uri.EscapeDataString(estado)}");
        var url = "/api/incidencias" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        var res = await _http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<List<IncidenciaModel>>(JsonOptions, ct);
    }

    public async Task<(bool Exito, string? Error)> ActualizarIncidenciaAsync(Guid id, string? descripcion, string? estado, string? resolucion, CancellationToken ct = default)
    {
        var payload = new { Descripcion = descripcion, Estado = estado, Resolucion = resolucion };
        var res = await _http.PutAsJsonAsync($"/api/incidencias/{id}", payload, JsonOptions, ct);
        if (res.IsSuccessStatusCode) return (true, null);
        var err = await ParseProblemDetailsOrStringAsync(res, ct);
        return (false, err);
    }

    public async Task<(bool Exito, string? MensajeId, string? Error)> EnviarSmsAsync(string telefono, string mensaje, CancellationToken ct = default)
    {
        try
        {
            var payload = new { Destinatario = telefono, Asunto = "MegaStore Comprobante", Cuerpo = mensaje };
            var res = await _http.PostAsJsonAsync("/api/mensajes/sms", payload, JsonOptions, ct);
            if (res.IsSuccessStatusCode)
            {
                var doc = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
                var id = doc.TryGetProperty("mensajeId", out var idProp) ? idProp.GetString() : null;
                return (true, id, null);
            }
            var err = await ParseProblemDetailsOrStringAsync(res, ct);
            return (false, null, err);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Exito, EnlaceReintentoInfoModel? Info, string? Error)> ObtenerReintentoPagoAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var res = await _http.GetAsync($"/api/pagos/reintentos/{Uri.EscapeDataString(token)}", ct);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, null, "El enlace de pago no existe o no fue encontrado.");
            }

            if (!res.IsSuccessStatusCode)
            {
                var err = await ParseProblemDetailsOrStringAsync(res, ct);
                return (false, null, err);
            }

            var info = await res.Content.ReadFromJsonAsync<EnlaceReintentoInfoModel>(JsonOptions, ct);
            return (true, info, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Ejecuta el reintento. Sin <paramref name="tokenMedioPago" /> ni <paramref name="cvv" />
    /// Pagos reusa el token del ultimo intento; si vencio responde 422 con los datos
    /// que hay que volver a pedir (<see cref="ResultadoReintentoModel.DatosRequeridos" />).
    /// </summary>
    public async Task<ResultadoReintentoModel> EjecutarReintentoPagoAsync(
        string token,
        string? tokenMedioPago = null,
        string? cvv = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/pagos/reintentos/{Uri.EscapeDataString(token)}";
            using var res = string.IsNullOrWhiteSpace(tokenMedioPago) && string.IsNullOrWhiteSpace(cvv)
                ? await _http.PostAsync(url, null, ct)
                : await _http.PostAsJsonAsync(url, new { tokenMedioPago, cvv }, ct);

            if (res.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                return new ResultadoReintentoModel(false, null, "El enlace de reintento ya se utilizó o ha expirado.");
            }

            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new ResultadoReintentoModel(false, null, "El enlace de reintento no fue encontrado.");
            }

            if (res.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var cuerpo = await res.Content.ReadAsStringAsync(ct);
                string? datosRequeridos = null;
                string? detalle = null;
                var errores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    using var documento = JsonDocument.Parse(cuerpo);
                    var raiz = documento.RootElement;
                    if (raiz.TryGetProperty("datosRequeridos", out var requeridos) && requeridos.ValueKind == JsonValueKind.String)
                    {
                        datosRequeridos = requeridos.GetString();
                    }

                    if (raiz.TryGetProperty("detail", out var detalleProp) && detalleProp.ValueKind == JsonValueKind.String)
                    {
                        detalle = detalleProp.GetString();
                    }

                    if (raiz.TryGetProperty("errores", out var porCampo) && porCampo.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var campo in porCampo.EnumerateObject())
                        {
                            errores[campo.Name] = campo.Value.GetString() ?? string.Empty;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Cuerpo no JSON: mensaje generico.
                }

                return new ResultadoReintentoModel(
                    false,
                    null,
                    detalle ?? "Revisa los datos de tu tarjeta.",
                    datosRequeridos,
                    errores.Count > 0 ? errores : null);
            }

            if (!res.IsSuccessStatusCode)
            {
                var err = await ParseProblemDetailsOrStringAsync(res, ct);
                return new ResultadoReintentoModel(false, null, err);
            }

            var pago = await res.Content.ReadFromJsonAsync<PagoReintentoResultadoModel>(JsonOptions, ct);
            return new ResultadoReintentoModel(true, pago, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ResultadoReintentoModel(false, null, "No pudimos comunicarnos con el servicio de pagos.");
        }
    }
}

