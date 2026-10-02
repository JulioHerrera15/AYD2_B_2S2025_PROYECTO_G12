namespace SgoLogic.Mocks.PayKlik;

/// <summary>
/// Datos de una tarjeta nueva dentro de un cargo (POST /v1/cargos). Los manda Pagos,
/// servidor a servidor, descifrados en memoria desde su boveda transitoria. Los campos
/// son anulables para responder errores por campo en lugar de un 400 del binding.
/// <see cref="ToString" /> no imprime el numero ni el CVV.
/// </summary>
public sealed record DatosTarjetaCargo(
    string? Numero,
    int? MesExp,
    int? AnioExp,
    string? Cvv,
    string? Titular)
{
    public override string ToString() => nameof(DatosTarjetaCargo) + " { datos de tarjeta omitidos }";
}

/// <summary>
/// Lo unico que el mock conserva de una tarjeta: ultimos cuatro, marca y vencimiento.
/// El PAN y el CVV se descartan apenas se validan.
/// </summary>
public sealed record ResumenTarjeta(
    string UltimosCuatro,
    string Marca,
    int MesExpiracion,
    int AnioExpiracion);

/// <summary>Un error de validacion de la tarjeta. Nunca incluye el valor recibido.</summary>
public sealed record ErrorValidacionTarjeta(string Campo, string Motivo);

public sealed record ResultadoValidacionTarjeta(
    ResumenTarjeta? Tarjeta,
    IReadOnlyList<ErrorValidacionTarjeta> Errores)
{
    public bool EsValida => Errores.Count == 0 && Tarjeta is not null;
}

/// <summary>
/// Valida la tarjeta de un cargo como lo haria el procesador: numero con Luhn y
/// longitud segun la marca, CVV de 3 digitos (4 si es AMEX), vencimiento no pasado
/// y titular presente. Deduce la marca del BIN. Los mensajes nunca repiten el valor
/// recibido.
/// </summary>
public static class ValidadorTarjeta
{
    private static readonly IReadOnlyDictionary<string, int[]> LongitudesPorMarca =
        new Dictionary<string, int[]>
        {
            ["VISA"] = new[] { 13, 16, 19 },
            ["MASTERCARD"] = new[] { 16 },
            ["AMEX"] = new[] { 15 }
        };

    public static ResultadoValidacionTarjeta Validar(DatosTarjetaCargo tarjeta, DateTime ahoraUtc)
    {
        var errores = new List<ErrorValidacionTarjeta>();

        var numero = tarjeta.Numero?.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
        string? marca = null;

        if (string.IsNullOrEmpty(numero))
        {
            errores.Add(new("numero", "El numero de tarjeta es obligatorio."));
        }
        else if (!numero.All(char.IsAsciiDigit))
        {
            errores.Add(new("numero", "El numero de tarjeta solo admite digitos."));
        }
        else
        {
            marca = DeducirMarca(numero);

            if (marca is null)
            {
                errores.Add(new("numero", "Marca no soportada. Se admiten VISA, MASTERCARD y AMEX."));
            }
            else if (!LongitudesPorMarca[marca].Contains(numero.Length))
            {
                errores.Add(new("numero", $"Longitud invalida para la marca {marca}."));
            }
            else if (!PasaLuhn(numero))
            {
                errores.Add(new("numero", "El numero de tarjeta no supera la verificacion de Luhn."));
            }
        }

        var cvv = tarjeta.Cvv?.Trim();
        if (string.IsNullOrEmpty(cvv))
        {
            errores.Add(new("cvv", "El CVV es obligatorio."));
        }
        else if (!BovedaTarjetas.CvvValido(marca ?? string.Empty, cvv))
        {
            errores.Add(new("cvv", marca == "AMEX" ? "El CVV debe tener 4 digitos." : "El CVV debe tener 3 digitos."));
        }

        if (string.IsNullOrWhiteSpace(tarjeta.Titular))
        {
            errores.Add(new("titular", "El titular es obligatorio."));
        }

        var mes = tarjeta.MesExp;
        var anio = tarjeta.AnioExp;
        if (mes is null || anio is null)
        {
            errores.Add(new("expiracion", "El mes y el anio de expiracion son obligatorios."));
        }
        else if (mes is < 1 or > 12)
        {
            errores.Add(new("mesExp", "El mes de expiracion debe estar entre 1 y 12."));
        }
        else
        {
            var anioCompleto = anio.Value is >= 0 and <= 99 ? 2000 + anio.Value : anio.Value;

            // La tarjeta vale hasta el fin de su mes de expiracion.
            if (anioCompleto * 12 + mes.Value < ahoraUtc.Year * 12 + ahoraUtc.Month)
            {
                errores.Add(new("expiracion", "La tarjeta esta vencida."));
            }
            else if (errores.Count == 0)
            {
                return new ResultadoValidacionTarjeta(
                    new ResumenTarjeta(numero![^4..], marca!, mes.Value, anioCompleto),
                    errores);
            }
        }

        return new ResultadoValidacionTarjeta(null, errores);
    }

    public static string? DeducirMarca(string numero)
    {
        if (numero.Length < 2)
        {
            return null;
        }

        if (numero[0] == '4')
        {
            return "VISA";
        }

        var dosDigitos = int.Parse(numero[..2]);
        if (dosDigitos is 34 or 37)
        {
            return "AMEX";
        }

        if (dosDigitos is >= 51 and <= 55)
        {
            return "MASTERCARD";
        }

        if (numero.Length >= 4 && int.Parse(numero[..4]) is >= 2221 and <= 2720)
        {
            return "MASTERCARD";
        }

        return null;
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
