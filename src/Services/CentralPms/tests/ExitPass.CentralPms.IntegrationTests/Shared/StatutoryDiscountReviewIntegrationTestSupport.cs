using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

internal sealed record SeededServiceChannelReview(
    PaymentTestContext Context,
    StatutoryDiscountDecisionV2Record Decision,
    StatutoryDiscountServiceChannelReviewDetail Review);

internal static class StatutoryDiscountReviewIntegrationTestSupport
{
    private static readonly SemaphoreSlim PatchLock = new(1, 1);

    public static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    public static async Task EnsureSchemaAsync()
    {
        await PatchLock.WaitAsync();
        try
        {
            await StatutoryDiscountCanonicalSchemaPrerequisite.EnsurePresentAsync(ConnectionString);
        }
        finally
        {
            PatchLock.Release();
        }
    }

    public static async Task<SeededServiceChannelReview> SeedAwaitingReviewAsync(
        string scenarioName,
        string sourceChannel,
        Guid? siteId = null,
        Guid? siteGroupId = null,
        string entitlementType = "SENIOR_CITIZEN",
        bool seedPaymentContext = true,
        Guid? reviewerUserId = null)
    {
        await EnsureSchemaAsync();
        var context = PaymentTestContext.Create(scenarioName);
        if (siteId.HasValue || siteGroupId.HasValue)
        {
            context = context with
            {
                SiteId = siteId ?? context.SiteId,
                SiteGroupId = siteGroupId ?? context.SiteGroupId
            };
        }

        if (seedPaymentContext)
        {
            await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, $"Seed {scenarioName}.");
            await SeedReviewerUserAsync(context, context.RequestedByUserId);
        }

        if (reviewerUserId.HasValue && reviewerUserId.Value != context.RequestedByUserId)
        {
            await SeedReviewerUserAsync(context, reviewerUserId.Value);
        }

        var staged = CreateStagedService();
        var created = await staged.CreateOrResolveDecisionAsync(
            DecisionCommand(context, sourceChannel, entitlementType),
            CancellationToken.None);
        var awaiting = await staged.MarkDecisionAwaitingReviewAsync(
            created.Record!.StatutoryDiscountDecisionCommandId,
            context.CorrelationId,
            CancellationToken.None);

        var repository = CreateReviewRepository();
        await repository.UpsertIntakeAsync(IntakeCommand(context, awaiting, sourceChannel, entitlementType), CancellationToken.None);
        var detail = await repository.GetAsync(awaiting.StatutoryDiscountDecisionCommandId, context.CorrelationId, CancellationToken.None);

