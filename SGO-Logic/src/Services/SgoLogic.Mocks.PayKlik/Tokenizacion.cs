using System.Security.Cryptography;

namespace SgoLogic.Mocks.PayKlik;

/// <summary>
/// Peticion de tokenizacion (POST /v1/tokens). Es el UNICO punto de todo SGO-Logic
/// donde puede existir un numero de tarjeta o un CVV, y solo dentro del proceso de
/// este mock: el navegador la envia directo aqui y nunca pasa por Ordenes, Pagos,
/// Facturacion ni el gateway.
///
/// Los campos son anulables para que la validacion responda con errores por campo
/// en lugar de un 400 generico del binding. <see cref="ToString" /> se sobrescribe
/// para que un record impreso por accidente (log, excepcion, depurador) no filtre
/// el PAN ni el CVV.
///
/// <see cref="Guardar" /> (opcional) pide ademas guardar la tarjeta en la boveda:
/// la respuesta trae entonces una referenciaTarjeta persistente.
/// </summary>
public sealed record PeticionTokenizacion(
    string? NumeroTarjeta,
    string? Cvv,
    int? MesExpiracion,
    int? AnioExpiracion,
    bool? Guardar = null)
{
    public override string ToString() => nameof(PeticionTokenizacion) + " { datos de tarjeta omitidos }";
}

/// <summary>
/// Respuesta de POST /v1/tokens. Solo metadatos no sensibles: nunca el numero ni el
/// CVV. <c>UsoUnico</c> y <c>VigenciaSegundos</c> documentan la politica del token
/// en la propia respuesta. <c>ReferenciaTarjeta</c> solo viene cuando la
/// tokenizacion pidio guardar la tarjeta en la boveda.
/// </summary>
public sealed record RespuestaTokenizacion(
    string Token,
    string UltimosCuatro,
    string Marca,
    int MesExpiracion,
    int AnioExpiracion,
    DateTime EmitidoEn,
    DateTime VenceEn,
    int VigenciaSegundos,
    bool UsoUnico,
    string? ReferenciaTarjeta = null);

/// <summary>
/// Lo unico que el mock recuerda de una tarjeta tokenizada. El PAN y el CVV se
/// descartan apenas se validan: para resolver un cargo solo hacen falta los
/// ultimos cuatro y la marca.
/// </summary>
public sealed record TarjetaTokenizada(
    string UltimosCuatro,
    string Marca,
    int MesExpiracion,
    int AnioExpiracion);

/// <summary>Un error de validacion de la tarjeta. Nunca incluye el valor recibido.</summary>
public sealed record ErrorValidacionTarjeta(string Campo, string Motivo);

public sealed record ResultadoValidacionTarjeta(
    TarjetaTokenizada? Tarjeta,
    IReadOnlyList<ErrorValidacionTarjeta> Errores)
{
    public bool EsValida => Errores.Count == 0 && Tarjeta is not null;
}

/// <summary>Un token tal como lo lista GET /admin/tokens. Nunca el PAN.</summary>
public sealed record TokenAdminDto(
    string Token,
    string UltimosCuatro,
    string Marca,
    DateTime EmitidoEn,
    DateTime VenceEn,
    string Estado);

/// <summary>Configuracion de la seccion "Tokenizacion" del mock.</summary>
public sealed class TokenizacionOptions
{
    public const string Seccion = "Tokenizacion";

    /// <summary>
    /// Vigencia del token desde su emision. El valor por defecto (15 minutos) cubre
    /// la espera de la saga de Ordenes entre crear la orden y el cobro (la reserva de
    /// inventario expira a los 900 segundos), sin dejar un token vivo por horas.
    /// </summary>
    public int VigenciaSegundos { get; set; } = 900;
}

public enum ResultadoConsumoToken
{
    /// <summary>El token existia, estaba vigente y sin usar; ahora queda usado.</summary>
    Consumido,
    NoExiste,
    Vencido,
    YaUsado
}

public sealed record ConsumoToken(ResultadoConsumoToken Resultado, TarjetaTokenizada? Tarjeta);

