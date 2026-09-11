using SgoLogic.Contracts.Enumeraciones;
using SgoLogic.Inventario.Api.Domain;
using Xunit;

namespace SgoLogic.Inventario.Tests.Domain;

public sealed class ReservaTests
{
    private static Reserva Nueva(int ttlSegundos = 900, DateTime? creadaEn = null) => Reserva.Crear(
        "RSV-2026-000042",
        "ORD-2026-000777",
        "CD-01",
        TipoUbicacion.CENTRO_DISTRIBUCION,
        ttlSegundos,
        new[] { ("SKU-1", 2), ("SKU-2", 1) },
        creadaEn ?? new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Crear_deja_la_reserva_activa_con_expiracion_igual_a_creada_mas_ttl()
    {
        var creada = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        var reserva = Nueva(ttlSegundos: 600, creadaEn: creada);

        Assert.Equal(EstadoReserva.ACTIVA, reserva.Estado);
        Assert.Equal(1, reserva.Secuencia);
        Assert.Equal(creada.AddSeconds(600), reserva.ExpiraEn);
        Assert.Equal(2, reserva.Lineas.Count);
    }

    [Fact]
    public void HaVencido_es_true_al_llegar_a_la_hora_de_expiracion()
    {
        var creada = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
        var reserva = Nueva(ttlSegundos: 300, creadaEn: creada);

        Assert.False(reserva.HaVencido(creada.AddSeconds(299)));
        Assert.True(reserva.HaVencido(creada.AddSeconds(300)));
    }

    [Fact]
    public void MarcarExpirada_cambia_el_estado_y_sube_la_secuencia_una_sola_vez()
    {
        var reserva = Nueva();

        reserva.MarcarExpirada();
        Assert.Equal(EstadoReserva.EXPIRADA, reserva.Estado);
        Assert.Equal(2, reserva.Secuencia);

        reserva.MarcarExpirada();
        Assert.Equal(2, reserva.Secuencia);
    }

    [Fact]
    public void Liberar_solo_actua_si_la_reserva_sigue_activa()
    {
        var reserva = Nueva();
        reserva.MarcarExpirada();

        reserva.Liberar();

        Assert.Equal(EstadoReserva.EXPIRADA, reserva.Estado);
    }

    [Fact]
    public void Crear_con_id_fuera_de_patron_lanza()
    {
        Assert.Throws<ArgumentException>(() => Reserva.Crear(
            "RESERVA-1",
            "ORD-2026-000777",
            "CD-01",
            TipoUbicacion.CENTRO_DISTRIBUCION,
            900,
            new[] { ("SKU-1", 1) }));
    }
}
