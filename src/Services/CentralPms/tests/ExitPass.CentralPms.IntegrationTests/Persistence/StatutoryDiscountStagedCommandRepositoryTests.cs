using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class StatutoryDiscountStagedCommandRepositoryTests
{
    private static readonly SemaphoreSlim PatchLock = new(1, 1);

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task Patch_AppliesAndValidatesStagedCommandObjects()
    {
        await EnsurePatchAppliedAndValidatedAsync();

        (await TableExistsAsync("discounts.statutory_discount_decision_commands")).Should().BeTrue();
        (await TableExistsAsync("discounts.statutory_discount_payable_basis_application_commands")).Should().BeTrue();
        (await IndexExistsAsync("discounts", "ux_stat_discount_pba_commands__decision_command")).Should().BeTrue();
    }

    [Fact]
    public async Task DecisionV1AndDecisionV2Records_CanCoexistAndRemainReadable()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var v1Context = PaymentTestContext.Create(nameof(DecisionV1AndDecisionV2Records_CanCoexistAndRemainReadable) + "V1");
        var v2Context = PaymentTestContext.Create(nameof(DecisionV1AndDecisionV2Records_CanCoexistAndRemainReadable) + "V2");
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, v1Context, "Seed decision-v1 coexistence data.");
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, v2Context, "Seed decision-v2 coexistence data.");

        try
        {
            var facadeRepository = new PostgresStatutoryDiscountDecisionFacadeRepository(ConnectionString);
            var stagedService = CreateService();
            var v1 = FacadeRepositoryCommand(v1Context);
            var v2Command = DecisionCommand(v2Context);

            var v1Result = await facadeRepository.ExecuteWithCommandLockAsync(
                v1,
                token => facadeRepository.BeginAsync(v1, token),
                CancellationToken.None);
            var v2Result = await stagedService.CreateOrResolveDecisionAsync(v2Command, CancellationToken.None);

            v1Result.Record.SemanticHashSourceVersion.Should().Be(StatutoryDiscountDecisionSemanticHash.SourceVersion);
            v2Result.Record!.SemanticHashSourceVersion.Should().Be(StatutoryDiscountDecisionV2SemanticHash.SourceVersion);
            (await DecisionSourceVersionsAsync(v1Context.ParkingSessionId, v2Context.ParkingSessionId))
                .Should()
                .BeEquivalentTo([StatutoryDiscountDecisionSemanticHash.SourceVersion, StatutoryDiscountDecisionV2SemanticHash.SourceVersion]);
        }
        finally
        {
            await CleanupCommandRowsAsync(v1Context.ParkingSessionId);
            await CleanupCommandRowsAsync(v2Context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, v1Context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, v2Context);
        }
    }

    [Fact]
    public async Task DecisionV2_WhenSameBusinessRequestReplays_ReturnsExistingWithoutDuplicateInsert()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var context = PaymentTestContext.Create(nameof(DecisionV2_WhenSameBusinessRequestReplays_ReturnsExistingWithoutDuplicateInsert));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed staged decision replay data.");

        try
        {
            var service = CreateService();
            var first = await service.CreateOrResolveDecisionAsync(DecisionCommand(context), CancellationToken.None);
            var replay = await service.CreateOrResolveDecisionAsync(DecisionCommand(
                context,
                requestReference: Guid.NewGuid(),
                idempotencyKey: "different-key"), CancellationToken.None);

            replay.Existing.Should().BeTrue();
            replay.SemanticConflict.Should().BeFalse();
            replay.Record!.StatutoryDiscountDecisionCommandId.Should().Be(first.Record!.StatutoryDiscountDecisionCommandId);
            (await DecisionRowCountAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
            await CleanupCommandRowsAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task DecisionV2_WhenMaterialFactsChange_ReturnsDeterministicConflict()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var context = PaymentTestContext.Create(nameof(DecisionV2_WhenMaterialFactsChange_ReturnsDeterministicConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed staged decision conflict data.");

        try
        {
            var service = CreateService();
            await service.CreateOrResolveDecisionAsync(DecisionCommand(context), CancellationToken.None);

            var conflict = await service.CreateOrResolveDecisionAsync(
                DecisionCommand(context, idempotencyKey: "changed-key") with
                {
                    EvidenceReferences = [Evidence("REJECTED")]
                },
                CancellationToken.None);

            conflict.SemanticConflict.Should().BeTrue();
            conflict.SafeErrorCode.Should().Be("STATUTORY_DISCOUNT_DECISION_SEMANTIC_CONFLICT");
            (await DecisionRowCountAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
            await CleanupCommandRowsAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task DecisionV2_WhenConcurrentSameBusinessCommandsRun_CreatesOneCommand()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var context = PaymentTestContext.Create(nameof(DecisionV2_WhenConcurrentSameBusinessCommandsRun_CreatesOneCommand));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed staged decision concurrency data.");

        try
        {
            var service = CreateService();
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index =>
                service.CreateOrResolveDecisionAsync(
                    DecisionCommand(context, requestReference: Guid.NewGuid(), idempotencyKey: $"decision-key-{index}"),
                    CancellationToken.None)));

            results.Select(result => result.Record!.StatutoryDiscountDecisionCommandId)
                .Distinct()
                .Should()
                .ContainSingle();
            (await DecisionRowCountAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
            await CleanupCommandRowsAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task DecisionV2_WhenMarkedAwaitingReview_PersistsPendingReviewState()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var context = PaymentTestContext.Create(nameof(DecisionV2_WhenMarkedAwaitingReview_PersistsPendingReviewState));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed staged pending-review decision data.");

        try
        {
            var service = CreateService();
            var created = await service.CreateOrResolveDecisionAsync(
                DecisionCommand(context, decision: StatutoryDiscountDecisionV2ResultStates.NotDecided),
                CancellationToken.None);

            var awaitingReview = await service.MarkDecisionAwaitingReviewAsync(
                created.Record!.StatutoryDiscountDecisionCommandId,
                context.CorrelationId,
                CancellationToken.None);
            var readback = await service.GetDecisionAsync(
                awaitingReview.StatutoryDiscountDecisionCommandId,
                CancellationToken.None);

            readback.Should().NotBeNull();
            readback!.CommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.AwaitingReview);
            readback.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
            readback.ResultClassification.Should().Be(StatutoryDiscountOneShotResultClassifications.AwaitingReview);
            readback.Retryable.Should().BeFalse();
            readback.RecoveryClassification.Should().Be(StatutoryDiscountDecisionRecoveryClassifications.AwaitingReview);
            (await ApplicationRowCountAsync(awaitingReview.StatutoryDiscountDecisionCommandId)).Should().Be(0);
        }
        finally
        {
            await CleanupCommandRowsAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ApplicationV1_ForApprovedDecision_ReplaysAndConflictsWithoutDuplicateApplicationCommand()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var context = PaymentTestContext.Create(nameof(ApplicationV1_ForApprovedDecision_ReplaysAndConflictsWithoutDuplicateApplicationCommand));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed staged application replay data.");

        try
        {
            var service = CreateService();
            var decision = await CreateApprovedDecisionAsync(service, context);
            var command = ApplicationCommand(context, decision.StatutoryDiscountDecisionCommandId);

            var created = await service.CreateOrResolveApplicationAsync(command, CancellationToken.None);
            var replay = await service.CreateOrResolveApplicationAsync(command with
            {
                RequestReference = Guid.NewGuid(),
                IdempotencyKey = "another-key"
            }, CancellationToken.None);
            var conflict = await service.CreateOrResolveApplicationAsync(command with
            {
                RequestReference = Guid.NewGuid(),
                IdempotencyKey = "conflict-key",
                ApprovedFinalPayableAmountMinorUnits = 8000
            }, CancellationToken.None);

            created.Record.Should().NotBeNull();
            replay.Existing.Should().BeTrue();
            replay.SemanticConflict.Should().BeFalse();
            conflict.SemanticConflict.Should().BeTrue();
            (await ApplicationRowCountAsync(decision.StatutoryDiscountDecisionCommandId)).Should().Be(1);
        }
        finally
        {
            await CleanupCommandRowsAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ApplicationV1_WhenDecisionRejectedOrMissing_ReturnsSafeResultWithoutInsert()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var context = PaymentTestContext.Create(nameof(ApplicationV1_WhenDecisionRejectedOrMissing_ReturnsSafeResultWithoutInsert));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed staged application rejected data.");

        try
        {
            var service = CreateService();
            var created = await service.CreateOrResolveDecisionAsync(DecisionCommand(context, decision: "REJECT"), CancellationToken.None);
            var rejectedDecision = await service.CompleteDecisionRejectedAsync(
                created.Record!.StatutoryDiscountDecisionCommandId,
                "INELIGIBLE",
                "STATUTORY_DISCOUNT_INELIGIBLE",
                context.CorrelationId,
                CancellationToken.None);

            var rejected = await service.CreateOrResolveApplicationAsync(
                ApplicationCommand(context, rejectedDecision.StatutoryDiscountDecisionCommandId),
                CancellationToken.None);
            var missing = await service.CreateOrResolveApplicationAsync(
                ApplicationCommand(context, Guid.NewGuid()),
                CancellationToken.None);

            rejected.ResultClassification.Should().Be(StatutoryDiscountPayableBasisApplicationV1ResultClassifications.DecisionNotApproved);
            missing.ResultClassification.Should().Be(StatutoryDiscountPayableBasisApplicationV1ResultClassifications.DecisionNotFound);
            (await ApplicationRowCountAsync(rejectedDecision.StatutoryDiscountDecisionCommandId)).Should().Be(0);
        }
        finally
        {
            await CleanupCommandRowsAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task ApplicationV1_WhenConcurrentSameDecisionCommandsRun_CreatesOneCommand()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var context = PaymentTestContext.Create(nameof(ApplicationV1_WhenConcurrentSameDecisionCommandsRun_CreatesOneCommand));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed staged application concurrency data.");

        try
        {
            var service = CreateService();
            var decision = await CreateApprovedDecisionAsync(service, context);

            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index =>
                service.CreateOrResolveApplicationAsync(
                    ApplicationCommand(
                        context,
                        decision.StatutoryDiscountDecisionCommandId,
                        requestReference: Guid.NewGuid(),
                        idempotencyKey: $"app-key-{index}"),
                    CancellationToken.None)));

            results.Select(result => result.Record!.StatutoryDiscountPayableBasisApplicationCommandId)
                .Distinct()
                .Should()
                .ContainSingle();
            (await ApplicationRowCountAsync(decision.StatutoryDiscountDecisionCommandId)).Should().Be(1);
        }
        finally
        {
            await CleanupCommandRowsAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static IStatutoryDiscountStagedCommandService CreateService() =>
        new StatutoryDiscountStagedCommandService(new PostgresStatutoryDiscountStagedCommandRepository(ConnectionString));

    private static async Task<StatutoryDiscountDecisionV2Record> CreateApprovedDecisionAsync(
        IStatutoryDiscountStagedCommandService service,
        PaymentTestContext context)
    {
        var created = await service.CreateOrResolveDecisionAsync(DecisionCommand(context), CancellationToken.None);
        return await service.CompleteDecisionApprovedAsync(
            created.Record!.StatutoryDiscountDecisionCommandId,
            statutoryDiscountValidationId: null,
            context.TariffSnapshotId,
            appliedPolicyReferenceId: null,
            fallbackPolicyReferenceId: null,
            "NATIONAL_DEFAULT",
            localOrdinanceApplied: false,
            new StatutoryDiscountDecisionV2TariffFacts(10000, 8929, 1071, 1786, 8214, "PHP"),
            "ELIGIBLE",
            context.CorrelationId,
            CancellationToken.None);
    }

    private static StatutoryDiscountDecisionV2Command DecisionCommand(
        PaymentTestContext context,
        Guid? requestReference = null,
        string idempotencyKey = "decision-key",
        string decision = "APPROVE") =>
        new(
            requestReference ?? Guid.NewGuid(),
            StatutoryDiscountSourceChannels.OperatorConsole,
            context.ParkingSessionId,
            context.SiteId,
            context.SiteGroupId,
            "ticket-repo-001",
            "abc1234",
            "senior_citizen",
            new StatutoryDiscountDecisionV2BeneficiaryMetadata("beneficiary-ref", "senior_citizen", "DRIVER", 1),
            new StatutoryDiscountDecisionV2IdentityMetadata("SENIOR_CITIZEN_ID", "OSCA", DateOnly.Parse("2030-01-01"), "SC-****-1234", null),
            [Evidence("VERIFIED")],
            new StatutoryDiscountDecisionV2AttestationFacts(true, "attestation-ref", "CUSTOMER_REQUEST", true),
            context.RequestedByUserId,
            context.RequestedByUserId,
            OperatorDeviceBindingId: null,
            OperatorShiftId: null,
            new StatutoryDiscountDecisionV2DecisionFacts(decision, decision == "APPROVE" ? "ELIGIBLE" : "INELIGIBLE", null),
            PolicyResolutionReferenceId: null,
            AppliedPolicyReferenceId: null,
            FallbackPolicyReferenceId: null,
            PolicyResolutionBasis: "NATIONAL_DEFAULT",
            LocalOrdinanceApplied: false,
            context.TariffSnapshotId,
            new StatutoryDiscountDecisionV2TariffFacts(10000, 8929, 1071, 1786, 8214, "PHP"),
            idempotencyKey,
            context.CorrelationId);

    private static StatutoryDiscountPayableBasisApplicationV1Command ApplicationCommand(
        PaymentTestContext context,
        Guid decisionId,
        Guid? requestReference = null,
        string idempotencyKey = "application-key") =>
        new(
            requestReference ?? Guid.NewGuid(),
            decisionId,
            context.ParkingSessionId,
            context.SiteId,
            "SENIOR_CITIZEN",
            StatutoryDiscountValidationId: null,
            context.TariffSnapshotId,
            TargetTariffSnapshotId: null,
            AppliedTariffSnapshotId: null,
            AppliedPolicyReferenceId: null,
            "NATIONAL_DEFAULT",
            ApprovedDiscountAmountMinorUnits: 1786,
            ApprovedVatExclusiveAmountMinorUnits: 8929,
            ApprovedVatAmountMinorUnits: 1071,
            ApprovedFinalPayableAmountMinorUnits: 8214,
            "PHP",
            StatutoryDiscountSourceChannels.OperatorConsole,
            idempotencyKey,
            context.CorrelationId);

    private static StatutoryDiscountEvidenceReference FacadeEvidence(string status) =>
        new(
            "SENIOR_CITIZEN_ID",
            "MANUAL_REFERENCE",
            FileName: null,
            ContentType: null,
            SizeBytes: null,
            StorageReference: "repo-evidence-ref-001",
            ReferenceNumberMasked: "SC-****-1234",
            status);

    private static StatutoryDiscountDecisionRepositoryCommand FacadeRepositoryCommand(PaymentTestContext context)
    {
        var command = new StatutoryDiscountDecisionCommand(
            Guid.NewGuid(),
            StatutoryDiscountSourceChannels.OperatorConsole,
            context.ParkingSessionId,
            context.SiteId,
            context.SiteGroupId,
            "TICKET-REPO-001",
            "ABC1234",
            "SENIOR_CITIZEN",
            "SENIOR_CITIZEN_ID",
            "OSCA",
            DateOnly.Parse("2030-01-01"),
            "SC-****-1234",
            EvidenceCaptureRequested: true,
            EvidenceReferences: [FacadeEvidence("VERIFIED")],
            context.RequestedByUserId,
            OperatorDeviceBindingId: null,
            OperatorShiftId: null,
            RequesterAttestation: true,
            AttestationNotes: "attested",
            ReasonCode: "CUSTOMER_REQUEST",
            Decision: "APPROVE",
            DecisionReasonCode: "ELIGIBLE",
            ReviewerUserId: context.RequestedByUserId,
            ReviewerAttestation: true,
            ApplyPayableBasis: true,
            context.TariffSnapshotId,
            "facade-key",
            context.CorrelationId);

        return new StatutoryDiscountDecisionRepositoryCommand(
            command,
            StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(command),
            StatutoryDiscountDecisionSemanticHash.Compute(command),
            StatutoryDiscountDecisionSemanticHash.SourceVersion,
            DateTimeOffset.UtcNow);
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

    private static async Task EnsurePatchAppliedAndValidatedAsync()
    {
        await PatchLock.WaitAsync();
        try
        {
            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountPayableBasisApplicationSchema_v1.2.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "validation", "Validate_StatutoryDiscountStagedCanonicalCommands_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "validation", "Validate_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql");
        }
        finally
        {
            PatchLock.Release();
        }
    }

    private static async Task ExecuteSqlFileAsync(params string[] pathParts)
    {
        var sql = await File.ReadAllTextAsync(ResolveRepoPath(pathParts));
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(string regclass)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass(@regclass) IS NOT NULL;", connection);
        command.Parameters.AddWithValue("regclass", regclass);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> IndexExistsAsync(string schema, string indexName)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_indexes
                WHERE schemaname = @schema
                  AND indexname = @index_name
            );
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("index_name", indexName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<int> DecisionRowCountAsync(Guid parkingSessionId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)::int
            FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @parking_session_id;
            """,
            connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> ApplicationRowCountAsync(Guid decisionCommandId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)::int
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id;
            """,
            connection);
        command.Parameters.AddWithValue("statutory_discount_decision_command_id", decisionCommandId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<IReadOnlyList<string>> DecisionSourceVersionsAsync(Guid firstParkingSessionId, Guid secondParkingSessionId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT semantic_hash_source_version
            FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id IN (@first_parking_session_id, @second_parking_session_id)
            ORDER BY semantic_hash_source_version;
            """,
            connection);
        command.Parameters.AddWithValue("first_parking_session_id", firstParkingSessionId);
        command.Parameters.AddWithValue("second_parking_session_id", secondParkingSessionId);
        var versions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetString(0));
        }

        return versions;
    }

    private static async Task CleanupCommandRowsAsync(Guid parkingSessionId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_decision_commands
            WHERE parking_session_id = @parking_session_id;
            """,
            connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
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
