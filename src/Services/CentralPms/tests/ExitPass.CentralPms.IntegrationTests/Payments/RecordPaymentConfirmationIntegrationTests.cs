using System;
using System.Threading.Tasks;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Infrastructure.Payments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Payments;

/// <summary>
/// Verifies DB-backed persistence rules for record_payment_confirmation().
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 10.7.9 Provider Outcome Traceability Invariant
/// - 10.7.10 Idempotent Payment Confirmation Invariant
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 7.3 Provider Callback / Confirmation Handling
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - Payment confirmation must remain tied to one canonical PaymentAttempt
/// - Same-attempt webhook replay must return the existing PaymentConfirmation deterministically
/// - Cross-attempt duplicate provider confirmation must not create ambiguous state
/// - Confirmation persistence must preserve provider reference traceability
/// </summary>
public sealed class RecordPaymentConfirmationIntegrationTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Verifies ExitPass v1.2 BRD 9.10 and 10.7.9, SDD 6.4 and 7.3, and the invariant that
    /// provider confirmation evidence is persisted against one canonical PaymentAttempt.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_WhenAttemptExists_PersistsConfirmation()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_WhenAttemptExists_PersistsConfirmation));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for record-payment-confirmation tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-record-confirmation-success",
                "payment-confirmation-test");

            var confirmation = await RecordPaymentConfirmationAsync(
                paymentAttemptId: attempt.PaymentAttemptId,
                providerReference: $"PCONF-{Guid.NewGuid():N}",
                providerStatus: "SUCCESS",
                requestedBy: "payment-provider-callback",
                correlationId: context.CorrelationId);

            Assert.NotNull(confirmation);
            Assert.Equal(attempt.PaymentAttemptId, confirmation!.PaymentAttemptId);
            Assert.Equal("SUCCESS", confirmation.ProviderStatus);
            Assert.False(string.IsNullOrWhiteSpace(confirmation.ProviderReference));

            var persisted = await GetPaymentConfirmationByIdAsync(confirmation.PaymentConfirmationId);
            Assert.NotNull(persisted);
            Assert.Equal(attempt.PaymentAttemptId, persisted!.PaymentAttemptId);
            Assert.Equal(confirmation.ProviderReference, persisted.ProviderReference);
            Assert.Equal("RECORDED", persisted.ProviderStatus);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies ExitPass v1.2 BRD 9.10 and 10.7.9, SDD 7.3 and 9.6, and the invariant that
    /// PaymentConfirmation cannot be recorded without an existing PaymentAttempt.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_WhenAttemptIsInvalid_RejectsPersistence()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_WhenAttemptIsInvalid_RejectsPersistence));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for record-payment-confirmation tests");

        try
        {
            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await RecordPaymentConfirmationAsync(
                    paymentAttemptId: Guid.NewGuid(),
                    providerReference: $"PCONF-{Guid.NewGuid():N}",
                    providerStatus: "SUCCESS",
                    requestedBy: "payment-provider-callback",
                    correlationId: context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies ExitPass v1.2 BRD 10.7.9 and 10.7.10, SDD 7.3 and 9.6, and the invariant that
    /// provider references cannot be reused across different canonical PaymentAttempts.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_WhenProviderReferenceUsedForDifferentAttempt_RejectsDuplicate()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_WhenProviderReferenceUsedForDifferentAttempt_RejectsDuplicate));
        var secondContext = PaymentTestContext.Create(
            $"{nameof(RecordPaymentConfirmation_WhenProviderReferenceUsedForDifferentAttempt_RejectsDuplicate)}SecondAttempt");

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for record-payment-confirmation tests");
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            secondContext,
            "Seed second attempt data for record-payment-confirmation duplicate-provider-reference tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-record-confirmation-replay",
                "payment-confirmation-test");
            var secondAttempt = await CreateAttemptAsync(
                ConnectionString,
                secondContext,
                "idem-record-confirmation-replay-second-attempt",
                "payment-confirmation-test");

            var providerReference = $"PCONF-{Guid.NewGuid():N}";

            var first = await RecordPaymentConfirmationAsync(
                paymentAttemptId: attempt.PaymentAttemptId,
                providerReference: providerReference,
                providerStatus: "SUCCESS",
                requestedBy: "payment-provider-callback",
                correlationId: context.CorrelationId);

            Assert.NotNull(first);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
            {
                await RecordPaymentConfirmationAsync(
                    paymentAttemptId: secondAttempt.PaymentAttemptId,
                    providerReference: providerReference,
                    providerStatus: "SUCCESS",
                    requestedBy: "payment-provider-callback",
                    correlationId: context.CorrelationId);
            });

            Assert.False(string.IsNullOrWhiteSpace(ex.SqlState));

            var persisted = await GetPaymentConfirmationByProviderRefAsync(providerReference);
            Assert.NotNull(persisted);
            Assert.Equal(attempt.PaymentAttemptId, persisted!.PaymentAttemptId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, secondContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies ExitPass v1.2 BRD 10.7.10, SDD 7.3 and 9.6, and the invariant that
    /// same-attempt same-provider-reference webhook replay returns the existing PaymentConfirmation.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_WhenProviderReferenceReplayed_IsIdempotent()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_WhenProviderReferenceReplayed_IsIdempotent));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for record-payment-confirmation tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-record-confirmation-idempotent",
                "payment-confirmation-test");

            var providerReference = $"PCONF-{Guid.NewGuid():N}";

            var first = await RecordPaymentConfirmationAsync(
                paymentAttemptId: attempt.PaymentAttemptId,
                providerReference: providerReference,
                providerStatus: "SUCCESS",
                requestedBy: "payment-provider-callback",
                correlationId: context.CorrelationId);

            var second = await RecordPaymentConfirmationAsync(
                paymentAttemptId: attempt.PaymentAttemptId,
                providerReference: providerReference,
                providerStatus: "SUCCESS",
                requestedBy: "payment-provider-callback",
                correlationId: context.CorrelationId);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first!.PaymentConfirmationId, second!.PaymentConfirmationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task RecordPaymentConfirmationGateway_WhenProviderAmountAndCurrencyMatchAttempt_PersistsConfirmation()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmationGateway_WhenProviderAmountAndCurrencyMatchAttempt_PersistsConfirmation));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for confirmation amount validation tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-record-confirmation-amount-match",
                "payment-confirmation-test");

            var service = new RecordPaymentConfirmationService(new RecordPaymentConfirmationGateway(ConnectionString));
            var confirmation = await service.ExecuteAsync(
                new RecordPaymentConfirmationCommand(
                    attempt.PaymentAttemptId,
                    $"PCONF-{Guid.NewGuid():N}",
                    "SUCCESS",
                    "payment-provider-callback",
                    RawCallbackReference: null,
                    ProviderSignatureValid: true,
                    ProviderPayloadHash: null,
                    AmountConfirmed: 100.00m,
                    CurrencyCode: "PHP",
                    context.CorrelationId),
                CancellationToken.None);

            var persisted = await PaymentRoutineTestHelper.GetPaymentConfirmationByIdAsync(
                ConnectionString,
                confirmation.PaymentConfirmationId);

            Assert.NotNull(persisted);
            Assert.Equal(100.00m, persisted!.AmountConfirmed);
            Assert.Equal("PHP", persisted.CurrencyCode.Trim());
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task RecordPaymentConfirmationGateway_WhenProviderAmountDiffersFromAttempt_RejectsConfirmation()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmationGateway_WhenProviderAmountDiffersFromAttempt_RejectsConfirmation));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for confirmation amount mismatch tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-record-confirmation-amount-mismatch",
                "payment-confirmation-test");

            var service = new RecordPaymentConfirmationService(new RecordPaymentConfirmationGateway(ConnectionString));
            var ex = await Assert.ThrowsAsync<PaymentConfirmationConflictException>(() =>
                service.ExecuteAsync(
                    new RecordPaymentConfirmationCommand(
                        attempt.PaymentAttemptId,
                        $"PCONF-{Guid.NewGuid():N}",
                        "SUCCESS",
                        "payment-provider-callback",
                        RawCallbackReference: null,
                        ProviderSignatureValid: true,
                        ProviderPayloadHash: null,
                        AmountConfirmed: 99.99m,
                        CurrencyCode: "PHP",
                        context.CorrelationId),
                    CancellationToken.None));

            Assert.Equal("PAYMENT_AMOUNT_MISMATCH", ex.ErrorCode);
            Assert.Equal(0, await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                attempt.PaymentAttemptId));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task RecordPaymentConfirmationGateway_WhenProviderCurrencyDiffersFromAttempt_RejectsConfirmation()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmationGateway_WhenProviderCurrencyDiffersFromAttempt_RejectsConfirmation));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for confirmation currency mismatch tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-record-confirmation-currency-mismatch",
                "payment-confirmation-test");

            var service = new RecordPaymentConfirmationService(new RecordPaymentConfirmationGateway(ConnectionString));
            var ex = await Assert.ThrowsAsync<PaymentConfirmationConflictException>(() =>
                service.ExecuteAsync(
                    new RecordPaymentConfirmationCommand(
                        attempt.PaymentAttemptId,
                        $"PCONF-{Guid.NewGuid():N}",
                        "SUCCESS",
                        "payment-provider-callback",
                        RawCallbackReference: null,
                        ProviderSignatureValid: true,
                        ProviderPayloadHash: null,
                        AmountConfirmed: 100.00m,
                        CurrencyCode: "USD",
                        context.CorrelationId),
                    CancellationToken.None));

            Assert.Equal("PAYMENT_CURRENCY_MISMATCH", ex.ErrorCode);
            Assert.Equal(0, await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                attempt.PaymentAttemptId));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task RecordPaymentConfirmationGateway_WhenAppliedSnapshotWasConsumed_ConfirmsAgainstAttemptSnapshot()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmationGateway_WhenAppliedSnapshotWasConsumed_ConfirmsAgainstAttemptSnapshot));

        await CleanupDiscountRowsAsync(context);
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for applied confirmation tests");

        try
        {
            var appliedTariffSnapshotId = await CreateAppliedPayableBasisAsync(context);
            var attempt = await CreateAttemptForTariffSnapshotAsync(
                context,
                appliedTariffSnapshotId,
                $"idem-record-confirmation-applied-consumed-{Guid.NewGuid():N}");

            var service = new RecordPaymentConfirmationService(new RecordPaymentConfirmationGateway(ConnectionString));
            var confirmation = await service.ExecuteAsync(
                new RecordPaymentConfirmationCommand(
                    attempt.PaymentAttemptId,
                    $"PCONF-{Guid.NewGuid():N}",
                    "SUCCESS",
                    "payment-provider-callback",
                    RawCallbackReference: null,
                    ProviderSignatureValid: true,
                    ProviderPayloadHash: null,
                    AmountConfirmed: 71.43m,
                    CurrencyCode: "PHP",
                    context.CorrelationId),
                CancellationToken.None);

            var persistedAttempt = await PaymentRoutineTestHelper.GetPaymentAttemptAsync(
                ConnectionString,
                attempt.PaymentAttemptId);
            var persistedConfirmation = await PaymentRoutineTestHelper.GetPaymentConfirmationByIdAsync(
                ConnectionString,
                confirmation.PaymentConfirmationId);

            Assert.NotNull(persistedAttempt);
            Assert.NotNull(persistedConfirmation);
            Assert.Equal(appliedTariffSnapshotId, persistedAttempt!.TariffSnapshotId);
            Assert.Equal(71.43m, persistedConfirmation!.AmountConfirmed);
            Assert.Equal("PHP", persistedConfirmation.CurrencyCode.Trim());
            Assert.Equal("CONSUMED", await ReadTariffSnapshotStatusAsync(appliedTariffSnapshotId));
        }
        finally
        {
            await CleanupDiscountRowsAsync(context);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static async Task<RecordPaymentConfirmationResult?> RecordPaymentConfirmationAsync(
        Guid paymentAttemptId,
        string providerReference,
        string providerStatus,
        string requestedBy,
        Guid correlationId)
    {
        const string sql = """
            SELECT
                payment_confirmation_id,
                payment_attempt_id,
                provider_reference,
                provider_status,
                verified_timestamp
            FROM core.record_payment_confirmation(
                @p_payment_attempt_id,
                @p_provider_reference,
                @p_provider_status,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("p_payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("p_provider_reference", providerReference);
        command.Parameters.AddWithValue("p_provider_status", providerStatus);
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new RecordPaymentConfirmationResult(
            PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ProviderReference: reader.GetString(reader.GetOrdinal("provider_reference")),
            ProviderStatus: reader.GetString(reader.GetOrdinal("provider_status")),
            VerifiedTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("verified_timestamp")));
    }

    private static async Task<PaymentConfirmationRow?> GetPaymentConfirmationByIdAsync(Guid paymentConfirmationId)
    {
        const string sql = """
            SELECT
                payment_confirmation_id,
                payment_attempt_id,
                provider_transaction_ref AS provider_reference,
                confirmation_status::text AS provider_status,
                verified_at AS verified_timestamp
            FROM core.payment_confirmations
            WHERE payment_confirmation_id = @payment_confirmation_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.AddWithValue("payment_confirmation_id", paymentConfirmationId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PaymentConfirmationRow(
            PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ProviderReference: reader.GetString(reader.GetOrdinal("provider_reference")),
            ProviderStatus: reader.GetString(reader.GetOrdinal("provider_status")),
            VerifiedTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("verified_timestamp")));
    }

    private static async Task<PaymentConfirmationRow?> GetPaymentConfirmationByProviderRefAsync(string providerReference)
    {
        const string sql = """
            SELECT
                payment_confirmation_id,
                payment_attempt_id,
                provider_transaction_ref AS provider_reference,
                confirmation_status::text AS provider_status,
                verified_at AS verified_timestamp
            FROM core.payment_confirmations
            WHERE provider_transaction_ref = @provider_reference;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };
        command.Parameters.AddWithValue("provider_reference", providerReference);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PaymentConfirmationRow(
            PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ProviderReference: reader.GetString(reader.GetOrdinal("provider_reference")),
            ProviderStatus: reader.GetString(reader.GetOrdinal("provider_status")),
            VerifiedTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("verified_timestamp")));
    }

    private sealed record RecordPaymentConfirmationResult(
        Guid PaymentConfirmationId,
        Guid PaymentAttemptId,
        string ProviderReference,
        string ProviderStatus,
        DateTimeOffset VerifiedTimestamp);

    private sealed record PaymentConfirmationRow(
        Guid PaymentConfirmationId,
        Guid PaymentAttemptId,
        string ProviderReference,
        string ProviderStatus,
        DateTimeOffset VerifiedTimestamp);

    private static async Task<Guid> CreateAppliedPayableBasisAsync(PaymentTestContext context)
    {
        var validationId = Guid.NewGuid();
        var appliedTariffSnapshotId = Guid.NewGuid();

        const string sql = """
            UPDATE core.tariff_snapshots
            SET snapshot_status = 'SUPERSEDED',
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @original_tariff_snapshot_id;

            INSERT INTO discounts.statutory_discount_validations (
                statutory_discount_validation_id,
                parking_session_id,
                tariff_snapshot_id,
                entitlement_type,
                policy_resolution_basis,
                local_ordinance_applied,
                national_law_fallback_applied,
                validation_channel,
                validation_status,
                currency_code,
                gross_amount_at_validation,
                statutory_discount_amount,
                net_amount_after_discount,
                evidence_required,
                evidence_captured,
                requested_at,
                validated_at,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                @validation_id,
                @parking_session_id,
                @original_tariff_snapshot_id,
                'SENIOR_CITIZEN',
                'NATIONAL_LAW_FALLBACK',
                FALSE,
                TRUE,
                'OPERATOR_ASSISTED',
                'APPROVED',
                'PHP',
                100.00,
                17.86,
                71.43,
                FALSE,
                TRUE,
                NOW(),
                NOW(),
                @correlation_id,
                NOW(),
                @service_identity_id,
                NOW(),
                @service_identity_id,
                1
            );

            INSERT INTO core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
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
                @applied_tariff_snapshot_id,
                ts.parking_session_id,
                ts.vendor_system_id,
                ts.vendor_tariff_ref,
                ts.tariff_version_reference || '|APPLIED',
                ts.currency_code,
                100.00,
                17.86,
                0.00,
                71.43,
                @validation_id,
                NULL,
                'ACTIVE',
                NOW(),
                ts.expires_at,
                NULL,
                @correlation_id,
                NOW(),
                ts.created_by_service_identity_id,
                NOW(),
                ts.updated_by_service_identity_id,
                1
            FROM core.tariff_snapshots AS ts
            WHERE ts.tariff_snapshot_id = @original_tariff_snapshot_id;

            UPDATE core.tariff_snapshots
            SET superseded_by_tariff_snapshot_id = @applied_tariff_snapshot_id,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @original_tariff_snapshot_id;

            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("validation_id", validationId);
        command.Parameters.AddWithValue("applied_tariff_snapshot_id", appliedTariffSnapshotId);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("original_tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("correlation_id", context.CorrelationId);
        command.Parameters.AddWithValue("service_identity_id", context.RequestedByUserId);
        await command.ExecuteNonQueryAsync();

        return appliedTariffSnapshotId;
    }

    private static async Task<CreateAttemptResult> CreateAttemptForTariffSnapshotAsync(
        PaymentTestContext context,
        Guid tariffSnapshotId,
        string idempotencyKey)
    {
        const string sql = """
            SELECT
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                attempt_status
            FROM core.create_or_reuse_payment_attempt(
                @p_parking_session_id,
                @p_tariff_snapshot_id,
                'GCASH',
                @p_idempotency_key,
                'payment-confirmation-test',
                @p_correlation_id,
                NOW()
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p_parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("p_tariff_snapshot_id", tariffSnapshotId);
        command.Parameters.AddWithValue("p_idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("p_correlation_id", context.CorrelationId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return new CreateAttemptResult(
            reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetString(reader.GetOrdinal("attempt_status")));
    }

    private static async Task<string> ReadTariffSnapshotStatusAsync(Guid tariffSnapshotId)
    {
        const string sql = """
            SELECT snapshot_status::text
            FROM core.tariff_snapshots
            WHERE tariff_snapshot_id = @tariff_snapshot_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);

        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }

    private static async Task CleanupDiscountRowsAsync(PaymentTestContext context)
    {
        const string sql = """
            UPDATE core.tariff_snapshots
            SET statutory_discount_validation_id = NULL,
                superseded_by_tariff_snapshot_id = NULL,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_payable_basis_applications
            WHERE parking_session_id = @parking_session_id;

            DELETE FROM discounts.statutory_discount_validations
            WHERE parking_session_id = @parking_session_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record CreateAttemptResult(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string AttemptStatus);
}
