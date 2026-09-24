using Npgsql;
using SgoLogic.Auditoria.Worker.Application;

namespace SgoLogic.Auditoria.Tests;

/// <summary>
/// El punto central de HU-006: la inmutabilidad no depende de que el codigo se
/// porte bien, la impone el motor. Dos capas independientes: el rol restringido
/// no tiene UPDATE/DELETE otorgado (falla por permisos, 42501), y el trigger
/// bloquea incluso a quien SI tendria permiso (el propietario).
/// </summary>
[Collection("auditoria")]
public sealed class PermisosInmutabilidadTests
{
    private readonly AuditoriaFixture _fixture;

    public PermisosInmutabilidadTests(AuditoriaFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<Guid> InsertarFilaDePruebaAsync()
    {
        var registrador = new RegistradorEventoAuditoria();
        var evento = EventosDePrueba.Crear();

        await using var contexto = _fixture.CrearContexto(_fixture.CadenaConexionAppRestringida());
        await registrador.RegistrarAsync(evento, contexto, CancellationToken.None);
        await contexto.SaveChangesAsync();

        return contexto.EventosAuditoria.Local.Single().Id;
    }

    [Fact]
    public async Task El_usuario_de_la_aplicacion_no_puede_hacer_UPDATE()
    {
        var id = await InsertarFilaDePruebaAsync();

        await using var conexion = new NpgsqlConnection(_fixture.CadenaConexionAppRestringida());
        await conexion.OpenAsync();

        await using var comando = conexion.CreateCommand();
        comando.CommandText = "UPDATE eventos_auditoria SET event_type = 'manipulado' WHERE id = @id";
        comando.Parameters.AddWithValue("id", id);

        var excepcion = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal("42501", excepcion.SqlState); // insufficient_privilege
    }

    [Fact]
    public async Task El_usuario_de_la_aplicacion_no_puede_hacer_DELETE()
    {
        var id = await InsertarFilaDePruebaAsync();

        await using var conexion = new NpgsqlConnection(_fixture.CadenaConexionAppRestringida());
        await conexion.OpenAsync();

        await using var comando = conexion.CreateCommand();
        comando.CommandText = "DELETE FROM eventos_auditoria WHERE id = @id";
        comando.Parameters.AddWithValue("id", id);

        var excepcion = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal("42501", excepcion.SqlState); // insufficient_privilege
    }

    [Fact]
    public async Task El_trigger_bloquea_UPDATE_incluso_para_el_propietario()
    {
        var id = await InsertarFilaDePruebaAsync();

        await using var conexion = new NpgsqlConnection(_fixture.CadenaConexionPropietario());
        await conexion.OpenAsync();

        await using var comando = conexion.CreateCommand();
        comando.CommandText = "UPDATE eventos_auditoria SET event_type = 'manipulado' WHERE id = @id";
        comando.Parameters.AddWithValue("id", id);

        var excepcion = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        Assert.Equal("P0001", excepcion.SqlState); // raise_exception, disparada por el trigger
    }

    /// <summary>
    /// mensajes_procesados no es la bitacora (no necesita ser inmutable), pero el
    /// rol restringido SI necesita poder leerla y escribirla: sin esto,
    /// EventConsumerBase no puede deduplicar y el consumidor entero falla con
    /// "permission denied for table mensajes_procesados" (se detecto asi, corriendo
    /// el stack completo con docker compose, no con un test: por eso este test).
    /// </summary>
    [Fact]
    public async Task El_usuario_de_la_aplicacion_puede_leer_y_escribir_mensajes_procesados()
    {
        await using var conexion = new NpgsqlConnection(_fixture.CadenaConexionAppRestringida());
        await conexion.OpenAsync();

        await using (var insertar = conexion.CreateCommand())
        {
            insertar.CommandText =
                "INSERT INTO mensajes_procesados (id, event_id, nombre_servicio, fecha_proceso) " +
                "VALUES (gen_random_uuid(), @eventId, 'auditoria-worker', now())";
            insertar.Parameters.AddWithValue("eventId", Guid.NewGuid().ToString());
            await insertar.ExecuteNonQueryAsync();
        }

        await using var leer = conexion.CreateCommand();
        leer.CommandText = "SELECT count(*) FROM mensajes_procesados";
        var total = (long)(await leer.ExecuteScalarAsync())!;

        Assert.True(total > 0);
    }
}