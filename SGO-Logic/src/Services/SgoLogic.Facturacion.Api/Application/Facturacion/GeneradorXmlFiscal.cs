using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SgoLogic.Contracts.Enumeraciones;

namespace SgoLogic.Facturacion.Api.Application.Facturacion;

/// <summary>Datos de entrada para generar el XML fiscal de un documento.</summary>
public sealed record DatosXmlFiscal(
    string OrdenId,
    TipoDocumentoFiscal TipoDocumento,
    string Serie,
    string NumeroDocumento,
    string CuiReceptor,
    string NombreReceptor,
    string DireccionReceptor,
    Pais PaisReceptor,
    Moneda Moneda,
    decimal Subtotal,
    decimal Impuestos,
    decimal Total,
    DateTime FechaEmision,
    IReadOnlyList<DatosXmlLinea> Lineas);

public sealed record DatosXmlLinea(
    int NumeroLinea,
    string Sku,
    string Descripcion,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Impuesto);

/// <summary>
/// Genera el XML fiscal del documento a partir del CUI del cliente y la serie
/// autorizada. Deterministico y sin dependencias: la unica variabilidad es
/// <see cref="DatosXmlFiscal.FechaEmision" />, que entra como dato.
/// </summary>
public sealed class GeneradorXmlFiscal
{
    private const string Ns = "urn:sgo-logic:facturacion:xml:v1";

    public string Generar(DatosXmlFiscal datos)
    {
        XNamespace ns = Ns;

        var encabezado = new XElement(ns + "Encabezado",
            new XElement(ns + "OrdenId", datos.OrdenId),
            new XElement(ns + "TipoDocumento", datos.TipoDocumento.ToString()),
            new XElement(ns + "Serie", datos.Serie),
            new XElement(ns + "Numero", datos.NumeroDocumento),
            new XElement(ns + "FechaEmision", datos.FechaEmision.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            new XElement(ns + "Moneda", datos.Moneda.ToString()));

        var receptor = new XElement(ns + "Receptor",
            new XElement(ns + "CUI", datos.CuiReceptor),
            new XElement(ns + "Nombre", datos.NombreReceptor),
            new XElement(ns + "Direccion", datos.DireccionReceptor),
            new XElement(ns + "Pais", datos.PaisReceptor.ToString()));

        var items = new XElement(ns + "Items",
            datos.Lineas.Select(linea => new XElement(ns + "Item",
                new XAttribute("numeroLinea", linea.NumeroLinea),
                new XElement(ns + "SKU", linea.Sku),
                new XElement(ns + "Descripcion", linea.Descripcion),
                new XElement(ns + "Cantidad", linea.Cantidad.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "PrecioUnitario", Decimal(linea.PrecioUnitario)),
                new XElement(ns + "Impuesto", Decimal(linea.Impuesto)))));

        var totales = new XElement(ns + "Totales",
            new XElement(ns + "Subtotal", Decimal(datos.Subtotal)),
            new XElement(ns + "Impuestos", Decimal(datos.Impuestos)),
            new XElement(ns + "GranTotal", Decimal(datos.Total)));

        var documento = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "DocumentoFiscal",
                new XAttribute("version", "1.0"),
                encabezado,
                receptor,
                items,
                totales));

        using var memoria = new MemoryStream();
        using (var xml = XmlWriter.Create(memoria, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false
        }))
        {
            documento.Save(xml);
        }

        return System.Text.Encoding.UTF8.GetString(memoria.ToArray());
    }

    private static string Decimal(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);
}
