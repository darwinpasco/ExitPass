using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

public sealed class OperatorConsoleStatutoryEvidenceReviewRepository
    : IOperatorConsoleStatutoryEvidenceReviewRepository
{
    private readonly string _connectionString;

    public OperatorConsoleStatutoryEvidenceReviewRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("A Central PMS database connection string is required.", nameof(connectionString));
    }

    public async Task<OperatorConsoleStatutoryEvidenceReviewRecord?> ReadAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string headerSql = """
            SELECT review.parking_session_id,
                   review.site_id,
                   review.site_group_id,
                   review.source_channel,
                   decision.decision_result_status,
                   review.review_status,
                   decision.evidence_required,
                   decision.evidence_recorded,
                   evidence_set.statutory_evidence_set_id,
                   evidence_set.evidence_set_reference,
                   evidence_set.set_status::text,
                   evidence_set.retention_status::text,
                   evidence_set.deletion_status::text,
                   evidence_set.hold_active,
                   evidence_set.row_version
            FROM operator_console.statutory_discount_service_channel_reviews AS review
            JOIN discounts.statutory_discount_decision_commands AS decision
              ON decision.statutory_discount_decision_command_id = review.statutory_discount_decision_command_id
            LEFT JOIN LATERAL (
                SELECT candidate.*
                FROM discounts.statutory_evidence_sets AS candidate
                WHERE candidate.statutory_discount_decision_command_id = review.statutory_discount_decision_command_id
                ORDER BY CASE WHEN candidate.set_status = 'TOMBSTONED' THEN 1 ELSE 0 END,
                         candidate.created_at DESC
                LIMIT 1
            ) AS evidence_set ON TRUE
            WHERE review.statutory_discount_decision_command_id = @decision_id;
            """;

        await using var command = new NpgsqlCommand(headerSql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("decision_id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ||
            reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
        {
            return null;
        }

        var record = new OperatorConsoleStatutoryEvidenceReviewRecord(
            statutoryDiscountDecisionCommandId,
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            !reader.IsDBNull(13) && reader.GetBoolean(13),
            reader.IsDBNull(14) ? null : reader.GetInt64(14),
            []);
        await reader.CloseAsync().ConfigureAwait(false);

        if (record.EvidenceSetId is null)
        {
            return record;
        }

        return record with
        {
            Items = await ReadItemsAsync(connection, record.EvidenceSetId.Value, cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<bool> IsCurrentPreviewTargetAsync(
        OperatorConsoleStatutoryEvidencePreviewTarget target,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM discounts.statutory_evidence_sets AS evidence_set
                JOIN discounts.statutory_evidence_items AS item
                  ON item.statutory_evidence_set_id = evidence_set.statutory_evidence_set_id
                JOIN discounts.statutory_evidence_upload_authorizations AS upload_authorization
                  ON upload_authorization.statutory_evidence_set_id = evidence_set.statutory_evidence_set_id
                 AND upload_authorization.statutory_evidence_item_id = item.statutory_evidence_item_id
                WHERE evidence_set.statutory_discount_decision_command_id = @decision_id
                  AND evidence_set.statutory_evidence_set_id = @set_id
                  AND evidence_set.evidence_set_reference = @set_reference
                  AND evidence_set.row_version = @set_row_version
                  AND evidence_set.site_id = @site_id
                  AND evidence_set.site_group_id = @site_group_id
                  AND evidence_set.parking_session_id = @parking_session_id
                  AND evidence_set.set_status <> 'TOMBSTONED'
                  AND evidence_set.retention_status IN ('ACTIVE', 'HELD')
                  AND evidence_set.deletion_status = 'NOT_REQUESTED'
                  AND item.statutory_evidence_item_id = @item_id
                  AND item.evidence_item_reference = @item_reference
                  AND item.row_version = @item_row_version
                  AND item.upload_status = 'UPLOADED'
                  AND item.validation_status = 'PASSED'
                  AND item.scan_status IN ('CLEAN', 'PASSED')
                  AND item.reviewability_status = 'REVIEWABLE'
                  AND item.binding_status NOT IN ('REJECTED', 'SUPERSEDED')
                  AND item.retention_status IN ('ACTIVE', 'HELD')
                  AND item.deletion_status = 'NOT_REQUESTED'
                  AND item.internal_storage_locator_ref = 'upload-authorization:' || @authorization_reference::text
                  AND item.internal_checksum_sha256 = @checksum
                  AND upload_authorization.statutory_evidence_upload_authorization_id = @authorization_id
                  AND upload_authorization.upload_authorization_reference = @authorization_reference
                  AND upload_authorization.row_version = @authorization_row_version
                  AND upload_authorization.authorization_status = 'CONSUMED'
                  AND upload_authorization.internal_object_key = @object_key
                  AND upload_authorization.verified_content_type = @content_type
                  AND upload_authorization.verified_content_length = @content_length
                  AND upload_authorization.verified_checksum_sha256 = @checksum
                  AND (upload_authorization.provider_object_version IS NOT DISTINCT FROM @object_version)
            );
            """,
            connection);
        command.Parameters.Add("decision_id", NpgsqlDbType.Uuid).Value = target.StatutoryDiscountDecisionCommandId;
        command.Parameters.Add("set_id", NpgsqlDbType.Uuid).Value = target.EvidenceSetId;
        command.Parameters.Add("set_reference", NpgsqlDbType.Uuid).Value = target.EvidenceSetReference;
        command.Parameters.AddWithValue("set_row_version", target.SetRowVersion);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = target.SiteId;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = target.SiteGroupId;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = target.ParkingSessionId;
        command.Parameters.Add("item_id", NpgsqlDbType.Uuid).Value = target.EvidenceItemId;
        command.Parameters.Add("item_reference", NpgsqlDbType.Uuid).Value = target.EvidenceItemReference;
        command.Parameters.AddWithValue("item_row_version", target.ItemRowVersion);
        command.Parameters.Add("authorization_id", NpgsqlDbType.Uuid).Value = target.UploadAuthorizationId;
        command.Parameters.Add("authorization_reference", NpgsqlDbType.Uuid).Value = target.UploadAuthorizationReference;
        command.Parameters.AddWithValue("authorization_row_version", target.UploadAuthorizationRowVersion);
        command.Parameters.AddWithValue("object_key", target.InternalObjectKey);
        command.Parameters.AddWithValue("content_type", target.ContentType);
        command.Parameters.AddWithValue("content_length", target.ContentLength);
        command.Parameters.AddWithValue("checksum", target.ChecksumSha256);
        command.Parameters.Add("object_version", NpgsqlDbType.Varchar).Value = (object?)target.ProviderObjectVersion ?? DBNull.Value;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    public async Task RecordAccessEventAsync(
        OperatorConsoleStatutoryEvidenceAccessEvent accessEvent,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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
                actor_user_id,
                actor_service_identity_id,
                correlation_id)
            VALUES (
                @event_type::discounts.statutory_evidence_event_type_enum,
                @event_result::discounts.statutory_evidence_event_result_enum,
                @set_id,
                @item_id,
                @reason_code,
                @source_channel,
                @site_id,
                @site_group_id,
                @parking_session_id,
                @actor_user_id,
                @actor_service_identity_id,
                @correlation_id);
            """,
            connection);
        command.Parameters.AddWithValue("event_type", accessEvent.EventType);
        command.Parameters.AddWithValue("event_result", accessEvent.EventResult);
        command.Parameters.AddWithValue("reason_code", accessEvent.SafeReasonCode);
        command.Parameters.AddWithValue("source_channel", accessEvent.Actor.SourceChannel);
        command.Parameters.Add("set_id", NpgsqlDbType.Uuid).Value = (object?)accessEvent.EvidenceSetId ?? DBNull.Value;
        command.Parameters.Add("item_id", NpgsqlDbType.Uuid).Value = (object?)accessEvent.EvidenceItemId ?? DBNull.Value;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = (object?)accessEvent.SiteId ?? DBNull.Value;
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = (object?)accessEvent.SiteGroupId ?? DBNull.Value;
        command.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = (object?)accessEvent.ParkingSessionId ?? DBNull.Value;
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = (object?)accessEvent.Actor.UserId ?? DBNull.Value;
        command.Parameters.Add("actor_service_identity_id", NpgsqlDbType.Uuid).Value = (object?)accessEvent.Actor.ServiceIdentityId ?? DBNull.Value;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = accessEvent.CorrelationId;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<OperatorConsoleStatutoryEvidenceReviewItemRecord>> ReadItemsAsync(
        NpgsqlConnection connection,
        Guid evidenceSetId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT item.statutory_evidence_item_id,
                   item.evidence_item_reference,
                   item.document_type::text,
                   item.item_role::text,
                   item.upload_status::text,
                   item.validation_status::text,
                   item.scan_status::text,
                   item.reviewability_status::text,
                   item.binding_status::text,
                   item.retention_status::text,
                   item.deletion_status::text,
                   item.hold_active,
                   item.declared_content_type,
                   item.internal_storage_locator_ref,
                   item.internal_checksum_sha256,
                   item.uploaded_at,
                   item.reviewable_at,
                   item.row_version,
                   upload_authorization.statutory_evidence_upload_authorization_id,
                   upload_authorization.upload_authorization_reference,
                   upload_authorization.authorization_status,
                   upload_authorization.bucket_reference,
                   upload_authorization.internal_object_key,
                   upload_authorization.verified_content_type,
                   upload_authorization.verified_content_length,
                   upload_authorization.verified_checksum_sha256,
                   upload_authorization.provider_object_version,
                   upload_authorization.consumed_at,
                   upload_authorization.row_version,
                   scan.completed_at
            FROM discounts.statutory_evidence_items AS item
            LEFT JOIN LATERAL (
                SELECT candidate.*
                FROM discounts.statutory_evidence_upload_authorizations AS candidate
                WHERE candidate.statutory_evidence_item_id = item.statutory_evidence_item_id
                  AND candidate.statutory_evidence_set_id = item.statutory_evidence_set_id
                  AND candidate.authorization_status = 'CONSUMED'
                  AND item.internal_storage_locator_ref = 'upload-authorization:' || candidate.upload_authorization_reference::text
                ORDER BY candidate.consumed_at DESC, candidate.created_at DESC
                LIMIT 1
            ) AS upload_authorization ON TRUE
            LEFT JOIN LATERAL (
                SELECT candidate.completed_at
                FROM discounts.statutory_evidence_scan_attempts AS candidate
                WHERE candidate.statutory_evidence_item_id = item.statutory_evidence_item_id
                  AND candidate.statutory_evidence_upload_authorization_id = upload_authorization.statutory_evidence_upload_authorization_id
                ORDER BY candidate.attempt_number DESC, candidate.created_at DESC
                LIMIT 1
            ) AS scan ON TRUE
            WHERE item.statutory_evidence_set_id = @set_id
            ORDER BY item.created_at, item.statutory_evidence_item_id;
            """,
            connection) { CommandTimeout = 30 };
        command.Parameters.Add("set_id", NpgsqlDbType.Uuid).Value = evidenceSetId;

        var items = new List<OperatorConsoleStatutoryEvidenceReviewItemRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new OperatorConsoleStatutoryEvidenceReviewItemRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetBoolean(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                ReadOffset(reader, 15),
                ReadOffset(reader, 16),
                reader.GetInt64(17),
                reader.IsDBNull(18) ? null : reader.GetGuid(18),
                reader.IsDBNull(19) ? null : reader.GetGuid(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetString(22),
                reader.IsDBNull(23) ? null : reader.GetString(23),
                reader.IsDBNull(24) ? null : reader.GetInt64(24),
                reader.IsDBNull(25) ? null : reader.GetString(25),
                reader.IsDBNull(26) ? null : reader.GetString(26),
                ReadOffset(reader, 27),
                reader.IsDBNull(28) ? null : reader.GetInt64(28),
                ReadOffset(reader, 29)));
        }

        return items;
    }

    private static DateTimeOffset? ReadOffset(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}
