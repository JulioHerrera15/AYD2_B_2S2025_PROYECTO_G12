namespace SgoLogic.Pagos.Api.Application.Tokens;

/// <summary>
/// Cuerpo de POST /api/pagos/tokens. Los campos son anulables para que la validacion
/// responda con errores por campo en lugar de un 400 generico del binding.
/// <see cref="ToString" /> no imprime el numero ni el CVV.
/// </summary>
public sealed record SolicitudTokenPago(
    string? Numero,
    int? MesExp,
    int? AnioExp,
    string? Cvv,
    string? Titular,
    bool Guardar = false)
{
    public override string ToString() => nameof(SolicitudTokenPago) + " { datos de tarjeta omitidos }";
}

/// <summary>
/// Respuesta de los dos endpoints de tokenizacion. Solo lo necesario para mostrar la
/// tarjeta: el token opaco, la marca, los ultimos cuatro y hasta cuando vale.
/// </summary>
public sealed record TokenPagoDto(string Token, string Marca, string Ultimos4, DateTime ExpiraEn);

/// <summary>Desenlace de tokenizar una tarjeta nueva: el token, o los errores por campo (422).</summary>
public sealed record ResultadoTokenizacion(TokenPagoDto? Token, IReadOnlyDictionary<string, string> Errores)
{
    public bool Exito => Token is not null;
}

public enum EstadoTokenTarjetaGuardada
{
    Emitido,
    NoEncontrada,
    Vencida,
    CvvInvalido
}

/// <summary>Desenlace de tokenizar una tarjeta guardada con el CVV que escribio el cliente.</summary>
public sealed record ResultadoTokenTarjetaGuardada(
    EstadoTokenTarjetaGuardada Estado,
    TokenPagoDto? Token = null,
    string? Mensaje = null);
