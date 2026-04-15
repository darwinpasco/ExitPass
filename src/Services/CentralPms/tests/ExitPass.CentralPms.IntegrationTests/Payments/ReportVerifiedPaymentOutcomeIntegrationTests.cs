using ExitPass.CentralPms.IntegrationTests.Shared;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Payments;

/// <summary>
/// Verifies the Central PMS internal verified-payment-outcome API.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.6 Internal Service APIs
///
/// Invariants Enforced:
/// - HTTP boundary requires X-Correlation-Id before side-effecting internal processing
/// - HTTP boundary requires Idempotency-Key before side-effecting internal processing
/// - Unknown payment attempts must fail deterministically with not found
/// - Duplicate provider references must fail deterministically with conflict
/// - Already-final payment attempts must fail deterministically with conflict
/// - A valid confirmed verified-payment-outcome request must finalize the attempt and issue exit authorization
/// </summary>
public sealed class ReportVerifiedPaymentOutcomeIntegrationTests
{
    private const string PrimaryDbConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";
    private const string AlternateDbConnectionStringEnvVar = "EXITPASS_TEST_DB_CONNECTION_STRING";
    private const string LegacyDbConnectionStringEnvVar = "ConnectionStrings__MainDatabase";

    private const string PrimaryApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_API_BASE_URL";
    private const string AlternateApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_BASE_URL";
    private const string LegacyApiBaseUrlEnvVar = "CENTRAL_PMS_BASE_URL";

