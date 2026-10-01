namespace SgoLogic.IntegracionPos.Worker.Application.Pos;

public sealed class ServicioMapeoPos : IServicioMapeoPos
{
    private readonly string _defaultTienda;

    public ServicioMapeoPos(string defaultTienda = "T01")
    {
        _defaultTienda = string.IsNullOrWhiteSpace(defaultTienda) ? "T01" : defaultTienda.Trim();
    }

    public string ResolverPosInstanciaId(string? tiendaDestinoId)
    {
        if (string.IsNullOrWhiteSpace(tiendaDestinoId))
        {
            return _defaultTienda;
        }

        var normalizada = tiendaDestinoId.Trim().ToUpperInvariant();

        // 1. Mapeo explicito de sucursales conocidas del frontend a instancias POS T01..T32
        if (normalizada.StartsWith("TIENDA-01", StringComparison.Ordinal) || normalizada.Contains("CENTRAL", StringComparison.Ordinal))
        {
            return "T01";
        }

        if (normalizada.StartsWith("TIENDA-02", StringComparison.Ordinal) || normalizada.Contains("MIRAFLORES", StringComparison.Ordinal))
        {
            return "T02";
        }

        if (normalizada.StartsWith("TIENDA-03", StringComparison.Ordinal) || normalizada.Contains("PORTALES", StringComparison.Ordinal))
        {
            return "T03";
        }

        if (normalizada.StartsWith("TIENDA-04", StringComparison.Ordinal) || normalizada.Contains("PRADERA", StringComparison.Ordinal))
        {
            return "T04";
        }

        // 2. Formato TDA-01..TDA-32
        if (normalizada.StartsWith("TDA-", StringComparison.Ordinal))
        {
            normalizada = normalizada[4..];
        }

        // 3. Formato TIENDA-01..TIENDA-32
        if (normalizada.StartsWith("TIENDA-", StringComparison.Ordinal))
        {
            normalizada = normalizada[7..];
        }

        // Si tiene sufijo (e.g. 05-OAKLAND), tomar solo la parte numerica
        if (normalizada.Contains('-'))
        {
            normalizada = normalizada[..normalizada.IndexOf('-')];
        }

        // 4. Si viene directamente como T01..T32 o T1..T32
        if (normalizada.StartsWith("T", StringComparison.Ordinal) && int.TryParse(normalizada[1..], out var tNum) && tNum is >= 1 and <= 32)
        {
            return $"T{tNum:D2}";
        }

        // 5. Si es numero plano 1..32 o 01..32
        if (int.TryParse(normalizada, out var numero) && numero is >= 1 and <= 32)
        {
            return $"T{numero:D2}";
        }

        return _defaultTienda;
    }
}
