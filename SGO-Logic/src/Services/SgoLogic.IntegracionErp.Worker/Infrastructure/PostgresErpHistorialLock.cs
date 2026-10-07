using Npgsql;

namespace SgoLogic.IntegracionErp.Worker.Infrastructure;

public interface IErpHistorialLock
{
    ValueTask<IErpHistorialLease?> TryAcquireAsync(CancellationToken cancellationToken);
}

public interface IErpHistorialLease : IAsyncDisposable
{
}

public sealed class PostgresErpHistorialLock : IErpHistorialLock
{
    private const string LockName = "sgo-logic.integracion-erp.historial-clientes";
    private readonly string _connectionString;

    public PostgresErpHistorialLock(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("La cadena de conexion de Postgres es obligatoria.", nameof(connectionString));

        _connectionString = connectionString;
    }

    public async ValueTask<IErpHistorialLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtextextended(@lock_name, 0));",
            connection);
        command.Parameters.AddWithValue("lock_name", LockName);

        var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        if (acquired)
            return new PostgresErpHistorialLease(connection);

        await connection.DisposeAsync();
        return null;
    }

    private sealed class PostgresErpHistorialLease : IErpHistorialLease
    {
        private readonly NpgsqlConnection _connection;
        private bool _released;

        public PostgresErpHistorialLease(NpgsqlConnection connection) => _connection = connection;

        public async ValueTask DisposeAsync()
        {
            if (_released)
                return;

            _released = true;
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtextextended(@lock_name, 0));",
                _connection);
            command.Parameters.AddWithValue("lock_name", LockName);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            await _connection.DisposeAsync();
        }
    }
}