    /// <summary>
    /// Gets the configured integration-test database connection string.
    /// </summary>
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(PrimaryDbConnectionStringEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateDbConnectionStringEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyDbConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Integration test database connection string is missing. Set one of: {PrimaryDbConnectionStringEnvVar}, {AlternateDbConnectionStringEnvVar}, or {LegacyDbConnectionStringEnvVar}.");

    /// <summary>
    /// Gets the configured Central PMS API base URI.
    /// </summary>
    private static Uri ApiBaseUri => new(
        Environment.GetEnvironmentVariable(PrimaryApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyApiBaseUrlEnvVar)
        ?? throw new InvalidOperationException(
            $"Central PMS API base URL is missing. Set one of: {PrimaryApiBaseUrlEnvVar}, {AlternateApiBaseUrlEnvVar}, or {LegacyApiBaseUrlEnvVar}."),
        UriKind.Absolute);

    /// <summary>
    /// Verifies that the endpoint rejects requests when the correlation header is missing.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_WhenCorrelationIdHeaderIsMissing_ReturnsBadRequest()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_WhenCorrelationIdHeaderIsMissing_ReturnsBadRequest));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for report-verified-payment-outcome API tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "outcome-test");

            using var client = CreateClient();

            var response = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: BuildValidRequest(created.PaymentAttemptId, context.ParkingSessionId, $"prov-{Guid.NewGuid():N}"),
                includeCorrelationId: false,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}");

            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("CORRELATION_ID_REQUIRED", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests when the idempotency header is missing.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_WhenIdempotencyKeyHeaderIsMissing_ReturnsBadRequest()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_WhenIdempotencyKeyHeaderIsMissing_ReturnsBadRequest));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for report-verified-payment-outcome API tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "outcome-test");

            using var client = CreateClient();

            var response = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: BuildValidRequest(created.PaymentAttemptId, context.ParkingSessionId, $"prov-{Guid.NewGuid():N}"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: false,
                idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}");

            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("IDEMPOTENCY_KEY_REQUIRED", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that reporting a verified outcome for an unknown payment attempt fails deterministically.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_WhenPaymentAttemptDoesNotExist_ReturnsNotFound()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_WhenPaymentAttemptDoesNotExist_ReturnsNotFound));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for report-verified-payment-outcome API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: BuildValidRequest(Guid.NewGuid(), context.ParkingSessionId, $"prov-{Guid.NewGuid():N}"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}");

            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("PAYMENT_ATTEMPT_NOT_FOUND", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that reusing the same provider reference for another verified outcome request is rejected.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_WhenProviderReferenceIsAlreadyRecorded_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_WhenProviderReferenceIsAlreadyRecorded_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for report-verified-payment-outcome API tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "outcome-test");

            var providerReference = $"prov-{Guid.NewGuid():N}";

            using var client = CreateClient();

            var firstResponse = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: BuildValidRequest(created.PaymentAttemptId, context.ParkingSessionId, providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"idem-outcome-1-{Guid.NewGuid():N}");

            var secondResponse = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: BuildValidRequest(created.PaymentAttemptId, context.ParkingSessionId, providerReference),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"idem-outcome-2-{Guid.NewGuid():N}");

            var secondRaw = await secondResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
            Assert.Contains("PROVIDER_REFERENCE_ALREADY_RECORDED", secondRaw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that reporting a confirmed verified outcome for an already-final attempt is rejected.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_WhenPaymentConfirmationAlreadyExistsForAttempt_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_WhenPaymentConfirmationAlreadyExistsForAttempt_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for report-verified-payment-outcome API tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "outcome-test");

            using var client = CreateClient();

            var firstResponse = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: BuildValidRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    $"prov-first-{Guid.NewGuid():N}"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"idem-outcome-1-{Guid.NewGuid():N}");

            var secondResponse = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: BuildValidRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    $"prov-second-{Guid.NewGuid():N}"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"idem-outcome-2-{Guid.NewGuid():N}");

            var secondRaw = await secondResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
            Assert.Contains("PAYMENT_CONFIRMATION_ALREADY_EXISTS", secondRaw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that a valid confirmed verified-payment-outcome request finalizes the attempt
    /// and issues an exit authorization.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_WhenRequestIsValidAndConfirmed_ReturnsOkAndIssuesExitAuthorization()
    {
        var context = PaymentTestContext.Create(
            nameof(ReportVerifiedPaymentOutcome_WhenRequestIsValidAndConfirmed_ReturnsOkAndIssuesExitAuthorization));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for report-verified-payment-outcome API tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "outcome-test");

            using var client = CreateClient();

            var response = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: BuildValidRequest(
                    created.PaymentAttemptId,
                    context.ParkingSessionId,
                    $"prov-{Guid.NewGuid():N}"),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}");

            var body = await response.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(body);
            Assert.Equal(created.PaymentAttemptId, body!.PaymentAttemptId);
            Assert.Equal("CONFIRMED", body.AttemptStatus);
            Assert.NotEqual(Guid.Empty, body.PaymentConfirmationId);
            Assert.NotNull(body.ExitAuthorizationId);
            Assert.Equal("ISSUED", body.AuthorizationStatus);
            Assert.False(string.IsNullOrWhiteSpace(body.AuthorizationToken));
            Assert.NotNull(body.IssuedAt);
            Assert.NotNull(body.ExpirationTimestamp);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Creates a configured HTTP client for Central PMS integration tests.
    /// </summary>
    /// <returns>Configured HTTP client.</returns>
    private static HttpClient CreateClient()
    {
        return new HttpClient
        {
            BaseAddress = ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Builds a valid internal verified-payment-outcome request.
    /// </summary>
    /// <param name="paymentAttemptId">Payment attempt identifier.</param>
    /// <param name="parkingSessionId">Parking session identifier.</param>
    /// <param name="providerReference">Provider reference.</param>
    /// <returns>The request payload.</returns>
    private static ReportVerifiedPaymentOutcomeRequest BuildValidRequest(
        Guid paymentAttemptId,
        Guid parkingSessionId,
        string providerReference)
    {
        return new ReportVerifiedPaymentOutcomeRequest(
            PaymentAttemptId: paymentAttemptId,
            ParkingSessionId: parkingSessionId,
            ProviderReference: providerReference,
            ProviderStatus: "SUCCESS",
            FinalAttemptStatus: "CONFIRMED",
            RequestedBy: "payment-orchestrator",
            RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId);
    }

    /// <summary>
    /// Sends an internal verified-payment-outcome request.
    /// </summary>
    /// <param name="client">HTTP client.</param>
    /// <param name="request">Outcome request body.</param>
    /// <param name="includeCorrelationId">Whether to include the correlation header.</param>
    /// <param name="correlationId">Correlation identifier.</param>
    /// <param name="includeIdempotencyKey">Whether to include the idempotency header.</param>
    /// <param name="idempotencyKey">Idempotency key.</param>
    /// <returns>HTTP response message.</returns>
    private static async Task<HttpResponseMessage> PostReportVerifiedPaymentOutcomeAsync(
        HttpClient client,
        ReportVerifiedPaymentOutcomeRequest request,
        bool includeCorrelationId,
        Guid correlationId,
        bool includeIdempotencyKey,
        string idempotencyKey)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/internal/payments/outcome")
        {
            Content = JsonContent.Create(request)
        };

        if (includeCorrelationId)
        {
            message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        }

        if (includeIdempotencyKey)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(message);
    }

    /// <summary>
    /// Internal verified-payment-outcome request contract.
    /// </summary>
    /// <param name="PaymentAttemptId">Payment attempt identifier.</param>
    /// <param name="ParkingSessionId">Parking session identifier.</param>
    /// <param name="ProviderReference">Provider-side unique reference.</param>
    /// <param name="ProviderStatus">Canonical provider status.</param>
    /// <param name="FinalAttemptStatus">Final payment attempt status.</param>
    /// <param name="RequestedBy">Calling internal service identity code or name.</param>
    /// <param name="RequestedByUserId">Calling actor identity identifier.</param>
    private sealed record ReportVerifiedPaymentOutcomeRequest(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        string ProviderReference,
        string ProviderStatus,
        string FinalAttemptStatus,
        string RequestedBy,
        Guid RequestedByUserId);

    /// <summary>
    /// Verified-payment-outcome response contract.
    /// </summary>
    /// <param name="PaymentConfirmationId">Payment confirmation identifier.</param>
    /// <param name="PaymentAttemptId">Payment attempt identifier.</param>
    /// <param name="AttemptStatus">Final attempt status.</param>
    /// <param name="ExitAuthorizationId">Issued exit authorization identifier.</param>
    /// <param name="AuthorizationToken">Issued authorization token.</param>
    /// <param name="AuthorizationStatus">Authorization status.</param>
    /// <param name="VerifiedTimestamp">Verification timestamp.</param>
    /// <param name="IssuedAt">Authorization issue timestamp.</param>
    /// <param name="ExpirationTimestamp">Authorization expiry timestamp.</param>
    private sealed record ReportVerifiedPaymentOutcomeResponse(
        Guid PaymentConfirmationId,
        Guid PaymentAttemptId,
        string AttemptStatus,
        Guid? ExitAuthorizationId,
        string? AuthorizationToken,
        string? AuthorizationStatus,
        DateTimeOffset VerifiedTimestamp,
        DateTimeOffset? IssuedAt,
        DateTimeOffset? ExpirationTimestamp);
}
