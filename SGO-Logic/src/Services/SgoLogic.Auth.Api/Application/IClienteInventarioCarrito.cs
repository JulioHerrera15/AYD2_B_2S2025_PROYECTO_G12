namespace SgoLogic.Auth.Api.Application;

/// <summary>
/// Puerto hacia Inventario para retener stock mientras el producto esta en el
/// carrito (endpoints /interno/inventario/carritos, solo red interna). El
/// carritoId es la misma clave de Redis que usa <see cref="CarritoService" />:
/// cart:{usuarioId} o guest:{sesionId}.
///
/// Errores:
/// <list type="bullet">
///   <item><see cref="ExcepcionInventarioRechazo" />: Inventario respondio 409 (sin
///   stock o choque de concurrencia). Trae el cuerpo tal cual para devolverlo.</item>
///   <item><see cref="ExcepcionInventarioNoDisponible" />: no respondio, tardo
///   demasiado o fallo con 5xx.</item>
/// </list>
/// </summary>
public interface IClienteInventarioCarrito
{
    /// <summary>Deja retenidas <paramref name="cantidad" /> unidades (total, no incremento) del SKU.</summary>
    Task<LineaRetencionInventario> RetenerAsync(
        string carritoId,
        string sku,
        int cantidad,
        string? ubicacionPreferidaId,
        CancellationToken ct = default);

    Task LiberarItemAsync(string carritoId, string sku, CancellationToken ct = default);

    /// <summary>Extiende o reactiva las lineas; las de SKU fuera de <paramref name="skus" /> se cierran.</summary>
    Task<RetencionesCarritoInventario> RenovarAsync(string carritoId, IReadOnlyList<string> skus, CancellationToken ct = default);

    Task<RetencionesCarritoInventario> TransferirAsync(string carritoOrigenId, string carritoDestinoId, CancellationToken ct = default);

    Task<RetencionesCarritoInventario> ObtenerAsync(string carritoId, CancellationToken ct = default);
}

/// <summary>Una linea retenida tal como la devuelve Inventario.</summary>
public sealed record LineaRetencionInventario(
    string Sku,
    string UbicacionId,
    string? NombreUbicacion,
    int Cantidad,
    DateTime ExpiraEn,
    string Estado,
    bool EnOtraSucursal);

/// <summary>Retenciones vigentes de un carrito segun Inventario.</summary>
public sealed record RetencionesCarritoInventario(
    string CarritoId,
    DateTime? ExpiraEn,
    IReadOnlyList<LineaRetencionInventario> Lineas);

/// <summary>
/// Inventario rechazo la operacion con 409. <see cref="Cuerpo" /> es el JSON que
/// respondio ({sku, disponible, mensaje, sucursalCercana} si fue por stock), para
/// que el carrito lo devuelva igual a quien llamo.
/// </summary>
public sealed class ExcepcionInventarioRechazo : Exception
{
    public ExcepcionInventarioRechazo(int status, string cuerpo, string? contentType)
        : base($"Inventario rechazo la operacion del carrito ({status}).")
    {
        Status = status;
        Cuerpo = cuerpo;
        ContentType = contentType;
    }

    public int Status { get; }

    public string Cuerpo { get; }

    public string? ContentType { get; }
}

/// <summary>Inventario no respondio (caido, timeout o 5xx). El carrito no debe modificarse.</summary>
public sealed class ExcepcionInventarioNoDisponible : Exception
{
    public ExcepcionInventarioNoDisponible(string mensaje, Exception? interna = null)
        : base(mensaje, interna)
    {
    }
}
