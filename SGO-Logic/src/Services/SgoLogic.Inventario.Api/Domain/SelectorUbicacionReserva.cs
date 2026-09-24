using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Inventario.Api.Domain;

/// <summary>Linea a reservar: SKU y cantidad pedida.</summary>
public sealed record LineaReservada(string Sku, int Cantidad);

/// <summary>Resultado de elegir ubicacion para una reserva.</summary>
/// <param name="UbicacionId">Ubicacion elegida, o null si ninguna puede satisfacer todas las lineas.</param>
/// <param name="Motivo">Cuando no hay ubicacion, por que: falta registro de existencia o falta disponible.</param>
/// <param name="Faltantes">Por SKU, cuanto se pidio y cuanto habia disponible en la mejor ubicacion evaluada.</param>
public sealed record SeleccionUbicacion(
    string? UbicacionId,
    MotivoRechazoReserva? Motivo,
    IReadOnlyList<FaltanteLinea> Faltantes);

/// <summary>Detalle de una linea que no se pudo cubrir.</summary>
public sealed record FaltanteLinea(string Sku, int Solicitado, int Disponible);

/// <summary>
/// Elige la unica ubicacion desde la que se reservara un pedido (HU-001). La
/// eleccion de <b>origen optimo</b> multi-ubicacion es HU-011: aca la regla es
/// simple y determinista.
///
/// Una ubicacion es elegible si tiene existencia registrada para <b>todos</b> los
/// SKU pedidos y su disponible cubre cada cantidad. Entre las elegibles se
/// prefiere, en orden: (1) la ubicacion preferida que sugiere Ordenes; (2) por
/// tipo segun el metodo de entrega (RECOGIDA_TIENDA prefiere TIENDA, DOMICILIO
/// prefiere CENTRO_DISTRIBUCION); (3) la de menor disponible sobrante, para no
/// fragmentar el stock grande; (4) el identificador de ubicacion, como desempate
/// estable.
/// </summary>
public static class SelectorUbicacionReserva
{
    public static SeleccionUbicacion Seleccionar(
        IReadOnlyCollection<LineaReservada> lineas,
        IReadOnlyCollection<Existencia> existencias,
        string? ubicacionPreferidaId,
        MetodoEntrega metodoEntrega)
    {
        var pedidoPorSku = lineas
            .GroupBy(linea => linea.Sku)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Sum(linea => linea.Cantidad));

        var porUbicacion = existencias
            .GroupBy(existencia => existencia.UbicacionId)
            .ToList();

        var elegibles = new List<(string UbicacionId, TipoUbicacion Tipo, int Sobrante)>();

        foreach (var grupo in porUbicacion)
        {
            var porSku = grupo.ToDictionary(existencia => existencia.Sku);

            var cubreTodo = pedidoPorSku.All(par =>
                porSku.TryGetValue(par.Key, out var existencia) && existencia.Disponible >= par.Value);

            if (!cubreTodo)
            {
                continue;
            }

            var sobrante = pedidoPorSku.Sum(par => porSku[par.Key].Disponible - par.Value);
            elegibles.Add((grupo.Key, porSku[pedidoPorSku.Keys.First()].TipoUbicacion, sobrante));
        }

        if (elegibles.Count == 0)
        {
            return new SeleccionUbicacion(null, DeterminarMotivo(pedidoPorSku, existencias), Faltantes(pedidoPorSku, existencias));
        }

        var tipoPreferido = metodoEntrega == MetodoEntrega.RECOGIDA_TIENDA
            ? TipoUbicacion.TIENDA
            : TipoUbicacion.CENTRO_DISTRIBUCION;

        var elegida = elegibles
            .OrderByDescending(candidata => candidata.UbicacionId == ubicacionPreferidaId)
            .ThenByDescending(candidata => candidata.Tipo == tipoPreferido)
            .ThenBy(candidata => candidata.Sobrante)
            .ThenBy(candidata => candidata.UbicacionId, StringComparer.Ordinal)
            .First();

        return new SeleccionUbicacion(elegida.UbicacionId, null, Array.Empty<FaltanteLinea>());
    }

    private static MotivoRechazoReserva DeterminarMotivo(
        IReadOnlyDictionary<string, int> pedidoPorSku,
        IReadOnlyCollection<Existencia> existencias)
    {
        var skusConExistencia = existencias.Select(existencia => existencia.Sku).ToHashSet();
        var algunSkuSinRegistro = pedidoPorSku.Keys.Any(sku => !skusConExistencia.Contains(sku));

        return algunSkuSinRegistro
            ? MotivoRechazoReserva.SIN_UBICACION_ELEGIBLE
            : MotivoRechazoReserva.STOCK_INSUFICIENTE;
    }

    private static IReadOnlyList<FaltanteLinea> Faltantes(
        IReadOnlyDictionary<string, int> pedidoPorSku,
        IReadOnlyCollection<Existencia> existencias)
    {
        var disponiblePorSku = existencias
            .GroupBy(existencia => existencia.Sku)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Max(existencia => existencia.Disponible));

        return pedidoPorSku
            .Select(par => new FaltanteLinea(
                par.Key,
                par.Value,
                disponiblePorSku.TryGetValue(par.Key, out var disponible) ? disponible : 0))
            .Where(faltante => faltante.Disponible < faltante.Solicitado)
            .ToList();
    }
}
