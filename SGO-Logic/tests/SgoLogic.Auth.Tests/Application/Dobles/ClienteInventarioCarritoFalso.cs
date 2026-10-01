using SgoLogic.Auth.Api.Application;

namespace SgoLogic.Auth.Tests.Application.Dobles;

/// <summary>
/// Doble de <see cref="IClienteInventarioCarrito" />. Por defecto aparta todo en
/// la ubicacion indicada; se puede armar para responder 409 (sin stock) o para
/// simular que Inventario esta caido.
/// </summary>
public sealed class ClienteInventarioCarritoFalso : IClienteInventarioCarrito
{
    public const string CuerpoSinStock =
        "{\"sku\":\"PROD-104\",\"disponible\":0,\"mensaje\":\"No hay existencias de PROD-104 en ninguna sucursal.\",\"sucursalCercana\":null}";

    public bool RechazarSinStock { get; set; }

    public bool Caido { get; set; }

    public string UbicacionAsignada { get; set; } = "TIENDA-01-CENTRAL";

    public List<(string CarritoId, string Sku, int Cantidad, string? Preferida)> Retenciones { get; } = new();

    public List<(string CarritoId, string Sku)> Liberaciones { get; } = new();

    public Task<LineaRetencionInventario> RetenerAsync(
        string carritoId,
        string sku,
        int cantidad,
        string? ubicacionPreferidaId,
        CancellationToken ct = default)
    {
        Fallar();
        Retenciones.Add((carritoId, sku, cantidad, ubicacionPreferidaId));

        return Task.FromResult(new LineaRetencionInventario(
            sku,
            UbicacionAsignada,
            "MegaStore Central",
            cantidad,
            DateTime.UtcNow.AddMinutes(15),
            "ACTIVA",
            ubicacionPreferidaId is not null && ubicacionPreferidaId != UbicacionAsignada));
    }

    public Task LiberarItemAsync(string carritoId, string sku, CancellationToken ct = default)
    {
        Fallar();
        Liberaciones.Add((carritoId, sku));
        return Task.CompletedTask;
    }

    public Task<RetencionesCarritoInventario> RenovarAsync(string carritoId, IReadOnlyList<string> skus, CancellationToken ct = default)
    {
        Fallar();
        return Task.FromResult(new RetencionesCarritoInventario(carritoId, null, Array.Empty<LineaRetencionInventario>()));
    }

    public Task<RetencionesCarritoInventario> TransferirAsync(string carritoOrigenId, string carritoDestinoId, CancellationToken ct = default)
    {
        Fallar();
        return Task.FromResult(new RetencionesCarritoInventario(carritoDestinoId, null, Array.Empty<LineaRetencionInventario>()));
    }

    public Task<RetencionesCarritoInventario> ObtenerAsync(string carritoId, CancellationToken ct = default)
    {
        Fallar();
        return Task.FromResult(new RetencionesCarritoInventario(carritoId, null, Array.Empty<LineaRetencionInventario>()));
    }

    private void Fallar()
    {
        if (Caido)
        {
            throw new ExcepcionInventarioNoDisponible("Inventario no respondio.", new HttpRequestException("connection refused"));
        }

        if (RechazarSinStock)
        {
            throw new ExcepcionInventarioRechazo(409, CuerpoSinStock, "application/json");
        }
    }
}
