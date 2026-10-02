namespace SgoLogic.Credito.Api.Domain;

/// <summary>
/// Ultimo historial comercial recibido del ERP para un cliente (una fila por
/// cliente). Es la entrada de la politica: el perfil se recalcula a partir de aqui.
/// </summary>
public sealed class HistorialCliente
{
    public const int LargoMaximoClienteId = 254;

    private HistorialCliente()
    {
    }

    public HistorialCliente(
        string clienteId,
        string codigoClienteErp,
        DateTime clienteDesde,
        int comprasUltimos12Meses,
        decimal montoCompradoUltimos12Meses,
        string moneda,
        int pagosPuntuales,
        int pagosTardios,
        int maxDiasAtraso,
        int devoluciones,
        int incidentesGraves,
        int otrosIncidentes,
        DateTime fechaCorte,
        string? loteOrigen,
        DateTime recibidoEn)
    {
        ClienteId = clienteId;
        Actualizar(
            codigoClienteErp,
            clienteDesde,
            comprasUltimos12Meses,
            montoCompradoUltimos12Meses,
            moneda,
            pagosPuntuales,
            pagosTardios,
            maxDiasAtraso,
            devoluciones,
            incidentesGraves,
            otrosIncidentes,
            fechaCorte,
            loteOrigen,
            recibidoEn);
    }

    public string ClienteId { get; private set; } = string.Empty;
    public string CodigoClienteErp { get; private set; } = string.Empty;
    public DateTime ClienteDesde { get; private set; }
    public int ComprasUltimos12Meses { get; private set; }
    public decimal MontoCompradoUltimos12Meses { get; private set; }
    public string Moneda { get; private set; } = string.Empty;
    public int PagosPuntuales { get; private set; }
    public int PagosTardios { get; private set; }
    public int MaxDiasAtraso { get; private set; }
    public int Devoluciones { get; private set; }
    public int IncidentesGraves { get; private set; }
    public int OtrosIncidentes { get; private set; }
    public DateTime FechaCorte { get; private set; }
    public string? LoteOrigen { get; private set; }
    public DateTime RecibidoEn { get; private set; }

    /// <summary>Devoluciones / compras de los ultimos 12 meses; 0 si no hubo compras.</summary>
    public decimal TasaDevolucion =>
        ComprasUltimos12Meses <= 0 ? 0m : (decimal)Devoluciones / ComprasUltimos12Meses;

    /// <summary>Dias completos transcurridos desde ClienteDesde hasta <paramref name="ahora" />; nunca negativo.</summary>
    public int DiasComoCliente(DateTime ahora) =>
        Math.Max(0, (int)Math.Floor((ahora - ClienteDesde).TotalDays));

    public void Actualizar(
        string codigoClienteErp,
        DateTime clienteDesde,
        int comprasUltimos12Meses,
        decimal montoCompradoUltimos12Meses,
        string moneda,
        int pagosPuntuales,
        int pagosTardios,
        int maxDiasAtraso,
        int devoluciones,
        int incidentesGraves,
        int otrosIncidentes,
        DateTime fechaCorte,
        string? loteOrigen,
        DateTime recibidoEn)
    {
        CodigoClienteErp = codigoClienteErp;
        ClienteDesde = clienteDesde;
        ComprasUltimos12Meses = comprasUltimos12Meses;
        MontoCompradoUltimos12Meses = montoCompradoUltimos12Meses;
        Moneda = moneda;
        PagosPuntuales = pagosPuntuales;
        PagosTardios = pagosTardios;
        MaxDiasAtraso = maxDiasAtraso;
        Devoluciones = devoluciones;
        IncidentesGraves = incidentesGraves;
        OtrosIncidentes = otrosIncidentes;
        FechaCorte = fechaCorte;
        LoteOrigen = loteOrigen;
        RecibidoEn = recibidoEn;
    }
}
