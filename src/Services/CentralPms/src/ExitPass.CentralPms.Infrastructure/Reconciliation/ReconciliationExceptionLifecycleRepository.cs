using ExitPass.CentralPms.Application.Reconciliation;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExitPass.CentralPms.Infrastructure.Reconciliation;

/// <summary>
/// PostgreSQL-backed reconciliation exception lifecycle repository.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 9.7 Recommended Database Functions
/// - Section 10 API Architecture
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - Lifecycle writes are limited to reconciliation exception workflow tables.
/// - Payment attempts, provider outcomes, payment confirmations, exit authorizations, gate consumptions, and settlement truth are not mutated.
/// </summary>
public sealed class ReconciliationExceptionLifecycleRepository : IReconciliationExceptionLifecycleRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ReconciliationExceptionLifecycleRepository> _logger;

    /// <summary>
    /// Creates a reconciliation exception lifecycle repository.
    /// </summary>
    public ReconciliationExceptionLifecycleRepository(
        string connectionString,
        ILogger<ReconciliationExceptionLifecycleRepository> logger)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ReconciliationExceptionDetailRecord> ReadAsync(
        ReadReconciliationExceptionQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(ExceptionDetailSql("WHERE re.reconciliation_exception_id = @reconciliation_exception_id"), connection);
        dbCommand.Parameters.AddWithValue("reconciliation_exception_id", query.ReconciliationExceptionId);

        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ReconciliationExceptionNotFoundException(query.ReconciliationExceptionId);
        }

        return ReadException(reader);
    }

    /// <inheritdoc />
    public async Task<ReconciliationExceptionLifecycleResult> AssignAsync(
        AssignReconciliationExceptionCommand command,
        string newStatus,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH target_exception AS (
                SELECT *
                FROM reconciliation.reconciliation_exceptions
                WHERE reconciliation_exception_id = @reconciliation_exception_id
            ),
            updated_exception AS (
                UPDATE reconciliation.reconciliation_exceptions re
                   SET assigned_to_user_id = @assigned_to_user_id,
                       assigned_to_service_identity_id = @assigned_to_service_identity_id,
                       assigned_at = now(),
                       exception_status = @new_status::reconciliation.reconciliation_exception_status_enum,
                       updated_at = now(),
                       updated_by_user_id = @actor_user_id,
                       updated_by_service_identity_id = @service_identity_id,
                       correlation_id = @correlation_id,
                       row_version = row_version + 1
                FROM target_exception te
                WHERE re.reconciliation_exception_id = te.reconciliation_exception_id
                RETURNING
                    re.reconciliation_exception_id,
                    re.reconciliation_run_id,
                    re.reconciliation_item_id,
                    te.exception_status::text AS previous_status,
                    re.exception_status::text AS current_status,
                    re.updated_at,
                    re.correlation_id
            ),
            inserted_history AS (
                INSERT INTO reconciliation.reconciliation_exception_status_history (
                    reconciliation_exception_id,
                    reconciliation_run_id,
                    reconciliation_item_id,
                    previous_exception_status,
                    new_exception_status,
                    reason_code,
                    transition_summary,
                    transition_detail,
                    changed_at,
                    changed_by_user_id,
                    changed_by_service_identity_id,
                    correlation_id
                )
                SELECT
                    ue.reconciliation_exception_id,
                    ue.reconciliation_run_id,
                    ue.reconciliation_item_id,
                    ue.previous_status::reconciliation.reconciliation_exception_status_enum,
                    ue.current_status::reconciliation.reconciliation_exception_status_enum,
                    @reason_code,
                    'Reconciliation exception assigned',
                    @detail,
                    now(),
                    @actor_user_id,
                    @service_identity_id,
                    @correlation_id
                FROM updated_exception ue
                WHERE ue.previous_status <> ue.current_status
                RETURNING reconciliation_exception_status_history_id
            )
            SELECT
                reconciliation_exception_id,
                previous_status,
                current_status,
                updated_at,
                correlation_id
            FROM updated_exception;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("reconciliation_exception_id", command.ReconciliationExceptionId);
        dbCommand.Parameters.AddWithValue("assigned_to_user_id", (object?)command.AssignedToUserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("assigned_to_service_identity_id", (object?)command.AssignedToServiceIdentityId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("new_status", newStatus);
        AddLifecycleParameters(dbCommand, command.ReasonCode, command.Detail, command.ActorUserId, command.ServiceIdentityId, command.CorrelationId);

        return await ExecuteLifecycleAsync(
            dbCommand,
            command.ReconciliationExceptionId,
            "ASSIGN",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReconciliationExceptionLifecycleResult> UpdateStatusAsync(
        UpdateReconciliationExceptionStatusCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH target_exception AS (
                SELECT *
                FROM reconciliation.reconciliation_exceptions
                WHERE reconciliation_exception_id = @reconciliation_exception_id
            ),
            updated_exception AS (
                UPDATE reconciliation.reconciliation_exceptions re
                   SET exception_status = @new_status::reconciliation.reconciliation_exception_status_enum,
                       resolved_at = CASE WHEN @new_status = 'RESOLVED' THEN now() ELSE re.resolved_at END,
                       resolved_by_user_id = CASE WHEN @new_status = 'RESOLVED' THEN @actor_user_id ELSE re.resolved_by_user_id END,
                       resolved_by_service_identity_id = CASE WHEN @new_status = 'RESOLVED' THEN @service_identity_id ELSE re.resolved_by_service_identity_id END,
                       resolution_reason_code = CASE WHEN @new_status = 'RESOLVED' THEN @reason_code ELSE re.resolution_reason_code END,
                       closed_at = CASE WHEN @new_status = 'CLOSED' THEN now() ELSE re.closed_at END,
                       closed_by_user_id = CASE WHEN @new_status = 'CLOSED' THEN @actor_user_id ELSE re.closed_by_user_id END,
                       closed_by_service_identity_id = CASE WHEN @new_status = 'CLOSED' THEN @service_identity_id ELSE re.closed_by_service_identity_id END,
                       closure_reason_code = CASE WHEN @new_status = 'CLOSED' THEN @reason_code ELSE re.closure_reason_code END,
                       updated_at = now(),
                       updated_by_user_id = @actor_user_id,
                       updated_by_service_identity_id = @service_identity_id,
                       correlation_id = @correlation_id,
                       row_version = row_version + 1
                FROM target_exception te
                WHERE re.reconciliation_exception_id = te.reconciliation_exception_id
                RETURNING
                    re.reconciliation_exception_id,
                    re.reconciliation_run_id,
                    re.reconciliation_item_id,
                    te.exception_status::text AS previous_status,
                    re.exception_status::text AS current_status,
                    re.updated_at,
                    re.correlation_id
            ),
            inserted_history AS (
                INSERT INTO reconciliation.reconciliation_exception_status_history (
                    reconciliation_exception_id,
                    reconciliation_run_id,
                    reconciliation_item_id,
                    previous_exception_status,
                    new_exception_status,
                    reason_code,
                    transition_summary,
                    transition_detail,
                    changed_at,
                    changed_by_user_id,
                    changed_by_service_identity_id,
                    correlation_id
                )
                SELECT
                    ue.reconciliation_exception_id,
                    ue.reconciliation_run_id,
                    ue.reconciliation_item_id,
                    ue.previous_status::reconciliation.reconciliation_exception_status_enum,
                    ue.current_status::reconciliation.reconciliation_exception_status_enum,
                    @reason_code,
                    @transition_summary,
                    @detail,
                    now(),
                    @actor_user_id,
                    @service_identity_id,
                    @correlation_id
                FROM updated_exception ue
                WHERE ue.previous_status <> ue.current_status
                RETURNING reconciliation_exception_status_history_id
            )
            SELECT
                reconciliation_exception_id,
                previous_status,
                current_status,
                updated_at,
                correlation_id
            FROM updated_exception;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var dbCommand = new NpgsqlCommand(sql, connection);
        dbCommand.Parameters.AddWithValue("reconciliation_exception_id", command.ReconciliationExceptionId);
        dbCommand.Parameters.AddWithValue("new_status", command.NewStatus);
        dbCommand.Parameters.AddWithValue("transition_summary", TransitionSummary(command.Action, command.NewStatus));
        AddLifecycleParameters(dbCommand, command.ReasonCode, command.Detail, command.ActorUserId, command.ServiceIdentityId, command.CorrelationId);

        return await ExecuteLifecycleAsync(
            dbCommand,
            command.ReconciliationExceptionId,
            command.Action,
            cancellationToken);
    }

    private async Task<ReconciliationExceptionLifecycleResult> ExecuteLifecycleAsync(
        NpgsqlCommand dbCommand,
        Guid reconciliationExceptionId,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new ReconciliationExceptionNotFoundException(reconciliationExceptionId);
            }

            return new ReconciliationExceptionLifecycleResult(
                reader.GetGuid("reconciliation_exception_id"),
                reader.GetString("previous_status"),
                reader.GetString("current_status"),
                action,
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
                reader.GetGuid("correlation_id"));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            _logger.LogWarning(ex, "Reconciliation exception lifecycle reference validation failed. constraint={ConstraintName}", ex.ConstraintName);
            throw new ReconciliationRunItemRejectedException(
                MapForeignKeyErrorCode(ex.ConstraintName),
                "One or more supplied reconciliation exception lifecycle references do not exist.");
        }
    }

    private static string ExceptionDetailSql(string whereClause) =>
        $"""
        SELECT
            re.reconciliation_exception_id,
            re.reconciliation_run_id,
            re.reconciliation_item_id,
            re.incident_record_id,
            re.exception_type::text AS exception_type,
            re.exception_severity::text AS exception_severity,
            re.exception_status::text AS exception_status,
            re.exception_reason_code,
            re.exception_summary,
            re.exception_detail,
            re.assigned_to_user_id,
            re.assigned_to_service_identity_id,
            re.created_from_status,
            re.detected_at,
            re.assigned_at,
            re.resolved_at,
            re.closed_at,
            re.resolution_reason_code,
            re.closure_reason_code,
            re.resolved_by_user_id,
            re.resolved_by_service_identity_id,
            re.closed_by_user_id,
            re.closed_by_service_identity_id,
            re.created_at,
            re.updated_at,
            re.correlation_id
        FROM reconciliation.reconciliation_exceptions re
        {whereClause};
        """;

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddLifecycleParameters(
        NpgsqlCommand dbCommand,
        string reasonCode,
        string? detail,
        Guid? actorUserId,
        Guid? serviceIdentityId,
        Guid correlationId)
    {
        dbCommand.Parameters.AddWithValue("reason_code", reasonCode);
        dbCommand.Parameters.AddWithValue("detail", (object?)detail ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("actor_user_id", (object?)actorUserId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("service_identity_id", (object?)serviceIdentityId ?? DBNull.Value);
        dbCommand.Parameters.AddWithValue("correlation_id", correlationId);
    }

    private static ReconciliationExceptionDetailRecord ReadException(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid("reconciliation_exception_id"),
            reader.GetGuid("reconciliation_run_id"),
            reader.GetNullableGuid("reconciliation_item_id"),
            reader.GetNullableGuid("incident_record_id"),
            reader.GetString("exception_type"),
            reader.GetString("exception_severity"),
            reader.GetString("exception_status"),
            reader.GetString("exception_reason_code"),
            reader.GetString("exception_summary"),
            reader.GetNullableString("exception_detail"),
            reader.GetNullableGuid("assigned_to_user_id"),
            reader.GetNullableGuid("assigned_to_service_identity_id"),
            reader.GetNullableString("created_from_status"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("detected_at")),
            reader.GetNullableDateTimeOffset("assigned_at"),
            reader.GetNullableDateTimeOffset("resolved_at"),
            reader.GetNullableDateTimeOffset("closed_at"),
            reader.GetNullableString("resolution_reason_code"),
            reader.GetNullableString("closure_reason_code"),
            reader.GetNullableGuid("resolved_by_user_id"),
            reader.GetNullableGuid("resolved_by_service_identity_id"),
            reader.GetNullableGuid("closed_by_user_id"),
            reader.GetNullableGuid("closed_by_service_identity_id"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")),
            reader.GetNullableGuid("correlation_id"));

    private static string TransitionSummary(string action, string status) =>
        action.ToUpperInvariant() switch
        {
            "RESOLVE" => "Reconciliation exception resolved",
            "REJECT" => "Reconciliation exception rejected",
            "ESCALATE" => "Reconciliation exception escalated",
            "CLOSE" => "Reconciliation exception closed",
            _ => $"Reconciliation exception status changed to {status}"
        };

    private static string MapForeignKeyErrorCode(string? constraintName) =>
        constraintName switch
        {
            "fk_reconciliation_exceptions__assigned_to_user_id" or
            "fk_reconciliation_exceptions__resolved_by_user_id" or
            "fk_reconciliation_exceptions__closed_by_user_id" or
            "fk_reconciliation_exceptions__updated_by_user_id" => "USER_NOT_FOUND",
            "fk_reconciliation_exceptions__assigned_to_service_identity_i" or
            "fk_reconciliation_exceptions__resolved_by_service_identity_i" or
            "fk_reconciliation_exceptions__closed_by_service_identity_id" or
            "fk_reconciliation_exceptions__updated_by_service_identity_id" => "SERVICE_IDENTITY_NOT_FOUND",
            _ => "RECONCILIATION_EXCEPTION_REFERENCE_NOT_FOUND"
        };
}
