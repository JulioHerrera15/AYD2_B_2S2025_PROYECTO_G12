namespace SgoLogic.Pagos.Api.Domain;

/// <summary>Marca de una tarjeta guardada. OTRA cubre cualquier prefijo no reconocido.</summary>
public enum MarcaTarjeta
{
    VISA,
    MASTERCARD,
    AMEX,
    OTRA
}

/// <summary>
/// Reglas puras sobre datos NO sensibles de una tarjeta: marca por prefijo, largo
/// del CVV, enmascarado y vencimiento. Ninguna recibe ni conserva un PAN completo:
/// <see cref="DetectarMarca" /> solo mira los primeros digitos.
/// </summary>
public static class ReglasTarjeta
{
    /// <summary>
    /// Marca por prefijo (BIN): Visa empieza en 4; Mastercard en 51-55 o 2221-2720;
    /// American Express en 34 o 37. Acepta un numero parcial (lo que el cliente lleva
    /// escrito) e ignora espacios y guiones.
    /// </summary>
    public static MarcaTarjeta DetectarMarca(string? numero)
    {
        var digitos = new string((numero ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (digitos.Length == 0)
        {
            return MarcaTarjeta.OTRA;
        }

        if (digitos[0] == '4')
        {
            return MarcaTarjeta.VISA;
        }

        if (digitos.Length >= 2)
        {
            var dos = int.Parse(digitos[..2]);

            if (dos is 34 or 37)
            {
                return MarcaTarjeta.AMEX;
            }

            if (dos is >= 51 and <= 55)
            {
                return MarcaTarjeta.MASTERCARD;
            }
        }

        if (digitos.Length >= 4 && int.Parse(digitos[..4]) is >= 2221 and <= 2720)
        {
            return MarcaTarjeta.MASTERCARD;
        }

        return MarcaTarjeta.OTRA;
    }

    /// <summary>Interpreta la marca que devuelve PayKlik ("VISA", "MASTERCARD", "AMEX"); cualquier otra es OTRA.</summary>
    public static MarcaTarjeta ParsearMarca(string? marca) =>
        Enum.TryParse<MarcaTarjeta>(marca?.Trim(), ignoreCase: true, out var valor) && Enum.IsDefined(valor)
            ? valor
            : MarcaTarjeta.OTRA;

    /// <summary>American Express usa CVV de 4 digitos; las demas marcas, de 3.</summary>
    public static int LargoCvv(MarcaTarjeta marca) => marca == MarcaTarjeta.AMEX ? 4 : 3;

    public static bool CvvValido(MarcaTarjeta marca, string? cvv) =>
        cvv is not null && cvv.Length == LargoCvv(marca) && cvv.All(char.IsAsciiDigit);

    /// <summary>"**** **** **** 1234" a partir de los ultimos cuatro digitos.</summary>
    public static string Enmascarar(string ultimos4) => $"**** **** **** {ultimos4}";

    /// <summary>
    /// La tarjeta vale hasta el ultimo dia de su mes de vencimiento: esta vencida
    /// solo cuando ese mes ya paso.
    /// </summary>
    public static bool EstaVencida(int mesExp, int anioExp, DateTime ahora) =>
        anioExp * 12 + mesExp < ahora.Year * 12 + ahora.Month;

    /// <summary>Normaliza un anio de 2 digitos (28) a 4 (2028).</summary>
    public static int NormalizarAnio(int anio) => anio is >= 0 and <= 99 ? 2000 + anio : anio;
}
