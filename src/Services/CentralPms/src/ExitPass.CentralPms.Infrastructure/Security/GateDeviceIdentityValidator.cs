using ExitPass.CentralPms.Application.Security;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.Security;

/// <summary>
/// PostgreSQL-backed validator for gate-device service identity and lane/site assignment.
///
/// BRD v1.2 Reference:
/// - Section 9.12 Exit Authorization
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 6.6 Consume Exit Authorization
/// - Section 10.6 Internal Service APIs
///
/// ExitPass v1.2 Invariants Enforced:
/// - Gate consume authorization requires an ACTIVE DEVICE service identity.
/// - Gate consume authorization requires an ACTIVE gate device assigned to the same site as the ExitAuthorization parking session.
/// - Validation reads identity/sites/gates/core tables only and does not mutate payment, provider, exit, gate, or settlement truth.
/// </summary>
public sealed class GateDeviceIdentityValidator : IGateDeviceIdentityValidator
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="GateDeviceIdentityValidator"/> class.
    /// </summary>
    /// <param name="connectionString">Database connection string.</param>
    public GateDeviceIdentityValidator(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<GateDeviceIdentityValidationResult> ValidateConsumeAsync(
        GateDeviceIdentityValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExitAuthorizationId == Guid.Empty)
        {
            return GateDeviceIdentityValidationResult.Rejected(
                "INVALID_REQUEST",
                "ExitAuthorizationId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.GateDeviceIdentifier))
        {
            return GateDeviceIdentityValidationResult.Rejected(
                "GATE_DEVICE_IDENTITY_REQUIRED",
                "X-Gate-Device-Id header is required.");
        }

        if (request.ServiceIdentityId == Guid.Empty)
        {
            return GateDeviceIdentityValidationResult.Rejected(
                "SERVICE_IDENTITY_REQUIRED",
                "X-Service-Identity-Id header is required.");
        }

        const string sql = """
            WITH requested AS (
                SELECT
                    @exit_authorization_id::uuid AS exit_authorization_id,
                    @service_identity_id::uuid AS service_identity_id,
                    NULLIF(BTRIM(@gate_device_identifier), '') AS gate_device_identifier,
                    @gate_device_uuid::uuid AS gate_device_uuid
            ),
            target_authorization AS (
                SELECT
                    ea.exit_authorization_id,
                    ps.site_id
                FROM requested r
                JOIN core.exit_authorizations ea
                    ON ea.exit_authorization_id = r.exit_authorization_id
                JOIN core.parking_sessions ps
                    ON ps.parking_session_id = ea.parking_session_id
            ),
            service_identity AS (
                SELECT
                    si.service_identity_id,
                    si.identity_type,
                    si.identity_status,
                    si.effective_from,
                    si.effective_to,
                    si.revoked_at
                FROM requested r
                JOIN identity.service_identities si
                    ON si.service_identity_id = r.service_identity_id
            ),
            candidate_gate_device AS (
                SELECT
                    gd.gate_device_id,
                    gd.site_id,
                    gd.lane_id,
                    gd.service_identity_id,
                    gd.device_status,
                    gd.retired_at
                FROM requested r
                JOIN gates.gate_devices gd
                    ON gd.service_identity_id = r.service_identity_id
                   AND (
                        gd.gate_device_id = r.gate_device_uuid
                        OR gd.device_code = r.gate_device_identifier
                        OR gd.vendor_device_ref = r.gate_device_identifier
                        OR gd.serial_number = r.gate_device_identifier
                   )
            ),
            active_assignment AS (
                SELECT
                    da.device_assignment_id,
                    da.gate_device_id,
                    da.service_identity_id,
                    da.site_id,
                    da.lane_id,
                    da.assignment_type,
                    da.assignment_status,
                    da.unassigned_at
                FROM sites.device_assignments da
                JOIN candidate_gate_device gd
                    ON da.gate_device_id = gd.gate_device_id
                   AND da.service_identity_id = gd.service_identity_id
                WHERE da.assignment_type IN ('GATE_DEVICE', 'LANE_CONTROLLER')
                  AND da.assignment_status = 'ACTIVE'
                  AND da.unassigned_at IS NULL
            ),
            lane_state AS (
                SELECT
                    l.lane_id,
                    l.site_id,
                    l.lane_status,
                    l.lane_direction,
                    l.effective_from,
                    l.effective_to
                FROM candidate_gate_device gd
                JOIN sites.lanes l
                    ON l.lane_id = gd.lane_id
            )
            SELECT
                EXISTS (SELECT 1 FROM target_authorization) AS authorization_exists,
                EXISTS (SELECT 1 FROM service_identity) AS service_identity_exists,
                EXISTS (
                    SELECT 1
                    FROM service_identity si
                    WHERE si.identity_type = 'DEVICE'
                      AND si.identity_status = 'ACTIVE'
                      AND si.effective_from <= now()
                      AND (si.effective_to IS NULL OR si.effective_to > now())
                      AND si.revoked_at IS NULL
                ) AS service_identity_active_device,
                EXISTS (SELECT 1 FROM candidate_gate_device) AS gate_device_exists,
                EXISTS (
                    SELECT 1
                    FROM candidate_gate_device gd
                    WHERE gd.device_status = 'ACTIVE'
                      AND gd.retired_at IS NULL
                ) AS gate_device_active,
                EXISTS (
                    SELECT 1
                    FROM target_authorization ta
                    JOIN candidate_gate_device gd
                      ON gd.site_id = ta.site_id
                ) AS gate_device_site_matches,
                EXISTS (
                    SELECT 1
                    FROM target_authorization ta
                    JOIN candidate_gate_device gd
                      ON gd.site_id = ta.site_id
                    JOIN active_assignment da
                      ON da.gate_device_id = gd.gate_device_id
                     AND da.service_identity_id = gd.service_identity_id
                     AND da.site_id = ta.site_id
                     AND (gd.lane_id IS NULL OR da.lane_id = gd.lane_id)
                ) AS active_assignment_matches,
                EXISTS (
                    SELECT 1
                    FROM candidate_gate_device gd
                    LEFT JOIN lane_state l
                      ON l.lane_id = gd.lane_id
                    WHERE gd.lane_id IS NULL
                       OR (
                            l.lane_status = 'ACTIVE'
                            AND l.lane_direction IN ('OUTBOUND', 'BIDIRECTIONAL')
                            AND l.effective_from <= now()
                            AND (l.effective_to IS NULL OR l.effective_to > now())
                          )
                ) AS lane_allows_exit,
                (
                    SELECT gd.gate_device_id
                    FROM candidate_gate_device gd
                    LIMIT 1
                ) AS gate_device_id,
                (
                    SELECT gd.site_id
                    FROM candidate_gate_device gd
                    LIMIT 1
                ) AS site_id,
                (
                    SELECT gd.lane_id
                    FROM candidate_gate_device gd
                    LIMIT 1
                ) AS lane_id;
            """;

        var parsedGateDeviceId = Guid.TryParse(request.GateDeviceIdentifier, out var gateDeviceUuid)
            ? gateDeviceUuid
            : (Guid?)null;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.Add("exit_authorization_id", NpgsqlDbType.Uuid).Value = request.ExitAuthorizationId;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = request.ServiceIdentityId;
        command.Parameters.Add("gate_device_identifier", NpgsqlDbType.Text).Value = request.GateDeviceIdentifier.Trim();
        command.Parameters.Add("gate_device_uuid", NpgsqlDbType.Uuid).Value = (object?)parsedGateDeviceId ?? DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return GateDeviceIdentityValidationResult.Rejected(
                "GATE_DEVICE_IDENTITY_VALIDATION_FAILED",
                "Gate device identity validation returned no result.");
        }

        if (!reader.GetBoolean(reader.GetOrdinal("authorization_exists")))
        {
            return GateDeviceIdentityValidationResult.Rejected(
                "EXIT_AUTHORIZATION_NOT_FOUND",
                "Exit authorization was not found.");
        }

        if (!reader.GetBoolean(reader.GetOrdinal("service_identity_exists")) ||
            !reader.GetBoolean(reader.GetOrdinal("service_identity_active_device")))
        {
            return GateDeviceIdentityValidationResult.Rejected(
                "SERVICE_IDENTITY_FORBIDDEN",
                "Service identity is not an active DEVICE identity.");
        }

        if (!reader.GetBoolean(reader.GetOrdinal("gate_device_exists")) ||
            !reader.GetBoolean(reader.GetOrdinal("gate_device_active")))
        {
            return GateDeviceIdentityValidationResult.Rejected(
                "GATE_DEVICE_FORBIDDEN",
                "Gate device is not active or is not linked to the service identity.");
        }

        if (!reader.GetBoolean(reader.GetOrdinal("gate_device_site_matches")) ||
            !reader.GetBoolean(reader.GetOrdinal("active_assignment_matches")))
        {
            return GateDeviceIdentityValidationResult.Rejected(
                "GATE_DEVICE_ASSIGNMENT_FORBIDDEN",
                "Gate device is not actively assigned to the authorization site and lane.");
        }

        if (!reader.GetBoolean(reader.GetOrdinal("lane_allows_exit")))
        {
            return GateDeviceIdentityValidationResult.Rejected(
                "GATE_DEVICE_LANE_FORBIDDEN",
                "Gate device lane is not active for outbound exit control.");
        }

        var gateDeviceId = reader.GetGuid(reader.GetOrdinal("gate_device_id"));
        var siteId = reader.GetGuid(reader.GetOrdinal("site_id"));
        var laneOrdinal = reader.GetOrdinal("lane_id");
        var laneId = reader.IsDBNull(laneOrdinal)
            ? (Guid?)null
            : reader.GetGuid(laneOrdinal);

        return GateDeviceIdentityValidationResult.Authorized(gateDeviceId, siteId, laneId);
    }
}