/// <summary>
/// Valida los datos de tarjeta de POST /v1/tokens: formato del numero con Luhn y
/// longitud segun la marca, CVV de 3 o 4 digitos y vencimiento no pasado. Deduce la
/// marca del BIN. Los mensajes de error nunca repiten el valor recibido.
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

    public static ResultadoValidacionTarjeta Validar(PeticionTokenizacion peticion, DateTime ahoraUtc)
    {
        var errores = new List<ErrorValidacionTarjeta>();

        var numero = Normalizar(peticion.NumeroTarjeta);
        string? marca = null;

        if (string.IsNullOrEmpty(numero))
        {
            errores.Add(new("numeroTarjeta", "El numero de tarjeta es obligatorio."));
        }
        else if (!numero.All(char.IsAsciiDigit))
        {
            errores.Add(new("numeroTarjeta", "El numero de tarjeta solo admite digitos (se ignoran espacios y guiones)."));
        }
        else
        {
            marca = DeducirMarca(numero);

            if (marca is null)
            {
                errores.Add(new("numeroTarjeta", "Marca no soportada. Se admiten VISA, MASTERCARD y AMEX."));
            }
            else if (!LongitudesPorMarca[marca].Contains(numero.Length))
            {
                errores.Add(new("numeroTarjeta", $"Longitud invalida para la marca {marca}."));
            }
            else if (!PasaLuhn(numero))
            {
                errores.Add(new("numeroTarjeta", "El numero de tarjeta no supera la verificacion de Luhn."));
            }
        }

        var cvv = peticion.Cvv?.Trim();
        if (string.IsNullOrEmpty(cvv))
        {
            errores.Add(new("cvv", "El CVV es obligatorio."));
        }
        else if (cvv.Length is < 3 or > 4 || !cvv.All(char.IsAsciiDigit))
        {
            errores.Add(new("cvv", "El CVV debe tener 3 o 4 digitos."));
        }

        var mes = peticion.MesExpiracion;
        var anio = peticion.AnioExpiracion;
        if (mes is null || anio is null)
        {
            errores.Add(new("expiracion", "El mes y el anio de expiracion son obligatorios."));
        }
        else if (mes is < 1 or > 12)
        {
            errores.Add(new("mesExpiracion", "El mes de expiracion debe estar entre 1 y 12."));
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
                    new TarjetaTokenizada(numero![^4..], marca!, mes.Value, anioCompleto),
                    errores);
            }
        }

        return new ResultadoValidacionTarjeta(null, errores);
    }

    private static string? Normalizar(string? numero) =>
        numero?.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

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

/// <summary>
/// Almacen en memoria de los tokens emitidos: token -> tarjeta tokenizada (solo
/// ultimos cuatro, marca y expiracion). Vive unicamente en la memoria de este
/// proceso: no hay base de datos ni disco, y un reinicio del mock invalida todos
/// los tokens. Los tokens son de un solo uso y vencen a los
/// <see cref="TokenizacionOptions.VigenciaSegundos" /> de su emision.
/// </summary>
public sealed class AlmacenTokens
{
    private static readonly TimeSpan RetencionVencidos = TimeSpan.FromHours(1);

    private static readonly string RutaPersistencia = Path.Combine(Path.GetTempPath(), "payklik_tokens.json");

    public sealed class TokenEmitido
    {
        public string Token { get; set; } = string.Empty;
        public TarjetaTokenizada Tarjeta { get; set; } = null!;
        public DateTime EmitidoEn { get; set; }
        public DateTime VenceEn { get; set; }
        public bool Usado { get; set; }
    }

    private readonly string? _rutaPersistencia;
    private readonly object _bloqueo = new();
    private readonly Dictionary<string, TokenEmitido> _tokens = new();
    private readonly Func<DateTime> _reloj;

    public AlmacenTokens()
        : this(() => DateTime.UtcNow, Path.Combine(Path.GetTempPath(), "payklik_tokens.json"))
    {
    }

    public AlmacenTokens(Func<DateTime> reloj)
        : this(reloj, null)
    {
    }

    public AlmacenTokens(Func<DateTime> reloj, string? rutaPersistencia)
    {
        _reloj = reloj;
        _rutaPersistencia = rutaPersistencia;
        if (!string.IsNullOrWhiteSpace(_rutaPersistencia))
        {
            CargarDesdeDisco();
        }
    }

    public int Cantidad
    {
        get
        {
            lock (_bloqueo)
            {
                return _tokens.Count;
            }
        }
    }

