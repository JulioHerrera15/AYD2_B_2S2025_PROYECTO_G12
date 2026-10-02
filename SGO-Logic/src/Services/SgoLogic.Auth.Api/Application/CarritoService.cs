using Microsoft.EntityFrameworkCore;
using SgoLogic.Auth.Api.Infrastructure;

namespace SgoLogic.Auth.Api.Application;

public interface ICarritoService
{
    Task<CarritoDto> ObtenerAsync(string clienteOSesionId, CancellationToken ct = default);
    Task AgregarItemAsync(string clienteOSesionId, AgregarItemRequest request, CancellationToken ct = default);
    Task ActualizarCantidadItemAsync(string clienteOSesionId, string sku, int nuevaCantidad, string? ubicacionPreferidaId = null, CancellationToken ct = default);
    Task EliminarItemAsync(string clienteOSesionId, string sku, CancellationToken ct = default);
    Task AsociarAsync(string sesionId, string clienteId, CancellationToken ct = default);

    /// <summary>
    /// Pide a Inventario que extienda las retenciones activas del carrito y reactive
    /// las vencidas donde todavia haya stock. Devuelve el carrito con el estado de
    /// cada item ya sincronizado.
    /// </summary>
    Task<CarritoDto> RenovarReservaAsync(string clienteOSesionId, CancellationToken ct = default);

    /// <summary>
    /// Vacia el carrito completo (borra la clave de Redis). Lo usa el endpoint
    /// interno que llama Ordenes al confirmar una orden: esa orden ya consumio
    /// el carrito, sin importar si el cobro posterior queda pendiente o
    /// exitoso. No libera el stock retenido: Inventario lo adopta al reservar la
    /// orden y, si la orden nunca llega, las retenciones vencen solas.
    /// </summary>
    Task VaciarAsync(string clienteOSesionId, CancellationToken ct = default);
}

/// <summary>
/// Carrito unificado en Redis (cart:{usuarioId} o guest:{sesionId}) con retencion
/// de stock en Inventario. Cada cambio de cantidad pasa primero por Inventario: si
/// Inventario rechaza (409) o no responde (503), la excepcion sube y Redis no se toca,
/// de modo que el carrito nunca muestra unidades que no estan apartadas.
/// </summary>
public sealed class CarritoService : ICarritoService
{
    private const string EstadoActiva = "ACTIVA";

    private readonly IAlmacenCarrito _almacen;
    private readonly AuthDbContext _dbContext;
    private readonly IClienteInventarioCarrito _inventario;
    private readonly int _ttlSegundos;

    // Prefijos de clave Redis
    private const string PrefijoCuenta = "cart:";
    private const string PrefijoInvitado = "guest:";

    public CarritoService(
        IAlmacenCarrito almacen,
        AuthDbContext dbContext,
        IClienteInventarioCarrito inventario,
        IConfiguration config)
    {
        _almacen = almacen;
        _dbContext = dbContext;
        _inventario = inventario;
        _ttlSegundos = config.GetValue<int>("Auth:CarritoTtlSegundos", 86_400);
    }

    public async Task<CarritoDto> ObtenerAsync(string clienteOSesionId, CancellationToken ct = default)
    {
        var clave = await ResolverClaveAsync(clienteOSesionId, ct);
        var items = await _almacen.LeerAsync(clave);

        // Si no se encontró por cuenta, intentar alternativo (por ejemplo si era invitado)
        if (items is null && !clienteOSesionId.Contains('@'))
        {
            var claveAlterna = clave.StartsWith(PrefijoCuenta)
                ? PrefijoInvitado + clienteOSesionId
                : PrefijoCuenta + clienteOSesionId;
            var itemsAlternos = await _almacen.LeerAsync(claveAlterna);
            if (itemsAlternos is not null)
            {
                items = itemsAlternos;
                clave = claveAlterna;
            }
        }

        if (items is null || items.Count == 0)
            return Construir(clienteOSesionId, clave, []);

        try
        {
            var retenciones = await _inventario.ObtenerAsync(clave, ct);
            items = Sincronizar(items, retenciones.Lineas);
        }
        catch (Exception ex) when (ex is ExcepcionInventarioNoDisponible or ExcepcionInventarioRechazo)
        {
            // Consultar no debe fallar por Inventario: se muestra lo ultimo que se supo
            // de cada item. Las operaciones que cambian el carrito si exigen Inventario.
        }

        return Construir(clienteOSesionId, clave, items);
    }

