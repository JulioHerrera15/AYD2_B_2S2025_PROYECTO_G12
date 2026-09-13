using System.Text.RegularExpressions;
using Npgsql;

namespace SgoLogic.Auditoria.Worker.Infrastructure;

/// <summary>
/// Aplica, con el usuario propietario de la base (el unico con permisos para
/// crear roles y otorgar privilegios), la inmutabilidad a nivel de motor de
/// eventos_auditoria:
///
/// 1. Crea (o actualiza la contrasena de) el rol restringido de la aplicacion.
/// 2. Le otorga solo INSERT y SELECT sobre eventos_auditoria, y le revoca
///    explicitamente UPDATE/DELETE/TRUNCATE como cinturon y tirantes.
/// 3. Crea un trigger BEFORE UPDATE OR DELETE que lanza excepcion: aunque algo
///    (una migracion futura, un rol mal configurado, un acceso directo con el
///    usuario propietario) intentara modificar una fila, el trigger lo bloquea
///    igual. Defensa en profundidad real: dos mecanismos independientes.
/// 4. Ademas otorga INSERT y SELECT sobre mensajes_procesados: es la tabla
///    transversal de idempotencia que EventConsumerBase (BuildingBlocks) usa
///    para deduplicar antes de llamar a este consumidor. No es la bitacora de
///    auditoria, no necesita ser inmutable, pero el rol restringido igual
///    necesita poder leerla y escribirla o el consumidor no puede operar.
///
/// Todo el SQL es idempotente: se ejecuta en cada arranque del servicio, despues
/// de correr las migraciones (ver Program.cs), sin que corran dos veces cause
/// error.
///
/// El usuario/password vienen de configuracion (variables de entorno), no de
/// input de usuario final, pero igual se valida el nombre de rol contra un
/// patron de identificador antes de interpolarlo, y la contrasena se escapa como
/// literal SQL antes de interpolarse.
/// </summary>
public static class PermisosAuditoriaAplicador
{
    private const string Tabla = "eventos_auditoria";
    private const string TablaIdempotencia = "mensajes_procesados";
    private static readonly Regex PatronIdentificador = new("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    public static async Task AplicarAsync(
        string cadenaConexionPropietario,
        string usuarioApp,
        string passwordApp,
        CancellationToken cancelacion)
    {
        if (!PatronIdentificador.IsMatch(usuarioApp))
        {
            throw new ArgumentException(
                $"'{usuarioApp}' no es un nombre de rol de PostgreSQL valido (Auditoria:AppUsuario).",
                nameof(usuarioApp));
        }

        var usuarioEntreComillas = $"\"{usuarioApp}\"";
        var usuarioLiteral = EscaparLiteral(usuarioApp);
        var passwordLiteral = EscaparLiteral(passwordApp);

        await using var conexion = new NpgsqlConnection(cadenaConexionPropietario);
        await conexion.OpenAsync(cancelacion);

        var baseDatosEntreComillas = $"\"{conexion.Database}\"";

        var sql = PlantillaSql
            .Replace("{{TABLA}}", Tabla)
            .Replace("{{TABLA_IDEMPOTENCIA}}", TablaIdempotencia)
            .Replace("{{BASE_DATOS}}", baseDatosEntreComillas)
            .Replace("{{USUARIO}}", usuarioEntreComillas)
            .Replace("{{USUARIO_LITERAL}}", usuarioLiteral)
            .Replace("{{PASSWORD_LITERAL}}", passwordLiteral);

        await using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        await comando.ExecuteNonQueryAsync(cancelacion);
    }

    private static string EscaparLiteral(string valor) => valor.Replace("'", "''");

    private const string PlantillaSql = """
        DO $do$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{USUARIO_LITERAL}}') THEN
                CREATE ROLE {{USUARIO}} LOGIN PASSWORD '{{PASSWORD_LITERAL}}';
            ELSE
                ALTER ROLE {{USUARIO}} PASSWORD '{{PASSWORD_LITERAL}}';
            END IF;
        END
        $do$;

        GRANT CONNECT ON DATABASE {{BASE_DATOS}} TO {{USUARIO}};
        GRANT USAGE ON SCHEMA public TO {{USUARIO}};

        REVOKE ALL ON TABLE {{TABLA}} FROM PUBLIC;
        REVOKE ALL ON TABLE {{TABLA}} FROM {{USUARIO}};
        GRANT INSERT, SELECT ON TABLE {{TABLA}} TO {{USUARIO}};

        REVOKE ALL ON TABLE {{TABLA_IDEMPOTENCIA}} FROM PUBLIC;
        REVOKE ALL ON TABLE {{TABLA_IDEMPOTENCIA}} FROM {{USUARIO}};
        GRANT INSERT, SELECT ON TABLE {{TABLA_IDEMPOTENCIA}} TO {{USUARIO}};

        CREATE OR REPLACE FUNCTION fn_bloquear_modificacion_eventos_auditoria()
        RETURNS TRIGGER AS $fn$
        BEGIN
            RAISE EXCEPTION
                'eventos_auditoria es de solo insercion (bitacora inmutable): % no esta permitido sobre event_id=%',
                TG_OP, OLD.event_id;
        END;
        $fn$ LANGUAGE plpgsql;

        DROP TRIGGER IF EXISTS trg_bloquear_modificacion_eventos_auditoria ON {{TABLA}};
        CREATE TRIGGER trg_bloquear_modificacion_eventos_auditoria
            BEFORE UPDATE OR DELETE ON {{TABLA}}
            FOR EACH ROW EXECUTE FUNCTION fn_bloquear_modificacion_eventos_auditoria();
        """;
}
