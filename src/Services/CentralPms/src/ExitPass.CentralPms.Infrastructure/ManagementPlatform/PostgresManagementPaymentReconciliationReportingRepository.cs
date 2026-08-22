using System.Data;
using ExitPass.CentralPms.Application.ManagementPlatform;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class PostgresManagementPaymentReconciliationReportingRepository
    : IManagementPaymentReconciliationReportingRepository
{
    private readonly string _connectionString;

    public PostgresManagementPaymentReconciliationReportingRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<ManagementDashboardActorValidationStatus> ValidateActorAsync(
        ManagementDashboardActor actor,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.human_sessions session
                JOIN identity.users user_record ON user_record.user_id = session.user_id
                WHERE session.human_session_id = @human_session_id
                  AND session.user_id = @actor_user_id
                  AND session.session_audience = 'MANAGEMENT_PLATFORM'
                  AND session.session_status = 'ACTIVE'
                  AND session.idle_expires_at > now()
                  AND session.absolute_expires_at > now()
                  AND session.authorization_epoch_snapshot = user_record.authorization_epoch
                  AND session.credential_version_snapshot = user_record.credential_version
                  AND user_record.user_status = 'ACTIVE'
                  AND user_record.effective_from <= now()
                  AND (user_record.effective_to IS NULL OR user_record.effective_to > now())
            );
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("human_session_id", actor.HumanSessionId);
            command.Parameters.AddWithValue("actor_user_id", actor.UserId);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false)
                ? ManagementDashboardActorValidationStatus.Valid
                : ManagementDashboardActorValidationStatus.Invalid;
        }
        catch (NpgsqlException)
        {
            return ManagementDashboardActorValidationStatus.SourceUnavailable;
        }
    }

    public async Task<ManagementDashboardScopeReadResult> ResolveScopeAsync(
        ManagementDashboardActor actor,
        string scopeType,
        Guid scopeReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH effective_reporting_roles AS (
                SELECT DISTINCT ur.user_role_id
                FROM identity.users u
                JOIN identity.user_roles ur ON ur.user_id = u.user_id
                JOIN identity.roles r ON r.role_id = ur.role_id
                JOIN identity.role_permissions rp ON rp.role_id = r.role_id
                JOIN identity.permissions p ON p.permission_id = rp.permission_id
                WHERE u.user_id = @actor_user_id
                  AND u.user_status = 'ACTIVE'
                  AND u.effective_from <= now()
                  AND (u.effective_to IS NULL OR u.effective_to > now())
                  AND ur.assignment_status = 'ACTIVE'
                  AND ur.effective_from <= now()
                  AND (ur.effective_to IS NULL OR ur.effective_to > now())
                  AND ur.revoked_at IS NULL
                  AND r.role_status = 'ACTIVE'
                  AND r.effective_from <= now()
                  AND (r.effective_to IS NULL OR r.effective_to > now())
                  AND rp.binding_status = 'ACTIVE'
                  AND rp.effective_from <= now()
                  AND (rp.effective_to IS NULL OR rp.effective_to > now())
                  AND rp.revoked_at IS NULL
                  AND p.permission_status = 'ACTIVE'
                  AND p.permission_code = @permission_code
            ), effective_grants AS (
                SELECT g.scope_type::text AS scope_type, g.site_id, g.site_group_id
                FROM effective_reporting_roles role_assignment
                JOIN identity.user_role_scope_grants g ON g.user_role_id = role_assignment.user_role_id
                WHERE g.grant_status = 'ACTIVE'
                  AND g.effective_from <= now()
                  AND (g.effective_to IS NULL OR g.effective_to > now())
                  AND g.revoked_at IS NULL
                  AND g.scope_type IN ('SITE', 'SITE_GROUP')
            ), requested_scope AS (
                SELECT
                    'SITE'::text AS scope_type,
                    s.site_id AS scope_reference,
                    s.site_name AS scope_name,
                    s.site_group_id,
                    s.updated_at AS scope_updated_at
                FROM sites.sites s
                WHERE @scope_type = 'SITE' AND s.site_id = @scope_reference

                UNION ALL

                SELECT
                    'SITE_GROUP'::text,
                    sg.site_group_id,
                    sg.site_group_name,
                    sg.site_group_id,
                    sg.updated_at
                FROM sites.site_groups sg
                WHERE @scope_type = 'SITE_GROUP' AND sg.site_group_id = @scope_reference
            ), authorized_scope AS (
                SELECT requested.*
                FROM requested_scope requested
                WHERE EXISTS (
                    SELECT 1
                    FROM effective_grants grant_scope
                    WHERE (requested.scope_type = 'SITE'
                           AND ((grant_scope.scope_type = 'SITE' AND grant_scope.site_id = requested.scope_reference)
                                OR (grant_scope.scope_type = 'SITE_GROUP' AND grant_scope.site_group_id = requested.site_group_id)))
                       OR (requested.scope_type = 'SITE_GROUP'
                           AND grant_scope.scope_type = 'SITE_GROUP'
                           AND grant_scope.site_group_id = requested.scope_reference)
                )
            )
            SELECT
                authorized.scope_type,
                authorized.scope_reference,
                authorized.scope_name,
                authorized.scope_updated_at,
                site.site_id,
                site.site_status::text AS site_status,
                site.payment_enabled,
                site.updated_at AS site_updated_at
            FROM authorized_scope authorized
            LEFT JOIN sites.sites site
              ON (authorized.scope_type = 'SITE' AND site.site_id = authorized.scope_reference)
              OR (authorized.scope_type = 'SITE_GROUP' AND site.site_group_id = authorized.scope_reference)
            ORDER BY site.site_name, site.site_id;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("actor_user_id", actor.UserId);
            command.Parameters.AddWithValue("permission_code", ManagementPaymentReconciliationReportingValues.Permission);
            command.Parameters.AddWithValue("scope_type", scopeType);
            command.Parameters.AddWithValue("scope_reference", scopeReference);

            string? resolvedType = null;
            Guid resolvedReference = Guid.Empty;
            string? displayName = null;
            DateTimeOffset scopeUpdatedAt = default;
            var sites = new List<ManagementDashboardSiteSnapshot>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                resolvedType ??= reader.GetString(reader.GetOrdinal("scope_type"));
                resolvedReference = reader.GetGuid(reader.GetOrdinal("scope_reference"));
                displayName ??= reader.GetString(reader.GetOrdinal("scope_name"));
                scopeUpdatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("scope_updated_at"));
                if (!reader.IsDBNull(reader.GetOrdinal("site_id")))
                {
                    sites.Add(new ManagementDashboardSiteSnapshot(
                        reader.GetGuid(reader.GetOrdinal("site_id")),
                        reader.GetString(reader.GetOrdinal("site_status")),
                        reader.GetBoolean(reader.GetOrdinal("payment_enabled")),
                        reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("site_updated_at"))));
                }
            }

            if (resolvedType is null || displayName is null)
            {
                return new ManagementDashboardScopeReadResult(ManagementDashboardScopeReadStatus.Denied, null);
            }

            var dataAsOf = sites.Select(site => site.UpdatedAt).Append(scopeUpdatedAt).Max();
            return new ManagementDashboardScopeReadResult(
                ManagementDashboardScopeReadStatus.Resolved,
                new ManagementDashboardScopeSnapshot(
                    resolvedType,
                    resolvedReference,
                    displayName,
                    dataAsOf,
                    sites));
        }
        catch (NpgsqlException)
        {
            return new ManagementDashboardScopeReadResult(ManagementDashboardScopeReadStatus.SourceUnavailable, null);
        }
    }

    public async Task<ManagementPaymentReconciliationReadResult> ReadSummaryAsync(
        ManagementDashboardScopeSnapshot scope,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        if (scope.Sites.Count == 0)
        {
            return ResolvedEmpty();
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY;", connection, transaction))
            {
                await readOnly.ExecuteNonQueryAsync(cancellationToken);
            }

            var siteIds = scope.Sites.Select(site => site.SiteId).Distinct().ToArray();
            var attempts = await ReadAggregatesAsync(connection, transaction, AttemptSql, siteIds, periodStart, periodEnd, cancellationToken);
            var confirmations = await ReadAggregatesAsync(connection, transaction, ConfirmationSql, siteIds, periodStart, periodEnd, cancellationToken);
            var outcomes = await ReadAggregatesAsync(connection, transaction, ProviderOutcomeSql, siteIds, periodStart, periodEnd, cancellationToken);
            var conditions = await ReadConditionsAsync(connection, transaction, siteIds, periodStart, periodEnd, cancellationToken);
            var dataAsOf = await ReadDataAsOfAsync(connection, transaction, siteIds, periodStart, periodEnd, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ManagementPaymentReconciliationReadResult(
                ManagementPaymentReconciliationReadStatus.Resolved,
                new ManagementPaymentReconciliationSourceSnapshot(attempts, confirmations, outcomes, conditions, dataAsOf));
        }
        catch (NpgsqlException)
        {
            return new ManagementPaymentReconciliationReadResult(ManagementPaymentReconciliationReadStatus.Unavailable, null);
        }
    }

    public async Task RecordAuditAsync(ManagementPaymentReconciliationAuditRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit.audit_events (
                audit_event_id, event_type, event_category, event_result, event_reason_code,
                target_entity_type, target_entity_id, related_entity_type, related_entity_id,
                source_schema, source_service_name, source_channel, actor_user_id, summary,
                occurred_at, recorded_at, correlation_id, created_at)
            VALUES (
                gen_random_uuid(), @event_type, 'SECURITY_RELEVANT', @event_result::audit.audit_event_result_enum,
                @reason_code, 'ManagementPaymentReconciliationReport', @scope_reference,
                'HumanSession', @human_session_id, 'reporting', 'central-pms', 'MANAGEMENT_PLATFORM',
                @actor_user_id, @summary, @occurred_at, now(), @correlation_id, now());
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("event_type", record.EventType);
            command.Parameters.AddWithValue("event_result", record.Result);
            command.Parameters.AddWithValue("reason_code", record.ReasonCode);
            command.Parameters.Add("scope_reference", NpgsqlDbType.Uuid).Value = (object?)record.ScopeReference ?? DBNull.Value;
            command.Parameters.AddWithValue("human_session_id", record.HumanSessionId);
            command.Parameters.AddWithValue("actor_user_id", record.ActorUserId);
            command.Parameters.AddWithValue("summary", BuildAuditSummary(record));
            command.Parameters.AddWithValue("occurred_at", record.OccurredAt);
            command.Parameters.AddWithValue("correlation_id", record.CorrelationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            throw new ManagementDashboardSourceUnavailableException("The payment reporting audit source is unavailable.", ex);
        }
    }

    private static string BuildAuditSummary(ManagementPaymentReconciliationAuditRecord record)
    {
        var period = record.PeriodStart is not null && record.PeriodEnd is not null
            ? $"; period [{record.PeriodStart:O}, {record.PeriodEnd:O})"
            : string.Empty;
        return $"Report {ManagementPaymentReconciliationReportingValues.ReportId}; scope {record.ScopeType ?? "NONE"}; result {record.ResultClassification}{period}.";
    }

    private static async Task<IReadOnlyList<ManagementPaymentAggregateRecord>> ReadAggregatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid[] siteIds,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddReportParameters(command, siteIds, periodStart, periodEnd);
        var rows = new List<ManagementPaymentAggregateRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ManagementPaymentAggregateRecord(
                reader.GetString(reader.GetOrdinal("currency_code")).Trim(),
                reader.GetString(reader.GetOrdinal("status")),
                reader.GetString(reader.GetOrdinal("channel_code")),
                reader.GetString(reader.GetOrdinal("channel_type")),
                reader.GetString(reader.GetOrdinal("provider_code")),
                reader.GetInt64(reader.GetOrdinal("record_count")),
                reader.GetDecimal(reader.GetOrdinal("amount"))));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<ManagementPaymentReconciliationConditionRecord>> ReadConditionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid[] siteIds,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReconciliationSql, connection, transaction);
        AddReportParameters(command, siteIds, periodStart, periodEnd);
        var rows = new List<ManagementPaymentReconciliationConditionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ManagementPaymentReconciliationConditionRecord(
                reader.GetString(reader.GetOrdinal("category_id")),
                reader.IsDBNull(reader.GetOrdinal("currency_code"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("currency_code")).Trim(),
                reader.GetInt64(reader.GetOrdinal("record_count")),
                reader.IsDBNull(reader.GetOrdinal("amount"))
                    ? null
                    : reader.GetDecimal(reader.GetOrdinal("amount"))));
        }

        return rows;
    }

    private static async Task<DateTimeOffset?> ReadDataAsOfAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid[] siteIds,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(DataAsOfSql, connection, transaction);
        AddReportParameters(command, siteIds, periodStart, periodEnd);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result switch
        {
            null or DBNull => null,
            DateTimeOffset timestamp => timestamp,
            DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("The payment reporting source returned an unsupported timestamp value.")
        };
    }

    private static void AddReportParameters(
        NpgsqlCommand command,
        Guid[] siteIds,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        command.Parameters.Add("site_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = siteIds;
        command.Parameters.Add("period_start", NpgsqlDbType.TimestampTz).Value = periodStart;
        command.Parameters.Add("period_end", NpgsqlDbType.TimestampTz).Value = periodEnd;
    }

    private static ManagementPaymentReconciliationReadResult ResolvedEmpty() =>
        new(
            ManagementPaymentReconciliationReadStatus.Resolved,
            new ManagementPaymentReconciliationSourceSnapshot([], [], [], [], null));

    private const string AttemptSql = """
        SELECT
            pa.currency_code,
            pa.attempt_status::text AS status,
            COALESCE(rail.rail_code, 'UNASSIGNED') AS channel_code,
            CASE WHEN rail.rail_code = 'CASH' THEN 'CASH' ELSE COALESCE(rail.rail_type::text, 'UNKNOWN') END AS channel_type,
            COALESCE(rail.provider_code, 'UNAVAILABLE') AS provider_code,
            count(*)::bigint AS record_count,
            sum(pa.amount)::numeric AS amount
        FROM core.payment_attempts pa
        JOIN core.parking_sessions session ON session.parking_session_id = pa.parking_session_id
        LEFT JOIN payments.payment_rails rail ON rail.payment_rail_id = pa.payment_rail_id
        WHERE session.site_id = ANY(@site_ids)
          AND pa.requested_at >= @period_start
          AND pa.requested_at < @period_end
        GROUP BY pa.currency_code, pa.attempt_status, channel_code, channel_type, provider_code
        ORDER BY pa.currency_code, pa.attempt_status, channel_code;
        """;

    private const string ConfirmationSql = """
        SELECT
            confirmation.currency_code,
            confirmation.confirmation_status::text AS status,
            COALESCE(rail.rail_code, 'UNASSIGNED') AS channel_code,
            CASE WHEN rail.rail_code = 'CASH' THEN 'CASH' ELSE COALESCE(rail.rail_type::text, 'UNKNOWN') END AS channel_type,
            COALESCE(rail.provider_code, 'UNAVAILABLE') AS provider_code,
            count(*)::bigint AS record_count,
            sum(confirmation.confirmed_amount)::numeric AS amount
        FROM core.payment_confirmations confirmation
        JOIN core.payment_attempts attempt ON attempt.payment_attempt_id = confirmation.payment_attempt_id
        JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
        LEFT JOIN payments.payment_rails rail
          ON rail.payment_rail_id = COALESCE(confirmation.payment_rail_id, attempt.payment_rail_id)
        WHERE session.site_id = ANY(@site_ids)
          AND confirmation.confirmed_at >= @period_start
          AND confirmation.confirmed_at < @period_end
        GROUP BY confirmation.currency_code, confirmation.confirmation_status, channel_code, channel_type, provider_code
        ORDER BY confirmation.currency_code, confirmation.confirmation_status, channel_code;
        """;

    private const string ProviderOutcomeSql = """
        SELECT
            outcome.currency_code,
            outcome.provider_outcome_status::text AS status,
            rail.rail_code AS channel_code,
            CASE WHEN rail.rail_code = 'CASH' THEN 'CASH' ELSE rail.rail_type::text END AS channel_type,
            rail.provider_code,
            count(*)::bigint AS record_count,
            sum(outcome.amount)::numeric AS amount
        FROM payments.provider_outcomes outcome
        JOIN core.payment_attempts attempt ON attempt.payment_attempt_id = outcome.payment_attempt_id
        JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
        JOIN payments.payment_rails rail ON rail.payment_rail_id = outcome.payment_rail_id
        WHERE session.site_id = ANY(@site_ids)
          AND outcome.verified_at >= @period_start
          AND outcome.verified_at < @period_end
        GROUP BY outcome.currency_code, outcome.provider_outcome_status, rail.rail_code, rail.rail_type, rail.provider_code
        ORDER BY outcome.currency_code, outcome.provider_outcome_status, rail.rail_code;
        """;

    private const string ReconciliationSql = """
        SELECT
            'ATTEMPT_CONFIRMATION_AMOUNT_MISMATCH'::text AS category_id,
            confirmation.currency_code,
            count(*)::bigint AS record_count,
            sum(abs(confirmation.confirmed_amount - attempt.amount))::numeric AS amount
        FROM core.payment_confirmations confirmation
        JOIN core.payment_attempts attempt ON attempt.payment_attempt_id = confirmation.payment_attempt_id
        JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
        WHERE session.site_id = ANY(@site_ids)
          AND confirmation.confirmed_at >= @period_start
          AND confirmation.confirmed_at < @period_end
          AND confirmation.confirmation_status = 'RECORDED'
          AND confirmation.currency_code = attempt.currency_code
          AND confirmation.confirmed_amount <> attempt.amount
        GROUP BY confirmation.currency_code

        UNION ALL

        SELECT
            'ATTEMPT_CONFIRMATION_CURRENCY_MISMATCH',
            NULL::char(3),
            count(*)::bigint,
            NULL::numeric
        FROM core.payment_confirmations confirmation
        JOIN core.payment_attempts attempt ON attempt.payment_attempt_id = confirmation.payment_attempt_id
        JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
        WHERE session.site_id = ANY(@site_ids)
          AND confirmation.confirmed_at >= @period_start
          AND confirmation.confirmed_at < @period_end
          AND confirmation.confirmation_status = 'RECORDED'
          AND confirmation.currency_code <> attempt.currency_code
        HAVING count(*) > 0

        UNION ALL

        SELECT
            'DUPLICATE_AUTHORITATIVE_PROVIDER_REFERENCE',
            NULL::char(3),
            sum(duplicate.reference_count)::bigint,
            NULL::numeric
        FROM (
            SELECT rail.provider_code, confirmation.provider_transaction_ref, count(*)::bigint AS reference_count
            FROM core.payment_confirmations confirmation
            JOIN core.payment_attempts attempt ON attempt.payment_attempt_id = confirmation.payment_attempt_id
            JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
            LEFT JOIN payments.payment_rails rail
              ON rail.payment_rail_id = COALESCE(confirmation.payment_rail_id, attempt.payment_rail_id)
            WHERE session.site_id = ANY(@site_ids)
              AND confirmation.confirmed_at >= @period_start
              AND confirmation.confirmed_at < @period_end
              AND confirmation.confirmation_status = 'RECORDED'
              AND NULLIF(btrim(confirmation.provider_transaction_ref), '') IS NOT NULL
              AND rail.provider_code IS NOT NULL
            GROUP BY rail.provider_code, confirmation.provider_transaction_ref
            HAVING count(*) > 1
        ) duplicate
        HAVING count(*) > 0

        UNION ALL

        SELECT
            'CONFIRMED_OUTCOME_WITHOUT_CONFIRMATION',
            outcome.currency_code,
            count(*)::bigint,
            sum(outcome.amount)::numeric
        FROM payments.provider_outcomes outcome
        JOIN core.payment_attempts attempt ON attempt.payment_attempt_id = outcome.payment_attempt_id
        JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
        LEFT JOIN core.payment_confirmations confirmation ON confirmation.provider_outcome_id = outcome.provider_outcome_id
        WHERE session.site_id = ANY(@site_ids)
          AND outcome.verified_at >= @period_start
          AND outcome.verified_at < @period_end
          AND outcome.provider_outcome_status = 'CONFIRMED'
          AND confirmation.payment_confirmation_id IS NULL
        GROUP BY outcome.currency_code

        UNION ALL

        SELECT
            'CONFIRMATION_ATTEMPT_STATUS_INCONSISTENT',
            confirmation.currency_code,
            count(*)::bigint,
            sum(confirmation.confirmed_amount)::numeric
        FROM core.payment_confirmations confirmation
        JOIN core.payment_attempts attempt ON attempt.payment_attempt_id = confirmation.payment_attempt_id
        JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
        WHERE session.site_id = ANY(@site_ids)
          AND confirmation.confirmed_at >= @period_start
          AND confirmation.confirmed_at < @period_end
          AND confirmation.confirmation_status = 'RECORDED'
          AND attempt.attempt_status <> 'CONFIRMED'
        GROUP BY confirmation.currency_code;
        """;

    private const string DataAsOfSql = """
        SELECT max(source_timestamp)
        FROM (
            SELECT max(attempt.updated_at) AS source_timestamp
            FROM core.payment_attempts attempt
            JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
            WHERE session.site_id = ANY(@site_ids)
              AND attempt.requested_at >= @period_start
              AND attempt.requested_at < @period_end

            UNION ALL

            SELECT max(confirmation.created_at)
            FROM core.payment_confirmations confirmation
            JOIN core.payment_attempts attempt ON attempt.payment_attempt_id = confirmation.payment_attempt_id
            JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
            WHERE session.site_id = ANY(@site_ids)
              AND confirmation.confirmed_at >= @period_start
              AND confirmation.confirmed_at < @period_end

            UNION ALL

            SELECT max(outcome.updated_at)
            FROM payments.provider_outcomes outcome
            JOIN core.payment_attempts attempt ON attempt.payment_attempt_id = outcome.payment_attempt_id
            JOIN core.parking_sessions session ON session.parking_session_id = attempt.parking_session_id
            WHERE session.site_id = ANY(@site_ids)
              AND outcome.verified_at >= @period_start
              AND outcome.verified_at < @period_end
        ) source;
        """;
}
