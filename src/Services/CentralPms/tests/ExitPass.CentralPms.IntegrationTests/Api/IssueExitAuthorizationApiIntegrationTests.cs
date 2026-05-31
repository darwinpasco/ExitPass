using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the internal HTTP contract for issuing exit authorizations.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 10.6 Internal Service APIs
///
/// Invariants Enforced:
/// - Only Central PMS may issue ExitAuthorization
/// - HTTP boundary requires correlation and idempotency headers before issuance
/// - ExitAuthorization may only be issued from confirmed payment finality
/// </summary>
public sealed class IssueExitAuthorizationApiIntegrationTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Creates the issue-authorization API integration test fixture.
    /// </summary>
    public IssueExitAuthorizationApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Verifies that issuance succeeds for a confirmed payment attempt.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenPaymentAttemptIsConfirmed_ReturnsOk()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenPaymentAttemptIsConfirmed_ReturnsOk));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization API tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-issue-auth-api-success",
                "issue-auth-api-test");

            await FinalizeAttemptAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                "CONFIRMED",
                "central-pms-finalizer",
                context.CorrelationId);

            var confirmation = await RecordPaymentConfirmationAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                $"prov-{Guid.NewGuid():N}",
                "issue-auth-api-test",
                context.CorrelationId);

            Assert.NotNull(confirmation);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/internal/payment-attempts/{attempt.PaymentAttemptId}/issue-exit-authorization");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            request.Headers.Add("Idempotency-Key", "idem-http-issue-auth-success");

            request.Content = JsonContent.Create(new IssueExitAuthorizationRequest(
                ParkingSessionId: attempt.ParkingSessionId,
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();
            Assert.NotNull(body);
            Assert.Equal(attempt.PaymentAttemptId, body!.PaymentAttemptId);
            Assert.Equal(attempt.ParkingSessionId, body.ParkingSessionId);
            Assert.Equal("ISSUED", body.AuthorizationStatus);
            Assert.False(string.IsNullOrWhiteSpace(body.AuthorizationToken));
            Assert.True(body.ExpirationTimestamp > body.IssuedAt);

            var persisted = await PaymentRoutineTestHelper.GetExitAuthorizationByIdAsync(
                ConnectionString,
                body.ExitAuthorizationId);
            Assert.NotNull(persisted);
            Assert.Equal(attempt.PaymentAttemptId, persisted!.PaymentAttemptId);

            var sideEffects = await CountBoundarySideEffectsAsync(ConnectionString, attempt.PaymentAttemptId);
            Assert.Equal(0, sideEffects.GateConsumptionCount);
            Assert.Equal(0, sideEffects.CouponApplicationCount);
            Assert.Equal(0, sideEffects.ReconciliationItemCount);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that issuance remains aligned to a consumed APPLIED/effective tariff snapshot stored on the payment attempt.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenConfirmedAttemptUsesAppliedEffectiveTariff_ReturnsOk()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenConfirmedAttemptUsesAppliedEffectiveTariff_ReturnsOk));
        var originalTariffSnapshotId = Guid.NewGuid();

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for APPLIED effective tariff issue-exit-authorization API tests");

        try
        {
            await PrepareAppliedEffectiveTariffSnapshotAsync(
                ConnectionString,
                context,
                originalTariffSnapshotId);

            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-issue-auth-api-applied-effective",
                "issue-auth-api-test");

            Assert.Equal(context.TariffSnapshotId, attempt.TariffSnapshotId);

            var confirmed = await RecordPaymentConfirmationAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                $"prov-{Guid.NewGuid():N}",
                "issue-auth-api-test",
                context.CorrelationId);

            Assert.NotNull(confirmed);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/internal/payment-attempts/{attempt.PaymentAttemptId}/issue-exit-authorization");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            request.Headers.Add("Idempotency-Key", "idem-http-issue-auth-applied-effective");
            request.Content = JsonContent.Create(new IssueExitAuthorizationRequest(
                ParkingSessionId: attempt.ParkingSessionId,
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();
            Assert.NotNull(body);
            Assert.Equal(attempt.PaymentAttemptId, body!.PaymentAttemptId);
            Assert.Equal("ISSUED", body.AuthorizationStatus);

            var basis = await ReadPaymentAttemptTariffBasisAsync(ConnectionString, attempt.PaymentAttemptId);
            Assert.Equal(context.TariffSnapshotId, basis.TariffSnapshotId);
            Assert.NotEqual(originalTariffSnapshotId, basis.TariffSnapshotId);
            Assert.Equal("CONSUMED", basis.TariffSnapshotStatus);
            Assert.Equal(89.29m, basis.PaymentAttemptAmount);
            Assert.Equal(89.29m, basis.TariffNetAmount);
            Assert.Equal("PHP", basis.PaymentAttemptCurrency);
            Assert.Equal("PHP", basis.TariffCurrency);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests without a correlation header.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenCorrelationHeaderIsMissing_ReturnsBadRequest()
    {
        var paymentAttemptId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization");

        request.Headers.Add("Idempotency-Key", "idem-http-issue-auth-missing-correlation");

        request.Content = JsonContent.Create(new IssueExitAuthorizationRequest(
            ParkingSessionId: Guid.NewGuid(),
            RequestedByUserId: Guid.NewGuid()));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("INVALID_REQUEST", body!.ErrorCode);
        Assert.Contains("X-Correlation-Id", body.Message);
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests without an idempotency key.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenIdempotencyKeyIsMissing_ReturnsBadRequest()
    {
        var paymentAttemptId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization");

        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());

        request.Content = JsonContent.Create(new IssueExitAuthorizationRequest(
            ParkingSessionId: Guid.NewGuid(),
            RequestedByUserId: Guid.NewGuid()));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("INVALID_REQUEST", body!.ErrorCode);
        Assert.Contains("Idempotency-Key", body.Message);
    }

    /// <summary>
    /// Verifies that issuance returns not found for a non-existent payment attempt.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenPaymentAttemptDoesNotExist_ReturnsNotFound()
    {
        var correlationId = Guid.NewGuid();
        var paymentAttemptId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization");

        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        request.Headers.Add("Idempotency-Key", "idem-http-issue-auth-not-found");

        request.Content = JsonContent.Create(new IssueExitAuthorizationRequest(
            ParkingSessionId: Guid.NewGuid(),
            RequestedByUserId: Guid.NewGuid()));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("PAYMENT_ATTEMPT_NOT_FOUND", body!.ErrorCode);
        Assert.Equal(correlationId, body.CorrelationId);
    }

    /// <summary>
    /// Verifies that issuance returns conflict when the payment attempt is not yet confirmed.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenPaymentAttemptIsNotConfirmed_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenPaymentAttemptIsNotConfirmed_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization API tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-issue-auth-api-not-confirmed",
                "issue-auth-api-test");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/internal/payment-attempts/{attempt.PaymentAttemptId}/issue-exit-authorization");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            request.Headers.Add("Idempotency-Key", "idem-http-issue-auth-not-confirmed");

            request.Content = JsonContent.Create(new IssueExitAuthorizationRequest(
                ParkingSessionId: attempt.ParkingSessionId,
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(body);
            Assert.Equal("PAYMENT_ATTEMPT_NOT_CONFIRMED", body!.ErrorCode);
            Assert.Equal(context.CorrelationId, body.CorrelationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that issuance rejects a confirmed payment attempt whose amount no longer matches its stored tariff snapshot.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenPaymentAttemptAmountDoesNotMatchTariff_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenPaymentAttemptAmountDoesNotMatchTariff_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization amount mismatch API tests");

        try
        {
            var attempt = await CreateConfirmedAttemptAsync(
                context,
                "idem-issue-auth-api-amount-mismatch");

            await MutatePaymentAttemptAmountAsync(ConnectionString, attempt.PaymentAttemptId, 101.00m);

            using var response = await PostIssueAuthorizationAsync(
                attempt.PaymentAttemptId,
                attempt.ParkingSessionId,
                context.RequestedByUserId,
                context.CorrelationId,
                "idem-http-issue-auth-amount-mismatch");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(body);
            Assert.Equal("PAYMENT_AMOUNT_MISMATCH", body!.ErrorCode);

            Assert.Equal(0, await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                attempt.ParkingSessionId,
                issuedOnly: false));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that issuance rejects a confirmed payment attempt whose currency no longer matches its stored tariff snapshot.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenPaymentAttemptCurrencyDoesNotMatchTariff_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenPaymentAttemptCurrencyDoesNotMatchTariff_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization currency mismatch API tests");

        try
        {
            var attempt = await CreateConfirmedAttemptAsync(
                context,
                "idem-issue-auth-api-currency-mismatch");

            await MutatePaymentAttemptCurrencyAsync(ConnectionString, attempt.PaymentAttemptId, "USD");

            using var response = await PostIssueAuthorizationAsync(
                attempt.PaymentAttemptId,
                attempt.ParkingSessionId,
                context.RequestedByUserId,
                context.CorrelationId,
                "idem-http-issue-auth-currency-mismatch");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(body);
            Assert.Equal("PAYMENT_CURRENCY_MISMATCH", body!.ErrorCode);

            Assert.Equal(0, await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                attempt.ParkingSessionId,
                issuedOnly: false));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that replay for the same confirmed payment attempt returns the existing authorization.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenReplayedForConfirmedAttempt_ReturnsExistingAuthorization()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenReplayedForConfirmedAttempt_ReturnsExistingAuthorization));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization replay API tests");

        try
        {
            var attempt = await CreateConfirmedAttemptAsync(
                context,
                "idem-issue-auth-api-replay");

            using var firstResponse = await PostIssueAuthorizationAsync(
                attempt.PaymentAttemptId,
                attempt.ParkingSessionId,
                context.RequestedByUserId,
                context.CorrelationId,
                "idem-http-issue-auth-replay-first");
            using var secondResponse = await PostIssueAuthorizationAsync(
                attempt.PaymentAttemptId,
                attempt.ParkingSessionId,
                context.RequestedByUserId,
                context.CorrelationId,
                "idem-http-issue-auth-replay-second");

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

            var first = await firstResponse.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();
            var second = await secondResponse.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first!.ExitAuthorizationId, second!.ExitAuthorizationId);
            Assert.Equal(1, await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                attempt.ParkingSessionId,
                issuedOnly: false));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests with an empty parking-session identifier in the body.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenBodyContainsEmptyParkingSessionId_ReturnsBadRequest()
    {
        var correlationId = Guid.NewGuid();
        var paymentAttemptId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization");

        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        request.Headers.Add("Idempotency-Key", "idem-http-issue-auth-empty-session");

        request.Content = JsonContent.Create(new IssueExitAuthorizationRequest(
            ParkingSessionId: Guid.Empty,
            RequestedByUserId: Guid.NewGuid()));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("INVALID_REQUEST", body!.ErrorCode);
        Assert.Contains("ParkingSessionId", body.Message);
    }

    private sealed record IssueExitAuthorizationRequest(
        Guid ParkingSessionId,
        Guid RequestedByUserId);

    private sealed record IssueExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken,
        string AuthorizationStatus,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpirationTimestamp);

    private async Task<PaymentRoutineTestHelper.CreateAttemptResult> CreateConfirmedAttemptAsync(
        PaymentTestContext context,
        string idempotencyKey)
    {
        var attempt = await CreateAttemptAsync(
            ConnectionString,
            context,
            idempotencyKey,
            "issue-auth-api-test");

        var confirmation = await RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            $"prov-{Guid.NewGuid():N}",
            "issue-auth-api-test",
            context.CorrelationId);

        Assert.NotNull(confirmation);
        return attempt;
    }

    private async Task<HttpResponseMessage> PostIssueAuthorizationAsync(
        Guid paymentAttemptId,
        Guid parkingSessionId,
        Guid requestedByUserId,
        Guid correlationId,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization");

        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new IssueExitAuthorizationRequest(
            ParkingSessionId: parkingSessionId,
            RequestedByUserId: requestedByUserId));

        return await _client.SendAsync(request);
    }

    private static async Task PrepareAppliedEffectiveTariffSnapshotAsync(
        string connectionString,
        PaymentTestContext context,
        Guid originalTariffSnapshotId)
    {
        const string sql = """
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
                @original_tariff_snapshot_id,
                ts.parking_session_id,
                @applied_tariff_snapshot_id,
                ts.vendor_system_id,
                'TEST-ORIGINAL-APPLIED',
                'TEST-ORIGINAL-APPLIED-V1',
                ts.currency_code,
                125.00,
                0.00,
                0.00,
                125.00,
                NULL,
                NULL,
                'SUPERSEDED',
                NOW(),
                NOW() + INTERVAL '1 hour',
                NULL,
                @correlation_id,
                NOW(),
                @requested_by,
                NOW(),
                @requested_by,
                1
            FROM core.tariff_snapshots ts
            WHERE ts.tariff_snapshot_id = @applied_tariff_snapshot_id;

            UPDATE core.tariff_snapshots
            SET
                superseded_by_tariff_snapshot_id = NULL,
                vendor_tariff_ref = 'TEST-APPLIED-EFFECTIVE',
                tariff_version_reference = 'TEST-APPLIED-EFFECTIVE-V1',
                gross_amount = 125.00,
                statutory_discount_amount = 35.71,
                coupon_discount_amount = 0.00,
                net_amount = 89.29,
                snapshot_status = 'ACTIVE',
                consumed_at = NULL,
                updated_at = NOW(),
                updated_by_service_identity_id = @requested_by,
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @applied_tariff_snapshot_id;
            """;

        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new Npgsql.NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30
        };

        command.Parameters.AddWithValue("original_tariff_snapshot_id", originalTariffSnapshotId);
        command.Parameters.AddWithValue("applied_tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("correlation_id", context.CorrelationId);
        command.Parameters.AddWithValue("requested_by", context.RequestedByUserId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task MutatePaymentAttemptAmountAsync(
        string connectionString,
        Guid paymentAttemptId,
        decimal amount)
    {
        const string sql = """
            UPDATE core.payment_attempts
            SET amount = @amount,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task MutatePaymentAttemptCurrencyAsync(
        string connectionString,
        Guid paymentAttemptId,
        string currencyCode)
    {
        const string sql = """
            UPDATE core.payment_attempts
            SET currency_code = @currency_code,
                updated_at = NOW(),
                row_version = row_version + 1
            WHERE payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("currency_code", currencyCode);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PaymentAttemptTariffBasis> ReadPaymentAttemptTariffBasisAsync(
        string connectionString,
        Guid paymentAttemptId)
    {
        const string sql = """
            SELECT
                pa.tariff_snapshot_id,
                pa.amount,
                pa.currency_code::text AS payment_attempt_currency,
                ts.net_amount,
                ts.currency_code::text AS tariff_currency,
                ts.snapshot_status::text AS tariff_snapshot_status
            FROM core.payment_attempts pa
            JOIN core.tariff_snapshots ts
              ON ts.tariff_snapshot_id = pa.tariff_snapshot_id
            WHERE pa.payment_attempt_id = @payment_attempt_id;
            """;

        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected payment attempt tariff basis row.");

        return new PaymentAttemptTariffBasis(
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetDecimal(reader.GetOrdinal("amount")),
            reader.GetString(reader.GetOrdinal("payment_attempt_currency")).Trim(),
            reader.GetDecimal(reader.GetOrdinal("net_amount")),
            reader.GetString(reader.GetOrdinal("tariff_currency")).Trim(),
            reader.GetString(reader.GetOrdinal("tariff_snapshot_status")));
    }

    private static async Task<BoundarySideEffects> CountBoundarySideEffectsAsync(
        string connectionString,
        Guid paymentAttemptId)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*)::int
                   FROM gates.gate_authorization_consumptions gac
                   JOIN core.exit_authorizations ea
                     ON ea.exit_authorization_id = gac.exit_authorization_id
                  WHERE ea.payment_attempt_id = @payment_attempt_id) AS gate_consumption_count,
                (SELECT COUNT(*)::int
                   FROM coupons.coupon_applications
                  WHERE payment_attempt_id = @payment_attempt_id) AS coupon_application_count,
                (SELECT COUNT(*)::int
                   FROM reconciliation.reconciliation_items
                  WHERE payment_attempt_id = @payment_attempt_id) AS reconciliation_item_count;
            """;

        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected boundary side-effect count row.");

        return new BoundarySideEffects(
            reader.GetInt32(reader.GetOrdinal("gate_consumption_count")),
            reader.GetInt32(reader.GetOrdinal("coupon_application_count")),
            reader.GetInt32(reader.GetOrdinal("reconciliation_item_count")));
    }

    private sealed record PaymentAttemptTariffBasis(
        Guid TariffSnapshotId,
        decimal PaymentAttemptAmount,
        string PaymentAttemptCurrency,
        decimal TariffNetAmount,
        string TariffCurrency,
        string TariffSnapshotStatus);

    private sealed record BoundarySideEffects(
        int GateConsumptionCount,
        int CouponApplicationCount,
        int ReconciliationItemCount);
}
