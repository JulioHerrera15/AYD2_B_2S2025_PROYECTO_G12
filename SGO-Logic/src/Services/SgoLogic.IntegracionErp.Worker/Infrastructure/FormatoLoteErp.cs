using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace SgoLogic.IntegracionErp.Worker.Infrastructure;

public sealed record RegistroVentaErp(string OrdenId, string Canal, string Tienda, decimal Monto, DateTime Fecha);
public sealed record LoteParaEnviar(string LoteId, long Secuencia, DateTime FechaGeneracion, IReadOnlyList<RegistroVentaErp> Registros)
{
    public decimal MontoTotal => Registros.Sum(x => x.Monto);
}

public interface IGeneradorLoteErp
{
    string Generar(LoteParaEnviar lote, string formato);
    string ContentType(string formato);
}

public sealed class GeneradorLoteErp : IGeneradorLoteErp
{
    public string Generar(LoteParaEnviar lote, string formato)
        => formato.Equals("XML", StringComparison.OrdinalIgnoreCase) ? GenerarXml(lote) : GenerarCsv(lote);

    public string ContentType(string formato)
        => formato.Equals("XML", StringComparison.OrdinalIgnoreCase) ? "application/xml" : "text/csv";

    private static string GenerarCsv(LoteParaEnviar lote)
    {
        var cultura = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', lote.LoteId, lote.Secuencia, lote.FechaGeneracion.ToUniversalTime().ToString("O", cultura), lote.Registros.Count, lote.MontoTotal.ToString("0.00", cultura)));
        sb.AppendLine("ORDEN,CANAL,TIENDA,MONTO,FECHA");
        foreach (var r in lote.Registros)
            sb.AppendLine(string.Join(',', r.OrdenId, r.Canal, r.Tienda, r.Monto.ToString("0.00", cultura), r.Fecha.ToUniversalTime().ToString("O", cultura)));
        return sb.ToString();
    }

    private static string GenerarXml(LoteParaEnviar lote)
    {
        var doc = new XDocument(new XElement("Lote",
            new XAttribute("id", lote.LoteId), new XAttribute("secuencia", lote.Secuencia), new XAttribute("fecha", lote.FechaGeneracion.ToUniversalTime().ToString("O")),
            new XElement("TotalesControl", new XAttribute("cantidadRegistros", lote.Registros.Count), new XAttribute("montoTotal", lote.MontoTotal.ToString("0.00", CultureInfo.InvariantCulture))),
            new XElement("Registros", lote.Registros.Select(r => new XElement("Orden",
                new XAttribute("id", r.OrdenId), new XAttribute("canal", r.Canal), new XAttribute("tienda", r.Tienda),
                new XAttribute("monto", r.Monto.ToString("0.00", CultureInfo.InvariantCulture)), new XAttribute("fecha", r.Fecha.ToUniversalTime().ToString("O")))))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }
}
