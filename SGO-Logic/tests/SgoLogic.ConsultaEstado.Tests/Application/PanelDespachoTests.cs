using SgoLogic.Web.Services;
using Xunit;

namespace SgoLogic.ConsultaEstado.Tests.Application;

/// <summary>
/// Panel web de despacho: el origen lo decide Inventario, asi que el panel no puede
/// depender de una lista fija de sedes (antes solo CD-01 y TIENDA-01-CENTRAL).
/// </summary>
public sealed class PanelDespachoTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static List<DespachoPreparacionDto> Despachos() => new()
    {
        new("DES-2026-000001", "ORD-2026-000001", "CD-01", "DOMICILIO", "GUIA_ASIGNADA", "LGX-1", T0),
        new("DES-2026-000002", "ORD-2026-000002", "CD-CENTRAL", "DOMICILIO", "EN_PREPARACION", "LGX-2", T0.AddMinutes(1)),
        new("DES-2026-000003", "ORD-2026-000003", "TIENDA-02-MIRAFLORES", "RECOGIDA_TIENDA", "RECOGIDA_TIENDA", null, T0.AddMinutes(2), "TIENDA-02-MIRAFLORES"),
    };

    [Fact]
    public void Con_todas_aparece_un_despacho_de_CD_CENTRAL()
    {
        var modelo = PanelDespacho.Construir(Despachos(), "TODOS", PanelDespacho.TodasLasUbicaciones);

        Assert.Equal(3, modelo.TotalDespachos);
        var central = Assert.Single(modelo.Items, i => i.OrdenId == "ORD-2026-000002");
        Assert.Equal("Envío a Domicilio (CD-CENTRAL)", central.Destino);
        Assert.True(central.PuedeEnviarARuta);
        Assert.Null(modelo.Error);
    }

    [Fact]
    public void El_destino_de_recogida_muestra_la_tienda_real()
    {
        var modelo = PanelDespacho.Construir(Despachos(), "RECOGIDA_TIENDA", PanelDespacho.TodasLasUbicaciones);

        var recogida = Assert.Single(modelo.Items);
        Assert.Equal("Recogida en Tienda (TIENDA-02-MIRAFLORES)", recogida.Destino);
        Assert.DoesNotContain("Tienda Central", recogida.Destino);
    }

    [Fact]
    public void Sin_tienda_de_recogida_el_destino_usa_la_ubicacion_de_origen()
    {
        var despachos = new List<DespachoPreparacionDto>
        {
            new("DES-2026-000009", "ORD-2026-000009", "TIENDA-03-NORTE", "RECOGIDA_TIENDA", "EN_PREPARACION", null, T0),
        };

        var modelo = PanelDespacho.Construir(despachos, "TODOS", PanelDespacho.TodasLasUbicaciones);

        Assert.Equal("Recogida en Tienda (TIENDA-03-NORTE)", Assert.Single(modelo.Items).Destino);
    }

    [Fact]
    public void El_selector_de_sedes_sale_de_los_origenes_reales_aunque_se_filtre_por_una()
    {
        var modelo = PanelDespacho.Construir(Despachos(), "TODOS", "CD-CENTRAL");

        Assert.Equal(new[] { "CD-01", "CD-CENTRAL", "TIENDA-02-MIRAFLORES" }, modelo.Sedes);
        Assert.Equal("ORD-2026-000002", Assert.Single(modelo.Items).OrdenId);
        Assert.Equal(1, modelo.TotalDespachos);
    }

    [Fact]
    public void Un_despacho_repetido_se_muestra_una_sola_vez()
    {
        var despachos = Despachos();
        despachos.Add(despachos[1]);

        var modelo = PanelDespacho.Construir(despachos, "TODOS", PanelDespacho.TodasLasUbicaciones);

        Assert.Equal(3, modelo.TotalDespachos);
    }

    [Fact]
    public void Si_el_servicio_falla_se_informa_el_error_y_no_una_lista_vacia()
    {
        var modelo = PanelDespacho.Construir(null, "TODOS", PanelDespacho.TodasLasUbicaciones);

        Assert.False(string.IsNullOrWhiteSpace(modelo.Error));
        Assert.Empty(modelo.Items);
    }
}
