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

/// <summary>Cuerpo de POST /api/pagos/tarjetas: lo que devolvio PayKlik al tokenizar con guardar=true, mas el titular.</summary>
public sealed record GuardarTarjetaRequest(
    string? ReferenciaTarjeta,
    string? Marca,
    string? Ultimos4,
    int MesExp,
    int AnioExp,
    string? Titular);

/// <summary>
/// Cuerpo de POST /api/pagos/tarjetas/{id}/token. El CVV solo pasa por aqui hacia
/// PayKlik: no se guarda ni se registra. <see cref="ToString" /> no lo imprime.
/// </summary>
public sealed record SolicitarTokenTarjetaRequest(string? Cvv)
{
    public override string ToString() => nameof(SolicitarTokenTarjetaRequest) + " { cvv omitido }";
}

/// <summary>
/// Token de un solo uso emitido por PayKlik desde una tarjeta guardada. Misma forma
/// que la respuesta de la tokenizacion directa (POST /v1/tokens), para que el
/// checkout siga igual: el token viaja a Ordenes como TokenMedioPago.
/// </summary>
public sealed record TokenTarjetaDto(
    string Token,
    string UltimosCuatro,
    string Marca,
    int MesExpiracion,
    int AnioExpiracion,
    DateTime EmitidoEn,
    DateTime VenceEn,
    int VigenciaSegundos,
    bool UsoUnico);

public enum EstadoTokenTarjeta
{
    Emitido,
    NoEncontrada,
    Vencida,
    CvvInvalido,
    YaNoValida,
    RechazadaPorProveedor,
    ProveedorNoDisponible
}

/// <summary>Desenlace de pedir un token para una tarjeta guardada.</summary>
public sealed record ResultadoTokenTarjeta(EstadoTokenTarjeta Estado, TokenTarjetaDto? Token = null, string? Mensaje = null);

public enum EstadoGuardarTarjeta
{
    Creada,
    Actualizada
}
