using System.Security.Cryptography;
using System.Text.Json;

namespace SgoLogic.Mocks.PayKlik;

/// <summary>
/// Lo que la boveda guarda de una tarjeta: referencia opaca persistente, marca,
/// ultimos cuatro y vencimiento. Ni PAN ni CVV: para simular un cargo el mock solo
/// necesita los ultimos cuatro y la marca.
/// </summary>
public sealed class TarjetaBoveda
{
    public string Referencia { get; set; } = string.Empty;
    public string UltimosCuatro { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public int MesExpiracion { get; set; }
    public int AnioExpiracion { get; set; }
    public DateTime GuardadaEn { get; set; }

    public ResumenTarjeta AResumen() => new(UltimosCuatro, Marca, MesExpiracion, AnioExpiracion);
}

/// <summary>Tarjeta de la boveda tal como la lista GET /admin/tarjetas. Nunca el PAN.</summary>
public sealed record TarjetaBovedaAdminDto(string Referencia, string Marca, string UltimosCuatro, int MesExpiracion, int AnioExpiracion, DateTime GuardadaEn);

/// <summary>Configuracion de la seccion "Boveda" del mock.</summary>
public sealed class BovedaOptions
{
    public const string Seccion = "Boveda";

    /// <summary>
    /// Archivo JSON donde se persiste la boveda. En docker-compose apunta a un volumen
    /// con nombre para que las tarjetas guardadas sobrevivan a reinicios del contenedor.
    /// Vacio: solo en memoria.
    /// </summary>
    public string? RutaArchivo { get; set; }
}

public enum ResultadoValidacionReferencia
{
    Valida,
    NoExiste,
    CvvInvalido,
    Vencida
}

/// <summary>
/// Boveda de tarjetas guardadas del mock. Cada cargo aprobado con guardar=true agrega
/// una entrada con una referencia nueva (card_pk_...); la tienda decide si
/// la reemplaza en su propio registro. Thread-safe y persistida en disco si hay ruta.
/// </summary>
public sealed class BovedaTarjetas
{
    private readonly object _bloqueo = new();
    private readonly Dictionary<string, TarjetaBoveda> _tarjetas = new();
    private readonly Func<DateTime> _reloj;
    private readonly string? _rutaArchivo;

    public BovedaTarjetas(Func<DateTime> reloj, string? rutaArchivo)
    {
        _reloj = reloj;
        _rutaArchivo = string.IsNullOrWhiteSpace(rutaArchivo) ? null : rutaArchivo;
        CargarDesdeDisco();
    }

    public string Guardar(ResumenTarjeta tarjeta)
    {
        var entrada = new TarjetaBoveda
        {
            Referencia = "card_pk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
            UltimosCuatro = tarjeta.UltimosCuatro,
            Marca = tarjeta.Marca,
            MesExpiracion = tarjeta.MesExpiracion,
            AnioExpiracion = tarjeta.AnioExpiracion,
            GuardadaEn = _reloj()
        };

        lock (_bloqueo)
        {
            _tarjetas[entrada.Referencia] = entrada;
            GuardarEnDisco();
        }

        return entrada.Referencia;
    }

    public TarjetaBoveda? Obtener(string? referencia)
    {
        if (string.IsNullOrWhiteSpace(referencia))
        {
            return null;
        }

        lock (_bloqueo)
        {
            return _tarjetas.TryGetValue(referencia.Trim(), out var tarjeta) ? tarjeta : null;
        }
    }

    /// <summary>
    /// Valida un cargo con tarjeta guardada: la referencia existe, el CVV tiene el largo
    /// de la marca (4 para AMEX, 3 para las demas) y la tarjeta no esta vencida.
    /// </summary>
    public (ResultadoValidacionReferencia Resultado, TarjetaBoveda? Tarjeta) Validar(string? referenciaTarjeta, string? cvv)
    {
        var tarjeta = Obtener(referenciaTarjeta);
        if (tarjeta is null)
        {
            return (ResultadoValidacionReferencia.NoExiste, null);
        }

        if (!CvvValido(tarjeta.Marca, cvv))
        {
            return (ResultadoValidacionReferencia.CvvInvalido, tarjeta);
        }

        var ahora = _reloj();
        if (tarjeta.AnioExpiracion * 12 + tarjeta.MesExpiracion < ahora.Year * 12 + ahora.Month)
        {
            return (ResultadoValidacionReferencia.Vencida, tarjeta);
        }

        return (ResultadoValidacionReferencia.Valida, tarjeta);
    }

    /// <summary>CVV de 4 digitos para American Express y de 3 para las demas marcas.</summary>
    public static bool CvvValido(string marca, string? cvv)
    {
        var largo = string.Equals(marca, "AMEX", StringComparison.OrdinalIgnoreCase) ? 4 : 3;
        return cvv is not null && cvv.Length == largo && cvv.All(char.IsAsciiDigit);
    }

    public IReadOnlyList<TarjetaBovedaAdminDto> Listar()
    {
        lock (_bloqueo)
        {
            return _tarjetas.Values
                .OrderBy(t => t.GuardadaEn)
                .Select(t => new TarjetaBovedaAdminDto(t.Referencia, t.Marca, t.UltimosCuatro, t.MesExpiracion, t.AnioExpiracion, t.GuardadaEn))
                .ToArray();
        }
    }

    private void CargarDesdeDisco()
    {
        if (_rutaArchivo is null || !File.Exists(_rutaArchivo))
        {
            return;
        }

        try
        {
            var lista = JsonSerializer.Deserialize<List<TarjetaBoveda>>(File.ReadAllText(_rutaArchivo));
            foreach (var tarjeta in lista ?? new List<TarjetaBoveda>())
            {
                _tarjetas[tarjeta.Referencia] = tarjeta;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Un archivo ilegible no debe impedir que el mock arranque: se empieza vacio.
        }
    }

    private void GuardarEnDisco()
    {
        if (_rutaArchivo is null)
        {
            return;
        }

        try
        {
            var directorio = Path.GetDirectoryName(_rutaArchivo);
            if (!string.IsNullOrEmpty(directorio))
            {
                Directory.CreateDirectory(directorio);
            }

            // Escritura atomica: primero un temporal y luego se reemplaza.
            var temporal = _rutaArchivo + ".tmp";
            File.WriteAllText(temporal, JsonSerializer.Serialize(_tarjetas.Values.ToList()));
            File.Move(temporal, _rutaArchivo, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin disco la boveda sigue funcionando en memoria.
        }
    }
}
