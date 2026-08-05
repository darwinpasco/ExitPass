using ExitPass.CentralPms.Application.StatutoryEvidence;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.StatutoryEvidence;

public sealed class StatutoryEvidenceScanRepository : IStatutoryEvidenceScanRepository
{
    private readonly string _connectionString;
    private readonly Func<CancellationToken, Task>? _beforeCompletionCommitForTests;

    public StatutoryEvidenceScanRepository(
        string connectionString,
        Func<CancellationToken, Task>? beforeCompletionCommitForTests = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _beforeCompletionCommitForTests = beforeCompletionCommitForTests;
    }

    public async Task<IReadOnlyList<StatutoryEvidenceScanWorkItem>> ClaimDueWorkAsync(
        string workerId,
        Guid? workerServiceIdentityId,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH due AS (
                SELECT attempt.statutory_evidence_scan_attempt_id
                FROM discounts.statutory_evidence_scan_attempts attempt
                JOIN discounts.statutory_evidence_items item
                  ON item.statutory_evidence_item_id = attempt.statutory_evidence_item_id
                JOIN discounts.statutory_evidence_upload_authorizations upload_authorization
                  ON upload_authorization.statutory_evidence_upload_authorization_id = attempt.statutory_evidence_upload_authorization_id
                WHERE (
                        attempt.attempt_status = 'PENDING'
                        OR (attempt.attempt_status = 'RETRY_PENDING' AND attempt.next_retry_at <= @now)
                        OR (attempt.attempt_status IN ('CLAIMED', 'IN_PROGRESS') AND attempt.lease_expires_at <= @now)
                      )
                  AND item.upload_status = 'UPLOADED'
                  AND item.deletion_status <> 'DELETED'
                  AND upload_authorization.authorization_status = 'CONSUMED'
                ORDER BY attempt.next_retry_at NULLS FIRST, attempt.created_at
                LIMIT @limit
                FOR UPDATE SKIP LOCKED
            ),
            claimed AS (
                UPDATE discounts.statutory_evidence_scan_attempts attempt
                   SET attempt_status = 'CLAIMED',
                       claimed_by_worker_id = @worker_id,
                       claimed_by_service_identity_id = @worker_service_identity_id,
                       claimed_at = @now,
                       lease_expires_at = @lease_expires_at,
                       started_at = COALESCE(started_at, @now),
                       next_retry_at = NULL,
                       updated_at = @now,
                       row_version = row_version + 1
                  FROM due
                 WHERE attempt.statutory_evidence_scan_attempt_id = due.statutory_evidence_scan_attempt_id
                 RETURNING attempt.*
            )
            SELECT claimed.statutory_evidence_scan_attempt_id,
                   claimed.scan_attempt_reference,
                   claimed.scan_work_identity,
                   claimed.statutory_evidence_set_id,
                   claimed.statutory_evidence_item_id,
                   claimed.statutory_evidence_upload_authorization_id,
                   claimed.attempt_number,
                   claimed.retry_count,
                   claimed.max_attempts,
                   evidence_set.site_id,
                   evidence_set.site_group_id,
                   evidence_set.parking_session_id,
                   evidence_set.source_channel,
                   claimed.correlation_id,
                   upload_authorization.bucket_reference,
                   upload_authorization.internal_object_key,
                   upload_authorization.expected_content_type,
                   upload_authorization.expected_content_length,
                   upload_authorization.expected_checksum_sha256,
                   upload_authorization.provider_object_version,
                   claimed.expected_item_row_version,
                   claimed.expected_upload_authorization_row_version
            FROM claimed
            JOIN discounts.statutory_evidence_sets evidence_set
              ON evidence_set.statutory_evidence_set_id = claimed.statutory_evidence_set_id
            JOIN discounts.statutory_evidence_upload_authorizations upload_authorization
              ON upload_authorization.statutory_evidence_upload_authorization_id = claimed.statutory_evidence_upload_authorization_id;
            """,
            connection);
        command.Parameters.AddWithValue("worker_id", workerId);
        command.Parameters.Add("worker_service_identity_id", NpgsqlDbType.Uuid).Value = (object?)workerServiceIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("lease_expires_at", now.Add(leaseDuration));
        command.Parameters.AddWithValue("limit", Math.Clamp(batchSize, 1, 100));

        var items = new List<StatutoryEvidenceScanWorkItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new StatutoryEvidenceScanWorkItem(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                workerId,
                reader.GetString(14),
                reader.GetString(15),
                reader.GetString(16),
                reader.GetInt64(17),
                reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.GetInt64(20),
                reader.GetInt64(21),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetGuid(9),
                reader.GetGuid(10),
                reader.GetGuid(11),
                reader.GetString(12),
                reader.GetGuid(13)));
        }

        return items;
    }

    public async Task CompleteAttemptAsync(
        StatutoryEvidenceScanWorkItem workItem,
        StatutoryEvidenceScanCompletion completion,
        Guid? workerServiceIdentityId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var attemptUpdated = await UpdateAttemptAsync(connection, transaction, workItem, completion, workerServiceIdentityId, completedAt, null, cancellationToken);
        if (attemptUpdated == 0)
        {
            return;
        }
        var itemUpdated = await UpdateItemAsync(connection, transaction, workItem, completion, workerServiceIdentityId, completedAt, cancellationToken);
        if (itemUpdated == 0)
        {
            await MarkAttemptStaleAsync(connection, transaction, workItem, workerServiceIdentityId, completedAt, cancellationToken);
            await InsertScanEventAsync(connection, transaction, workItem, "STALE_OBJECT_ATTEMPT_REJECTED", "STALE_OBJECT_VERSION", workerServiceIdentityId, completedAt, cancellationToken);
        }
        else
        {
            await InsertScanEventAsync(connection, transaction, workItem, EventFor(completion), completion.SafeFailureClassification, workerServiceIdentityId, completedAt, cancellationToken);
        }

        if (_beforeCompletionCommitForTests is not null)
        {
            await _beforeCompletionCommitForTests(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ScheduleRetryAsync(
        StatutoryEvidenceScanWorkItem workItem,
        StatutoryEvidenceScanCompletion completion,
        Guid? workerServiceIdentityId,
        DateTimeOffset nextRetryAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var attemptUpdated = await UpdateAttemptAsync(connection, transaction, workItem, completion, workerServiceIdentityId, now, nextRetryAt, cancellationToken);
        if (attemptUpdated == 0)
        {
            return;
        }
        var itemUpdated = await UpdateItemAsync(connection, transaction, workItem, completion, workerServiceIdentityId, now, cancellationToken);
        if (itemUpdated == 0)
        {
            await MarkAttemptStaleAsync(connection, transaction, workItem, workerServiceIdentityId, now, cancellationToken);
            await InsertScanEventAsync(connection, transaction, workItem, "STALE_OBJECT_ATTEMPT_REJECTED", "STALE_OBJECT_VERSION", workerServiceIdentityId, now, cancellationToken);
        }
        else
        {
            await AdvanceExpectedItemVersionForRetryAsync(connection, transaction, workItem, cancellationToken);
            await InsertScanEventAsync(connection, transaction, workItem, "SCAN_RETRY_SCHEDULED", completion.SafeFailureClassification, workerServiceIdentityId, now, cancellationToken);
        }

        if (_beforeCompletionCommitForTests is not null)
        {
            await _beforeCompletionCommitForTests(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<int> UpdateAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryEvidenceScanWorkItem workItem,
        StatutoryEvidenceScanCompletion completion,
        Guid? workerServiceIdentityId,
        DateTimeOffset now,
        DateTimeOffset? nextRetryAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE discounts.statutory_evidence_scan_attempts
               SET attempt_status = @attempt_status::discounts.statutory_evidence_scan_attempt_status_enum,
                   validation_status = @validation_status::discounts.statutory_evidence_validation_status_enum,
                   validation_result = @validation_result::discounts.statutory_evidence_validation_result_enum,
                   malware_scan_status = @malware_scan_status::discounts.statutory_evidence_scan_status_enum,
                   malware_scan_result = @malware_scan_result::discounts.statutory_evidence_malware_scan_result_enum,
                   safe_failure_classification = @safe_failure,
                   claimed_by_service_identity_id = COALESCE(claimed_by_service_identity_id, @service_identity_id),
                   completed_at = CASE WHEN @terminal THEN @now ELSE NULL END,
                   next_retry_at = @next_retry_at,
                   retry_count = CASE WHEN @next_retry_at IS NULL THEN retry_count ELSE retry_count + 1 END,
                   retryable = @retryable,
                   terminal = @terminal,
                   updated_at = @now,
                   row_version = row_version + 1
             WHERE statutory_evidence_scan_attempt_id = @attempt_id
               AND attempt_status IN ('CLAIMED', 'IN_PROGRESS')
               AND claimed_by_worker_id = @worker_id
               AND (@service_identity_id IS NULL OR claimed_by_service_identity_id IS NULL OR claimed_by_service_identity_id = @service_identity_id);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("attempt_id", workItem.ScanAttemptId);
        command.Parameters.AddWithValue("worker_id", workItem.WorkerId);
        command.Parameters.AddWithValue("attempt_status", completion.AttemptStatus);
        command.Parameters.AddWithValue("validation_status", completion.ValidationStatus);
        command.Parameters.AddWithValue("validation_result", completion.ValidationResult);
        command.Parameters.AddWithValue("malware_scan_status", completion.MalwareScanStatus);
        command.Parameters.AddWithValue("malware_scan_result", completion.MalwareScanResult);
        command.Parameters.Add("safe_failure", NpgsqlDbType.Text).Value = (object?)completion.SafeFailureClassification ?? DBNull.Value;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)workerServiceIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("now", now);
        command.Parameters.Add("next_retry_at", NpgsqlDbType.TimestampTz).Value = (object?)nextRetryAt ?? DBNull.Value;
        command.Parameters.AddWithValue("retryable", completion.Retryable);
        command.Parameters.AddWithValue("terminal", completion.Terminal);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> UpdateItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryEvidenceScanWorkItem workItem,
        StatutoryEvidenceScanCompletion completion,
        Guid? workerServiceIdentityId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reviewability = completion.MalwareScanStatus == "CLEAN" && completion.ValidationStatus == "PASSED"
            ? "REVIEWABLE"
            : "NOT_REVIEWABLE";
        await using var command = new NpgsqlCommand(
            """
            UPDATE discounts.statutory_evidence_items
               SET validation_status = @validation_status::discounts.statutory_evidence_validation_status_enum,
                   scan_status = @scan_status::discounts.statutory_evidence_scan_status_enum,
                   reviewability_status = @reviewability_status::discounts.statutory_evidence_reviewability_status_enum,
                   validation_result_classification = @validation_result,
                   scan_result_classification = @scan_result,
                   reviewable_at = CASE WHEN @reviewability_status = 'REVIEWABLE' THEN @now ELSE reviewable_at END,
                   updated_at = @now,
                   updated_by_service_identity_id = @service_identity_id,
                   row_version = row_version + 1
             WHERE statutory_evidence_item_id = @item_id
               AND row_version = @expected_item_row_version
               AND upload_status = 'UPLOADED';
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("item_id", workItem.EvidenceItemId);
        command.Parameters.AddWithValue("validation_status", completion.ValidationStatus);
        command.Parameters.AddWithValue("scan_status", completion.MalwareScanStatus);
        command.Parameters.AddWithValue("reviewability_status", reviewability);
        command.Parameters.AddWithValue("validation_result", completion.ValidationResult);
        command.Parameters.AddWithValue("scan_result", completion.MalwareScanResult);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)workerServiceIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("expected_item_row_version", workItem.ExpectedItemRowVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AdvanceExpectedItemVersionForRetryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryEvidenceScanWorkItem workItem,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE discounts.statutory_evidence_scan_attempts
               SET expected_item_row_version = expected_item_row_version + 1
             WHERE statutory_evidence_scan_attempt_id = @attempt_id
               AND attempt_status = 'RETRY_PENDING';
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("attempt_id", workItem.ScanAttemptId);
        command.Parameters.AddWithValue("worker_id", workItem.WorkerId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkAttemptStaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryEvidenceScanWorkItem workItem,
        Guid? workerServiceIdentityId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE discounts.statutory_evidence_scan_attempts
               SET attempt_status = 'STALE_REJECTED',
                   validation_status = 'FAILED',
                   validation_result = 'STALE_OBJECT_VERSION',
                   malware_scan_status = 'ERROR_TERMINAL',
                   malware_scan_result = 'NOT_RUN',
                   safe_failure_classification = 'STALE_OBJECT_VERSION',
                   completed_at = @now,
                   next_retry_at = NULL,
                   retryable = false,
                   terminal = true,
                   claimed_by_service_identity_id = COALESCE(claimed_by_service_identity_id, @service_identity_id),
                   updated_at = @now,
                   row_version = row_version + 1
             WHERE statutory_evidence_scan_attempt_id = @attempt_id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("attempt_id", workItem.ScanAttemptId);
        command.Parameters.AddWithValue("worker_id", workItem.WorkerId);
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)workerServiceIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertScanEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryEvidenceScanWorkItem workItem,
        string eventType,
        string? reasonCode,
        Guid? workerServiceIdentityId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO discounts.statutory_evidence_events (
                event_type,
                event_result,
                statutory_evidence_set_id,
                statutory_evidence_item_id,
                safe_reason_code,
                source_channel,
                site_id,
                site_group_id,
                parking_session_id,
                actor_service_identity_id,
                correlation_id,
                occurred_at)
            VALUES (
                @event_type::discounts.statutory_evidence_event_type_enum,
                @event_result::discounts.statutory_evidence_event_result_enum,
                @set_id,
                @item_id,
                @reason_code,
                'CENTRAL_PMS',
                @site_id,
                @site_group_id,
                @parking_session_id,
                @service_identity_id,
                @correlation_id,
                @now);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("event_result", reasonCode is null ? "ACCEPTED" : "DENIED");
        command.Parameters.AddWithValue("set_id", workItem.EvidenceSetId);
        command.Parameters.AddWithValue("item_id", workItem.EvidenceItemId);
        command.Parameters.AddWithValue("reason_code", (object?)reasonCode ?? DBNull.Value);
        command.Parameters.AddWithValue("site_id", workItem.SiteId);
        command.Parameters.AddWithValue("site_group_id", workItem.SiteGroupId);
        command.Parameters.AddWithValue("parking_session_id", workItem.ParkingSessionId);
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)workerServiceIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("correlation_id", workItem.CorrelationId);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string EventFor(StatutoryEvidenceScanCompletion completion)
    {
        if (completion.MalwareScanStatus == "CLEAN")
        {
            return "SCAN_CLEAN";
        }

        return completion.MalwareScanStatus switch
        {
            "MALICIOUS" => "MALWARE_DETECTED",
            "SUSPICIOUS" => "SUSPICIOUS_RESULT",
            "ERROR_RETRYABLE" or "UNAVAILABLE" or "TIMEOUT" => "SCAN_PROVIDER_UNAVAILABLE",
            _ when completion.ValidationStatus == "PASSED" => "SCAN_RETRY_EXHAUSTED",
            _ => "VALIDATION_FAILED"
        };
    }
}
