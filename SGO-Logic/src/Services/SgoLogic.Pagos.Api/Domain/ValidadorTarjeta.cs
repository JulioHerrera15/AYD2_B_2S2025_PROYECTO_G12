using System.Text.RegularExpressions;

namespace SgoLogic.Pagos.Api.Domain;

/// <summary>
/// Datos de una tarjeta tal como los escribe el cliente, para validarlos antes de
/// tokenizar. <see cref="ToString" /> no imprime el numero ni el CVV: un record
/// impreso por accidente (log, excepcion, depurador) no los filtra.
/// </summary>
public sealed record DatosTarjeta(string? Numero, int? MesExp, int? AnioExp, string? Cvv, string? Titular)
{
    public override string ToString() => nameof(DatosTarjeta) + " { datos de tarjeta omitidos }";
}

/// <summary>
/// Resultado de validar una tarjeta: errores por campo (numero, mesExp, anioExp,
/// cvv, titular) o, si no hay ninguno, los datos ya normalizados. Los mensajes nunca
/// repiten el valor recibido.
/// </summary>
public sealed class ResultadoValidacionTarjeta
{
    private ResultadoValidacionTarjeta(IReadOnlyDictionary<string, string> errores, TarjetaValidada? tarjeta)
    {
        Errores = errores;
        Tarjeta = tarjeta;
    }

    public IReadOnlyDictionary<string, string> Errores { get; }

    public TarjetaValidada? Tarjeta { get; }

    public bool EsValida => Errores.Count == 0 && Tarjeta is not null;

    internal static ResultadoValidacionTarjeta ConErrores(IReadOnlyDictionary<string, string> errores) => new(errores, null);

    internal static ResultadoValidacionTarjeta Valida(TarjetaValidada tarjeta) =>
        new(new Dictionary<string, string>(), tarjeta);
}

/// <summary>Tarjeta que supero la validacion, con el numero solo en digitos y el anio en 4 cifras.</summary>
public sealed record TarjetaValidada(string Numero, MarcaTarjeta Marca, int MesExp, int AnioExp, string Cvv, string Titular)
{
    public string Ultimos4 => Numero[^4..];

    public override string ToString() => nameof(TarjetaValidada) + $" {{ {Marca} terminada en {Ultimos4} }}";
}

/// <summary>
/// Reglas de validacion de una tarjeta nueva (A1). Es la autoridad: la Web replica
/// estas reglas en JavaScript solo para dar respuesta inmediata.
///
/// <list type="bullet">
///   <item>numero: solo digitos (se ignoran espacios y guiones), marca reconocida
///   por <see cref="ReglasTarjeta.DetectarMarca" />, longitud de la marca (VISA
///   13/16/19, MASTERCARD 16, AMEX 15) y Luhn.</item>
///   <item>mesExp / anioExp: mes 1-12; la tarjeta vale hasta el ultimo dia del mes
///   indicado, asi que vence cuando ese mes ya paso.</item>
///   <item>cvv: 3 digitos, o 4 si es AMEX.</item>
///   <item>titular: obligatorio, solo letras y espacios, de 2 a 60 caracteres.</item>
/// </list>
/// </summary>
public static class ValidadorTarjeta
{
    public const int LargoMinimoTitular = 2;
    public const int LargoMaximoTitular = 60;

    private static readonly IReadOnlyDictionary<MarcaTarjeta, int[]> LongitudesPorMarca =
        new Dictionary<MarcaTarjeta, int[]>
        {
            [MarcaTarjeta.VISA] = new[] { 13, 16, 19 },
            [MarcaTarjeta.MASTERCARD] = new[] { 16 },
            [MarcaTarjeta.AMEX] = new[] { 15 }
        };

    private static readonly Regex PatronTitular = new(@"^[\p{L} ]+$", RegexOptions.Compiled);

    public static ResultadoValidacionTarjeta Validar(DatosTarjeta datos, DateTime ahora)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var errores = new Dictionary<string, string>();