    /// <summary>
    /// Agrega o suma unidades de un SKU. El precio y la descripcion del cliente se
    /// ignoran: se toman del catalogo de Inventario (B1). Si el SKU no existe o esta
    /// inactivo lanza <see cref="ExcepcionProductoNoEncontrado" /> sin tocar nada.
    /// </summary>
    public async Task AgregarItemAsync(string clienteOSesionId, AgregarItemRequest request, CancellationToken ct = default)
    {
        if (request.Cantidad <= 0)
            throw new ArgumentException("La cantidad debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(request.Sku))
            throw new ArgumentException("El SKU es obligatorio.");

        var producto = await ObtenerProductoVendibleAsync(request.Sku, ct);

        var clave = await ResolverClaveAsync(clienteOSesionId, ct);
        var items = await _almacen.LeerAsync(clave) ?? new List<ItemCarritoDto>();

        var existente = items.FirstOrDefault(i => i.Sku == request.Sku);
        var cantidadTotal = (existente?.Cantidad ?? 0) + request.Cantidad;

        // Primero Inventario: si no aparta, Redis no cambia.
        var linea = await _inventario.RetenerAsync(clave, request.Sku, cantidadTotal, request.UbicacionPreferidaId, ct);

        var itemBase = existente ?? new ItemCarritoDto(request.Sku, producto.Nombre, 0, producto.Precio);
        if (existente is not null)
        {
            // Sumar cantidad si el SKU ya esta en el carrito
            items.Remove(existente);
        }

        items.Add(ConRetencion(
            itemBase with { Cantidad = cantidadTotal, Descripcion = producto.Nombre, PrecioUnitario = producto.Precio },
            linea));
        RenovarExpiraciones(items, linea.ExpiraEn);

        await _almacen.GuardarAsync(clave, items, TimeSpan.FromSeconds(_ttlSegundos));
    }

    public async Task ActualizarCantidadItemAsync(
        string clienteOSesionId,
        string sku,
        int nuevaCantidad,
        string? ubicacionPreferidaId = null,
        CancellationToken ct = default)
    {
        if (nuevaCantidad <= 0)
        {
            await EliminarItemAsync(clienteOSesionId, sku, ct);
            return;
        }

        var clave = await ResolverClaveAsync(clienteOSesionId, ct);
        var items = await _almacen.LeerAsync(clave);
        if (items is null) return;

        var existente = items.FirstOrDefault(i => i.Sku == sku);
        if (existente is null) return;

        // El precio vigente se refresca desde Inventario en cada cambio de cantidad.
        var producto = await ObtenerProductoVendibleAsync(sku, ct);

        var linea = await _inventario.RetenerAsync(clave, sku, nuevaCantidad, ubicacionPreferidaId, ct);

        items.Remove(existente);
        items.Add(ConRetencion(
            existente with { Cantidad = nuevaCantidad, Descripcion = producto.Nombre, PrecioUnitario = producto.Precio },
            linea));
        RenovarExpiraciones(items, linea.ExpiraEn);

        await _almacen.GuardarAsync(clave, items, TimeSpan.FromSeconds(_ttlSegundos));
    }

    public async Task EliminarItemAsync(string clienteOSesionId, string sku, CancellationToken ct = default)
    {
        var clave = await ResolverClaveAsync(clienteOSesionId, ct);

        // Liberar en Inventario es idempotente: se llama aunque Redis ya no tenga el
        // item, para no dejar stock apartado sin carrito que lo respalde.
        await _inventario.LiberarItemAsync(clave, sku, ct);

        var items = await _almacen.LeerAsync(clave);
        if (items is null) return;

        items.RemoveAll(i => i.Sku == sku);

        if (items.Count == 0)
            await _almacen.BorrarAsync(clave);
        else
            await _almacen.GuardarAsync(clave, items, TimeSpan.FromSeconds(_ttlSegundos));
    }

    public async Task<CarritoDto> RenovarReservaAsync(string clienteOSesionId, CancellationToken ct = default)
    {
        var clave = await ResolverClaveAsync(clienteOSesionId, ct);
        var items = await _almacen.LeerAsync(clave);

        if (items is null || items.Count == 0)
            return Construir(clienteOSesionId, clave, []);

        var retenciones = await _inventario.RenovarAsync(clave, items.Select(i => i.Sku).Distinct().ToList(), ct);
        items = Sincronizar(items, retenciones.Lineas);

        await _almacen.GuardarAsync(clave, items, TimeSpan.FromSeconds(_ttlSegundos));
        return Construir(clienteOSesionId, clave, items);
    }

    public async Task VaciarAsync(string clienteOSesionId, CancellationToken ct = default)
    {
        var clave = await ResolverClaveAsync(clienteOSesionId, ct);
        await _almacen.BorrarAsync(clave);
    }

    public async Task AsociarAsync(string sesionId, string clienteId, CancellationToken ct = default)
    {
        // Migra guest:{sesionId} → cart:{clienteId}
        // Si cart:{clienteId} ya existe, mergea sumando cantidades por SKU
        var claveInvitado = sesionId.StartsWith(PrefijoInvitado) ? sesionId : PrefijoInvitado + sesionId;
        var claveCuenta = clienteId.StartsWith(PrefijoCuenta) ? clienteId : PrefijoCuenta + clienteId;

        var itemsInvitado = await _almacen.LeerAsync(claveInvitado);
        if (itemsInvitado is null)
        {
            var claveInvitadoAlt = sesionId.StartsWith(PrefijoCuenta) ? sesionId : PrefijoCuenta + sesionId;
            itemsInvitado = await _almacen.LeerAsync(claveInvitadoAlt);
            if (itemsInvitado is not null)
            {
                claveInvitado = claveInvitadoAlt;
            }
            else
            {
                return; // no hay carrito de invitado, nada que hacer
            }
        }

        if (string.Equals(claveInvitado, claveCuenta, StringComparison.Ordinal))
        {
            return;
        }

        // Las retenciones de stock se mueven primero: si Inventario no responde, el
        // carrito de invitado sigue intacto y se puede reintentar.
        var retenciones = await _inventario.TransferirAsync(claveInvitado, claveCuenta, ct);

        var itemsCuenta = await _almacen.LeerAsync(claveCuenta) ?? new List<ItemCarritoDto>();

        // Mergear: si el SKU ya existe en la cuenta, se suman las cantidades;
        // si no existe, se agrega el item completo del invitado.
        foreach (var itemInvitado in itemsInvitado)
        {
            var existente = itemsCuenta.FirstOrDefault(i => i.Sku == itemInvitado.Sku);
            if (existente is not null)
            {
                itemsCuenta.Remove(existente);
                itemsCuenta.Add(existente with { Cantidad = existente.Cantidad + itemInvitado.Cantidad });
            }
            else
            {
                itemsCuenta.Add(itemInvitado);
            }
        }

        // Si la suma no cupo en Inventario, el carrito muestra lo que quedo apartado.
        itemsCuenta = Sincronizar(itemsCuenta, retenciones.Lineas)
            .Select(item => item.EstadoReserva == EstadoActiva
                            && retenciones.Lineas.FirstOrDefault(l => l.Sku == item.Sku) is { } linea
                            && linea.Cantidad < item.Cantidad
                ? item with { Cantidad = linea.Cantidad }
                : item)
            .ToList();

        await _almacen.GuardarAsync(claveCuenta, itemsCuenta, TimeSpan.FromSeconds(_ttlSegundos));
        await _almacen.BorrarAsync(claveInvitado);
    }

    /// <summary>El producto del catalogo de Inventario; 404 (excepcion) si no existe o esta inactivo.</summary>
    private async Task<ProductoInventario> ObtenerProductoVendibleAsync(string sku, CancellationToken ct)
    {
        var producto = await _inventario.ObtenerProductoAsync(sku.Trim(), ct);
        if (producto is null || !producto.Activo)
        {
            throw new ExcepcionProductoNoEncontrado(sku.Trim());
        }

        return producto;
    }

    private static ItemCarritoDto ConRetencion(ItemCarritoDto item, LineaRetencionInventario? linea) =>
        linea is null
            ? item with
            {
                UbicacionId = null,
                NombreUbicacion = null,
                ExpiraEn = null,
                EstadoReserva = null,
                EnOtraSucursal = null
            }
            : item with
            {
                UbicacionId = linea.UbicacionId,
                NombreUbicacion = linea.NombreUbicacion,
                ExpiraEn = linea.ExpiraEn,
                EstadoReserva = linea.Estado,
                EnOtraSucursal = linea.EnOtraSucursal
            };

    /// <summary>Copia a cada item el estado de su retencion en Inventario (por SKU).</summary>
    private static List<ItemCarritoDto> Sincronizar(
        IEnumerable<ItemCarritoDto> items,
        IReadOnlyList<LineaRetencionInventario> lineas) =>
        items
            .Select(item => ConRetencion(item, lineas.FirstOrDefault(linea => linea.Sku == item.Sku)))
            .ToList();

    /// <summary>
    /// Cualquier cambio renueva en Inventario el plazo de todas las lineas activas
    /// del carrito: aqui se refleja sin otra consulta.
    /// </summary>
    private static void RenovarExpiraciones(List<ItemCarritoDto> items, DateTime expiraEn)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].EstadoReserva == EstadoActiva)
            {
                items[i] = items[i] with { ExpiraEn = expiraEn };
            }
        }
    }

    private static CarritoDto Construir(string clienteOSesionId, string clave, IReadOnlyList<ItemCarritoDto> items)
    {
        var total = items.Sum(i => i.PrecioUnitario * i.Cantidad);
        var expiraEn = items
            .Where(i => i.EstadoReserva == EstadoActiva && i.ExpiraEn is not null)
            .Select(i => i.ExpiraEn)
            .Min();

        return new CarritoDto(clienteOSesionId, items, total, expiraEn, clave);
    }

    private async Task<string> ResolverClaveAsync(string id, CancellationToken ct)
    {
        if (id.StartsWith(PrefijoInvitado) || id.StartsWith(PrefijoCuenta))
            return id;

        // Si el identificador es un correo electrónico, resolver el usuarioId en PostgreSQL
        if (id.Contains('@'))
        {
            var usuario = await _dbContext.Usuarios.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == id.ToLower(), ct);
            if (usuario is not null)
            {
                return PrefijoCuenta + usuario.Id.ToString();
            }
        }

        return PrefijoCuenta + id;
    }
}
