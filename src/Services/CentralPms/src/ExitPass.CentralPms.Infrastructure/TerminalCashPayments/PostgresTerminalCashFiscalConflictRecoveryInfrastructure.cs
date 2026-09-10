using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.TerminalCashPayments;

public sealed class PostgresTerminalCashFiscalConflictRecoveryGuardRepository(string connectionString)
    : ITerminalCashFiscalConflictRecoveryGuardRepository
{
    private readonly string _connectionString = !string.IsNullOrWhiteSpace(connectionString)
        ? connectionString
        : throw new ArgumentException("Connection string is required.", nameof(connectionString));

    public async Task<TerminalCashFiscalConflictRecoveryFacts?> ReadAsync(
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                pa.payment_attempt_id,
                pc.payment_confirmation_id,
                pa.parking_session_id,
                pa.tariff_snapshot_id,
                pa.attempt_status::text,
                pc.confirmation_status::text,
                pa.currency_code::text,
                ROUND(pa.amount * 100)::bigint AS payment_attempt_amount_minor_units,
                pc.currency_code::text,
                ROUND(pc.confirmed_amount * 100)::bigint AS payment_confirmation_amount_minor_units,
                (
                    SELECT count(*)::int
                    FROM core.exit_authorizations ea
                    WHERE ea.parking_session_id = pa.parking_session_id
                       OR ea.payment_attempt_id = pa.payment_attempt_id
                ) AS exit_authorization_count
            FROM core.payment_attempts pa
            INNER JOIN core.payment_confirmations pc
                ON pc.payment_attempt_id = pa.payment_attempt_id
            WHERE pa.payment_attempt_id = @payment_attempt_id
              AND pc.payment_confirmation_id = @payment_confirmation_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = paymentAttemptId;
        command.Parameters.Add("payment_confirmation_id", NpgsqlDbType.Uuid).Value = paymentConfirmationId;
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new TerminalCashFiscalConflictRecoveryFacts(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6).Trim(),
            reader.GetInt64(7),
            reader.GetString(8).Trim(),
            reader.GetInt64(9),
            reader.GetInt32(10));
    }
}

public sealed class PostgresTerminalCashFiscalConflictRecoveryLock(string connectionString)
    : ITerminalCashFiscalConflictRecoveryLock
{
    private const string LockNamespace = "exitpass:terminal-cash-fiscal-conflict-recovery:v1:";
    private readonly string _connectionString = !string.IsNullOrWhiteSpace(connectionString)
        ? connectionString
        : throw new ArgumentException("Connection string is required.", nameof(connectionString));

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var lockKey = DeriveLockKey(fiscalIssuanceReferenceId);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lock_key);", connection);
            command.Parameters.Add("lock_key", NpgsqlDbType.Bigint).Value = lockKey;
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new Lease(connection, lockKey);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static long DeriveLockKey(Guid fiscalIssuanceReferenceId)
    {
        var input = Encoding.UTF8.GetBytes(LockNamespace + fiscalIssuanceReferenceId.ToString("D"));
        return BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(input).AsSpan(0, sizeof(long)));
    }

    private sealed class Lease(NpgsqlConnection connection, long lockKey) : IAsyncDisposable
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
                if (current.FullState.HasFlag(ConnectionState.Open))
                {
                    await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lock_key);", current);
                    command.Parameters.Add("lock_key", NpgsqlDbType.Bigint).Value = lockKey;
                    await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await current.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
