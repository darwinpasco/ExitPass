using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.Infrastructure.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

/// <summary>
/// Verifies durable Vendor PMS payment acknowledgment persistence.
/// </summary>
public sealed class VendorPaymentAcknowledgmentRepositoryTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Verifies a pending Vendor PMS acknowledgment can be created after ExitPass confirmation evidence exists.
    /// </summary>
    [Fact]
    public async Task CreatePendingAsync_WhenConfirmationExists_PersistsPendingAcknowledgment()
    {
        var context = CreateContext(nameof(CreatePendingAsync_WhenConfirmationExists_PersistsPendingAcknowledgment));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed Vendor PMS acknowledgment test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();

            var record = await repository.CreatePendingAsync(
                CreatePendingCommand(context, attempt, confirmation),
                CancellationToken.None);

            Assert.NotEqual(Guid.Empty, record.VendorPaymentAcknowledgmentId);
            Assert.Equal(confirmation.PaymentAttemptId, record.PaymentAttemptId);
            Assert.Equal(confirmation.PaymentConfirmationId, record.PaymentConfirmationId);
            Assert.Equal(attempt.ParkingSessionId, record.ParkingSessionId);
            Assert.Equal("HIKCENTRAL", record.VendorSystemCode);
            Assert.Equal("HIK:CARD-123", record.VendorSessionRef);
            Assert.Equal("TICKET-123", record.TicketNumber);
            Assert.Equal("CARD-123", record.CardNum);
            Assert.Equal(VendorPaymentAcknowledgmentStatuses.Pending, record.AcknowledgmentStatus);
            Assert.Equal(5000, record.RequestFeeMinorUnits);
            Assert.Equal("PHP", record.RequestCurrencyCode);
            Assert.Equal(0, record.AttemptCount);
            Assert.Equal(context.CorrelationId, record.CorrelationId);
        }
        finally
        {
            await CleanupAcknowledgmentsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies one durable acknowledgment per payment confirmation and Vendor PMS system.
    /// </summary>
    [Fact]
    public async Task CreatePendingAsync_WhenDuplicateConfirmationAndVendorSystem_RejectsDuplicate()
    {
        var context = CreateContext(nameof(CreatePendingAsync_WhenDuplicateConfirmationAndVendorSystem_RejectsDuplicate));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed Vendor PMS acknowledgment duplicate test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();
            var command = CreatePendingCommand(context, attempt, confirmation);

            await repository.CreatePendingAsync(command, CancellationToken.None);

            var ex = await Assert.ThrowsAsync<VendorPaymentAcknowledgmentConflictException>(() =>
                repository.CreatePendingAsync(command, CancellationToken.None));

            Assert.Equal("VENDOR_PAYMENT_ACKNOWLEDGMENT_ALREADY_EXISTS", ex.ErrorCode);
        }
        finally
        {
            await CleanupAcknowledgmentsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies Vendor PMS confirmation result metadata is persisted without changing ExitPass payment finality.
    /// </summary>
    [Fact]
    public async Task MarkConfirmedAsync_WhenPending_PersistsVendorConfirmationEvidence()
    {
        var context = CreateContext(nameof(MarkConfirmedAsync_WhenPending_PersistsVendorConfirmationEvidence));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed Vendor PMS confirmed acknowledgment test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();
            var pending = await repository.CreatePendingAsync(CreatePendingCommand(context, attempt, confirmation), CancellationToken.None);
            var vendorConfirmedAt = DateTimeOffset.Parse("2026-06-17T12:19:02+08:00");

            var confirmed = await repository.MarkConfirmedAsync(
                new MarkVendorPaymentAcknowledgmentConfirmedCommand(
                    pending.VendorPaymentAcknowledgmentId,
                    "0",
                    "Success",
                    5000,
                    vendorConfirmedAt,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);

            Assert.Equal(VendorPaymentAcknowledgmentStatuses.Confirmed, confirmed.AcknowledgmentStatus);
            Assert.Equal("0", confirmed.VendorCode);
            Assert.Equal("Success", confirmed.VendorMessage);
            Assert.Equal(5000, confirmed.ConfirmedFeeMinorUnits);
            Assert.NotNull(confirmed.VendorConfirmedAt);
            Assert.Equal(
                vendorConfirmedAt.ToUnixTimeMilliseconds(),
                confirmed.VendorConfirmedAt.Value.ToUnixTimeMilliseconds());
            Assert.Equal(1, confirmed.AttemptCount);
            Assert.NotNull(confirmed.LastAttemptedAt);

            var attemptStatus = await ReadPaymentAttemptStatusAsync(confirmation.PaymentAttemptId);
            Assert.Equal("CONFIRMED", attemptStatus);
        }
        finally
        {
            await CleanupAcknowledgmentsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies failed Vendor PMS acknowledgments retain safe vendor code/message and retry metadata.
    /// </summary>
    [Fact]
    public async Task MarkFailedAsync_WhenPending_PersistsFailureDiagnostics()
    {
        var context = CreateContext(nameof(MarkFailedAsync_WhenPending_PersistsFailureDiagnostics));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed Vendor PMS failed acknowledgment test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();
            var pending = await repository.CreatePendingAsync(CreatePendingCommand(context, attempt, confirmation), CancellationToken.None);
            var attemptedAt = DateTimeOffset.UtcNow;

            var failed = await repository.MarkFailedAsync(
                new MarkVendorPaymentAcknowledgmentFailedCommand(
                    pending.VendorPaymentAcknowledgmentId,
                    "128",
                    "The request resource does not exist. [vehicle is not exist]",
                    attemptedAt,
                    NextRetryAt: null,
                    UpdatedAt: attemptedAt),
                CancellationToken.None);

            Assert.Equal(VendorPaymentAcknowledgmentStatuses.Failed, failed.AcknowledgmentStatus);
            Assert.Equal("128", failed.VendorCode);
            Assert.Equal("The request resource does not exist. [vehicle is not exist]", failed.VendorMessage);
            Assert.NotNull(failed.LastAttemptedAt);
            Assert.Equal(
                attemptedAt.ToUnixTimeMilliseconds(),
                failed.LastAttemptedAt.Value.ToUnixTimeMilliseconds());
            Assert.Null(failed.NextRetryAt);
            Assert.Equal(1, failed.AttemptCount);
        }
        finally
        {
            await CleanupAcknowledgmentsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies the durable model can represent disabled Vendor PMS confirmation.
    /// </summary>
    [Fact]
    public async Task MarkSkippedDisabledAsync_WhenPending_PersistsSkippedDisabledStatus()
    {
        var context = CreateContext(nameof(MarkSkippedDisabledAsync_WhenPending_PersistsSkippedDisabledStatus));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed Vendor PMS skipped acknowledgment test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();
            var pending = await repository.CreatePendingAsync(CreatePendingCommand(context, attempt, confirmation), CancellationToken.None);

            var skipped = await repository.MarkSkippedDisabledAsync(
                new MarkVendorPaymentAcknowledgmentSkippedDisabledCommand(
                    pending.VendorPaymentAcknowledgmentId,
                    "HIKCENTRAL_CONFIRM_PAYMENT_ENABLED is false.",
                    DateTimeOffset.UtcNow),
                CancellationToken.None);

            Assert.Equal(VendorPaymentAcknowledgmentStatuses.SkippedDisabled, skipped.AcknowledgmentStatus);
            Assert.Equal("CONFIRM_DISABLED", skipped.VendorCode);
            Assert.Equal("HIKCENTRAL_CONFIRM_PAYMENT_ENABLED is false.", skipped.VendorMessage);
            Assert.Equal(0, skipped.AttemptCount);
        }
        finally
        {
            await CleanupAcknowledgmentsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies the workflow basis is loaded only from already-recorded confirmation and confirmed finality state.
    /// </summary>
    [Fact]
    public async Task LoadBasisAsync_WhenPaymentFinalityIsConfirmed_ReturnsImmutablePaymentBasis()
    {
        var context = CreateContext(nameof(LoadBasisAsync_WhenPaymentFinalityIsConfirmed_ReturnsImmutablePaymentBasis));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed Vendor PMS acknowledgment basis test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();

            var basis = await repository.LoadBasisAsync(
                confirmation.PaymentAttemptId,
                confirmation.PaymentConfirmationId,
                attempt.ParkingSessionId,
                CancellationToken.None);

            Assert.NotNull(basis);
            Assert.Equal(confirmation.PaymentAttemptId, basis.PaymentAttemptId);
            Assert.Equal(confirmation.PaymentConfirmationId, basis.PaymentConfirmationId);
            Assert.Equal(attempt.ParkingSessionId, basis.ParkingSessionId);
            Assert.Equal(context.VendorSystemCode, basis.VendorSystemCode);
            Assert.False(string.IsNullOrWhiteSpace(basis.VendorSessionRef));
            Assert.True(
                !string.IsNullOrWhiteSpace(basis.TicketNumber) ||
                !string.IsNullOrWhiteSpace(basis.CardNum));
            Assert.Equal(5000, basis.RequestFeeMinorUnits);
            Assert.Equal("PHP", basis.RequestCurrencyCode);
        }
        finally
        {
            await CleanupAcknowledgmentsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies read helpers can return a durable acknowledgment by confirmation and by latest payment attempt.
    /// </summary>
    [Fact]
    public async Task ReadHelpers_WhenAcknowledgmentExists_ReturnPersistedStatus()
    {
        var context = CreateContext(nameof(ReadHelpers_WhenAcknowledgmentExists_ReturnPersistedStatus));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed Vendor PMS acknowledgment read test data.");

        try
        {
            var (attempt, confirmation) = await CreateConfirmedPaymentAsync(context);
            var repository = CreateRepository();
            var pending = await repository.CreatePendingAsync(CreatePendingCommand(context, attempt, confirmation), CancellationToken.None);

            var byConfirmation = await repository.ReadByPaymentConfirmationAsync(
                confirmation.PaymentConfirmationId,
                "HIKCENTRAL",
                CancellationToken.None);
            var latest = await repository.ReadLatestByPaymentAttemptAsync(
                confirmation.PaymentAttemptId,
                CancellationToken.None);

            Assert.NotNull(byConfirmation);
            Assert.NotNull(latest);
            Assert.Equal(pending.VendorPaymentAcknowledgmentId, byConfirmation.VendorPaymentAcknowledgmentId);
            Assert.Equal(pending.VendorPaymentAcknowledgmentId, latest.VendorPaymentAcknowledgmentId);
            Assert.Equal(VendorPaymentAcknowledgmentStatuses.Pending, latest.AcknowledgmentStatus);
        }
        finally
        {
            await CleanupAcknowledgmentsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies due retry selection is bounded to RETRY_PENDING rows whose retry time has arrived.
    /// </summary>
    [Fact]
    public async Task FindDueRetryPendingAsync_ReturnsOnlyDueRetryPendingAcknowledgments()
    {
        var context = CreateContext(nameof(FindDueRetryPendingAsync_ReturnsOnlyDueRetryPendingAcknowledgments));

        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed Vendor PMS retry query test data.");

        try
        {
            var repository = CreateRepository();
            var now = DateTimeOffset.UtcNow;

            var (nullRetryAttempt, nullRetryConfirmation) = await CreateConfirmedPaymentAsync(context);
            var nullRetry = await repository.CreatePendingAsync(CreatePendingCommand(context, nullRetryAttempt, nullRetryConfirmation), CancellationToken.None);
            await SetRetryPendingAsync(nullRetry.VendorPaymentAcknowledgmentId, nextRetryAt: null);

            var (dueAttempt, dueConfirmation) = await CreateConfirmedPaymentAsync(context);
            var due = await repository.CreatePendingAsync(CreatePendingCommand(context, dueAttempt, dueConfirmation), CancellationToken.None);
            await SetRetryPendingAsync(due.VendorPaymentAcknowledgmentId, now.AddMinutes(-1));

            var (futureAttempt, futureConfirmation) = await CreateConfirmedPaymentAsync(context);
            var future = await repository.CreatePendingAsync(CreatePendingCommand(context, futureAttempt, futureConfirmation), CancellationToken.None);
            await SetRetryPendingAsync(future.VendorPaymentAcknowledgmentId, now.AddMinutes(10));

            var (confirmedAttempt, confirmedConfirmation) = await CreateConfirmedPaymentAsync(context);
            var confirmedPending = await repository.CreatePendingAsync(CreatePendingCommand(context, confirmedAttempt, confirmedConfirmation), CancellationToken.None);
            await repository.MarkConfirmedAsync(
                new MarkVendorPaymentAcknowledgmentConfirmedCommand(
                    confirmedPending.VendorPaymentAcknowledgmentId,
                    "0",
                    "Success",
                    5000,
                    now,
                    now),
                CancellationToken.None);

            var (skippedAttempt, skippedConfirmation) = await CreateConfirmedPaymentAsync(context);
            var skippedPending = await repository.CreatePendingAsync(CreatePendingCommand(context, skippedAttempt, skippedConfirmation), CancellationToken.None);
            await repository.MarkSkippedDisabledAsync(
                new MarkVendorPaymentAcknowledgmentSkippedDisabledCommand(
                    skippedPending.VendorPaymentAcknowledgmentId,
                    "HIKCENTRAL_CONFIRM_PAYMENT_ENABLED is false.",
                    now),
                CancellationToken.None);

            var records = await repository.FindDueRetryPendingAsync(now, limit: 10, CancellationToken.None);
            var ids = records.Select(record => record.VendorPaymentAcknowledgmentId).ToHashSet();

            Assert.Contains(nullRetry.VendorPaymentAcknowledgmentId, ids);
            Assert.Contains(due.VendorPaymentAcknowledgmentId, ids);
            Assert.DoesNotContain(future.VendorPaymentAcknowledgmentId, ids);
            Assert.DoesNotContain(confirmedPending.VendorPaymentAcknowledgmentId, ids);
            Assert.DoesNotContain(skippedPending.VendorPaymentAcknowledgmentId, ids);
            Assert.All(records, record => Assert.Equal(VendorPaymentAcknowledgmentStatuses.RetryPending, record.AcknowledgmentStatus));
        }
        finally
        {
            await CleanupAcknowledgmentsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies the expected DB enum, uniqueness constraint, and query indexes exist.
    /// </summary>
    [Fact]
    public async Task VendorPaymentAcknowledgmentsSchema_HasExpectedStatusesConstraintAndIndexes()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var statuses = await ReadStatusLabelsAsync(connection);
        Assert.Equal(
            [
                VendorPaymentAcknowledgmentStatuses.Pending,
                VendorPaymentAcknowledgmentStatuses.Confirmed,
                VendorPaymentAcknowledgmentStatuses.Failed,
                VendorPaymentAcknowledgmentStatuses.SkippedDisabled,
                VendorPaymentAcknowledgmentStatuses.RetryPending,
                VendorPaymentAcknowledgmentStatuses.Cancelled
            ],
            statuses);

        Assert.True(await ConstraintExistsAsync(connection, "uq_vendor_payment_ack__payment_confirmation_vendor"));

        foreach (var indexName in new[]
                 {
                     "ix_vendor_payment_ack__payment_attempt_id",
                     "ix_vendor_payment_ack__payment_confirmation_id",
                     "ix_vendor_payment_ack__acknowledgment_status",
                     "ix_vendor_payment_ack__next_retry_at",
                     "ix_vendor_payment_ack__correlation_id"
                 })
        {
            Assert.True(await IndexExistsAsync(connection, indexName), $"Missing index {indexName}.");
        }
    }

    private static VendorPaymentAcknowledgmentRepository CreateRepository()
    {
        return new VendorPaymentAcknowledgmentRepository(ConnectionString);
    }

    private static PaymentTestContext CreateContext(string testName)
    {
        return PaymentTestContext.Create($"{testName}_{Guid.NewGuid():N}");
    }

    private static CreateVendorPaymentAcknowledgmentCommand CreatePendingCommand(
        PaymentTestContext context,
        CreateAttemptResult attempt,
        RecordPaymentConfirmationResult confirmation)
    {
        return new CreateVendorPaymentAcknowledgmentCommand(
            confirmation.PaymentAttemptId,
            confirmation.PaymentConfirmationId,
            attempt.ParkingSessionId,
            "HIKCENTRAL",
            "HIK:CARD-123",
            "TICKET-123",
            "CARD-123",
            5000,
            "PHP",
            $"vendor-ack-{confirmation.PaymentConfirmationId:N}",
            context.CorrelationId,
            DateTimeOffset.UtcNow);
    }

    private static async Task<(CreateAttemptResult Attempt, RecordPaymentConfirmationResult Confirmation)> CreateConfirmedPaymentAsync(
        PaymentTestContext context)
    {
        var parkingSessionId = Guid.NewGuid();
        var paymentAttemptId = Guid.NewGuid();
        var paymentConfirmationId = Guid.NewGuid();
        var tariffSnapshotId = Guid.NewGuid();
        var providerReference = $"PCONF-VENDOR-ACK-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        const string sql = """
            INSERT INTO core.parking_sessions (
                parking_session_id,
                site_group_id,
                site_id,
                vendor_system_id,
                vendor_session_ref,
                plate_number_hash,
                plate_number_masked,
                ticket_number_hash,
                ticket_number_masked,
                entry_at,
                vendor_session_status,
                session_status,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            SELECT
                @parking_session_id,
                site_group_id,
                site_id,
                vendor_system_id,
                @vendor_session_ref,
                plate_number_hash,
                plate_number_masked,
                ticket_number_hash,
                ticket_number_masked,
                entry_at,
                vendor_session_status,
                session_status,
                @correlation_id,
                @now,
                @requested_by,
                @now,
                @requested_by,
                1
            FROM core.parking_sessions
            WHERE parking_session_id = @source_parking_session_id;

            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
                superseded_by_tariff_snapshot_id,
                vendor_system_id,
                vendor_tariff_ref,
                tariff_version_reference,
                currency_code,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                statutory_discount_validation_id,
                coupon_application_id,
                snapshot_status,
                calculated_at,
                expires_at,
                consumed_at,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            SELECT
                @tariff_snapshot_id,
                @parking_session_id,
                NULL,
                vendor_system_id,
                @vendor_tariff_ref,
                @tariff_version_reference,
                currency_code,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                statutory_discount_validation_id,
                coupon_application_id,
                snapshot_status,
                @now,
                @now + INTERVAL '1 hour',
                NULL,
                @correlation_id,
                @now,
                @requested_by,
                @now,
                @requested_by,
                1
            FROM core.tariff_snapshots
            WHERE tariff_snapshot_id = @source_tariff_snapshot_id;

            INSERT INTO core.payment_attempts (
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                idempotency_key,
                payment_rail_id,
                currency_code,
                amount,
                attempt_status,
                requested_at,
                expires_at,
                finalized_at,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @payment_attempt_id,
                @parking_session_id,
                @tariff_snapshot_id,
                @idempotency_key,
                NULL,
                'PHP',
                50.00,
                'CONFIRMED'::core.payment_attempt_status_enum,
                @now,
                @now + INTERVAL '15 minutes',
                @now,
                @correlation_id,
                @now,
                @requested_by,
                @now,
                @requested_by,
                1
            );

            INSERT INTO core.payment_confirmations (
                payment_confirmation_id,
                payment_attempt_id,
                provider_outcome_id,
                payment_rail_id,
                provider_transaction_ref,
                currency_code,
                confirmed_amount,
                confirmation_status,
                verified_at,
                confirmed_at,
                correlation_id,
                created_at,
                created_by_service_identity_id
            )
            VALUES (
                @payment_confirmation_id,
                @payment_attempt_id,
                NULL,
                NULL,
                @provider_reference,
                'PHP',
                50.00,
                'RECORDED'::core.payment_confirmation_status_enum,
                @now,
                @now,
                @correlation_id,
                @now,
                @requested_by
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("payment_confirmation_id", paymentConfirmationId);
        command.Parameters.AddWithValue("source_parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        command.Parameters.AddWithValue("vendor_session_ref", $"VACK-VSESSION-{parkingSessionId:N}");
        command.Parameters.AddWithValue("source_tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);
        command.Parameters.AddWithValue("vendor_tariff_ref", $"VTAR-{tariffSnapshotId:N}");
        command.Parameters.AddWithValue("tariff_version_reference", $"TVR-{tariffSnapshotId:N}");
        command.Parameters.AddWithValue("idempotency_key", $"idem-vendor-ack-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("provider_reference", providerReference);
        command.Parameters.AddWithValue("correlation_id", context.CorrelationId);
        command.Parameters.AddWithValue("requested_by", context.RequestedByUserId);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync();

        return (
            new CreateAttemptResult(
                paymentAttemptId,
                parkingSessionId,
                tariffSnapshotId,
                "CONFIRMED",
                "DIRECT_TEST"),
            new RecordPaymentConfirmationResult(
                paymentConfirmationId,
                paymentAttemptId,
                providerReference,
                "RECORDED",
                now));
    }

    private static async Task CleanupAcknowledgmentsAsync(PaymentTestContext context)
    {
        const string sql = """
            DELETE FROM integration.vendor_payment_acknowledgments
            WHERE correlation_id = @correlation_id
               OR payment_attempt_id IN (
                    SELECT payment_attempt_id
                    FROM core.payment_attempts
                    WHERE parking_session_id = @parking_session_id
               )
               OR payment_confirmation_id IN (
                    SELECT pc.payment_confirmation_id
                    FROM core.payment_confirmations pc
                    INNER JOIN core.payment_attempts pa
                        ON pa.payment_attempt_id = pc.payment_attempt_id
                    WHERE pa.parking_session_id = @parking_session_id
               );

            DELETE FROM core.payment_confirmations
            WHERE payment_attempt_id IN (
                SELECT pa.payment_attempt_id
                FROM core.payment_attempts pa
                INNER JOIN core.parking_sessions ps
                    ON ps.parking_session_id = pa.parking_session_id
                WHERE ps.correlation_id = @correlation_id
                  AND ps.parking_session_id <> @parking_session_id
            );

            DELETE FROM core.payment_attempts
            WHERE payment_attempt_id IN (
                SELECT pa.payment_attempt_id
                FROM core.payment_attempts pa
                INNER JOIN core.parking_sessions ps
                    ON ps.parking_session_id = pa.parking_session_id
                WHERE ps.correlation_id = @correlation_id
                  AND ps.parking_session_id <> @parking_session_id
            );

            DELETE FROM core.tariff_snapshots
            WHERE parking_session_id IN (
                SELECT parking_session_id
                FROM core.parking_sessions
                WHERE correlation_id = @correlation_id
                  AND parking_session_id <> @parking_session_id
            );

            DELETE FROM core.parking_sessions
            WHERE correlation_id = @correlation_id
              AND parking_session_id <> @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("correlation_id", context.CorrelationId);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetRetryPendingAsync(
        Guid vendorPaymentAcknowledgmentId,
        DateTimeOffset? nextRetryAt)
    {
        const string sql = """
            UPDATE integration.vendor_payment_acknowledgments
            SET acknowledgment_status = 'RETRY_PENDING'::integration.vendor_payment_acknowledgment_status_enum,
                next_retry_at = @next_retry_at,
                updated_at = @updated_at
            WHERE vendor_payment_acknowledgment_id = @vendor_payment_acknowledgment_id;
            """;

        var now = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("vendor_payment_acknowledgment_id", vendorPaymentAcknowledgmentId);
        command.Parameters.Add("next_retry_at", NpgsqlTypes.NpgsqlDbType.TimestampTz).Value =
            nextRetryAt.HasValue ? nextRetryAt.Value : (object)DBNull.Value;
        command.Parameters.Add("updated_at", NpgsqlTypes.NpgsqlDbType.TimestampTz).Value = now;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadPaymentAttemptStatusAsync(Guid paymentAttemptId)
    {
        const string sql = """
            SELECT attempt_status::text
            FROM core.payment_attempts
            WHERE payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }

    private static async Task<IReadOnlyList<string>> ReadStatusLabelsAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT e.enumlabel
            FROM pg_type t
            INNER JOIN pg_namespace n ON n.oid = t.typnamespace
            INNER JOIN pg_enum e ON e.enumtypid = t.oid
            WHERE n.nspname = 'integration'
              AND t.typname = 'vendor_payment_acknowledgment_status_enum'
            ORDER BY e.enumsortorder;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<bool> ConstraintExistsAsync(NpgsqlConnection connection, string constraintName)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_constraint c
                INNER JOIN pg_namespace n ON n.oid = c.connamespace
                WHERE n.nspname = 'integration'
                  AND c.conname = @constraint_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("constraint_name", constraintName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> IndexExistsAsync(NpgsqlConnection connection, string indexName)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_indexes
                WHERE schemaname = 'integration'
                  AND indexname = @index_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("index_name", indexName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }
}
