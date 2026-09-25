using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.SalesInvoiceCustomerInformation;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.SalesInvoiceCustomerInformation;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class ParkingSessionInvoiceCustomerInformationRepositoryTests(
    StatutoryDiscountCanonicalDatabaseFixture database)
{
    [Fact]
    public async Task CreateBeforePayment_PersistsNormalizedAuthorityAndAuditAcrossRepositoryInstances()
    {
        var context = PaymentTestContext.Create(nameof(CreateBeforePayment_PersistsNormalizedAuthorityAndAuditAcrossRepositoryInstances));
        var userId = Guid.NewGuid();
        await SeedAsync(context, userId);
        try
        {
            (await CountAsync("core.payment_attempts", "parking_session_id", context.ParkingSessionId)).Should().Be(0);
            (await CountAsync("operator_console.statutory_discount_service_channel_reviews", "parking_session_id", context.ParkingSessionId)).Should().Be(0);

            var service = Service();
            var created = await service.SaveAsync(Command(context, userId, "  ABC Corporation  ", "  Makati City  ", " 123-456-789 ", " ABC Retail ", null), CancellationToken.None);

            created.Status.Should().Be(InvoiceCustomerInformationSaveStatus.Created);
            created.Record!.RowVersion.Should().Be(1);
            created.Record.CustomerName.Should().Be("ABC Corporation");

            var reloaded = await Service().ReadAsync(context.ParkingSessionId, Scope(context), CancellationToken.None);
            reloaded.Status.Should().Be(InvoiceCustomerInformationReadStatus.Found);
            reloaded.Record.Should().BeEquivalentTo(created.Record);

            var provenance = await ReadProvenanceAsync(context.ParkingSessionId);
            provenance.Should().Be((userId, userId, "OPERATOR_CONSOLE", "OPERATOR_CONSOLE"));
            (await CountAuditAsync(context.ParkingSessionId, "INVOICE_CUSTOMER_INFORMATION_CREATE")).Should().Be(1);
            var audit = await ReadAuditAsync(context.ParkingSessionId, "INVOICE_CUSTOMER_INFORMATION_CREATE");
            audit.ActorUserId.Should().Be(userId);
            audit.SiteId.Should().Be(context.SiteId);
            audit.SourceChannel.Should().Be("OPERATOR_CONSOLE");
            audit.Summary.Should().NotContain("ABC Corporation").And.NotContain("123-456-789");
        }
        finally
        {
            await CleanupAsync(context, userId);
        }
    }

    [Fact]
    public async Task UpdateAndIdenticalReplay_AdvanceVersionOnlyForMaterialChange()
    {
        var context = PaymentTestContext.Create(nameof(UpdateAndIdenticalReplay_AdvanceVersionOnlyForMaterialChange));
        var userId = Guid.NewGuid();
        await SeedAsync(context, userId);
        try
        {
            var service = Service();
            var created = await service.SaveAsync(Command(context, userId, "ABC", null, null, null, null), CancellationToken.None);
            var updated = await service.SaveAsync(Command(context, userId, "ABC", "Makati", null, null, created.Record!.RowVersion), CancellationToken.None);
            var replay = await service.SaveAsync(Command(context, userId, " ABC ", " Makati ", null, null, updated.Record!.RowVersion), CancellationToken.None);

            updated.Status.Should().Be(InvoiceCustomerInformationSaveStatus.Updated);
            updated.Record!.RowVersion.Should().Be(2);
            replay.Status.Should().Be(InvoiceCustomerInformationSaveStatus.Unchanged);
            replay.Record!.RowVersion.Should().Be(2);
            (await CountAuditAsync(context.ParkingSessionId, "INVOICE_CUSTOMER_INFORMATION_UPDATE")).Should().Be(1);
            (await CountAuditAsync(context.ParkingSessionId, "INVOICE_CUSTOMER_INFORMATION_UPDATE_REPLAY")).Should().Be(1);
        }
        finally
        {
            await CleanupAsync(context, userId);
        }
    }

    [Fact]
    public async Task ConcurrentCreateAndUpdate_ProduceOneRowAndDeterministicVersionConflict()
    {
        var context = PaymentTestContext.Create(nameof(ConcurrentCreateAndUpdate_ProduceOneRowAndDeterministicVersionConflict));
        var userId = Guid.NewGuid();
        await SeedAsync(context, userId);
        try
        {
            var firstCreate = Service().SaveAsync(Command(context, userId, "ABC", null, null, null, null), CancellationToken.None);
            var secondCreate = Service().SaveAsync(Command(context, userId, "ABC", null, null, null, null), CancellationToken.None);
            var creates = await Task.WhenAll(firstCreate, secondCreate);

            creates.Select(result => result.Status).Should().BeEquivalentTo([
                InvoiceCustomerInformationSaveStatus.Created,
                InvoiceCustomerInformationSaveStatus.Unchanged]);
            (await CountAsync("core.parking_session_invoice_customer_information", "parking_session_id", context.ParkingSessionId)).Should().Be(1);

            var updateA = Service().SaveAsync(Command(context, userId, "ABC A", null, null, null, 1), CancellationToken.None);
            var updateB = Service().SaveAsync(Command(context, userId, "ABC B", null, null, null, 1), CancellationToken.None);
            var updates = await Task.WhenAll(updateA, updateB);

            updates.Count(result => result.Status == InvoiceCustomerInformationSaveStatus.Updated).Should().Be(1);
            updates.Count(result => result.Status == InvoiceCustomerInformationSaveStatus.VersionConflict).Should().Be(1);
            var authoritative = await Service().ReadAsync(context.ParkingSessionId, Scope(context), CancellationToken.None);
            authoritative.Record!.RowVersion.Should().Be(2);
            authoritative.Record.CustomerName.Should().BeOneOf("ABC A", "ABC B");
            (await CountAuditAsync(context.ParkingSessionId, "INVOICE_CUSTOMER_INFORMATION_VERSION_CONFLICT")).Should().Be(1);
        }
        finally
        {
            await CleanupAsync(context, userId);
        }
    }

    [Fact]
    public async Task SiteIsolation_ConcealsReadAndUpdateAcrossSites()
    {
        var siteA = PaymentTestContext.Create("invoice-info-site-a");
        var siteB = PaymentTestContext.Create("invoice-info-site-b");
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedAsync(siteA, userA);
        await SeedAsync(siteB, userB);
        try
        {
            await Service().SaveAsync(Command(siteB, userB, "Site B Customer", null, null, null, null), CancellationToken.None);

            var concealedRead = await Service().ReadAsync(siteB.ParkingSessionId, Scope(siteA), CancellationToken.None);
            var concealedUpdate = await Service().SaveAsync(Command(siteB, userA, "Changed", null, null, null, 1) with { Scope = Scope(siteA) }, CancellationToken.None);

            concealedRead.Status.Should().Be(InvoiceCustomerInformationReadStatus.ParkingSessionNotFound);
            concealedUpdate.Status.Should().Be(InvoiceCustomerInformationSaveStatus.ParkingSessionNotFound);
            var unchanged = await Service().ReadAsync(siteB.ParkingSessionId, Scope(siteB), CancellationToken.None);
            unchanged.Record!.CustomerName.Should().Be("Site B Customer");
        }
        finally
        {
            await CleanupAsync(siteA, userA);
            await CleanupAsync(siteB, userB);
        }
    }

    [Fact]
    public async Task RecordedFiscalEvidence_RejectsMutationAndAuditsFinality()
    {
        var context = PaymentTestContext.Create(nameof(RecordedFiscalEvidence_RejectsMutationAndAuditsFinality));
        var userId = Guid.NewGuid();
        await SeedAsync(context, userId);
        try
        {
            var service = Service();
            var created = await service.SaveAsync(Command(context, userId, "Before issuance", null, null, null, null), CancellationToken.None);
            var attempt = await CreateAttemptAsync(database.ConnectionString, context, $"attempt-{Guid.NewGuid():N}", "invoice-information-test");
            var confirmation = await RecordPaymentConfirmationAsync(database.ConnectionString, attempt.PaymentAttemptId, $"confirmation-{Guid.NewGuid():N}", "invoice-information-test", context.CorrelationId);
            confirmation.Should().NotBeNull();
            await new PostgresFiscalIssuanceReferenceRepository(database.ConnectionString).CreateAsync(
                RecordedFiscalRequest(context, attempt, confirmation!), CancellationToken.None);

            var rejected = await service.SaveAsync(Command(context, userId, "After issuance", null, null, null, created.Record!.RowVersion), CancellationToken.None);

            rejected.Status.Should().Be(InvoiceCustomerInformationSaveStatus.FiscalFinality);
            var authoritative = await service.ReadAsync(context.ParkingSessionId, Scope(context), CancellationToken.None);
            authoritative.Record!.CustomerName.Should().Be("Before issuance");
            (await CountAuditAsync(context.ParkingSessionId, "INVOICE_CUSTOMER_INFORMATION_FINALITY_REJECTION")).Should().Be(1);
        }
        finally
        {
            await CleanupAsync(context, userId);
        }
    }

    private ParkingSessionInvoiceCustomerInformationService Service() =>
        new(new PostgresParkingSessionInvoiceCustomerInformationRepository(database.ConnectionString));

    private static InvoiceCustomerInformationScope Scope(PaymentTestContext context) => new(context.SiteId, context.SiteGroupId);

    private static SaveParkingSessionInvoiceCustomerInformationCommand Command(
        PaymentTestContext context,
        Guid userId,
        string? name,
        string? address,
        string? tin,
        string? style,
        long? expectedVersion) =>
        new(context.ParkingSessionId, name, address, tin, style, expectedVersion,
            new InvoiceCustomerInformationActor(userId, null, InvoiceCustomerInformationSourceChannels.OperatorConsole),
            Scope(context), context.CorrelationId);

    private async Task SeedAsync(PaymentTestContext context, Guid userId)
    {
        await PaymentTestDataHelper.ResetAndSeedAsync(database.ConnectionString, context, "Seed parking-session invoice customer information test.");
        const string sql = """
            INSERT INTO identity.users (
                user_id, username, display_name, user_type, user_status, effective_from,
                created_by_service_identity_id, updated_by_service_identity_id)
            VALUES (@user_id, @username, 'Invoice Information Operator', 'SITE_OPERATOR', 'ACTIVE', now() - interval '1 minute',
                    @service_identity_id, @service_identity_id);
            """;
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("username", $"invoice-info-{userId:N}");
        command.Parameters.AddWithValue("service_identity_id", context.RequestedByUserId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task CleanupAsync(PaymentTestContext context, Guid userId)
    {
        const string sql = """
            DELETE FROM audit.audit_events
            WHERE target_entity_type = 'ParkingSessionInvoiceCustomerInformation'
              AND target_entity_id = @parking_session_id;
            DELETE FROM core.parking_session_invoice_customer_information
            WHERE parking_session_id = @parking_session_id;
            DELETE FROM core.fiscal_issuance_references
            WHERE parking_session_id = @parking_session_id;
            DELETE FROM identity.users WHERE user_id = @user_id;
            """;
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync();
        await PaymentTestDataHelper.CleanupAsync(database.ConnectionString, context);
    }

    private async Task<long> CountAsync(string table, string column, Guid value)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {column} = @value;", connection);
        command.Parameters.AddWithValue("value", value);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> CountAuditAsync(Guid parkingSessionId, string eventType)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit.audit_events WHERE target_entity_id = @id AND event_type = @event_type;", connection);
        command.Parameters.AddWithValue("id", parkingSessionId);
        command.Parameters.AddWithValue("event_type", eventType);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<(Guid ActorUserId, Guid SiteId, string SourceChannel, string Summary)> ReadAuditAsync(Guid parkingSessionId, string eventType)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT actor_user_id, related_entity_id, source_channel, summary FROM audit.audit_events WHERE target_entity_id = @id AND event_type = @event_type;", connection);
        command.Parameters.AddWithValue("id", parkingSessionId);
        command.Parameters.AddWithValue("event_type", eventType);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3));
    }

    private async Task<(Guid CreatedUser, Guid UpdatedUser, string CreatedSource, string UpdatedSource)> ReadProvenanceAsync(Guid parkingSessionId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT created_by_user_id, updated_by_user_id, created_source_channel, updated_source_channel FROM core.parking_session_invoice_customer_information WHERE parking_session_id = @id;", connection);
        command.Parameters.AddWithValue("id", parkingSessionId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3));
    }

    private static CreateFiscalIssuanceReferenceRequest RecordedFiscalRequest(
        PaymentTestContext context,
        CreateAttemptResult attempt,
        RecordPaymentConfirmationResult confirmation)
    {
        var sequence = Random.Shared.Next(100000, 999999);
        return new(
            confirmation.PaymentConfirmationId, attempt.PaymentAttemptId, context.ParkingSessionId,
            context.TariffSnapshotId, context.SiteId, Guid.NewGuid(), $"site-pos-{context.SiteCode}",
            Guid.NewGuid(), "SALES_INVOICE", context.TariffSnapshotId.ToString("N"),
            $"finality-{confirmation.PaymentConfirmationId:N}", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            sequence, $"SI-{sequence}", "SI", "SI-", null, DateTimeOffset.UtcNow, "pos-server-test",
            Guid.NewGuid(), FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned, FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, null, null, null,
            context.CorrelationId, DateTimeOffset.UtcNow, context.RequestedByUserId);
    }
}
