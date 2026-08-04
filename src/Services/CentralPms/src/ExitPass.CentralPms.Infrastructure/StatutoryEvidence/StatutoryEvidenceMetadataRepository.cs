using ExitPass.CentralPms.Application.StatutoryEvidence;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.StatutoryEvidence;

public sealed class StatutoryEvidenceMetadataRepository : IStatutoryEvidenceMetadataRepository, IStatutoryEvidenceUploadRepository
{
    private const string HashSourceVersion = StatutoryEvidenceMetadataConstants.SemanticHashSourceVersion;
    private readonly string _connectionString;

    public StatutoryEvidenceMetadataRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<bool> ApprovedRetentionPolicyExistsAsync(
        string retentionClassCode,
        string retentionPolicyVersion,
        string environmentScope,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM discounts.statutory_evidence_retention_policies
                WHERE retention_class_code = @retention_class_code
                  AND retention_policy_version = @retention_policy_version
                  AND environment_scope = @environment_scope
                  AND policy_status = 'APPROVED_ENABLED'
                  AND effective_from <= now()
                  AND (effective_to IS NULL OR effective_to > now())
            );
            """,
            connection);
        command.Parameters.AddWithValue("retention_class_code", retentionClassCode);
        command.Parameters.AddWithValue("retention_policy_version", retentionPolicyVersion);
        command.Parameters.AddWithValue("environment_scope", environmentScope);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<StatutoryEvidenceDurableRequestBinding?> ResolveRequestBindingAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT command.statutory_discount_decision_command_id,
                   command.statutory_discount_validation_id,
                   command.parking_session_id,
                   session.site_id,
                   session.site_group_id,
                   command.entitlement_type::text,
                   command.source_channel
            FROM discounts.statutory_discount_decision_commands command
            JOIN core.parking_sessions session
              ON session.parking_session_id = command.parking_session_id
            WHERE command.statutory_discount_decision_command_id = @decision_command_id;
            """,
            connection);
        command.Parameters.AddWithValue("decision_command_id", statutoryDiscountDecisionCommandId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StatutoryEvidenceDurableRequestBinding(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    public async Task<bool> ActorHasScopeAsync(
        StatutoryEvidenceActor actor,
        string operation,
        Guid siteId,
        Guid siteGroupId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM discounts.statutory_evidence_principal_scope_grants grant_scope
                WHERE grant_scope.grant_status = 'ACTIVE'
                  AND grant_scope.source_channel = @source_channel
                  AND grant_scope.effective_from <= now()
                  AND (grant_scope.effective_to IS NULL OR grant_scope.effective_to > now())
                  AND (
                        (@actor_user_id IS NOT NULL AND grant_scope.actor_user_id = @actor_user_id)
                        OR (@actor_service_identity_id IS NOT NULL AND grant_scope.actor_service_identity_id = @actor_service_identity_id)
                      )
                  AND (
                        grant_scope.site_id = @site_id
                        OR (grant_scope.site_id IS NULL AND grant_scope.site_group_id = @site_group_id)
                        OR (grant_scope.site_id = @site_id AND grant_scope.site_group_id = @site_group_id)
                      )
                  AND CASE @operation
                        WHEN 'CAPTURE' THEN grant_scope.capture_allowed
                        WHEN 'VIEW' THEN grant_scope.view_allowed
                        WHEN 'REVIEW_LOCK' THEN grant_scope.review_lock_allowed
                        WHEN 'HOLD' THEN grant_scope.hold_allowed
                        WHEN 'DELETE_REQUEST' THEN grant_scope.deletion_request_allowed
                        ELSE false
                      END
            );
            """,
            connection);
        command.Parameters.AddWithValue("source_channel", actor.SourceChannel.ToUpperInvariant());
        command.Parameters.AddWithValue("operation", operation);
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = (object?)actor.UserId ?? DBNull.Value;
        command.Parameters.Add("actor_service_identity_id", NpgsqlDbType.Uuid).Value = (object?)actor.ServiceIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("site_group_id", siteGroupId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<StatutoryEvidenceOperationReplay?> FindOperationAsync(
        string idempotencyScope,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT operation_status::text, semantic_request_hash, statutory_evidence_set_id, statutory_evidence_item_id
            FROM discounts.statutory_evidence_operations
            WHERE idempotency_scope = @idempotency_scope
              AND idempotency_key = @idempotency_key;
            """,
            connection);
        command.Parameters.AddWithValue("idempotency_scope", idempotencyScope);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StatutoryEvidenceOperationReplay(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
    }

    public async Task<StatutoryEvidenceCreatedSet> CreateEvidenceSetAsync(
        StatutoryEvidenceCreateSetCommand command,
        string semanticRequestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var evidenceSetId = Guid.NewGuid();
        var evidenceSetReference = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO discounts.statutory_evidence_sets (
                statutory_evidence_set_id,
                evidence_set_reference,
                statutory_discount_decision_command_id,
                statutory_discount_validation_id,
                parking_session_id,
                site_id,
                site_group_id,
                entitlement_type,
                source_channel,
                required_document_profile_code,
                required_document_profile_version,
                retention_class_code,
                retention_policy_version,
                correlation_id,
                created_by_user_id,
                created_by_service_identity_id,
                updated_by_user_id,
                updated_by_service_identity_id)
            VALUES (
                @set_id,
                @set_reference,
                @decision_command_id,
                @validation_id,
                @parking_session_id,
                @site_id,
                @site_group_id,
                @entitlement_type::discounts.statutory_entitlement_type_enum,
                @source_channel,
                @profile_code,
                @profile_version,
                @retention_class_code,
                @retention_policy_version,
                @correlation_id,
                @user_id,
                @service_identity_id,
                @user_id,
                @service_identity_id);
            """,
            connection,
            transaction))
        {
            AddCreateSetParameters(insert, command, evidenceSetId, evidenceSetReference);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var operationId = await InsertOperationAsync(
            connection,
            transaction,
            "CREATE_SET",
            "ACCEPTED",
            command.IdempotencyScope,
            command.IdempotencyKey,
            semanticRequestHash,
            evidenceSetId,
            null,
            "ACCEPTED",
            command.CorrelationId,
            command.Actor,
            cancellationToken);

        await InsertEventAsync(
            connection,
            transaction,
            "EVIDENCE_SET_CREATED",
            "ACCEPTED",
            evidenceSetId,
            null,
            operationId,
            null,
            command.Actor.SourceChannel,
            command.SiteId,
            command.SiteGroupId,
            command.ParkingSessionId,
            command.Actor,
            command.CorrelationId,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new StatutoryEvidenceCreatedSet(evidenceSetId, (await GetEvidenceSetByIdAsync(evidenceSetId, cancellationToken))!);
    }

    public async Task<StatutoryEvidenceCreatedItem?> AddEvidenceItemAsync(
        StatutoryEvidenceAddItemCommand command,
        string semanticRequestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var setInfo = await GetSetInfoAsync(connection, transaction, command.EvidenceSetReference, cancellationToken);
        if (setInfo is null || !string.Equals(setInfo.Value.Status, "OPEN", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var itemId = Guid.NewGuid();
        var itemReference = Guid.NewGuid();
        try
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO discounts.statutory_evidence_items (
                    statutory_evidence_item_id,
                    evidence_item_reference,
                    statutory_evidence_set_id,
                    document_type,
                    item_role,
                    expected_media_class,
                    declared_content_type,
                    profile_code,
                    correlation_id,
                    created_by_user_id,
                    created_by_service_identity_id,
                    updated_by_user_id,
                    updated_by_service_identity_id)
                VALUES (
                    @item_id,
                    @item_reference,
                    @set_id,
                    @document_type::discounts.statutory_evidence_document_type_enum,
                    @item_role::discounts.statutory_evidence_item_role_enum,
                    @expected_media_class::discounts.statutory_evidence_media_class_enum,
                    @declared_content_type,
                    @profile_code,
                    @correlation_id,
                    @user_id,
                    @service_identity_id,
                    @user_id,
                    @service_identity_id);
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("item_id", itemId);
            insert.Parameters.AddWithValue("item_reference", itemReference);
            insert.Parameters.AddWithValue("set_id", setInfo.Value.SetId);
            insert.Parameters.AddWithValue("document_type", StatutoryEvidenceMetadataConstants.DocumentTypes.Contains(command.DocumentType) ? command.DocumentType.ToUpperInvariant() : command.DocumentType);
            insert.Parameters.AddWithValue("item_role", command.ItemRole.ToUpperInvariant());
            insert.Parameters.AddWithValue("expected_media_class", string.IsNullOrWhiteSpace(command.ExpectedMediaClass) ? "DOCUMENT_PROFILE_ONLY" : command.ExpectedMediaClass.ToUpperInvariant());
            insert.Parameters.AddWithValue("declared_content_type", (object?)command.DeclaredContentType ?? DBNull.Value);
            insert.Parameters.AddWithValue("profile_code", command.ProfileCode);
            AddActorParameters(insert, command.CorrelationId, command.Actor);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var operationId = await InsertOperationAsync(connection, transaction, "ADD_ITEM", "ACCEPTED", command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash, setInfo.Value.SetId, itemId, "ACCEPTED", command.CorrelationId, command.Actor, cancellationToken);
        await InsertEventAsync(connection, transaction, "EVIDENCE_ITEM_CREATED", "ACCEPTED", setInfo.Value.SetId, itemId, operationId, null, command.Actor.SourceChannel, setInfo.Value.SiteId, setInfo.Value.SiteGroupId, setInfo.Value.ParkingSessionId, command.Actor, command.CorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var set = (await GetEvidenceSetByIdAsync(setInfo.Value.SetId, cancellationToken))!;
        return new StatutoryEvidenceCreatedItem(setInfo.Value.SetId, itemId, set, set.Items.Single(item => item.EvidenceItemReference == itemReference));
    }

    public Task<StatutoryEvidenceSetReadModel?> GetEvidenceSetAsync(Guid evidenceSetReference, CancellationToken cancellationToken) =>
        ReadEvidenceSetAsync("evidence_set_reference", evidenceSetReference, cancellationToken);

    public Task<StatutoryEvidenceSetReadModel?> GetEvidenceSetByIdAsync(Guid evidenceSetId, CancellationToken cancellationToken) =>
        ReadEvidenceSetAsync("statutory_evidence_set_id", evidenceSetId, cancellationToken);

    public Task<StatutoryEvidenceSetReadModel?> LockForReviewAsync(StatutoryEvidenceLockForReviewCommand command, string semanticRequestHash, CancellationToken cancellationToken) =>
        TransitionSetAsync(command.EvidenceSetReference, "LOCK_FOR_REVIEW", command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash, command.CorrelationId, command.Actor, "REVIEW_LOCKED", "ACCEPTED", "set_status = 'LOCKED_FOR_REVIEW'::discounts.statutory_evidence_set_status_enum", "set_status = 'OPEN'::discounts.statutory_evidence_set_status_enum", cancellationToken);

    public Task<StatutoryEvidenceSetReadModel?> PlaceHoldAsync(StatutoryEvidenceHoldCommand command, string semanticRequestHash, CancellationToken cancellationToken) =>
        TransitionSetAsync(command.EvidenceSetReference, "PLACE_HOLD", command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash, command.CorrelationId, command.Actor, "HOLD_PLACED", "ACCEPTED", "hold_active = true, hold_reason_code = @reason_code, hold_placed_at = now(), retention_status = 'HELD'::discounts.statutory_evidence_retention_status_enum", "hold_active = false AND set_status <> 'TOMBSTONED'::discounts.statutory_evidence_set_status_enum", cancellationToken, command.ReasonCode);

    public Task<StatutoryEvidenceSetReadModel?> ReleaseHoldAsync(StatutoryEvidenceReleaseHoldCommand command, string semanticRequestHash, CancellationToken cancellationToken) =>
        TransitionSetAsync(command.EvidenceSetReference, "RELEASE_HOLD", command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash, command.CorrelationId, command.Actor, "HOLD_RELEASED", "ACCEPTED", "hold_active = false, hold_reason_code = NULL, hold_released_at = now(), retention_status = 'ACTIVE'::discounts.statutory_evidence_retention_status_enum", "hold_active = true", cancellationToken);

    public Task<StatutoryEvidenceSetReadModel?> RequestDeletionAsync(StatutoryEvidenceDeletionRequestCommand command, string semanticRequestHash, CancellationToken cancellationToken) =>
        TransitionSetAsync(command.EvidenceSetReference, "REQUEST_DELETION", command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash, command.CorrelationId, command.Actor, "DELETION_REQUESTED", "ACCEPTED", "deletion_status = 'REQUESTED'::discounts.statutory_evidence_deletion_status_enum", "hold_active = false AND deletion_status = 'NOT_REQUESTED'::discounts.statutory_evidence_deletion_status_enum", cancellationToken);

    public async Task RecordSemanticConflictAsync(string operationType, string idempotencyScope, string idempotencyKey, Guid correlationId, StatutoryEvidenceActor actor, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await InsertEventAsync(connection, null, "SEMANTIC_CONFLICT", "CONFLICT", null, null, null, "IDEMPOTENCY_SEMANTIC_CONFLICT", actor.SourceChannel, null, null, null, actor, correlationId, cancellationToken);
    }

    public async Task RecordAccessDeniedAsync(Guid? evidenceSetReference, Guid? siteId, Guid? siteGroupId, Guid? parkingSessionId, Guid correlationId, StatutoryEvidenceActor actor, string reasonCode, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await InsertEventAsync(connection, null, evidenceSetReference.HasValue ? "CROSS_SCOPE_ATTEMPT" : "MALFORMED_REFERENCE_LOOKUP", "DENIED", null, null, null, reasonCode, actor.SourceChannel, siteId, siteGroupId, parkingSessionId, actor, correlationId, cancellationToken);
    }

    public async Task<StatutoryEvidenceUploadTarget?> GetUploadTargetAsync(
        Guid evidenceSetReference,
        Guid evidenceItemReference,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT set_table.statutory_evidence_set_id,
                   item.statutory_evidence_item_id
            FROM discounts.statutory_evidence_sets set_table
            JOIN discounts.statutory_evidence_items item
              ON item.statutory_evidence_set_id = set_table.statutory_evidence_set_id
            WHERE set_table.evidence_set_reference = @set_reference
              AND item.evidence_item_reference = @item_reference;
            """,
            connection);
        command.Parameters.AddWithValue("set_reference", evidenceSetReference);
        command.Parameters.AddWithValue("item_reference", evidenceItemReference);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var setId = reader.GetGuid(0);
        var itemId = reader.GetGuid(1);
        await reader.CloseAsync();
        var set = await GetEvidenceSetByIdAsync(setId, cancellationToken);
        var item = set?.Items.SingleOrDefault(value => value.EvidenceItemReference == evidenceItemReference);
        return set is null || item is null
            ? null
            : new StatutoryEvidenceUploadTarget(setId, itemId, set, item);
    }

    public async Task<bool> HasSemanticConflictAsync(
        string idempotencyScope,
        string idempotencyKey,
        string semanticRequestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM discounts.statutory_evidence_operations
                WHERE idempotency_scope = @scope
                  AND idempotency_key = @key
                  AND semantic_request_hash <> @hash
            );
            """,
            connection);
        command.Parameters.AddWithValue("scope", idempotencyScope);
        command.Parameters.AddWithValue("key", idempotencyKey);
        command.Parameters.AddWithValue("hash", semanticRequestHash);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<StatutoryEvidenceUploadAuthorizationStorageRecord?> FindUploadAuthorizationByOperationAsync(
        string idempotencyScope,
        string idempotencyKey,
        string semanticRequestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT upload_authorization.statutory_evidence_upload_authorization_id,
                   upload_authorization.upload_authorization_reference,
                   upload_authorization.statutory_evidence_set_id,
                   upload_authorization.statutory_evidence_item_id,
                   upload_authorization.statutory_evidence_operation_id,
                   upload_authorization.provider_type,
                   upload_authorization.bucket_reference,
                   upload_authorization.internal_object_key,
                   upload_authorization.upload_method,
                   upload_authorization.expected_content_type,
                   upload_authorization.expected_content_length,
                   upload_authorization.checksum_algorithm,
                   upload_authorization.expected_checksum_sha256,
                   upload_authorization.authorization_status,
                   upload_authorization.issued_at,
                   upload_authorization.expires_at,
                   upload_authorization.consumed_at,
                   upload_authorization.verified_content_type,
                   upload_authorization.verified_content_length,
                   upload_authorization.verified_checksum_sha256,
                   upload_authorization.provider_object_version,
                   upload_authorization.provider_encryption_classification,
                   upload_authorization.failure_classification
            FROM discounts.statutory_evidence_operations operation
            JOIN discounts.statutory_evidence_upload_authorizations upload_authorization
              ON upload_authorization.statutory_evidence_operation_id = operation.statutory_evidence_operation_id
            WHERE operation.idempotency_scope = @scope
              AND operation.idempotency_key = @key
              AND operation.semantic_request_hash = @hash;
            """,
            connection);
        command.Parameters.AddWithValue("scope", idempotencyScope);
        command.Parameters.AddWithValue("key", idempotencyKey);
        command.Parameters.AddWithValue("hash", semanticRequestHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUploadAuthorization(reader) : null;
    }

    public async Task<StatutoryEvidenceUploadAuthorizationStorageRecord> CreateUploadAuthorizationAsync(
        StatutoryEvidenceUploadAuthorizationCommand command,
        StatutoryEvidenceUploadTarget target,
        string semanticRequestHash,
        string providerType,
        string bucketReference,
        string internalObjectKey,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var operationId = await InsertOperationAsync(
            connection,
            transaction,
            "AUTHORIZE_UPLOAD",
            "ACCEPTED",
            command.IdempotencyScope,
            command.IdempotencyKey,
            semanticRequestHash,
            target.EvidenceSetId,
            target.EvidenceItemId,
            "ACCEPTED",
            command.CorrelationId,
            command.Actor,
            cancellationToken);

        var authorizationId = Guid.NewGuid();
        var authorizationReference = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO discounts.statutory_evidence_upload_authorizations (
                statutory_evidence_upload_authorization_id,
                upload_authorization_reference,
                statutory_evidence_set_id,
                statutory_evidence_item_id,
                statutory_evidence_operation_id,
                provider_type,
                bucket_reference,
                internal_object_key,
                upload_method,
                expected_content_type,
                expected_content_length,
                checksum_algorithm,
                expected_checksum_sha256,
                expires_at,
                correlation_id,
                created_by_user_id,
                created_by_service_identity_id,
                updated_by_user_id,
                updated_by_service_identity_id)
            VALUES (
                @authorization_id,
                @authorization_reference,
                @set_id,
                @item_id,
                @operation_id,
                @provider_type,
                @bucket_reference,
                @internal_object_key,
                'PUT',
                @expected_content_type,
                @expected_content_length,
                'SHA256',
                @expected_checksum,
                @expires_at,
                @correlation_id,
                @user_id,
                @service_identity_id,
                @user_id,
                @service_identity_id);
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("authorization_id", authorizationId);
            insert.Parameters.AddWithValue("authorization_reference", authorizationReference);
            insert.Parameters.AddWithValue("set_id", target.EvidenceSetId);
            insert.Parameters.AddWithValue("item_id", target.EvidenceItemId);
            insert.Parameters.AddWithValue("operation_id", operationId);
            insert.Parameters.AddWithValue("provider_type", providerType);
            insert.Parameters.AddWithValue("bucket_reference", bucketReference);
            insert.Parameters.AddWithValue("internal_object_key", internalObjectKey);
            insert.Parameters.AddWithValue("expected_content_type", command.DeclaredContentType);
            insert.Parameters.AddWithValue("expected_content_length", command.DeclaredContentLength);
            insert.Parameters.AddWithValue("expected_checksum", command.DeclaredChecksumSha256.ToLowerInvariant());
            insert.Parameters.AddWithValue("expires_at", expiresAt);
            AddActorParameters(insert, command.CorrelationId, command.Actor);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateItem = new NpgsqlCommand(
            """
            UPDATE discounts.statutory_evidence_items
               SET upload_status = 'AUTHORIZED'::discounts.statutory_evidence_upload_status_enum,
                   declared_content_type = @declared_content_type,
                   updated_at = now(),
                   updated_by_user_id = @user_id,
                   updated_by_service_identity_id = @service_identity_id,
                   row_version = row_version + 1
             WHERE statutory_evidence_item_id = @item_id
               AND upload_status IN ('NOT_AUTHORIZED', 'AUTHORIZED', 'FAILED', 'EXPIRED')
               AND deletion_status <> 'DELETED'::discounts.statutory_evidence_deletion_status_enum;
            """,
            connection,
            transaction))
        {
            updateItem.Parameters.AddWithValue("item_id", target.EvidenceItemId);
            updateItem.Parameters.AddWithValue("declared_content_type", command.DeclaredContentType);
            updateItem.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = (object?)command.Actor.UserId ?? DBNull.Value;
            updateItem.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)command.Actor.ServiceIdentityId ?? DBNull.Value;
            if (await updateItem.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("Evidence item upload lifecycle does not allow authorization.");
            }
        }

        await InsertEventAsync(connection, transaction, "UPLOAD_AUTHORIZATION_ISSUED", "ACCEPTED", target.EvidenceSetId, target.EvidenceItemId, operationId, null, command.Actor.SourceChannel, target.EvidenceSet.SiteId, target.EvidenceSet.SiteGroupId, target.EvidenceSet.ParkingSessionId, command.Actor, command.CorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetUploadAuthorizationAsync(authorizationReference, target.EvidenceSetId, target.EvidenceItemId, cancellationToken))!;
    }

    public async Task<StatutoryEvidenceUploadAuthorizationStorageRecord?> GetUploadAuthorizationAsync(
        Guid authorizationReference,
        Guid evidenceSetId,
        Guid evidenceItemId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT statutory_evidence_upload_authorization_id,
                   upload_authorization_reference,
                   statutory_evidence_set_id,
                   statutory_evidence_item_id,
                   statutory_evidence_operation_id,
                   provider_type,
                   bucket_reference,
                   internal_object_key,
                   upload_method,
                   expected_content_type,
                   expected_content_length,
                   checksum_algorithm,
                   expected_checksum_sha256,
                   authorization_status,
                   issued_at,
                   expires_at,
                   consumed_at,
                   verified_content_type,
                   verified_content_length,
                   verified_checksum_sha256,
                   provider_object_version,
                   provider_encryption_classification,
                   failure_classification
            FROM discounts.statutory_evidence_upload_authorizations
            WHERE upload_authorization_reference = @authorization_reference
              AND statutory_evidence_set_id = @set_id
              AND statutory_evidence_item_id = @item_id;
            """,
            connection);
        command.Parameters.AddWithValue("authorization_reference", authorizationReference);
        command.Parameters.AddWithValue("set_id", evidenceSetId);
        command.Parameters.AddWithValue("item_id", evidenceItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUploadAuthorization(reader) : null;
    }

    public async Task<StatutoryEvidenceItemReadModel?> FinalizeUploadAsync(
        StatutoryEvidenceUploadFinalizationCommand command,
        StatutoryEvidenceUploadTarget target,
        StatutoryEvidenceUploadAuthorizationStorageRecord authorization,
        StatutoryEvidenceObjectMetadata metadata,
        string semanticRequestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var operationId = await InsertOperationAsync(connection, transaction, "FINALIZE_UPLOAD", "ACCEPTED", command.IdempotencyScope, command.IdempotencyKey, semanticRequestHash, target.EvidenceSetId, target.EvidenceItemId, "ACCEPTED", command.CorrelationId, command.Actor, cancellationToken);
        await InsertEventAsync(connection, transaction, "UPLOAD_VERIFICATION_STARTED", "ACCEPTED", target.EvidenceSetId, target.EvidenceItemId, operationId, null, command.Actor.SourceChannel, target.EvidenceSet.SiteId, target.EvidenceSet.SiteGroupId, target.EvidenceSet.ParkingSessionId, command.Actor, command.CorrelationId, cancellationToken);

        await using (var updateAuthorization = new NpgsqlCommand(
            """
            UPDATE discounts.statutory_evidence_upload_authorizations
               SET authorization_status = 'CONSUMED',
                   consumed_at = now(),
                   verified_content_type = @verified_content_type,
                   verified_content_length = @verified_content_length,
                   verified_checksum_sha256 = @verified_checksum,
                   provider_object_version = @provider_object_version,
                   provider_encryption_classification = @provider_encryption,
                   updated_at = now(),
                   updated_by_user_id = @user_id,
                   updated_by_service_identity_id = @service_identity_id,
                   row_version = row_version + 1
             WHERE statutory_evidence_upload_authorization_id = @authorization_id
               AND authorization_status = 'ISSUED';
            """,
            connection,
            transaction))
        {
            updateAuthorization.Parameters.AddWithValue("authorization_id", authorization.UploadAuthorizationId);
            updateAuthorization.Parameters.AddWithValue("verified_content_type", metadata.ContentType);
            updateAuthorization.Parameters.AddWithValue("verified_content_length", metadata.ContentLength);
            updateAuthorization.Parameters.AddWithValue("verified_checksum", metadata.ChecksumSha256!.ToLowerInvariant());
            updateAuthorization.Parameters.AddWithValue("provider_object_version", (object?)metadata.ObjectVersion ?? DBNull.Value);
            updateAuthorization.Parameters.AddWithValue("provider_encryption", (object?)metadata.EncryptionClassification ?? DBNull.Value);
            updateAuthorization.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = (object?)command.Actor.UserId ?? DBNull.Value;
            updateAuthorization.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)command.Actor.ServiceIdentityId ?? DBNull.Value;
            if (await updateAuthorization.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        await using (var updateItem = new NpgsqlCommand(
            """
            UPDATE discounts.statutory_evidence_items
               SET upload_status = 'UPLOADED'::discounts.statutory_evidence_upload_status_enum,
                   internal_storage_locator_ref = @storage_locator_ref,
                   internal_checksum_sha256 = @checksum,
                   uploaded_at = now(),
                   updated_at = now(),
                   updated_by_user_id = @user_id,
                   updated_by_service_identity_id = @service_identity_id,
                   row_version = row_version + 1
             WHERE statutory_evidence_item_id = @item_id
               AND upload_status = 'AUTHORIZED'::discounts.statutory_evidence_upload_status_enum
               AND validation_status = 'NOT_STARTED'::discounts.statutory_evidence_validation_status_enum
               AND scan_status = 'NOT_STARTED'::discounts.statutory_evidence_scan_status_enum
               AND reviewability_status = 'NOT_REVIEWABLE'::discounts.statutory_evidence_reviewability_status_enum
               AND deletion_status <> 'DELETED'::discounts.statutory_evidence_deletion_status_enum;
            """,
            connection,
            transaction))
        {
            updateItem.Parameters.AddWithValue("item_id", target.EvidenceItemId);
            updateItem.Parameters.AddWithValue("storage_locator_ref", $"upload-authorization:{authorization.UploadAuthorizationReference:D}");
            updateItem.Parameters.AddWithValue("checksum", metadata.ChecksumSha256!.ToLowerInvariant());
            updateItem.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = (object?)command.Actor.UserId ?? DBNull.Value;
            updateItem.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)command.Actor.ServiceIdentityId ?? DBNull.Value;
            if (await updateItem.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        await InsertEventAsync(connection, transaction, "UPLOAD_VERIFIED", "ACCEPTED", target.EvidenceSetId, target.EvidenceItemId, operationId, null, command.Actor.SourceChannel, target.EvidenceSet.SiteId, target.EvidenceSet.SiteGroupId, target.EvidenceSet.ParkingSessionId, command.Actor, command.CorrelationId, cancellationToken);
        await InsertEventAsync(connection, transaction, "UPLOAD_FINALIZED", "ACCEPTED", target.EvidenceSetId, target.EvidenceItemId, operationId, null, command.Actor.SourceChannel, target.EvidenceSet.SiteId, target.EvidenceSet.SiteGroupId, target.EvidenceSet.ParkingSessionId, command.Actor, command.CorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var set = await GetEvidenceSetByIdAsync(target.EvidenceSetId, cancellationToken);
        return set?.Items.SingleOrDefault(item => item.EvidenceItemReference == command.EvidenceItemReference);
    }

    public async Task RecordUploadDeniedAsync(Guid? evidenceSetReference, Guid? evidenceItemReference, Guid? siteId, Guid? siteGroupId, Guid? parkingSessionId, Guid correlationId, StatutoryEvidenceActor actor, string reasonCode, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await InsertEventAsync(connection, null, evidenceSetReference.HasValue || evidenceItemReference.HasValue ? "CROSS_SCOPE_ATTEMPT" : "MALFORMED_REFERENCE_LOOKUP", "DENIED", null, null, null, reasonCode, actor.SourceChannel, siteId, siteGroupId, parkingSessionId, actor, correlationId, cancellationToken);
    }

    public async Task RecordUploadConflictAsync(string operationType, string idempotencyScope, string idempotencyKey, Guid correlationId, StatutoryEvidenceActor actor, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await InsertEventAsync(connection, null, "SEMANTIC_CONFLICT", "CONFLICT", null, null, null, "IDEMPOTENCY_SEMANTIC_CONFLICT", actor.SourceChannel, null, null, null, actor, correlationId, cancellationToken);
    }

    public async Task RecordUploadVerificationFailureAsync(StatutoryEvidenceUploadFinalizationCommand command, StatutoryEvidenceUploadTarget target, StatutoryEvidenceUploadAuthorizationStorageRecord authorization, string reasonCode, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = new NpgsqlCommand(
            """
            UPDATE discounts.statutory_evidence_upload_authorizations
               SET authorization_status = CASE WHEN authorization_status = 'ISSUED' THEN 'FAILED' ELSE authorization_status END,
                   failure_classification = @reason_code,
                   updated_at = now(),
                   updated_by_user_id = @user_id,
                   updated_by_service_identity_id = @service_identity_id,
                   row_version = row_version + 1
             WHERE statutory_evidence_upload_authorization_id = @authorization_id;
            """,
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("authorization_id", authorization.UploadAuthorizationId);
            update.Parameters.AddWithValue("reason_code", reasonCode);
            update.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = (object?)command.Actor.UserId ?? DBNull.Value;
            update.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)command.Actor.ServiceIdentityId ?? DBNull.Value;
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertEventAsync(connection, transaction, reasonCode == StatutoryEvidenceUploadConstants.ProviderUnavailable ? "PROVIDER_UNAVAILABLE" : "UPLOAD_VERIFICATION_FAILED", "DENIED", target.EvidenceSetId, target.EvidenceItemId, null, reasonCode, command.Actor.SourceChannel, target.EvidenceSet.SiteId, target.EvidenceSet.SiteGroupId, target.EvidenceSet.ParkingSessionId, command.Actor, command.CorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<StatutoryEvidenceSetReadModel?> TransitionSetAsync(Guid reference, string operationType, string scope, string key, string hash, Guid correlationId, StatutoryEvidenceActor actor, string eventType, string eventResult, string setClause, string whereClause, CancellationToken cancellationToken, string? reasonCode = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var setInfo = await GetSetInfoAsync(connection, transaction, reference, cancellationToken);
        if (setInfo is null)
        {
            return null;
        }

        await using var update = new NpgsqlCommand(
            $"""
            UPDATE discounts.statutory_evidence_sets
               SET {setClause},
                   updated_at = now(),
                   updated_by_user_id = @user_id,
                   updated_by_service_identity_id = @service_identity_id,
                   row_version = row_version + 1
             WHERE statutory_evidence_set_id = @set_id
               AND {whereClause};
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("set_id", setInfo.Value.SetId);
        update.Parameters.AddWithValue("user_id", (object?)actor.UserId ?? DBNull.Value);
        update.Parameters.AddWithValue("service_identity_id", (object?)actor.ServiceIdentityId ?? DBNull.Value);
        if (reasonCode is not null)
        {
            update.Parameters.AddWithValue("reason_code", reasonCode);
        }

        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var operationId = await InsertOperationAsync(connection, transaction, operationType, "ACCEPTED", scope, key, hash, setInfo.Value.SetId, null, "ACCEPTED", correlationId, actor, cancellationToken);
        await InsertEventAsync(connection, transaction, eventType, eventResult, setInfo.Value.SetId, null, operationId, reasonCode, actor.SourceChannel, setInfo.Value.SiteId, setInfo.Value.SiteGroupId, setInfo.Value.ParkingSessionId, actor, correlationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetEvidenceSetByIdAsync(setInfo.Value.SetId, cancellationToken);
    }

    private async Task<StatutoryEvidenceSetReadModel?> ReadEvidenceSetAsync(string keyColumn, Guid key, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT statutory_evidence_set_id, evidence_set_reference, statutory_discount_decision_command_id,
                   statutory_discount_validation_id, parking_session_id, site_id, site_group_id,
                   entitlement_type::text, source_channel, set_status::text, required_document_profile_code,
                   required_document_profile_version, retention_class_code, retention_policy_version,
                   retention_status::text, deletion_status::text, hold_active, hold_reason_code,
                   correlation_id, created_at, updated_at
            FROM discounts.statutory_evidence_sets
            WHERE {keyColumn} = @key;
            """,
            connection);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var setId = reader.GetGuid(0);
        var set = new StatutoryEvidenceSetReadModel(
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetGuid(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetBoolean(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.GetGuid(18),
            ToOffset(reader.GetDateTime(19)),
            ToOffset(reader.GetDateTime(20)),
            []);
        await reader.CloseAsync();
        return set with { Items = await ReadItemsAsync(connection, setId, cancellationToken) };
    }

    private static async Task<IReadOnlyList<StatutoryEvidenceItemReadModel>> ReadItemsAsync(NpgsqlConnection connection, Guid setId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT evidence_item_reference, document_type::text, item_role::text, upload_status::text,
                   validation_status::text, scan_status::text, reviewability_status::text, binding_status::text,
                   retention_status::text, deletion_status::text, hold_active, expected_media_class::text,
                   declared_content_type, profile_code, validation_result_classification, scan_result_classification,
                   created_at, updated_at
            FROM discounts.statutory_evidence_items
            WHERE statutory_evidence_set_id = @set_id
            ORDER BY created_at, statutory_evidence_item_id;
            """,
            connection);
        command.Parameters.AddWithValue("set_id", setId);
        var items = new List<StatutoryEvidenceItemReadModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new StatutoryEvidenceItemReadModel(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetBoolean(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                ToOffset(reader.GetDateTime(16)),
                ToOffset(reader.GetDateTime(17))));
        }

        return items;
    }

    private static async Task<Guid> InsertOperationAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string operationType, string operationStatus, string scope, string key, string hash, Guid? setId, Guid? itemId, string result, Guid correlationId, StatutoryEvidenceActor actor, CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO discounts.statutory_evidence_operations (
                statutory_evidence_operation_id, operation_type, operation_status, idempotency_scope,
                idempotency_key, semantic_request_hash, semantic_hash_source_version,
                statutory_evidence_set_id, statutory_evidence_item_id, safe_result_classification,
                correlation_id, created_by_user_id, created_by_service_identity_id)
            VALUES (
                @operation_id, @operation_type::discounts.statutory_evidence_operation_type_enum,
                @operation_status::discounts.statutory_evidence_operation_status_enum, @scope, @key,
                @hash, @version, @set_id, @item_id, @result, @correlation_id, @user_id, @service_identity_id);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("operation_id", operationId);
        command.Parameters.AddWithValue("operation_type", operationType);
        command.Parameters.AddWithValue("operation_status", operationStatus);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("version", HashSourceVersion);
        command.Parameters.AddWithValue("set_id", (object?)setId ?? DBNull.Value);
        command.Parameters.AddWithValue("item_id", (object?)itemId ?? DBNull.Value);
        command.Parameters.AddWithValue("result", result);
        AddActorParameters(command, correlationId, actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return operationId;
    }

    private static async Task InsertEventAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string eventType, string eventResult, Guid? setId, Guid? itemId, Guid? operationId, string? reasonCode, string? sourceChannel, Guid? siteId, Guid? siteGroupId, Guid? parkingSessionId, StatutoryEvidenceActor actor, Guid correlationId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO discounts.statutory_evidence_events (
                event_type, event_result, statutory_evidence_set_id, statutory_evidence_item_id,
                statutory_evidence_operation_id, safe_reason_code, source_channel, site_id, site_group_id,
                parking_session_id, actor_user_id, actor_service_identity_id, correlation_id)
            VALUES (
                @event_type::discounts.statutory_evidence_event_type_enum,
                @event_result::discounts.statutory_evidence_event_result_enum,
                @set_id, @item_id, @operation_id, @reason_code, @source_channel, @site_id,
                @site_group_id, @parking_session_id, @user_id, @service_identity_id, @correlation_id);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("event_result", eventResult);
        command.Parameters.AddWithValue("set_id", (object?)setId ?? DBNull.Value);
        command.Parameters.AddWithValue("item_id", (object?)itemId ?? DBNull.Value);
        command.Parameters.AddWithValue("operation_id", (object?)operationId ?? DBNull.Value);
        command.Parameters.AddWithValue("reason_code", (object?)reasonCode ?? DBNull.Value);
        command.Parameters.AddWithValue("source_channel", (object?)sourceChannel ?? DBNull.Value);
        command.Parameters.AddWithValue("site_id", (object?)siteId ?? DBNull.Value);
        command.Parameters.AddWithValue("site_group_id", (object?)siteGroupId ?? DBNull.Value);
        command.Parameters.AddWithValue("parking_session_id", (object?)parkingSessionId ?? DBNull.Value);
        AddActorParameters(command, correlationId, actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(Guid SetId, string Status, Guid SiteId, Guid SiteGroupId, Guid ParkingSessionId)?> GetSetInfoAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid reference, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT statutory_evidence_set_id, set_status::text, site_id, site_group_id, parking_session_id
            FROM discounts.statutory_evidence_sets
            WHERE evidence_set_reference = @reference;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("reference", reference);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetGuid(3), reader.GetGuid(4));
    }

    private static void AddCreateSetParameters(NpgsqlCommand command, StatutoryEvidenceCreateSetCommand value, Guid evidenceSetId, Guid evidenceSetReference)
    {
        command.Parameters.AddWithValue("set_id", evidenceSetId);
        command.Parameters.AddWithValue("set_reference", evidenceSetReference);
        command.Parameters.AddWithValue("decision_command_id", value.StatutoryDiscountDecisionCommandId);
        command.Parameters.AddWithValue("validation_id", (object?)value.StatutoryDiscountValidationId ?? DBNull.Value);
        command.Parameters.AddWithValue("parking_session_id", value.ParkingSessionId);
        command.Parameters.AddWithValue("site_id", value.SiteId);
        command.Parameters.AddWithValue("site_group_id", value.SiteGroupId);
        command.Parameters.AddWithValue("entitlement_type", value.EntitlementType.ToUpperInvariant());
        command.Parameters.AddWithValue("source_channel", value.Actor.SourceChannel.ToUpperInvariant());
        command.Parameters.AddWithValue("profile_code", value.RequiredDocumentProfileCode);
        command.Parameters.AddWithValue("profile_version", value.RequiredDocumentProfileVersion);
        command.Parameters.AddWithValue("retention_class_code", value.RetentionClassCode);
        command.Parameters.AddWithValue("retention_policy_version", value.RetentionPolicyVersion);
        AddActorParameters(command, value.CorrelationId, value.Actor);
    }

    private static void AddActorParameters(NpgsqlCommand command, Guid correlationId, StatutoryEvidenceActor actor)
    {
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = (object?)actor.UserId ?? DBNull.Value;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)actor.ServiceIdentityId ?? DBNull.Value;
    }

    private static StatutoryEvidenceUploadAuthorizationStorageRecord ReadUploadAuthorization(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetInt64(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            ToOffset(reader.GetDateTime(14)),
            ToOffset(reader.GetDateTime(15)),
            reader.IsDBNull(16) ? null : ToOffset(reader.GetDateTime(16)),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetInt64(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            reader.IsDBNull(21) ? null : reader.GetString(21),
            reader.IsDBNull(22) ? null : reader.GetString(22));

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
