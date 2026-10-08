using System.Text.RegularExpressions;
using SgoLogic.Credito.Api.Application;
using SgoLogic.Credito.Api.Domain;

namespace SgoLogic.Credito.Tests.Application;

public sealed class CatalogoMensajesCreditoTests
{
    private static readonly string[] Motivos =
    {
        CodigosMotivoCredito.SinHistorial,
        CodigosMotivoCredito.IncidenteGrave,
        CodigosMotivoCredito.AntiguedadInsuficiente,
        CodigosMotivoCredito.DevolucionesExcesivas,
        CodigosMotivoCredito.PuntajeInsuficiente
    };

    private static readonly string[] Valoraciones =
    {
        ValoracionesFactor.Favorable, ValoracionesFactor.Aceptable, ValoracionesFactor.AMejorar
    };

    /// <summary>Todos los textos que el catalogo puede devolver al cliente.</summary>
    private static IEnumerable<string> TodosLosTextos()
    {
        var mensajes = new List<MensajeCredito>
        {
            CatalogoMensajesCredito.Disponible(1500m, 3500m, 30, null, null),
            CatalogoMensajesCredito.Disponible(1500m, 3500m, 30, 900m, new DateTime(2026, 11, 1)),
            CatalogoMensajesCredito.CupoInsuficiente(1500m, 2000m),
            CatalogoMensajesCredito.SinHistorial(),
            CatalogoMensajesCredito.LineaBloqueada()
        };

        foreach (var motivo in Motivos)
        {
            foreach (var factor in Enum.GetValues<FactorCliente>())
            {
                mensajes.Add(CatalogoMensajesCredito.NoElegible(motivo, 90, 50, factor));
            }

            mensajes.Add(CatalogoMensajesCredito.NoElegible(motivo, 90, 1, null));
        }

        foreach (var mensaje in mensajes)
        {
            yield return mensaje.Titulo;
            yield return mensaje.Mensaje;
            yield return mensaje.Recomendacion;
        }

        foreach (var factor in Enum.GetValues<FactorCliente>())
        {
            yield return CatalogoMensajesCredito.NombreFactor(factor);
            foreach (var valoracion in Valoraciones)
            {
                yield return CatalogoMensajesCredito.DescripcionFactor(factor, valoracion);
            }
        }

        foreach (var nivel in Enum.GetValues<NivelCredito>())
        {
            yield return CatalogoMensajesCredito.EtiquetaNivel(nivel);
        }
    }

    [Fact]
    public void Ningun_texto_menciona_puntaje_ERP_ni_fraude()
    {
        foreach (var texto in TodosLosTextos())
        {
            Assert.DoesNotContain("puntaje", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fraude", texto, StringComparison.OrdinalIgnoreCase);
            Assert.False(Regex.IsMatch(texto, @"\bERP\b", RegexOptions.IgnoreCase), $"Menciona ERP: {texto}");
        }
    }

    [Fact]
    public void Ningun_texto_trae_codigos_internos()
    {
        foreach (var texto in TodosLosTextos())
        {
            Assert.False(Regex.IsMatch(texto, @"\b[A-Z]+_[A-Z_]+\b"), $"Trae un codigo interno: {texto}");
            Assert.False(string.IsNullOrWhiteSpace(texto));
        }
    }

    [Fact]
    public void Montos_y_fechas_se_escriben_para_el_cliente()
    {
        Assert.Equal("Q7,500.00", CatalogoMensajesCredito.Monto(7500m));
        Assert.Equal("Q0.00", CatalogoMensajesCredito.Monto(0m));
        Assert.Equal("1 de noviembre de 2026", CatalogoMensajesCredito.Fecha(new DateTime(2026, 11, 1)));
    }

    [Fact]
    public void Antiguedad_con_un_dia_usa_singular()
    {
        var mensaje = CatalogoMensajesCredito.NoElegible(CodigosMotivoCredito.AntiguedadInsuficiente, 90, 1, null);

        Assert.Contains("Te falta 1 día.", mensaje.Mensaje);
    }

    [Theory]
    [InlineData(FactorCliente.PagosATiempo, "fecha acordada")]
    [InlineData(FactorCliente.MontoCompras, "historial de compras")]
    [InlineData(FactorCliente.TiempoYFrecuencia, "historial de compras")]
    [InlineData(FactorCliente.DevolucionesYReclamos, "devolución")]
    public void Puntaje_insuficiente_explica_el_factor_mas_debil(FactorCliente factor, string fragmento)
    {
        var mensaje = CatalogoMensajesCredito.NoElegible(CodigosMotivoCredito.PuntajeInsuficiente, 90, null, factor);

        Assert.Contains(fragmento, mensaje.Mensaje);
    }
}