        return new SeededServiceChannelReview(context, awaiting, detail!);
    }

    public static async Task<PaymentTestContext> SeedPaymentContextAsync(string scenarioName)
    {
        await EnsureSchemaAsync();
        var context = PaymentTestContext.Create(scenarioName);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, $"Seed {scenarioName}.");
        await SeedReviewerUserAsync(context, context.RequestedByUserId);
        return context;
    }

    public static IStatutoryDiscountStagedCommandService CreateStagedService() =>
        new StatutoryDiscountStagedCommandService(new PostgresStatutoryDiscountStagedCommandRepository(ConnectionString));

    public static IStatutoryDiscountServiceChannelReviewRepository CreateReviewRepository() =>
        new PostgresStatutoryDiscountServiceChannelReviewRepository(ConnectionString);

    public static StatutoryDiscountDecisionV2Command DecisionCommand(
        PaymentTestContext context,
        string sourceChannel,
        string entitlementType = "SENIOR_CITIZEN",
        string idempotencyKey = "review-linkage-decision-key") =>
        new(
            Guid.NewGuid(),
            sourceChannel,
            context.ParkingSessionId,
            context.SiteId,
            context.SiteGroupId,
            $"TICKET-{context.SiteCode}",
            "ABC1234",
            entitlementType,
            new StatutoryDiscountDecisionV2BeneficiaryMetadata("beneficiary-ref", entitlementType, "DRIVER", 1),
            new StatutoryDiscountDecisionV2IdentityMetadata("SENIOR_CITIZEN_ID", "OSCA", DateOnly.Parse("2030-01-01"), "SC-****-1234", null),
            [Evidence("VERIFIED")],
            new StatutoryDiscountDecisionV2AttestationFacts(true, "attestation-ref", "CUSTOMER_REQUEST", ReviewerAttested: false),
            context.RequestedByUserId,
            ReviewerUserId: null,
            OperatorDeviceBindingId: null,
            OperatorShiftId: null,
            new StatutoryDiscountDecisionV2DecisionFacts(StatutoryDiscountDecisionV2ResultStates.NotDecided, null, null),
            PolicyResolutionReferenceId: null,
            AppliedPolicyReferenceId: null,
            FallbackPolicyReferenceId: null,
            PolicyResolutionBasis: "NATIONAL_DEFAULT",
            LocalOrdinanceApplied: false,
            context.TariffSnapshotId,
            new StatutoryDiscountDecisionV2TariffFacts(10000, 8929, 1071, 1786, 8214, "PHP"),
            idempotencyKey,
            context.CorrelationId);

    public static StatutoryDiscountServiceChannelReviewIntakeCommand IntakeCommand(
        PaymentTestContext context,
        StatutoryDiscountDecisionV2Record decision,
        string sourceChannel,
        string entitlementType = "SENIOR_CITIZEN") =>
        new(
            decision.StatutoryDiscountDecisionCommandId,
            decision.RequestReference,
            context.ParkingSessionId,
            sourceChannel,
            context.SiteId,
            context.SiteGroupId,
            $"TICKET-{context.SiteCode}",
            "ABC1234",
            entitlementType,
            "SENIOR_CITIZEN_ID",
            "OSCA",
            DateOnly.Parse("2030-01-01"),
            "SC-****-1234",
            [new StatutoryDiscountServiceChannelReviewEvidenceFact(
                "SENIOR_CITIZEN_ID",
                "MANUAL_REFERENCE",
                "evidence-ref-001",
                "SC-****-1234",
                "VERIFIED")],
            RequesterAttestation: true,
            AttestationNotes: "Customer attested statutory discount eligibility.",
            ReasonCode: "CUSTOMER_REQUEST",
            context.TariffSnapshotId,
            context.CorrelationId,
            DateTimeOffset.UtcNow);

    public static async Task CleanupAsync(PaymentTestContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM operator_console.statutory_discount_service_channel_reviews
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.discount_evidence_references
            WHERE statutory_discount_validation_id IN (
                SELECT statutory_discount_validation_id
                FROM discounts.statutory_discount_validations
                WHERE parking_session_id = @parking_session_id
            );

            UPDATE discounts.statutory_discount_validations
            SET
                tariff_snapshot_id = NULL,
                updated_at = NOW()
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.payment_attempts
            WHERE parking_session_id = @parking_session_id;

            UPDATE core.tariff_snapshots
            SET
                superseded_by_tariff_snapshot_id = NULL,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM core.tariff_snapshots
            WHERE parking_session_id = @parking_session_id
              AND statutory_discount_validation_id IN (
                  SELECT statutory_discount_validation_id
                  FROM discounts.statutory_discount_validations
                  WHERE parking_session_id = @parking_session_id
              );

            UPDATE core.tariff_snapshots
            SET
                snapshot_status = CASE
                    WHEN snapshot_status = 'SUPERSEDED' THEN 'ACTIVE'
                    ELSE snapshot_status
                END,
                statutory_discount_validation_id = NULL,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM identity.users
            WHERE user_id = @requested_by_user_id;
            """,
            connection);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("requested_by_user_id", context.RequestedByUserId);
        await command.ExecuteNonQueryAsync();

        await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
    }

    public static async Task<int> DecisionRowCountAsync(Guid parkingSessionId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @id;
            """,
            parkingSessionId);

    public static async Task<int> ApplicationCommandRowCountAsync(Guid decisionCommandId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE statutory_discount_decision_command_id = @id;
            """,
            decisionCommandId);

    public static async Task<int> PayableBasisApplicationRowCountAsync(Guid parkingSessionId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @id;
            """,
            parkingSessionId);

    public static async Task<int> AppliedTariffSnapshotRowCountAsync(Guid parkingSessionId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM core.tariff_snapshots
            WHERE parking_session_id = @id
              AND statutory_discount_validation_id IS NOT NULL;
            """,
            parkingSessionId);

    public static async Task<int> PaymentBoundaryRowCountAsync(Guid parkingSessionId) =>
        await CountAsync(
            """
            SELECT
                (
                    SELECT COUNT(*)::int
                    FROM core.payment_attempts
                    WHERE parking_session_id = @id
                )
                +
                (
                    SELECT COUNT(*)::int
                    FROM core.payment_confirmations AS pc
                    INNER JOIN core.payment_attempts AS pa
                        ON pa.payment_attempt_id = pc.payment_attempt_id
                    WHERE pa.parking_session_id = @id
                )
                +
                (
                    SELECT COUNT(*)::int
                    FROM core.exit_authorizations
                    WHERE parking_session_id = @id
                )
                +
                (
                    SELECT COUNT(*)::int
                    FROM core.fiscal_issuance_references
                    WHERE parking_session_id = @id
                );
            """,
            parkingSessionId);

    public static async Task<Guid?> ValidationIdForDecisionAsync(Guid statutoryDiscountDecisionCommandId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT statutory_discount_validation_id
            FROM discounts.statutory_discount_decision_commands
            WHERE statutory_discount_decision_command_id = @id;
            """,
            connection);
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = statutoryDiscountDecisionCommandId;
        var value = await command.ExecuteScalarAsync();
        return value is DBNull or null ? null : (Guid)value;
    }

    public static async Task<int> ReviewRowCountAsync(Guid decisionCommandId) =>
        await CountAsync(
            """
            SELECT COUNT(*)::int
            FROM operator_console.statutory_discount_service_channel_reviews
            WHERE statutory_discount_decision_command_id = @id;
            """,
            decisionCommandId);

    public static async Task<IReadOnlyList<string>> SensitiveReviewColumnNamesAsync()
    {
        const string sql = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'operator_console'
              AND table_name = 'statutory_discount_service_channel_reviews'
              AND (
                  column_name ILIKE '%base64%'
                  OR column_name ILIKE '%image%'
                  OR column_name ILIKE '%raw%'
                  OR column_name ILIKE '%full%id%'
                  OR column_name ILIKE '%identity_value%'
              )
            ORDER BY column_name;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static StatutoryDiscountDecisionV2EvidenceReference Evidence(string status) =>
        new(
            "SENIOR_CITIZEN_ID",
            "MANUAL_REFERENCE",
            "evidence-ref-001",
            "SC-****-1234",
            status,
            "verification-ref-001",
            DateTimeOffset.Parse("2026-07-21T01:00:00Z"));

    private static async Task<int> CountAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task SeedReviewerUserAsync(PaymentTestContext context, Guid reviewerUserId)
    {
        const string sql = """
            INSERT INTO identity.users (
                user_id,
                username,
                email,
                email_normalized,
                display_name,
                user_type,
                user_status,
                effective_from,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @user_id,
                @username,
                @email,
                @email_normalized,
                @display_name,
                'SITE_OPERATOR'::identity.user_type_enum,
                'ACTIVE'::identity.user_status_enum,
                NOW() - INTERVAL '1 minute',
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            )
            ON CONFLICT (user_id) DO UPDATE
            SET
                username = EXCLUDED.username,
                email = EXCLUDED.email,
                email_normalized = EXCLUDED.email_normalized,
                display_name = EXCLUDED.display_name,
                user_type = EXCLUDED.user_type,
                user_status = EXCLUDED.user_status,
                updated_at = NOW(),
                updated_by_service_identity_id = EXCLUDED.updated_by_service_identity_id,
                row_version = identity.users.row_version + 1;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = reviewerUserId;
        command.Parameters.AddWithValue("username", $"stat-disc-reviewer-{reviewerUserId:N}");
        command.Parameters.AddWithValue("email", $"stat-disc-reviewer-{reviewerUserId:N}@example.test");
        command.Parameters.AddWithValue("email_normalized", $"STAT-DISC-REVIEWER-{reviewerUserId:N}@EXAMPLE.TEST");
        command.Parameters.AddWithValue("display_name", $"Statutory discount reviewer {context.SiteCode}");
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = context.RequestedByUserId;
        await command.ExecuteNonQueryAsync();
    }

}
