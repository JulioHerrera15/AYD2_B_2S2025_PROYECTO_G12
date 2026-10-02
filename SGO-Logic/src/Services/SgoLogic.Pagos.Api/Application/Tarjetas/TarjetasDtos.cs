using SgoLogic.Pagos.Api.Domain;

namespace SgoLogic.Pagos.Api.Application.Tarjetas;

/// <summary>Una tarjeta guardada tal como la ve su dueno. Nunca trae la referencia de PayKlik.</summary>
public sealed record TarjetaGuardadaDto(
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
    public static TarjetaGuardadaDto Desde(TarjetaGuardada tarjeta, DateTime ahora) => new(
        tarjeta.Id,
        tarjeta.Marca.ToString(),
        tarjeta.Ultimos4,
        tarjeta.MesExp,
        tarjeta.AnioExp,
        tarjeta.Titular,
        tarjeta.Enmascarado,
        tarjeta.EstaVencida(ahora),
        tarjeta.CreadaEn,
        tarjeta.UltimoUsoEn);
}

/// <summary>
/// Cuerpo de POST /api/pagos/tarjetas: una referencia de boveda de PayKlik, mas el
/// titular. El checkout ya no lo usa (la tarjeta se guarda al cobrar con
/// guardar=true); se conserva para registrar referencias existentes.
/// </summary>
public sealed record GuardarTarjetaRequest(
    string? ReferenciaTarjeta,
    string? Marca,
    string? Ultimos4,
    int MesExp,
    int AnioExp,
    string? Titular);

/// <summary>
/// Cuerpo de POST /api/pagos/tarjetas/{id}/token. El CVV solo pasa por aqui hacia la
/// boveda transitoria, cifrado: no se registra. <see cref="ToString" /> no lo imprime.
/// </summary>
public sealed record SolicitarTokenTarjetaRequest(string? Cvv)
{
    public override string ToString() => nameof(SolicitarTokenTarjetaRequest) + " { cvv omitido }";
}

public enum EstadoGuardarTarjeta
{
    Creada,
    Actualizada
}
