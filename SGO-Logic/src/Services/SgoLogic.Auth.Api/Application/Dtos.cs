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

public sealed record ItemCarritoDto(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario);

public sealed record CarritoDto(
    string ClienteOSesionId,
    IReadOnlyList<ItemCarritoDto> Items,
    decimal Total);

public sealed record AgregarItemRequest(
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario);

public sealed record AsociarCarritoRequest(
    string SesionId);

public sealed record ActualizarCantidadRequest(
    int Cantidad);

