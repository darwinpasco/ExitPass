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
    public async Task FiscalSnapshotCapture_RejectsMutationBeforePosIssuanceAndAuditsLock()
    {
        var context = PaymentTestContext.Create(nameof(FiscalSnapshotCapture_RejectsMutationBeforePosIssuanceAndAuditsLock));
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
                PendingFiscalRequest(context, attempt, confirmation!), CancellationToken.None);

            var rejected = await service.SaveAsync(Command(context, userId, "After issuance", null, null, null, created.Record!.RowVersion), CancellationToken.None);

            rejected.Status.Should().Be(InvoiceCustomerInformationSaveStatus.FiscalSnapshotLocked);
            var authoritative = await service.ReadAsync(context.ParkingSessionId, Scope(context), CancellationToken.None);
            authoritative.Record!.CustomerName.Should().Be("Before issuance");
            (await CountAuditAsync(context.ParkingSessionId, "INVOICE_CUSTOMER_INFORMATION_SNAPSHOT_LOCK_REJECTION")).Should().Be(1);
        }
        finally
        {
            await CleanupAsync(context, userId);
        }
    }

    [Fact]
    public async Task CustomerUpdateCommitsFirst_FiscalSnapshotCapturesOneCompleteUpdatedVersion()
    {
        var context = PaymentTestContext.Create(nameof(CustomerUpdateCommitsFirst_FiscalSnapshotCapturesOneCompleteUpdatedVersion));
        var userId = Guid.NewGuid();
        await SeedAsync(context, userId);
        try
        {
            await Service().SaveAsync(Command(context, userId, "Version One", "Old Address", "111", "Old Style", null), CancellationToken.None);
            var attempt = await CreateAttemptAsync(database.ConnectionString, context, $"attempt-{Guid.NewGuid():N}", "invoice-information-test");
            var confirmation = await RecordPaymentConfirmationAsync(database.ConnectionString, attempt.PaymentAttemptId, $"confirmation-{Guid.NewGuid():N}", "invoice-information-test", context.CorrelationId);

            await using var updateConnection = new NpgsqlConnection(database.ConnectionString);
            await updateConnection.OpenAsync();
            await using var updateTransaction = await updateConnection.BeginTransactionAsync();
            await using (var update = new NpgsqlCommand("""
                SELECT 1 FROM core.parking_sessions WHERE parking_session_id = @id FOR UPDATE;
                UPDATE core.parking_session_invoice_customer_information
                SET customer_name = 'Version Two', customer_address = 'New Address',
                    customer_tin = '222', business_style = 'New Style',
                    row_version = row_version + 1, updated_at = current_timestamp
                WHERE parking_session_id = @id;
                """, updateConnection, updateTransaction))
            {
                update.Parameters.AddWithValue("id", context.ParkingSessionId);
                await update.ExecuteNonQueryAsync();
            }

            var snapshotTask = new PostgresFiscalIssuanceReferenceRepository(database.ConnectionString).CreateAsync(
                PendingFiscalRequest(context, attempt, confirmation!), CancellationToken.None);
            var first = await Task.WhenAny(snapshotTask, Task.Delay(TimeSpan.FromMilliseconds(200)));
            first.Should().NotBe(snapshotTask, "fiscal snapshot creation must wait for the parking-session update lock");
            await updateTransaction.CommitAsync();

            var reference = await snapshotTask;
            reference.InvoiceCustomerInformationSnapshot.Should().NotBeNull();
            reference.InvoiceCustomerInformationSnapshot!.SourceRowVersion.Should().Be(2);
            reference.InvoiceCustomerInformationSnapshot.CustomerName.Should().Be("Version Two");
            reference.InvoiceCustomerInformationSnapshot.Address.Should().Be("New Address");
            reference.InvoiceCustomerInformationSnapshot.Tin.Should().Be("222");
            reference.InvoiceCustomerInformationSnapshot.BusinessStyle.Should().Be("New Style");
        }
        finally
        {
            await CleanupAsync(context, userId);
        }
    }

    [Fact]
    public async Task CapturedEmptyState_IsDistinctFromLegacyReferenceWithoutSnapshot()
    {
        var context = PaymentTestContext.Create(nameof(CapturedEmptyState_IsDistinctFromLegacyReferenceWithoutSnapshot));
        var userId = Guid.NewGuid();
        await SeedAsync(context, userId);
        try
        {
            var attempt = await CreateAttemptAsync(database.ConnectionString, context, $"attempt-{Guid.NewGuid():N}", "invoice-information-test");
            var confirmation = await RecordPaymentConfirmationAsync(database.ConnectionString, attempt.PaymentAttemptId, $"confirmation-{Guid.NewGuid():N}", "invoice-information-test", context.CorrelationId);
            var repository = new PostgresFiscalIssuanceReferenceRepository(database.ConnectionString);
            var capturedEmpty = await repository.CreateAsync(
                PendingFiscalRequest(context, attempt, confirmation!), CancellationToken.None);

            capturedEmpty.InvoiceCustomerInformationSnapshot.Should().NotBeNull();
            capturedEmpty.InvoiceCustomerInformationSnapshot!.SourceRowVersion.Should().BeNull();
            capturedEmpty.InvoiceCustomerInformationSnapshot.CustomerName.Should().BeNull();

            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                UPDATE core.fiscal_issuance_references
                SET invoice_customer_information_snapshot_captured_at = NULL
                WHERE fiscal_issuance_reference_id = @id;
                """, connection);
            command.Parameters.AddWithValue("id", capturedEmpty.FiscalIssuanceReferenceId);
            await command.ExecuteNonQueryAsync();

            var legacy = await repository.FindByFiscalIssuanceReferenceIdAsync(
                capturedEmpty.FiscalIssuanceReferenceId, CancellationToken.None);
            legacy!.InvoiceCustomerInformationSnapshot.Should().BeNull();
        }
        finally
        {
            await CleanupAsync(context, userId);
        }
    }

    [Fact]
    public async Task PaymentAttemptSnapshot_DoesNotOverrideParkingSessionFiscalSnapshot()
    {
        var context = PaymentTestContext.Create(nameof(PaymentAttemptSnapshot_DoesNotOverrideParkingSessionFiscalSnapshot));
        var userId = Guid.NewGuid();
        await SeedAsync(context, userId);
        try
        {
            await Service().SaveAsync(Command(context, userId, "Authoritative Name", "Authoritative Address", "999", "Authoritative Style", null), CancellationToken.None);
            var attempt = await CreateAttemptAsync(database.ConnectionString, context, $"attempt-{Guid.NewGuid():N}", "invoice-information-test");
            var confirmation = await RecordPaymentConfirmationAsync(database.ConnectionString, attempt.PaymentAttemptId, $"confirmation-{Guid.NewGuid():N}", "invoice-information-test", context.CorrelationId);
            await InsertPaymentAttemptSnapshotAsync(context, attempt.PaymentAttemptId);

            var reference = await new PostgresFiscalIssuanceReferenceRepository(database.ConnectionString).CreateAsync(
                PendingFiscalRequest(context, attempt, confirmation!), CancellationToken.None);

            reference.InvoiceCustomerInformationSnapshot!.CustomerName.Should().Be("Authoritative Name");
            reference.InvoiceCustomerInformationSnapshot.Address.Should().Be("Authoritative Address");
            reference.InvoiceCustomerInformationSnapshot.Tin.Should().Be("999");
            reference.InvoiceCustomerInformationSnapshot.BusinessStyle.Should().Be("Authoritative Style");
        }
        finally
        {
            await CleanupAsync(context, userId);
        }
    }

    [Fact]
    public async Task FiscalSnapshot_UsesOnlyTheFiscalizedSessionAndRejectsMismatchedSite()
    {
        var siteA = PaymentTestContext.Create(nameof(FiscalSnapshot_UsesOnlyTheFiscalizedSessionAndRejectsMismatchedSite) + "A");
        var siteB = PaymentTestContext.Create(nameof(FiscalSnapshot_UsesOnlyTheFiscalizedSessionAndRejectsMismatchedSite) + "B");
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedAsync(siteA, userA);
        await SeedAsync(siteB, userB);
        try
        {
            await Service().SaveAsync(Command(siteA, userA, "Site A Customer", null, null, null, null), CancellationToken.None);
            await Service().SaveAsync(Command(siteB, userB, "Site B Customer", null, null, null, null), CancellationToken.None);
            var attempt = await CreateAttemptAsync(database.ConnectionString, siteA, $"attempt-{Guid.NewGuid():N}", "invoice-information-test");
            var confirmation = await RecordPaymentConfirmationAsync(database.ConnectionString, attempt.PaymentAttemptId, $"confirmation-{Guid.NewGuid():N}", "invoice-information-test", siteA.CorrelationId);
            var repository = new PostgresFiscalIssuanceReferenceRepository(database.ConnectionString);
            var request = PendingFiscalRequest(siteA, attempt, confirmation!);

            var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.CreateAsync(request with { SiteId = siteB.SiteId }, CancellationToken.None));
            mismatch.Message.Should().Be("FISCAL_PARKING_SESSION_SITE_MISMATCH");

            var reference = await repository.CreateAsync(request, CancellationToken.None);
            reference.ParkingSessionId.Should().Be(siteA.ParkingSessionId);
            reference.SiteId.Should().Be(siteA.SiteId);
            reference.InvoiceCustomerInformationSnapshot!.CustomerName.Should().Be("Site A Customer");
            reference.InvoiceCustomerInformationSnapshot.CustomerName.Should().NotBe("Site B Customer");
        }
        finally
        {
            await CleanupAsync(siteA, userA);
            await CleanupAsync(siteB, userB);
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
            DELETE FROM core.payment_attempt_invoice_customer_information AS customer_information
            USING core.payment_attempts AS payment_attempt
            WHERE customer_information.payment_attempt_id = payment_attempt.payment_attempt_id
              AND payment_attempt.parking_session_id = @parking_session_id;
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

    private async Task InsertPaymentAttemptSnapshotAsync(PaymentTestContext context, Guid paymentAttemptId)
    {
        const string sql = """
            INSERT INTO core.payment_attempt_invoice_customer_information (
                payment_attempt_id, parking_session_id, tariff_snapshot_id,
                customer_name, customer_address, customer_tin, business_style, created_at)
            VALUES (@payment_attempt_id, @parking_session_id, @tariff_snapshot_id,
                    'Stale PaymentAttempt Name', 'Stale Address', '000', 'Stale Style', current_timestamp);
            """;
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("tariff_snapshot_id", context.TariffSnapshotId);
        await command.ExecuteNonQueryAsync();
    }

    private static CreateFiscalIssuanceReferenceRequest PendingFiscalRequest(
        PaymentTestContext context,
        CreateAttemptResult attempt,
        RecordPaymentConfirmationResult confirmation) =>
        RecordedFiscalRequest(context, attempt, confirmation) with
        {
            PosServerFiscalDocumentId = null,
            FiscalIdentityId = null,
            FiscalSequencePolicyId = null,
            FiscalSequenceValue = null,
            FiscalDocumentNumber = null,
            FiscalSeries = null,
            FiscalNumberPrefixText = null,
            FiscalNumberAssignedAt = null,
            FiscalNumberAssignedByRef = null,
            FiscalDocumentStatusCodeId = null,
            ResultClassification = null,
            FiscalIssuanceEvidenceStatus = null,
            FiscalNumberAssignmentState = FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState = FiscalIssuanceIntegrationState.PendingFiscalIssuance,
            PosServerResponseTimestamp = null
        };

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
