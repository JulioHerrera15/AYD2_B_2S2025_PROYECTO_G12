using System.Xml.Linq;
using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Facturacion.Api.Application.Facturacion;

namespace SgoLogic.Facturacion.Tests.Application;

public sealed class GeneradorXmlFiscalTests
{
    private static DatosXmlFiscal Datos(string cui = "2547896320101", string serie = "A", string numero = "00000042") => new(
        OrdenId: "ORD-2026-000123",
        TipoDocumento: TipoDocumentoFiscal.FACTURA,
        Serie: serie,
        NumeroDocumento: numero,
        CuiReceptor: cui,
        NombreReceptor: "Angel David Martinez Garcia",
        DireccionReceptor: "5a avenida 3-21 zona 2, San Juan Sacatepequez",
        PaisReceptor: Pais.GT,
        Moneda: Moneda.GTQ,
        Subtotal: 1250.00m,
        Impuestos: 150.00m,
        Total: 1400.00m,
        FechaEmision: new DateTime(2026, 9, 10, 14, 31, 30, DateTimeKind.Utc),
        Lineas: new[]
        {
            new DatosXmlLinea(1, "SKU-LB-00871", "Refrigeradora 12 pies", 1, 1250.00m, 150.00m)
        });

    [Fact]
    public void El_xml_se_genera_con_el_cui_y_la_serie_correctos()
    {
        var xml = new GeneradorXmlFiscal().Generar(Datos(cui: "2547896320101", serie: "A", numero: "00000042"));

        var documento = XDocument.Parse(xml);
        XNamespace ns = "urn:sgo-logic:facturacion:xml:v1";

        Assert.Equal("2547896320101", documento.Descendants(ns + "CUI").Single().Value);
        Assert.Equal("A", documento.Descendants(ns + "Serie").Single().Value);
        Assert.Equal("00000042", documento.Descendants(ns + "Numero").Single().Value);
        Assert.Equal("FACTURA", documento.Descendants(ns + "TipoDocumento").Single().Value);
        Assert.Equal("GTQ", documento.Descendants(ns + "Moneda").Single().Value);
        Assert.Equal("1400.00", documento.Descendants(ns + "GranTotal").Single().Value);
        Assert.Equal("SKU-LB-00871", documento.Descendants(ns + "SKU").Single().Value);
    }

    [Fact]
    public void El_xml_cambia_con_el_cui_del_receptor()
    {
        var generador = new GeneradorXmlFiscal();

        var xmlUno = generador.Generar(Datos(cui: "2547896320101"));
        var xmlDos = generador.Generar(Datos(cui: "1111111110101"));

        Assert.Contains("2547896320101", xmlUno);
        Assert.DoesNotContain("2547896320101", xmlDos);
        Assert.Contains("1111111110101", xmlDos);
    }
}
