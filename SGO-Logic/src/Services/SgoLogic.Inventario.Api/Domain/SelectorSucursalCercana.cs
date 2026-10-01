using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Domain;

/// <summary>Una tienda que puede cubrir el pedido, con lo que tiene y a que distancia esta del origen.</summary>
/// <param name="Disponible">Menor disponible entre las lineas pedidas (con una sola linea, el de ese SKU).</param>
/// <param name="DistanciaKm">Distancia desde la ubicacion de origen, o null si a alguna le faltan coordenadas.</param>
public sealed record SucursalCercana(Sucursal Sucursal, int Disponible, double? DistanciaKm);

/// <summary>
/// Busca la TIENDA activa mas cercana a una ubicacion de origen que tenga, para
/// todas las lineas pedidas, disponible suficiente. Excluye la propia ubicacion de
/// origen: la pregunta es "donde mas hay".
///
/// Orden de preferencia:
/// <list type="bullet">
///   <item>si el origen es una sucursal conocida con coordenadas, por distancia
///   Haversine (las candidatas sin coordenadas van al final);</item>
///   <item>si no, por menor disponible sobrante, el mismo criterio que usa
///   <see cref="SelectorUbicacionReserva" /> para no fragmentar el stock grande;</item>
///   <item>el identificador de ubicacion, como desempate estable.</item>
/// </list>
///
/// Solo considera tiendas: los centros de distribucion no atienden publico, asi que
/// no tiene sentido sugerirlos como "cambia a esta sucursal".
/// </summary>
public static class SelectorSucursalCercana
{
    public static SucursalCercana? Elegir(
        IReadOnlyCollection<LineaReservada> lineas,
        IReadOnlyCollection<Existencia> existencias,
        IReadOnlyCollection<Sucursal> sucursales,
        string? ubicacionOrigenId,
        IReadOnlyDictionary<(string UbicacionId, string Sku), int>? disponibleAdicional = null) =>
        Ordenar(lineas, existencias, sucursales, ubicacionOrigenId, disponibleAdicional).FirstOrDefault();

    public static IReadOnlyList<SucursalCercana> Ordenar(
        IReadOnlyCollection<LineaReservada> lineas,
        IReadOnlyCollection<Existencia> existencias,
        IReadOnlyCollection<Sucursal> sucursales,
        string? ubicacionOrigenId,
        IReadOnlyDictionary<(string UbicacionId, string Sku), int>? disponibleAdicional = null)
    {
        var pedidoPorSku = lineas
            .GroupBy(linea => linea.Sku)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Sum(linea => linea.Cantidad));

        if (pedidoPorSku.Count == 0)
        {
            return Array.Empty<SucursalCercana>();
        }

        var sucursalesPorId = sucursales
            .GroupBy(sucursal => sucursal.Id)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.First());

        var origen = ubicacionOrigenId is not null && sucursalesPorId.TryGetValue(ubicacionOrigenId, out var encontrada)
            ? encontrada
            : null;

        var candidatas = new List<(SucursalCercana Candidata, int Sobrante)>();

        foreach (var grupo in existencias.GroupBy(existencia => existencia.UbicacionId))
        {
            if (grupo.Key == ubicacionOrigenId
                || !sucursalesPorId.TryGetValue(grupo.Key, out var sucursal)
                || !sucursal.Activa
                || sucursal.TipoUbicacion != TipoUbicacion.TIENDA)
            {
                continue;
            }

            var porSku = grupo.ToDictionary(existencia => existencia.Sku);

            var cubreTodo = pedidoPorSku.All(par =>
                porSku.TryGetValue(par.Key, out var existencia)
                && SelectorUbicacionReserva.DisponibleEfectivo(existencia, disponibleAdicional) >= par.Value);

            if (!cubreTodo)
            {
                continue;
            }

            var disponibles = pedidoPorSku.Keys
                .Select(sku => SelectorUbicacionReserva.DisponibleEfectivo(porSku[sku], disponibleAdicional))
                .ToList();

            var sobrante = pedidoPorSku.Sum(par =>
                SelectorUbicacionReserva.DisponibleEfectivo(porSku[par.Key], disponibleAdicional) - par.Value);

            var distancia = origen?.DistanciaKmA(sucursal);

            candidatas.Add((new SucursalCercana(sucursal, disponibles.Min(), distancia), sobrante));
        }

        IOrderedEnumerable<(SucursalCercana Candidata, int Sobrante)> ordenadas = origen is { TieneCoordenadas: true }
            ? candidatas
                .OrderBy(par => par.Candidata.DistanciaKm is null)
                .ThenBy(par => par.Candidata.DistanciaKm ?? double.MaxValue)
                .ThenBy(par => par.Sobrante)
            : candidatas.OrderBy(par => par.Sobrante);

        return ordenadas
            .ThenBy(par => par.Candidata.Sucursal.Id, StringComparer.Ordinal)
            .Select(par => par.Candidata)
            .ToList();
    }
}
