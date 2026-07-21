using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class StatutoryDiscountDecisionFacadeRepositoryTests
{
    private static readonly SemaphoreSlim PatchLock = new(1, 1);

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task Patch_AppliesAndValidatesDecisionFacadeObjects()
    {
        await EnsurePatchAppliedAndValidatedAsync();

        (await TableExistsAsync("discounts.statutory_discount_decision_commands")).Should().BeTrue();
        (await IndexExistsAsync("discounts", "ux_statutory_discount_decision_commands__business_identity")).Should().BeTrue();
    }

    [Fact]
    public async Task BeginAsync_WhenSameBusinessRequestReplaysAcrossChannel_ReturnsExistingWithoutDuplicateInsert()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var context = PaymentTestContext.Create(nameof(BeginAsync_WhenSameBusinessRequestReplaysAcrossChannel_ReturnsExistingWithoutDuplicateInsert));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory decision facade repository test data.");

        try
        {
            var repository = new PostgresStatutoryDiscountDecisionFacadeRepository(ConnectionString);
            var first = RepositoryCommand(context, sourceChannel: "OPERATOR_CONSOLE", requestReference: Guid.NewGuid(), idempotencyKey: "repo-test-key-operator");
            var second = RepositoryCommand(context, sourceChannel: "WEBPAY", requestReference: Guid.NewGuid(), idempotencyKey: "repo-test-key-webpay");

            var created = await repository.ExecuteWithCommandLockAsync(
                first,
                token => repository.BeginAsync(first, token),
                CancellationToken.None);
            var replay = await repository.ExecuteWithCommandLockAsync(
                second,
                token => repository.BeginAsync(second, token),
                CancellationToken.None);

            created.Existing.Should().BeFalse();
            replay.Existing.Should().BeTrue();
            replay.SemanticConflict.Should().BeFalse();
            replay.Record.StatutoryDiscountDecisionCommandId.Should().Be(created.Record.StatutoryDiscountDecisionCommandId);
            replay.Record.SourceChannel.Should().Be("OPERATOR_CONSOLE");
            (await CommandRowCountAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
            await CleanupCommandRowsAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task BeginAsync_WhenSameBusinessIdentityChangesMaterialFacts_ReturnsSemanticConflict()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var context = PaymentTestContext.Create(nameof(BeginAsync_WhenSameBusinessIdentityChangesMaterialFacts_ReturnsSemanticConflict));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed statutory decision facade repository conflict test data.");

        try
        {
            var repository = new PostgresStatutoryDiscountDecisionFacadeRepository(ConnectionString);
            var first = RepositoryCommand(context, evidenceStatus: "VERIFIED");
            var changed = RepositoryCommand(context, evidenceStatus: "REJECTED", idempotencyKey: "repo-test-key-changed");

            await repository.ExecuteWithCommandLockAsync(
                first,
                token => repository.BeginAsync(first, token),
                CancellationToken.None);
            var conflict = await repository.ExecuteWithCommandLockAsync(
                changed,
                token => repository.BeginAsync(changed, token),
                CancellationToken.None);

            conflict.Existing.Should().BeTrue();
            conflict.SemanticConflict.Should().BeTrue();
            (await CommandRowCountAsync(context.ParkingSessionId)).Should().Be(1);
        }
        finally
        {
            await CleanupCommandRowsAsync(context.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task BeginAsync_WhenSameRequestReferenceTargetsDifferentBusinessIdentity_ReturnsConflictRecord()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var firstContext = PaymentTestContext.Create(nameof(BeginAsync_WhenSameRequestReferenceTargetsDifferentBusinessIdentity_ReturnsConflictRecord) + "A");
        var secondContext = PaymentTestContext.Create(nameof(BeginAsync_WhenSameRequestReferenceTargetsDifferentBusinessIdentity_ReturnsConflictRecord) + "B");
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, firstContext, "Seed first statutory decision facade request reference test data.");
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, secondContext, "Seed second statutory decision facade request reference test data.");
        var requestReference = Guid.NewGuid();

        try
        {
            var repository = new PostgresStatutoryDiscountDecisionFacadeRepository(ConnectionString);
            var first = RepositoryCommand(firstContext, requestReference: requestReference);
            var second = RepositoryCommand(secondContext, requestReference: requestReference, idempotencyKey: "repo-test-key-second-session");

            await repository.ExecuteWithCommandLockAsync(
                first,
                token => repository.BeginAsync(first, token),
                CancellationToken.None);
            var conflict = await repository.ExecuteWithCommandLockAsync(
                second,
                token => repository.BeginAsync(second, token),
                CancellationToken.None);

            conflict.Existing.Should().BeTrue();
            conflict.SemanticConflict.Should().BeTrue();
            conflict.Record.ParkingSessionId.Should().Be(firstContext.ParkingSessionId);
        }
        finally
        {
            await CleanupCommandRowsAsync(firstContext.ParkingSessionId);
            await CleanupCommandRowsAsync(secondContext.ParkingSessionId);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, firstContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, secondContext);
        }
    }

    [Fact]
    public async Task GetAsync_WhenReferenceMissing_ReturnsNull()
    {
        await EnsurePatchAppliedAndValidatedAsync();
        var repository = new PostgresStatutoryDiscountDecisionFacadeRepository(ConnectionString);

        var result = await repository.GetAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    private static StatutoryDiscountDecisionRepositoryCommand RepositoryCommand(
        PaymentTestContext context,
        string sourceChannel = "OPERATOR_CONSOLE",
        Guid? requestReference = null,
        string idempotencyKey = "repo-test-key",
        string evidenceStatus = "VERIFIED")
    {
        var command = new StatutoryDiscountDecisionCommand(
            requestReference ?? Guid.NewGuid(),
            sourceChannel,
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
            EvidenceReferences:
            [
                new StatutoryDiscountEvidenceReference(
                    "SENIOR_CITIZEN_ID",
                    "MANUAL_REFERENCE",
                    FileName: null,
                    ContentType: null,
                    SizeBytes: null,
                    StorageReference: "repo-evidence-ref-001",
                    ReferenceNumberMasked: "SC-****-1234",
                    evidenceStatus)
            ],
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
            idempotencyKey,
            context.CorrelationId);

        return new StatutoryDiscountDecisionRepositoryCommand(
            command,
            StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(command),
            StatutoryDiscountDecisionSemanticHash.Compute(command),
            StatutoryDiscountDecisionSemanticHash.SourceVersion,
            DateTimeOffset.UtcNow);
    }

    private static async Task EnsurePatchAppliedAndValidatedAsync()
    {
        await PatchLock.WaitAsync();
        try
        {
            await ExecuteSqlFileAsync("infra", "db", "patches", "ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql");
            await ExecuteSqlFileAsync("infra", "db", "patches", "validation", "Validate_StatutoryDiscountDecisionFacade_v1.3.sql");
        }
        finally
        {
            PatchLock.Release();
        }
    }

    private static async Task ExecuteSqlFileAsync(params string[] pathParts)
    {
        var path = ResolveRepoPath(pathParts);
        var sql = await File.ReadAllTextAsync(path);
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

    private static async Task<int> CommandRowCountAsync(Guid parkingSessionId)
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

    private static async Task CleanupCommandRowsAsync(Guid parkingSessionId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
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
