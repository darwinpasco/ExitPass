using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Infrastructure.StatutoryEvidence;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class StatutoryEvidenceScanRepositoryIntegrationTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task FinalizedUpload_CreatesOneClaimableScanWork_AndCompletionUpdatesLifecycle()
    {
        await EnsureScanSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(FinalizedUpload_CreatesOneClaimableScanWork_AndCompletionUpdatesLifecycle));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence scan worker test data.");
        await SeedEvidencePrerequisitesAsync(context, seed);

        try
        {
            var metadataService = new StatutoryEvidenceMetadataService(new StatutoryEvidenceMetadataRepository(ConnectionString));
            var uploadService = new StatutoryEvidenceUploadService(
                new StatutoryEvidenceMetadataRepository(ConnectionString),
                new FakeStorageAdapter(),
                UploadOptions());

            var created = await metadataService.CreateOrResolveSetAsync(CreateSetCommand(context, seed), CancellationToken.None);
            created.Classification.Should().Be("ACCEPTED");
            var item = await metadataService.AddItemAsync(AddItemCommand(created.EvidenceSet!.EvidenceSetReference, seed), CancellationToken.None);
            item.Classification.Should().Be("ACCEPTED");

            var authorized = await uploadService.AuthorizeUploadAsync(AuthorizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem!.EvidenceItemReference, seed), CancellationToken.None);
            authorized.Classification.Should().Be("ACCEPTED");
            var finalized = await uploadService.FinalizeUploadAsync(FinalizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem.EvidenceItemReference, authorized.UploadAuthorization!.UploadAuthorizationReference, seed), CancellationToken.None);
            finalized.Classification.Should().Be("ACCEPTED");

            var repository = new StatutoryEvidenceScanRepository(ConnectionString);
            var firstClaim = await repository.ClaimDueWorkAsync("worker-a", seed.ActorServiceIdentityId, 5, TimeSpan.FromMinutes(2), DateTimeOffset.UtcNow, CancellationToken.None);

            firstClaim.Should().ContainSingle();
            firstClaim[0].EvidenceItemId.Should().NotBeEmpty();
            firstClaim[0].ExpectedContentType.Should().Be("image/jpeg");
            firstClaim[0].ExpectedChecksumSha256.Should().Be(Hash);

            var duplicateClaim = await repository.ClaimDueWorkAsync("worker-b", seed.ActorServiceIdentityId, 5, TimeSpan.FromMinutes(2), DateTimeOffset.UtcNow, CancellationToken.None);
            duplicateClaim.Should().BeEmpty();

            await repository.CompleteAttemptAsync(
                firstClaim[0],
                new StatutoryEvidenceScanCompletion("COMPLETED", "PASSED", "PASSED", "CLEAN", "CLEAN", null, false, true),
                seed.ActorServiceIdentityId,
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            var status = await ReadItemStatusAsync(item.EvidenceItem.EvidenceItemReference);
            status.ValidationStatus.Should().Be("PASSED");
            status.ScanStatus.Should().Be("CLEAN");
            status.ReviewabilityStatus.Should().Be("REVIEWABLE");
            (await CountScanAttemptsAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ClaimedWork_WhenLeaseExpires_CanBeRecoveredByAnotherWorker()
    {
        await EnsureScanSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(ClaimedWork_WhenLeaseExpires_CanBeRecoveredByAnotherWorker));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence scan lease recovery test data.");
        await SeedEvidencePrerequisitesAsync(context, seed);

        try
        {
            var metadataService = new StatutoryEvidenceMetadataService(new StatutoryEvidenceMetadataRepository(ConnectionString));
            var uploadService = new StatutoryEvidenceUploadService(
                new StatutoryEvidenceMetadataRepository(ConnectionString),
                new FakeStorageAdapter(),
                UploadOptions());

            var created = await metadataService.CreateOrResolveSetAsync(CreateSetCommand(context, seed), CancellationToken.None);
            var item = await metadataService.AddItemAsync(AddItemCommand(created.EvidenceSet!.EvidenceSetReference, seed), CancellationToken.None);
            var authorized = await uploadService.AuthorizeUploadAsync(AuthorizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem!.EvidenceItemReference, seed), CancellationToken.None);
            await uploadService.FinalizeUploadAsync(FinalizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem.EvidenceItemReference, authorized.UploadAuthorization!.UploadAuthorizationReference, seed), CancellationToken.None);

            var repository = new StatutoryEvidenceScanRepository(ConnectionString);
            var now = DateTimeOffset.UtcNow;
            var firstClaim = await repository.ClaimDueWorkAsync("worker-a", seed.ActorServiceIdentityId, 1, TimeSpan.FromSeconds(5), now, CancellationToken.None);
            firstClaim.Should().ContainSingle();

            var recovered = await repository.ClaimDueWorkAsync("worker-b", seed.ActorServiceIdentityId, 1, TimeSpan.FromSeconds(5), now.AddSeconds(6), CancellationToken.None);

            recovered.Should().ContainSingle();
            recovered[0].ScanAttemptId.Should().Be(firstClaim[0].ScanAttemptId);
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ClaimedWork_WhenRecoveredWorkerCompletes_ExpiredOriginalWorkerCannotOverwriteResult()
    {
        await EnsureScanSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(ClaimedWork_WhenRecoveredWorkerCompletes_ExpiredOriginalWorkerCannotOverwriteResult));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence recovered completion guard test data.");
        await SeedEvidencePrerequisitesAsync(context, seed);

        try
        {
            var (itemReference, _) = await CreateFinalizedEvidenceItemAsync(context, seed);
            var repository = new StatutoryEvidenceScanRepository(ConnectionString);
            var now = DateTimeOffset.UtcNow;
            var firstClaim = await repository.ClaimDueWorkAsync("worker-a", seed.ActorServiceIdentityId, 1, TimeSpan.FromSeconds(5), now, CancellationToken.None);
            firstClaim.Should().ContainSingle();

            var recovered = await repository.ClaimDueWorkAsync("worker-b", seed.ActorServiceIdentityId, 1, TimeSpan.FromSeconds(5), now.AddSeconds(6), CancellationToken.None);
            recovered.Should().ContainSingle();

            await repository.CompleteAttemptAsync(
                recovered[0],
                new StatutoryEvidenceScanCompletion("COMPLETED", "PASSED", "PASSED", "CLEAN", "CLEAN", null, false, true),
                seed.ActorServiceIdentityId,
                now.AddSeconds(7),
                CancellationToken.None);

            await repository.CompleteAttemptAsync(
                firstClaim[0],
                new StatutoryEvidenceScanCompletion("FAILED_TERMINAL", "PASSED", "PASSED", "MALICIOUS", "MALWARE_DETECTED", "MALWARE_DETECTED", false, true),
                seed.ActorServiceIdentityId,
                now.AddSeconds(8),
                CancellationToken.None);

            var status = await ReadItemStatusAsync(itemReference);
            status.ValidationStatus.Should().Be("PASSED");
            status.ScanStatus.Should().Be("CLEAN");
            status.ReviewabilityStatus.Should().Be("REVIEWABLE");

            var attempt = await ReadAttemptStatusAsync(firstClaim[0].ScanAttemptReference);
            attempt.AttemptStatus.Should().Be("COMPLETED");
            attempt.ValidationResult.Should().Be("PASSED");
            attempt.SafeFailureClassification.Should().BeNull();
            (await CountStaleAttemptEventsAsync(context.ParkingSessionId)).Should().Be(0);
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task CompletionFailureBeforeCommit_RollsBackAttemptItemAndEvents_ThenCompletesOnRetry()
    {
        await EnsureScanSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(CompletionFailureBeforeCommit_RollsBackAttemptItemAndEvents_ThenCompletesOnRetry));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence rollback test data.");
        await SeedEvidencePrerequisitesAsync(context, seed);

        try
        {
            var (itemReference, _) = await CreateFinalizedEvidenceItemAsync(context, seed);
            var repository = new StatutoryEvidenceScanRepository(ConnectionString);
            var claimed = await repository.ClaimDueWorkAsync("worker-a", seed.ActorServiceIdentityId, 1, TimeSpan.FromMinutes(2), DateTimeOffset.UtcNow, CancellationToken.None);
            claimed.Should().ContainSingle();
            var before = await ReadItemStatusAsync(itemReference);
            var beforeEvents = await CountScanEventsAsync(context.ParkingSessionId);

            var failingRepository = new StatutoryEvidenceScanRepository(
                ConnectionString,
                _ => throw new InvalidOperationException("I015_ROLLBACK_PROOF"));

            await failingRepository.Invoking(repo => repo.CompleteAttemptAsync(
                    claimed[0],
                    new StatutoryEvidenceScanCompletion("COMPLETED", "PASSED", "PASSED", "CLEAN", "CLEAN", null, false, true),
                    seed.ActorServiceIdentityId,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None))
                .Should().ThrowAsync<InvalidOperationException>();

            var afterFailure = await ReadItemStatusAsync(itemReference);
            afterFailure.Should().Be(before);
            (await CountScanEventsAsync(context.ParkingSessionId)).Should().Be(beforeEvents);
            (await ReadAttemptStatusAsync(claimed[0].ScanAttemptReference)).AttemptStatus.Should().Be("CLAIMED");

            await repository.CompleteAttemptAsync(
                claimed[0],
                new StatutoryEvidenceScanCompletion("COMPLETED", "PASSED", "PASSED", "CLEAN", "CLEAN", null, false, true),
                seed.ActorServiceIdentityId,
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            var final = await ReadItemStatusAsync(itemReference);
            final.ValidationStatus.Should().Be("PASSED");
            final.ScanStatus.Should().Be("CLEAN");
            final.ReviewabilityStatus.Should().Be("REVIEWABLE");
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }
    [Fact]
    public async Task RetryPendingWork_WhenDue_ReclaimsAndCompletesWithoutStaleRejection()
    {
        await EnsureScanSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(RetryPendingWork_WhenDue_ReclaimsAndCompletesWithoutStaleRejection));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence retry recovery test data.");
        await SeedEvidencePrerequisitesAsync(context, seed);

        try
        {
            var metadataService = new StatutoryEvidenceMetadataService(new StatutoryEvidenceMetadataRepository(ConnectionString));
            var uploadService = new StatutoryEvidenceUploadService(
                new StatutoryEvidenceMetadataRepository(ConnectionString),
                new FakeStorageAdapter(),
                UploadOptions());

            var created = await metadataService.CreateOrResolveSetAsync(CreateSetCommand(context, seed), CancellationToken.None);
            var item = await metadataService.AddItemAsync(AddItemCommand(created.EvidenceSet!.EvidenceSetReference, seed), CancellationToken.None);
            var authorized = await uploadService.AuthorizeUploadAsync(AuthorizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem!.EvidenceItemReference, seed), CancellationToken.None);
            await uploadService.FinalizeUploadAsync(FinalizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem.EvidenceItemReference, authorized.UploadAuthorization!.UploadAuthorizationReference, seed), CancellationToken.None);

            var repository = new StatutoryEvidenceScanRepository(ConnectionString);
            var now = DateTimeOffset.UtcNow;
            var firstClaim = await repository.ClaimDueWorkAsync("worker-a", seed.ActorServiceIdentityId, 1, TimeSpan.FromMinutes(2), now, CancellationToken.None);
            firstClaim.Should().ContainSingle();

            await repository.ScheduleRetryAsync(
                firstClaim[0],
                new StatutoryEvidenceScanCompletion("RETRY_PENDING", "PASSED", "PASSED", "ERROR_RETRYABLE", "SCANNER_UNAVAILABLE", "SCANNER_UNAVAILABLE", true, false),
                seed.ActorServiceIdentityId,
                now.AddSeconds(1),
                now,
                CancellationToken.None);

            var retryClaim = await repository.ClaimDueWorkAsync("worker-b", seed.ActorServiceIdentityId, 1, TimeSpan.FromMinutes(2), now.AddSeconds(2), CancellationToken.None);
            retryClaim.Should().ContainSingle();
            retryClaim[0].ScanAttemptId.Should().Be(firstClaim[0].ScanAttemptId);

            await repository.CompleteAttemptAsync(
                retryClaim[0],
                new StatutoryEvidenceScanCompletion("COMPLETED", "PASSED", "PASSED", "CLEAN", "CLEAN", null, false, true),
                seed.ActorServiceIdentityId,
                now.AddSeconds(3),
                CancellationToken.None);

            var status = await ReadItemStatusAsync(item.EvidenceItem.EvidenceItemReference);
            status.ValidationStatus.Should().Be("PASSED");
            status.ScanStatus.Should().Be("CLEAN");
            status.ReviewabilityStatus.Should().Be("REVIEWABLE");

            var attempt = await ReadAttemptStatusAsync(firstClaim[0].ScanAttemptReference);
            attempt.AttemptStatus.Should().Be("COMPLETED");
            attempt.ValidationResult.Should().Be("PASSED");
            attempt.SafeFailureClassification.Should().BeNull();
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ClaimedWork_WhenItemVersionAdvances_CannotUpdateCurrentItemAndRecordsStaleAttempt()
    {
        await EnsureScanSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(ClaimedWork_WhenItemVersionAdvances_CannotUpdateCurrentItemAndRecordsStaleAttempt));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence stale-object protection test data.");
        await SeedEvidencePrerequisitesAsync(context, seed);

        try
        {
            var metadataService = new StatutoryEvidenceMetadataService(new StatutoryEvidenceMetadataRepository(ConnectionString));
            var uploadService = new StatutoryEvidenceUploadService(
                new StatutoryEvidenceMetadataRepository(ConnectionString),
                new FakeStorageAdapter(),
                UploadOptions());

            var created = await metadataService.CreateOrResolveSetAsync(CreateSetCommand(context, seed), CancellationToken.None);
            var item = await metadataService.AddItemAsync(AddItemCommand(created.EvidenceSet!.EvidenceSetReference, seed), CancellationToken.None);
            var authorized = await uploadService.AuthorizeUploadAsync(AuthorizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem!.EvidenceItemReference, seed), CancellationToken.None);
            await uploadService.FinalizeUploadAsync(FinalizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem.EvidenceItemReference, authorized.UploadAuthorization!.UploadAuthorizationReference, seed), CancellationToken.None);

            var repository = new StatutoryEvidenceScanRepository(ConnectionString);
            var claimed = await repository.ClaimDueWorkAsync("worker-a", seed.ActorServiceIdentityId, 1, TimeSpan.FromMinutes(2), DateTimeOffset.UtcNow, CancellationToken.None);
            claimed.Should().ContainSingle();

            await AdvanceItemVersionAsync(item.EvidenceItem.EvidenceItemReference);
            var advancedStatus = await ReadItemStatusAsync(item.EvidenceItem.EvidenceItemReference);

            await repository.CompleteAttemptAsync(
                claimed[0],
                new StatutoryEvidenceScanCompletion("COMPLETED", "PASSED", "PASSED", "CLEAN", "CLEAN", null, false, true),
                seed.ActorServiceIdentityId,
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            var status = await ReadItemStatusAsync(item.EvidenceItem.EvidenceItemReference);
            status.ValidationStatus.Should().Be("PENDING");
            status.ScanStatus.Should().Be("PENDING");
            status.ReviewabilityStatus.Should().Be("NOT_REVIEWABLE");
            status.RowVersion.Should().Be(advancedStatus.RowVersion);

            var attempt = await ReadAttemptStatusAsync(claimed[0].ScanAttemptReference);
            attempt.AttemptStatus.Should().Be("STALE_REJECTED");
            attempt.ValidationResult.Should().Be("STALE_OBJECT_VERSION");
            attempt.SafeFailureClassification.Should().Be("STALE_OBJECT_VERSION");
            attempt.NextRetryAt.Should().BeNull();
            (await CountStaleAttemptEventsAsync(context.ParkingSessionId)).Should().Be(1);
            var reclaimed = await repository.ClaimDueWorkAsync("worker-c", seed.ActorServiceIdentityId, 1, TimeSpan.FromMinutes(2), DateTimeOffset.UtcNow.AddMinutes(3), CancellationToken.None);
            reclaimed.Should().BeEmpty();
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static async Task<(Guid ItemReference, Guid SetReference)> CreateFinalizedEvidenceItemAsync(PaymentTestContext context, EvidenceSeed seed)
    {
        var metadataService = new StatutoryEvidenceMetadataService(new StatutoryEvidenceMetadataRepository(ConnectionString));
        var uploadService = new StatutoryEvidenceUploadService(
            new StatutoryEvidenceMetadataRepository(ConnectionString),
            new FakeStorageAdapter(),
            UploadOptions());

        var created = await metadataService.CreateOrResolveSetAsync(CreateSetCommand(context, seed), CancellationToken.None);
        created.Classification.Should().Be("ACCEPTED");
        var item = await metadataService.AddItemAsync(AddItemCommand(created.EvidenceSet!.EvidenceSetReference, seed), CancellationToken.None);
        item.Classification.Should().Be("ACCEPTED");
        var authorized = await uploadService.AuthorizeUploadAsync(AuthorizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem!.EvidenceItemReference, seed), CancellationToken.None);
        authorized.Classification.Should().Be("ACCEPTED");
        var finalized = await uploadService.FinalizeUploadAsync(FinalizeCommand(created.EvidenceSet.EvidenceSetReference, item.EvidenceItem.EvidenceItemReference, authorized.UploadAuthorization!.UploadAuthorizationReference, seed), CancellationToken.None);
        finalized.Classification.Should().Be("ACCEPTED");
        return (item.EvidenceItem.EvidenceItemReference, created.EvidenceSet.EvidenceSetReference);
    }
    private static StatutoryEvidenceUploadOptions UploadOptions() =>
        new()
        {
            Endpoint = "http://127.0.0.1:1",
            PublicUploadEndpoint = "http://127.0.0.1:1",
            BucketName = "private-evidence",
            BucketReference = "configured-private-evidence-bucket",
            AccessKeyId = "synthetic-access-key",
            SecretAccessKey = "synthetic-secret-key",
            MaxContentLengthBytes = 5_000_000,
            AuthorizationTtlSeconds = 300
        };

    private static StatutoryEvidenceCreateSetCommand CreateSetCommand(PaymentTestContext context, EvidenceSeed seed) =>
        new(
            seed.DecisionCommandId,
            null,
            context.ParkingSessionId,
            context.SiteId,
            context.SiteGroupId,
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID_FRONT_BACK_V1",
            "1",
            seed.RetentionClassCode,
            seed.RetentionPolicyVersion,
            "LOCAL_TEST",
            seed.Scope,
            "create-set",
            seed.CorrelationId,
            new StatutoryEvidenceActor(null, seed.ActorServiceIdentityId, "WEBPAY"));

    private static StatutoryEvidenceAddItemCommand AddItemCommand(Guid evidenceSetReference, EvidenceSeed seed) =>
        new(
            evidenceSetReference,
            "SENIOR_CITIZEN_ID",
            "FRONT",
            "IMAGE_JPEG",
            "image/jpeg",
            "SENIOR_CITIZEN_ID_FRONT_BACK_V1",
            seed.Scope,
            "add-item",
            seed.CorrelationId,
            new StatutoryEvidenceActor(null, seed.ActorServiceIdentityId, "WEBPAY"));

    private static StatutoryEvidenceUploadAuthorizationCommand AuthorizeCommand(Guid evidenceSetReference, Guid evidenceItemReference, EvidenceSeed seed) =>
        new(
            evidenceSetReference,
            evidenceItemReference,
            "image/jpeg",
            1024,
            "IMAGE_JPEG",
            "SHA256",
            Hash,
            seed.Scope,
            "upload-auth",
            seed.CorrelationId,
            new StatutoryEvidenceActor(null, seed.ActorServiceIdentityId, "WEBPAY"));

    private static StatutoryEvidenceUploadFinalizationCommand FinalizeCommand(Guid evidenceSetReference, Guid evidenceItemReference, Guid uploadAuthorizationReference, EvidenceSeed seed) =>
        new(
            evidenceSetReference,
            evidenceItemReference,
            uploadAuthorizationReference,
            seed.Scope,
            "upload-finalize",
            seed.CorrelationId,
            new StatutoryEvidenceActor(null, seed.ActorServiceIdentityId, "WEBPAY"));

    private static async Task EnsureScanSchemaPresentAsync()
    {
        await StatutoryDiscountCanonicalSchemaPrerequisite.EnsurePresentAsync(ConnectionString);
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass('discounts.statutory_evidence_scan_attempts') IS NOT NULL;", connection);
        ((bool)(await command.ExecuteScalarAsync() ?? false)).Should().BeTrue();
    }

    private static async Task SeedEvidencePrerequisitesAsync(PaymentTestContext context, EvidenceSeed seed)
    {
        const string sql = """
            INSERT INTO discounts.statutory_evidence_retention_policies (
                retention_class_code, retention_policy_version, policy_status, environment_scope,
                purpose_code, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
            VALUES (
                @retention_class_code, @retention_policy_version, 'APPROVED_ENABLED', 'LOCAL_TEST',
                'STATUTORY_EVIDENCE_SCAN_INTEGRATION_TEST', now() - interval '1 minute',
                @actor_service_identity_id, @actor_service_identity_id)
            ON CONFLICT (retention_class_code, retention_policy_version) DO UPDATE
            SET policy_status = EXCLUDED.policy_status,
                environment_scope = EXCLUDED.environment_scope,
                updated_at = now(),
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                row_version = discounts.statutory_evidence_retention_policies.row_version + 1;

            INSERT INTO discounts.statutory_discount_decision_commands (
                statutory_discount_decision_command_id, request_reference, parking_session_id,
                source_channel, entitlement_type, idempotency_scope, idempotency_key,
                semantic_request_hash, semantic_hash_source_version, decision_status,
                command_status, decision_result_status, result_classification, retryable,
                recovery_classification, evidence_required, evidence_recorded,
                original_correlation_id, created_at, updated_at)
            VALUES (
                @decision_command_id, @request_reference, @parking_session_id,
                'WEBPAY', 'SENIOR_CITIZEN', @decision_scope, @decision_key,
                @decision_hash, 'statutory-discount-decision:sha256:v2', 'PENDING_OPERATOR_REVIEW',
                'AWAITING_REVIEW', 'NOT_DECIDED', 'ACCEPTED', false,
                'AWAITING_REVIEW', true, false,
                @correlation_id, now(), now());

            INSERT INTO discounts.statutory_evidence_principal_scope_grants (
                actor_service_identity_id, source_channel, site_id, site_group_id,
                capture_allowed, view_allowed, hold_allowed, deletion_request_allowed,
                reason_code, created_by_service_identity_id, updated_by_service_identity_id)
            VALUES (
                @actor_service_identity_id, 'WEBPAY', @site_id, @site_group_id,
                true, true, false, false,
                'I015_SCAN_TEST', @actor_service_identity_id, @actor_service_identity_id);
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("retention_class_code", seed.RetentionClassCode);
        command.Parameters.AddWithValue("retention_policy_version", seed.RetentionPolicyVersion);
        command.Parameters.AddWithValue("actor_service_identity_id", seed.ActorServiceIdentityId);
        command.Parameters.AddWithValue("decision_command_id", seed.DecisionCommandId);
        command.Parameters.AddWithValue("request_reference", seed.RequestReference);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("decision_scope", seed.Scope);
        command.Parameters.AddWithValue("decision_key", "decision");
        command.Parameters.AddWithValue("decision_hash", "sha256:" + new string('1', 64));
        command.Parameters.AddWithValue("correlation_id", seed.CorrelationId);
        command.Parameters.AddWithValue("site_id", context.SiteId);
        command.Parameters.AddWithValue("site_group_id", context.SiteGroupId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupEvidenceRowsAsync(PaymentTestContext context, EvidenceSeed seed)
    {
        const string sql = """
            DELETE FROM discounts.statutory_evidence_events
            WHERE correlation_id = @correlation_id
               OR parking_session_id = @parking_session_id
               OR actor_service_identity_id = @actor_service_identity_id;

            DELETE FROM discounts.statutory_evidence_scan_attempts
            WHERE statutory_evidence_set_id IN (
                SELECT statutory_evidence_set_id
                FROM discounts.statutory_evidence_sets
                WHERE statutory_discount_decision_command_id = @decision_command_id
                   OR parking_session_id = @parking_session_id
            );

            DELETE FROM discounts.statutory_evidence_upload_authorizations
            WHERE statutory_evidence_set_id IN (
                SELECT statutory_evidence_set_id
                FROM discounts.statutory_evidence_sets
                WHERE statutory_discount_decision_command_id = @decision_command_id
                   OR parking_session_id = @parking_session_id
            );

            DELETE FROM discounts.statutory_evidence_operations
            WHERE idempotency_scope = @scope
               OR created_by_service_identity_id = @actor_service_identity_id;

            DELETE FROM discounts.statutory_evidence_items
            WHERE statutory_evidence_set_id IN (
                SELECT statutory_evidence_set_id
                FROM discounts.statutory_evidence_sets
                WHERE statutory_discount_decision_command_id = @decision_command_id
                   OR parking_session_id = @parking_session_id
            );

            DELETE FROM discounts.statutory_evidence_sets
            WHERE statutory_discount_decision_command_id = @decision_command_id
               OR parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_evidence_principal_scope_grants
            WHERE actor_service_identity_id = @actor_service_identity_id
              AND reason_code = 'I015_SCAN_TEST';

            DELETE FROM discounts.statutory_discount_decision_commands
            WHERE statutory_discount_decision_command_id = @decision_command_id;

            DELETE FROM discounts.statutory_evidence_retention_policies
            WHERE retention_class_code = @retention_class_code
              AND retention_policy_version = @retention_policy_version;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("correlation_id", seed.CorrelationId);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("actor_service_identity_id", seed.ActorServiceIdentityId);
        command.Parameters.AddWithValue("scope", seed.Scope);
        command.Parameters.AddWithValue("decision_command_id", seed.DecisionCommandId);
        command.Parameters.AddWithValue("retention_class_code", seed.RetentionClassCode);
        command.Parameters.AddWithValue("retention_policy_version", seed.RetentionPolicyVersion);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<ItemStatus> ReadItemStatusAsync(Guid evidenceItemReference)
    {
        const string sql = """
            SELECT validation_status::text, scan_status::text, reviewability_status::text, row_version
            FROM discounts.statutory_evidence_items
            WHERE evidence_item_reference = @evidence_item_reference;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("evidence_item_reference", evidenceItemReference);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new ItemStatus(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3));
    }

    private static async Task AdvanceItemVersionAsync(Guid evidenceItemReference)
    {
        const string sql = """
            UPDATE discounts.statutory_evidence_items
               SET updated_at = now(),
                   row_version = row_version + 1
             WHERE evidence_item_reference = @evidence_item_reference;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("evidence_item_reference", evidenceItemReference);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static async Task<ScanAttemptStatusSnapshot> ReadAttemptStatusAsync(Guid scanAttemptReference)
    {
        const string sql = """
            SELECT attempt_status::text, validation_result::text, safe_failure_classification, next_retry_at
            FROM discounts.statutory_evidence_scan_attempts
            WHERE scan_attempt_reference = @scan_attempt_reference;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("scan_attempt_reference", scanAttemptReference);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new ScanAttemptStatusSnapshot(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetDateTime(3));
    }

    private static async Task<long> CountStaleAttemptEventsAsync(Guid parkingSessionId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_evidence_events
            WHERE parking_session_id = @parking_session_id
              AND event_type = 'STALE_OBJECT_ATTEMPT_REJECTED';
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<long> CountScanEventsAsync(Guid parkingSessionId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_evidence_events
            WHERE parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }
    private static async Task<long> CountScanAttemptsAsync(Guid parkingSessionId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM discounts.statutory_evidence_scan_attempts attempt
            JOIN discounts.statutory_evidence_sets evidence_set
              ON evidence_set.statutory_evidence_set_id = attempt.statutory_evidence_set_id
            WHERE evidence_set.parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private sealed record ItemStatus(string ValidationStatus, string ScanStatus, string ReviewabilityStatus, long RowVersion);

    private sealed record ScanAttemptStatusSnapshot(string AttemptStatus, string ValidationResult, string? SafeFailureClassification, DateTime? NextRetryAt);

    private sealed class FakeStorageAdapter : IStatutoryEvidenceProtectedObjectStorageAdapter
    {
        public Task<StatutoryEvidenceObjectUploadAuthorization> CreateUploadAuthorizationAsync(StatutoryEvidenceObjectUploadAuthorizationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new StatutoryEvidenceObjectUploadAuthorization(
                new Uri("http://127.0.0.1:1/upload"),
                new Dictionary<string, string>
                {
                    ["Content-Type"] = request.ContentType,
                    ["x-amz-checksum-sha256"] = request.ChecksumSha256
                }));

        public Task<StatutoryEvidenceObjectMetadata?> GetObjectMetadataAsync(StatutoryEvidenceObjectMetadataRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<StatutoryEvidenceObjectMetadata?>(new("image/jpeg", 1024, Hash, "v1", "AES256"));

        public Task<StatutoryEvidenceObjectContent> GetObjectContentAsync(StatutoryEvidenceObjectContentRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Object bytes are not required for repository persistence tests.");
    }

    private sealed record EvidenceSeed(
        Guid DecisionCommandId,
        Guid RequestReference,
        Guid ActorServiceIdentityId,
        Guid CorrelationId,
        string Scope,
        string RetentionClassCode,
        string RetentionPolicyVersion)
    {
        public static EvidenceSeed Create(PaymentTestContext context)
        {
            var suffix = context.SiteCode.Replace("-", "_", StringComparison.OrdinalIgnoreCase);
            return new EvidenceSeed(
                Guid.NewGuid(),
                Guid.NewGuid(),
                context.RequestedByUserId,
                context.CorrelationId,
                $"i015-scan-{suffix}",
                $"I015_EVIDENCE_SCAN_{suffix}",
                "v1");
        }
    }
}