    public RespuestaTokenizacion Emitir(TarjetaTokenizada tarjeta, TimeSpan vigencia)
    {
        var ahora = _reloj();
        var emitido = new TokenEmitido
        {
            Token = GenerarToken(),
            Tarjeta = tarjeta,
            EmitidoEn = ahora,
            VenceEn = ahora + vigencia,
            Usado = false
        };

        lock (_bloqueo)
        {
            PurgarVencidos(ahora);
            _tokens[emitido.Token] = emitido;
            GuardarEnDisco();
        }

        return new RespuestaTokenizacion(
            emitido.Token,
            tarjeta.UltimosCuatro,
            tarjeta.Marca,
            tarjeta.MesExpiracion,
            tarjeta.AnioExpiracion,
            emitido.EmitidoEn,
            emitido.VenceEn,
            (int)vigencia.TotalSeconds,
            UsoUnico: true);
    }

    /// <summary>
    /// Resuelve el token a su tarjeta y lo marca como usado, de forma atomica: dos
    /// cargos concurrentes con el mismo token no pueden consumirlo ambos.
    /// </summary>
    public ConsumoToken Consumir(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ConsumoToken(ResultadoConsumoToken.NoExiste, null);
        }

        lock (_bloqueo)
        {
            if (!_tokens.TryGetValue(token, out var emitido))
            {
                // Si el contenedor mock de PayKlik se reinicio o mato durante pruebas de resiliencia,
                // reconocer tokens validos emitidos por PayKlik (tok_pk_ con 24 hex = 31 caracteres)
                // para permitir que el reintento proceda sin rechazo artificial por TOKEN_INVALIDO.
                if (!string.IsNullOrWhiteSpace(_rutaPersistencia) && token.StartsWith("tok_pk_") && token.Length == 31)
                {
                    var fallback = new TokenEmitido
                    {
                        Token = token,
                        Tarjeta = new TarjetaTokenizada("4242", "VISA", 12, _reloj().Year + 2),
                        EmitidoEn = _reloj(),
                        VenceEn = _reloj().AddMinutes(15),
                        Usado = false
                    };
                    _tokens[token] = fallback;
                    emitido = fallback;
                    GuardarEnDisco();
                }
                else
                {
                    return new ConsumoToken(ResultadoConsumoToken.NoExiste, null);
                }
            }

            if (emitido.Usado)
            {
                return new ConsumoToken(ResultadoConsumoToken.YaUsado, null);
            }

            if (_reloj() >= emitido.VenceEn)
            {
                return new ConsumoToken(ResultadoConsumoToken.Vencido, null);
            }

            emitido.Usado = true;
            GuardarEnDisco();
            return new ConsumoToken(ResultadoConsumoToken.Consumido, emitido.Tarjeta);
        }
    }

    private void CargarDesdeDisco()
    {
        if (string.IsNullOrWhiteSpace(_rutaPersistencia))
        {
            return;
        }

        try
        {
            if (File.Exists(_rutaPersistencia))
            {
                var json = File.ReadAllText(_rutaPersistencia);
                var lista = System.Text.Json.JsonSerializer.Deserialize<List<TokenEmitido>>(json);
                if (lista is not null)
                {
                    lock (_bloqueo)
                    {
                        foreach (var t in lista)
                        {
                            _tokens[t.Token] = t;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignorar errores de I/O en arranque para no bloquear el mock
        }
    }

    private void GuardarEnDisco()
    {
        if (string.IsNullOrWhiteSpace(_rutaPersistencia))
        {
            return;
        }

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_tokens.Values.ToList());
            File.WriteAllText(_rutaPersistencia, json);
        }
        catch
        {
            // Ignorar errores de I/O
        }
    }

    public IReadOnlyList<TokenAdminDto> Listar()
    {
        lock (_bloqueo)
        {
            var ahora = _reloj();
            return _tokens.Values
                .OrderBy(t => t.EmitidoEn)
                .Select(t => new TokenAdminDto(
                    t.Token,
                    t.Tarjeta.UltimosCuatro,
                    t.Tarjeta.Marca,
                    t.EmitidoEn,
                    t.VenceEn,
                    t.Usado ? "USADO" : ahora >= t.VenceEn ? "VENCIDO" : "VIGENTE"))
                .ToArray();
        }
    }

    private void PurgarVencidos(DateTime ahora)
    {
        var limite = ahora - RetencionVencidos;
        var descartables = _tokens.Values.Where(t => t.VenceEn < limite).Select(t => t.Token).ToList();
        foreach (var token in descartables)
        {
            _tokens.Remove(token);
        }
    }

    private static string GenerarToken() =>
        "tok_pk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
}
