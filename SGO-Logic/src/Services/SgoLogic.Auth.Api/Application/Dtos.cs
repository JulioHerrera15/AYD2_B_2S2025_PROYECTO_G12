namespace SgoLogic.Auth.Api.Application;

// ── Registro ──────────────────────────────────────────────────────────────────

public sealed record RegistroRequest(
    string Email,
    string Contrasena,
    string NombreCompleto);

public sealed record RegistroResponse(
    string UsuarioId,
    string Email,
    string Mensaje);

// ── Verificacion ──────────────────────────────────────────────────────────────

public sealed record VerificarEmailRequest(
    string Email,
    string Codigo);

public sealed record VerificarEmailResponse(string Mensaje);

public sealed record ReenviarCodigoRequest(string Email);

// ── Login ─────────────────────────────────────────────────────────────────────

public sealed record LoginRequest(
    string Email,
    string Contrasena);

public sealed record LoginResponse(
    string Token,
    string UsuarioId,
    string Email,
    string NombreCompleto,
    DateTime ExpiraEn,
    string Rol = "CLIENTE");

// ── Carrito ───────────────────────────────────────────────────────────────────

/// <summary>
/// Item del carrito. Los cinco campos finales describen la retencion de stock en
/// Inventario (null si el item no tiene retencion conocida): donde quedo apartado,
/// hasta cuando, su estado (ACTIVA o EXPIRADA) y si es otra sucursal distinta de la
/// que eligio el cliente.
/// </summary>
public sealed record ItemCarritoDto(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    string? UbicacionId = null,
    string? NombreUbicacion = null,
    DateTime? ExpiraEn = null,
    string? EstadoReserva = null,
    bool? EnOtraSucursal = null);

/// <param name="ReservaExpiraEn">La menor expiracion entre las retenciones activas del carrito.</param>
/// <param name="CarritoId">Clave del carrito (cart:{usuarioId} o guest:{sesionId}); es el carritoId de la orden.</param>
public sealed record CarritoDto(
    string ClienteOSesionId,
    IReadOnlyList<ItemCarritoDto> Items,
    decimal Total,
    DateTime? ReservaExpiraEn = null,
    string? CarritoId = null);

/// <param name="UbicacionPreferidaId">Sucursal elegida por el cliente: Inventario intenta apartar ahi primero.</param>
public sealed record AgregarItemRequest(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    string? UbicacionPreferidaId = null);

public sealed record AsociarCarritoRequest(
    string SesionId);

public sealed record ActualizarCantidadRequest(
    int Cantidad,
    string? UbicacionPreferidaId = null);

// ── Gestion de Roles y Usuarios ──────────────────────────────────────────────

public sealed record AsignarRolRequest(
    string Email,
    string Rol);

public sealed record AsignarRolResponse(
    string Email,
    string Rol,
    string Mensaje);

public sealed record UsuarioDto(
    Guid Id,
    string Email,
    string NombreCompleto,
    string Rol,
    bool EmailVerificado,
    DateTime CreadoEn);
