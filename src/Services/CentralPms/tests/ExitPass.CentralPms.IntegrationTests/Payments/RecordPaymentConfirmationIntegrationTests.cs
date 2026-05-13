using System;
using System.Threading.Tasks;
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
/// - Duplicate provider confirmation must not create ambiguous state
/// - Confirmation persistence must preserve provider reference traceability
/// </summary>
public sealed class RecordPaymentConfirmationIntegrationTests
{
    private const string ConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{ConnectionStringEnvVar}'. " +
            "Point it at the ExitPass integration database.");

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
    /// duplicate provider references are rejected deterministically.
    /// </summary>
    [Fact]
    public async Task RecordPaymentConfirmation_WhenProviderReferenceReplayed_RejectsDuplicate()
    {
        var context = PaymentTestContext.Create(
            nameof(RecordPaymentConfirmation_WhenProviderReferenceReplayed_RejectsDuplicate));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for record-payment-confirmation tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-record-confirmation-replay",
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
                    paymentAttemptId: attempt.PaymentAttemptId,
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
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Documents the deferred ExitPass v1.2 BRD 10.7.10 idempotent replay behavior while the active
    /// contract enforces deterministic duplicate rejection.
    /// </summary>
    [Fact(Skip = "Enable after record_payment_confirmation() contract is locked to idempotent same-reference replay behavior.")]
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
}
