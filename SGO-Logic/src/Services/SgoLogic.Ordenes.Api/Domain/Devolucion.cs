using System.Text.RegularExpressions;
using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Ordenes.Api.Domain;

/// <summary>
/// Devolucion o cambio sobre una orden entregada (CU-14, HU-014). Agregado
/// hijo de Orden, ligado a ella por <see cref="OrdenId" />. El monto de
/// reembolso se calcula en el servidor a partir de las lineas devueltas; nunca
/// se acepta del cliente.
/// </summary>
public sealed partial class Devolucion
{
    [GeneratedRegex(@"^DEV-\d{4}-\d{6}$")]
    private static partial Regex PatronId();

    private readonly List<DevolucionLinea> _lineas = new();

    private Devolucion()
    {
        Id = string.Empty;
        OrdenOriginalId = string.Empty;
        ClienteId = string.Empty;
        UbicacionReingresoId = string.Empty;
        PagoOriginalId = string.Empty;
        DocumentoFiscalOriginalId = string.Empty;
        Moneda = string.Empty;
        RegistradaPor = string.Empty;
    }

    public Devolucion(
        string id,
        string ordenOriginalId,
        string clienteId,
        TipoDevolucion tipo,
        bool esParcial,
        string ubicacionReingresoId,
        decimal montoReembolso,
        string pagoOriginalId,
        string documentoFiscalOriginalId,
        string moneda,
        string registradaPor,
        IEnumerable<DevolucionLinea> lineas,
        DateTime? registradaEn = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("El identificador de la devolucion es obligatorio.", nameof(id));
        }

        if (!PatronId().IsMatch(id))
        {
            throw new ArgumentException(
                "El identificador de la devolucion debe cumplir el patron DEV-<anio>-<correlativo de 6 digitos>.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(ordenOriginalId))
        {
            throw new ArgumentException("El identificador de la orden original es obligatorio.", nameof(ordenOriginalId));
        }

        var lineasMaterializadas = lineas?.ToList() ?? new List<DevolucionLinea>();
        if (lineasMaterializadas.Count == 0)
        {
            throw new ArgumentException("La devolucion requiere al menos una linea.", nameof(lineas));
        }

        Id = id;
        OrdenOriginalId = ordenOriginalId;
        ClienteId = clienteId;
        Tipo = tipo;
        EsParcial = esParcial;
        UbicacionReingresoId = ubicacionReingresoId;
        MontoReembolso = montoReembolso;
        PagoOriginalId = pagoOriginalId;
        DocumentoFiscalOriginalId = documentoFiscalOriginalId;
        Moneda = moneda;
        RegistradaPor = registradaPor;
        Estado = EstadoDevolucion.REGISTRADA;
        RegistradaEn = registradaEn ?? DateTime.UtcNow;

        _lineas.AddRange(lineasMaterializadas);
    }

    public string Id { get; private set; }

    public string OrdenOriginalId { get; private set; }

    public string ClienteId { get; private set; }

    public TipoDevolucion Tipo { get; private set; }

    public bool EsParcial { get; private set; }

    public string UbicacionReingresoId { get; private set; }

    public decimal MontoReembolso { get; private set; }

    public string PagoOriginalId { get; private set; }

    public string DocumentoFiscalOriginalId { get; private set; }

    public string Moneda { get; private set; }

    public string RegistradaPor { get; private set; }

    public EstadoDevolucion Estado { get; private set; }

    public DateTime RegistradaEn { get; private set; }

    public IReadOnlyList<DevolucionLinea> Lineas => _lineas.AsReadOnly();

    public void MarcarReembolsoAplicado(DateTime? cuando = null)
    {
        Estado = EstadoDevolucion.REEMBOLSO_APLICADO;
    }

    public void MarcarReembolsoFallido(DateTime? cuando = null)
    {
        Estado = EstadoDevolucion.REEMBOLSO_FALLIDO;
    }

    public void MarcarCerrada(DateTime? cuando = null)
    {
        Estado = EstadoDevolucion.CERRADA;
    }
}

/// <summary>Linea individual de una devolucion.</summary>
public sealed class DevolucionLinea
{
    private DevolucionLinea()
    {
        Sku = string.Empty;
        Motivo = string.Empty;
    }

    public DevolucionLinea(
        string sku,
        int cantidad,
        string motivo,
        string? skuReemplazo = null,
        decimal precioUnitario = 0m)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("El SKU es obligatorio.", nameof(sku));
        }

        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad devuelta debe ser mayor que cero.");
        }

        Id = Guid.NewGuid();
        Sku = sku;
        Cantidad = cantidad;
        Motivo = motivo;
        SkuReemplazo = skuReemplazo;
        PrecioUnitario = precioUnitario;
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; }

    public int Cantidad { get; private set; }

    public string Motivo { get; private set; }

    public string? SkuReemplazo { get; private set; }

    public decimal PrecioUnitario { get; private set; }

    public decimal Subtotal => PrecioUnitario * Cantidad;
}

/// <summary>Estados de una devolucion.</summary>
public enum EstadoDevolucion
{
    REGISTRADA,
    REEMBOLSO_APLICADO,
    REEMBOLSO_FALLIDO,
    CERRADA
}
