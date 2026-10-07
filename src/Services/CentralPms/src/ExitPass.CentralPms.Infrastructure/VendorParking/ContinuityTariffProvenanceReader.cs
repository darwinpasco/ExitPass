using ExitPass.CentralPms.Application.VendorParking;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.VendorParking;

public sealed class ContinuityTariffProvenanceReader : IContinuityTariffProvenanceReader
{
    private readonly string _connectionString;

    public ContinuityTariffProvenanceReader(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<bool> IsContinuityTariffAsync(
        Guid parkingSessionId,
        Guid? tariffSnapshotId,
        Guid? paymentAttemptId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM core.tariff_snapshots AS tariff
                LEFT JOIN core.payment_attempts AS attempt
                  ON attempt.tariff_snapshot_id = tariff.tariff_snapshot_id
                 AND attempt.payment_attempt_id = @payment_attempt_id
                WHERE tariff.parking_session_id = @parking_session_id
                  AND (
                        (@tariff_snapshot_id IS NOT NULL AND tariff.tariff_snapshot_id = @tariff_snapshot_id)
                     OR (@payment_attempt_id IS NOT NULL AND attempt.payment_attempt_id = @payment_attempt_id)
                  )
                  AND tariff.tariff_version_reference LIKE 'EXITPASS-CONTINUITY:%'
            );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        command.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value =
            (object?)tariffSnapshotId ?? DBNull.Value;
        command.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value =
            (object?)paymentAttemptId ?? DBNull.Value;
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
