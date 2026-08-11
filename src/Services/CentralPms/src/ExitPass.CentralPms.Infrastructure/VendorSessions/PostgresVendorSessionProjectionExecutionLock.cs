using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.VendorSessions;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.VendorSessions;

/// <summary>
/// PostgreSQL session advisory lock scoped to one projection target.
/// </summary>
public sealed class PostgresVendorSessionProjectionExecutionLock(string connectionString)
    : IVendorSessionProjectionExecutionLock
{
    private const string LockNamespace = "exitpass:vendor-session-projection:v1:";
    private readonly string _connectionString = !string.IsNullOrWhiteSpace(connectionString)
        ? connectionString
        : throw new ArgumentException("Connection string is required.", nameof(connectionString));

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> TryAcquireAsync(
        Guid projectionSyncTargetId,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var key = DeriveLockKey(projectionSyncTargetId);
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(@lock_key);",
                connection);
            command.Parameters.Add("lock_key", NpgsqlDbType.Bigint).Value = key;
            var acquired = await command.ExecuteScalarAsync(cancellationToken) is true;
            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Lease(connection, key);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal static long DeriveLockKey(Guid projectionSyncTargetId)
    {
        var input = Encoding.UTF8.GetBytes(LockNamespace + projectionSyncTargetId.ToString("D"));
        var hash = SHA256.HashData(input);
        return BinaryPrimitives.ReadInt64BigEndian(hash.AsSpan(0, sizeof(long)));
    }

    private sealed class Lease(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        private NpgsqlConnection? _connection = connection;

        public async ValueTask DisposeAsync()
        {
            var current = Interlocked.Exchange(ref _connection, null);
            if (current is null)
            {
                return;
            }

            try
            {
                if (current.FullState.HasFlag(System.Data.ConnectionState.Open))
                {
                    await using var command = new NpgsqlCommand(
                        "SELECT pg_advisory_unlock(@lock_key);",
                        current);
                    command.Parameters.Add("lock_key", NpgsqlDbType.Bigint).Value = key;
                    await command.ExecuteScalarAsync(CancellationToken.None);
                }
            }
            finally
            {
                await current.DisposeAsync();
            }
        }
    }
}
