using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Infrastructure.StatutoryEvidence;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class StatutoryEvidenceMetadataRepositoryIntegrationTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task CreateOrResolveSet_UsesDurableDecisionBindingAndServerScope()
    {
        await EnsureEvidenceSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(CreateOrResolveSet_UsesDurableDecisionBindingAndServerScope));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence metadata binding test data.");
        await SeedEvidencePrerequisitesAsync(context, seed, captureAllowed: true);

        try
        {
            var service = CreateService();
            var before = await ReadEvidenceCountsAsync();

            var result = await service.CreateOrResolveSetAsync(CreateSetCommand(context, seed), CancellationToken.None);

            result.Classification.Should().Be("ACCEPTED");
            result.EvidenceSet!.ParkingSessionId.Should().Be(context.ParkingSessionId);
            result.EvidenceSet.SiteId.Should().Be(context.SiteId);
            result.EvidenceSet.SiteGroupId.Should().Be(context.SiteGroupId);
            result.EvidenceSet.SourceChannel.Should().Be("WEBPAY");

            var after = await ReadEvidenceCountsAsync();
            after.Sets.Should().Be(before.Sets + 1);
            after.Operations.Should().Be(before.Operations + 1);
            after.Events.Should().Be(before.Events + 1);
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task CreateOrResolveSet_WhenCallerSiteDoesNotMatchDurableBinding_DeniesWithoutWorkflowMutation()
    {
        await EnsureEvidenceSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(CreateOrResolveSet_WhenCallerSiteDoesNotMatchDurableBinding_DeniesWithoutWorkflowMutation));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence metadata mismatch test data.");
        await SeedEvidencePrerequisitesAsync(context, seed, captureAllowed: true);

        try
        {
            var service = CreateService();
            var before = await ReadEvidenceCountsAsync();

            var result = await service.CreateOrResolveSetAsync(
                CreateSetCommand(context, seed) with { SiteId = Guid.NewGuid() },
                CancellationToken.None);

            result.Classification.Should().Be("REJECTED");
            result.ErrorCode.Should().Be("BINDING_MISMATCH");

            var after = await ReadEvidenceCountsAsync();
            after.Sets.Should().Be(before.Sets);
            after.Items.Should().Be(before.Items);
            after.Operations.Should().Be(before.Operations);
            after.Events.Should().Be(before.Events + 1);
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task OpaqueReferenceAccess_WhenPrincipalIsOutOfScope_DeniesWithoutRowVersionOrOperationMutation()
    {
        await EnsureEvidenceSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(OpaqueReferenceAccess_WhenPrincipalIsOutOfScope_DeniesWithoutRowVersionOrOperationMutation));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence metadata out-of-scope test data.");
        await SeedEvidencePrerequisitesAsync(context, seed, captureAllowed: true, viewAllowed: false);

        try
        {
            var service = CreateService();
            var created = await service.CreateOrResolveSetAsync(CreateSetCommand(context, seed), CancellationToken.None);
            var before = await ReadEvidenceCountsAsync();
            var rowVersionBefore = await ReadSetRowVersionAsync(created.EvidenceSet!.EvidenceSetReference);

            var read = await service.GetEvidenceSetAsync(
                created.EvidenceSet.EvidenceSetReference,
                new StatutoryEvidenceActor(null, seed.ActorServiceIdentityId, "WEBPAY"),
                seed.CorrelationId,
                CancellationToken.None);

            read.Should().BeNull();
            var rowVersionAfter = await ReadSetRowVersionAsync(created.EvidenceSet.EvidenceSetReference);
            rowVersionAfter.Should().Be(rowVersionBefore);

            var after = await ReadEvidenceCountsAsync();
            after.Sets.Should().Be(before.Sets);
            after.Items.Should().Be(before.Items);
            after.Operations.Should().Be(before.Operations);
            after.Events.Should().Be(before.Events + 1);
            (await ReadDeniedEventReasonAsync(seed.CorrelationId)).Should().Be("SCOPE_DENIED");
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task HoldGrant_DoesNotBroadenViewAuthority()
    {
        await EnsureEvidenceSchemaPresentAsync();
        var context = PaymentTestContext.Create(nameof(HoldGrant_DoesNotBroadenViewAuthority));
        var seed = EvidenceSeed.Create(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory evidence metadata hold-scope test data.");
        await SeedEvidencePrerequisitesAsync(context, seed, captureAllowed: true, holdAllowed: true, viewAllowed: false);

        try
        {
            var service = CreateService();
            var created = await service.CreateOrResolveSetAsync(CreateSetCommand(context, seed), CancellationToken.None);

            var held = await service.PlaceHoldAsync(
                new StatutoryEvidenceHoldCommand(
                    created.EvidenceSet!.EvidenceSetReference,
                    "INVESTIGATION",
                    seed.Scope,
                    "hold",
                    seed.CorrelationId,
                    new StatutoryEvidenceActor(null, seed.ActorServiceIdentityId, "WEBPAY")),
                CancellationToken.None);
            var read = await service.GetEvidenceSetAsync(
                created.EvidenceSet.EvidenceSetReference,
                new StatutoryEvidenceActor(null, seed.ActorServiceIdentityId, "WEBPAY"),
                seed.CorrelationId,
                CancellationToken.None);

            held.Classification.Should().Be("ACCEPTED");
            held.EvidenceSet!.HoldActive.Should().BeTrue();
            read.Should().BeNull();
        }
        finally
        {
            await CleanupEvidenceRowsAsync(context, seed);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static StatutoryEvidenceMetadataService CreateService() =>
        new(new StatutoryEvidenceMetadataRepository(ConnectionString));

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

    private static async Task EnsureEvidenceSchemaPresentAsync()
    {
        await StatutoryDiscountCanonicalSchemaPrerequisite.EnsurePresentAsync(ConnectionString);
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var required = new[]
        {
            "discounts.statutory_evidence_sets",
            "discounts.statutory_evidence_items",
            "discounts.statutory_evidence_operations",
            "discounts.statutory_evidence_events",
            "discounts.statutory_evidence_principal_scope_grants"
        };

        foreach (var table in required)
        {
            await using var command = new NpgsqlCommand("SELECT to_regclass(@table_name) IS NOT NULL;", connection);
            command.Parameters.AddWithValue("table_name", table);
            ((bool)(await command.ExecuteScalarAsync() ?? false)).Should().BeTrue($"{table} must exist in the canonical disposable database");
        }
    }

    private static async Task SeedEvidencePrerequisitesAsync(
        PaymentTestContext context,
        EvidenceSeed seed,
        bool captureAllowed,
        bool viewAllowed = true,
        bool holdAllowed = false,
        bool deletionAllowed = false)
    {
        const string sql = """
            INSERT INTO discounts.statutory_evidence_retention_policies (
                retention_class_code, retention_policy_version, policy_status, environment_scope,
                purpose_code, effective_from, created_by_service_identity_id, updated_by_service_identity_id)
            VALUES (
                @retention_class_code, @retention_policy_version, 'APPROVED_ENABLED', 'LOCAL_TEST',
                'STATUTORY_EVIDENCE_METADATA_INTEGRATION_TEST', now() - interval '1 minute',
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
                @capture_allowed, @view_allowed, @hold_allowed, @deletion_allowed,
                'I012_SCOPE_TEST', @actor_service_identity_id, @actor_service_identity_id);
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
        command.Parameters.AddWithValue("capture_allowed", captureAllowed);
        command.Parameters.AddWithValue("view_allowed", viewAllowed);
        command.Parameters.AddWithValue("hold_allowed", holdAllowed);
        command.Parameters.AddWithValue("deletion_allowed", deletionAllowed);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupEvidenceRowsAsync(PaymentTestContext context, EvidenceSeed seed)
    {
        const string sql = """
            DELETE FROM discounts.statutory_evidence_events
            WHERE correlation_id = @correlation_id
               OR parking_session_id = @parking_session_id
               OR actor_service_identity_id = @actor_service_identity_id;

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
              AND reason_code = 'I012_SCOPE_TEST';

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

    private static async Task<EvidenceCounts> ReadEvidenceCountsAsync()
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM discounts.statutory_evidence_sets),
                (SELECT COUNT(*) FROM discounts.statutory_evidence_items),
                (SELECT COUNT(*) FROM discounts.statutory_evidence_operations),
                (SELECT COUNT(*) FROM discounts.statutory_evidence_events);
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new EvidenceCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static async Task<long> ReadSetRowVersionAsync(Guid evidenceSetReference)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT row_version
            FROM discounts.statutory_evidence_sets
            WHERE evidence_set_reference = @evidence_set_reference;
            """,
            connection);
        command.Parameters.AddWithValue("evidence_set_reference", evidenceSetReference);
        return (long)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Evidence set row was not found."));
    }

    private static async Task<string?> ReadDeniedEventReasonAsync(Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT safe_reason_code
            FROM discounts.statutory_evidence_events
            WHERE correlation_id = @correlation_id
              AND event_result = 'DENIED'::discounts.statutory_evidence_event_result_enum
            ORDER BY occurred_at DESC
            LIMIT 1;
            """,
            connection);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        return (string?)await command.ExecuteScalarAsync();
    }

    private sealed record EvidenceCounts(long Sets, long Items, long Operations, long Events);

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
                $"i012-evidence-{suffix}",
                $"I012_EVIDENCE_REVIEW_{suffix}",
                "v1");
        }
    }
}