        var numero = (datos.Numero ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
        var marca = MarcaTarjeta.OTRA;

        if (numero.Length == 0)
        {
            errores["numero"] = "El numero de tarjeta es obligatorio.";
        }
        else if (!numero.All(char.IsAsciiDigit))
        {
            errores["numero"] = "El numero de tarjeta solo admite digitos.";
        }
        else
        {
            marca = ReglasTarjeta.DetectarMarca(numero);

            if (marca == MarcaTarjeta.OTRA)
            {
                errores["numero"] = "Marca no soportada. Se admiten VISA, MASTERCARD y AMEX.";
            }
            else if (!LongitudValida(marca, numero.Length))
            {
                errores["numero"] = $"Longitud invalida para una tarjeta {marca}.";
            }
            else if (!PasaLuhn(numero))
            {
                errores["numero"] = "El numero de tarjeta no es valido.";
            }
        }

        var anio = datos.AnioExp is int anioRecibido ? ReglasTarjeta.NormalizarAnio(anioRecibido) : (int?)null;

        if (datos.MesExp is null)
        {
            errores["mesExp"] = "El mes de vencimiento es obligatorio.";
        }
        else if (datos.MesExp is < 1 or > 12)
        {
            errores["mesExp"] = "El mes de vencimiento debe estar entre 1 y 12.";
        }

        if (anio is null)
        {
            errores["anioExp"] = "El anio de vencimiento es obligatorio.";
        }
        else if (anio is < 2000 or > 2100)
        {
            errores["anioExp"] = "El anio de vencimiento no es valido.";
        }
        else if (!errores.ContainsKey("mesExp") && ReglasTarjeta.EstaVencida(datos.MesExp!.Value, anio.Value, ahora))
        {
            errores["anioExp"] = "La tarjeta esta vencida.";
        }

        var cvv = (datos.Cvv ?? string.Empty).Trim();
        if (cvv.Length == 0)
        {
            errores["cvv"] = "El codigo de seguridad es obligatorio.";
        }
        else if (!ReglasTarjeta.CvvValido(marca, cvv))
        {
            errores["cvv"] = $"El codigo de seguridad debe tener {ReglasTarjeta.LargoCvv(marca)} digitos.";
        }

        var titular = NormalizarTitular(datos.Titular);
        var errorTitular = ValidarTitular(titular);
        if (errorTitular is not null)
        {
            errores["titular"] = errorTitular;
        }

        if (errores.Count > 0)
        {
            return ResultadoValidacionTarjeta.ConErrores(errores);
        }

        return ResultadoValidacionTarjeta.Valida(
            new TarjetaValidada(numero, marca, datos.MesExp!.Value, anio!.Value, cvv, titular));
    }

    /// <summary>Longitud aceptada por marca: VISA 13/16/19, MASTERCARD 16, AMEX 15.</summary>
    public static bool LongitudValida(MarcaTarjeta marca, int longitud) =>
        LongitudesPorMarca.TryGetValue(marca, out var permitidas) && permitidas.Contains(longitud);

    /// <summary>Titular sin espacios de sobra: recortado y con un solo espacio entre palabras.</summary>
    public static string NormalizarTitular(string? titular) =>
        string.Join(' ', (titular ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Null si el titular es valido; si no, el motivo.</summary>
    public static string? ValidarTitular(string titular)
    {
        if (titular.Length == 0)
        {
            return "El nombre del titular es obligatorio.";
        }

        if (titular.Length is < LargoMinimoTitular or > LargoMaximoTitular)
        {
            return $"El nombre del titular debe tener entre {LargoMinimoTitular} y {LargoMaximoTitular} caracteres.";
        }

        return PatronTitular.IsMatch(titular)
            ? null
            : "El nombre del titular solo admite letras y espacios.";
    }

    public static bool PasaLuhn(string numero)
    {
        var suma = 0;
        var duplicar = false;

        for (var i = numero.Length - 1; i >= 0; i--)
        {
            var digito = numero[i] - '0';
            if (duplicar)
            {
                digito *= 2;
                if (digito > 9)
                {
                    digito -= 9;
                }
            }

            suma += digito;
            duplicar = !duplicar;
        }

        return suma % 10 == 0;
    }
}
