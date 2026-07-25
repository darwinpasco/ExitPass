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
            await using var lockConnection = new NpgsqlConnection(ConnectionString);
            await lockConnection.OpenAsync();
            await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext('statutory_discount_review_linkage_test_schema'));", lockConnection))
            {
                await lockCommand.ExecuteNonQueryAsync();
            }

            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "validation", "Validate_StatutoryDiscountStagedCanonicalCommands_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "validation", "Validate_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "validation", "Validate_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql");

            await using (var unlockCommand = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtext('statutory_discount_review_linkage_test_schema'));", lockConnection))
            {
                await unlockCommand.ExecuteNonQueryAsync();
            }
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
        bool seedPaymentContext = true)
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
            """,
            connection);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
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

    private static async Task ExecuteSqlFileAsync(params string[] pathParts)
    {
        var sql = await File.ReadAllTextAsync(ResolveRepoPath(pathParts));
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    private static string ResolveRepoPath(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, ".git")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException("Repository root could not be resolved.");
        }

        return Path.Combine(new[] { current.FullName }.Concat(pathParts).ToArray());
    }
}
