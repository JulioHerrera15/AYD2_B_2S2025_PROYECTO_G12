using SgoLogic.IntegracionErp.Worker.Infrastructure;

namespace SgoLogic.IntegracionErp.Tests;

public sealed class FormatoLoteErpTests
{
    private static LoteParaEnviar Lote() => new(
        "LOTE-TEST-001",
        7,
        new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc),
        [
            new RegistroVentaErp("ORD-1", "WEB", "T01", 100.50m, new DateTime(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc)),
            new RegistroVentaErp("ORD-2", "TIENDA", "T02", 25.25m, new DateTime(2026, 9, 20, 11, 5, 0, DateTimeKind.Utc))
        ]);

    [Fact]
    public void Genera_CSV_con_metadatos_y_totales_de_control()
    {
        var generador = new GeneradorLoteErp();
        var csv = generador.Generar(Lote(), "CSV");

        Assert.Contains("LOTE-TEST-001,7,", csv);
        Assert.Contains(",2,125.75", csv);
        Assert.Contains("ORDEN,CANAL,TIENDA,MONTO,FECHA", csv);
        Assert.Contains("ORD-1,WEB,T01,100.50", csv);
    }

    [Fact]
    public void Genera_XML_compatible_con_el_mock_ERP()
    {
        var generador = new GeneradorLoteErp();
        var xml = generador.Generar(Lote(), "XML");

        Assert.Contains("<Lote id=\"LOTE-TEST-001\" secuencia=\"7\"", xml);
        Assert.Contains("<TotalesControl cantidadRegistros=\"2\" montoTotal=\"125.75\"", xml);
        Assert.Contains("<Orden id=\"ORD-1\" canal=\"WEB\" tienda=\"T01\"", xml);
    }
}
