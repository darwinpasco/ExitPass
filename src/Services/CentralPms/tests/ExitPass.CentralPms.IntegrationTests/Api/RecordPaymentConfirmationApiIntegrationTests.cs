using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Live API integration tests for the Central PMS payment outcome endpoint.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.11 Vendor PMS Payment Acknowledgment
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 10.5.3 Report Verified Payment Outcome
/// - 11.8 Replay, Duplicate, and Abuse Controls
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state through the canonical outcome path
/// - Payment finality must be recorded through the canonical Central PMS path
/// - Duplicate provider evidence must not create duplicate financial finality
/// - Outcome reporting must use a payment attempt and parking session that actually belong together
/// </summary>
public sealed class RecordPaymentConfirmationApiIntegrationTests
{
    private const string OutcomeRoute = "/v1/internal/payments/outcome";

    private const string RequestedByActor = "payment-orchestrator";

    private const string PrimaryApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_API_BASE_URL";
    private const string AlternateApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_BASE_URL";
    private const string LegacyApiBaseUrlEnvVar = "CENTRAL_PMS_BASE_URL";

    private const string PrimaryDbConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";
    private const string AlternateDbConnectionStringEnvVar = "EXITPASS_TEST_DB_CONNECTION_STRING";
    private const string LegacyDbConnectionStringEnvVar = "ConnectionStrings__MainDatabase";

    private static Uri ApiBaseUri => new(
        Environment.GetEnvironmentVariable(PrimaryApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyApiBaseUrlEnvVar)
        ?? throw new InvalidOperationException(
            $"Central PMS API base URL is missing. Set one of: {PrimaryApiBaseUrlEnvVar}, {AlternateApiBaseUrlEnvVar}, or {LegacyApiBaseUrlEnvVar}."),
        UriKind.Absolute);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(PrimaryDbConnectionStringEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateDbConnectionStringEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyDbConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Integration test database connection string is missing. Set one of: {PrimaryDbConnectionStringEnvVar}, {AlternateDbConnectionStringEnvVar}, or {LegacyDbConnectionStringEnvVar}.");

    /// <summary>
    /// Verifies that a valid request records payment finality successfully.
    /// </summary>
    [Fact]
    public async Task PostPaymentOutcome_WithValidRequest_ReturnsSuccessStatus()
    {
        var context = PaymentTestContext.Create(nameof(PostPaymentOutcome_WithValidRequest_ReturnsSuccessStatus));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for record-payment-confirmation API tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "record-payment-confirmation-test");

            var request = BuildValidRequest(
                paymentAttemptId: created.PaymentAttemptId,
                parkingSessionId: created.ParkingSessionId);

            using var message = BuildOutcomeRequestMessage(
                correlationId: context.CorrelationId.ToString(),
                idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}",
                request: request);

            using var client = CreateClient();
            using var response = await client.SendAsync(message);
            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = JsonSerializer.Deserialize<RecordPaymentOutcomeResponse>(
                raw,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.NotNull(body);
            Assert.Equal(created.PaymentAttemptId, body!.PaymentAttemptId);
            Assert.NotEqual(Guid.Empty, body.PaymentConfirmationId);
            Assert.Equal("CONFIRMED", body.AttemptStatus);
            Assert.Equal("ISSUED", body.AuthorizationStatus);
            Assert.NotNull(body.ExitAuthorizationId);
            Assert.False(string.IsNullOrWhiteSpace(body.AuthorizationToken));
            Assert.True(body.VerifiedTimestamp > DateTimeOffset.MinValue);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests without an idempotency key.
    /// </summary>
    [Fact]
    public async Task PostPaymentOutcome_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var context = PaymentTestContext.Create(nameof(PostPaymentOutcome_WithoutIdempotencyKey_ReturnsBadRequest));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for record-payment-confirmation API tests");

        try
        {
            var request = BuildValidRequest(Guid.NewGuid(), context.ParkingSessionId);

            using var message = new HttpRequestMessage(HttpMethod.Post, OutcomeRoute)
            {
                Content = JsonContent.Create(request)
            };

            message.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());

            using var client = CreateClient();
            using var response = await client.SendAsync(message);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests without a correlation id.
    /// </summary>
    [Fact]
    public async Task PostPaymentOutcome_WithoutCorrelationId_ReturnsBadRequest()
    {
        var context = PaymentTestContext.Create(nameof(PostPaymentOutcome_WithoutCorrelationId_ReturnsBadRequest));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for record-payment-confirmation API tests");

        try
        {
            var request = BuildValidRequest(Guid.NewGuid(), context.ParkingSessionId);

            using var message = new HttpRequestMessage(HttpMethod.Post, OutcomeRoute)
            {
                Content = JsonContent.Create(request)
            };

            message.Headers.Add("Idempotency-Key", $"idem-outcome-{Guid.NewGuid():N}");

            using var client = CreateClient();
            using var response = await client.SendAsync(message);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that reporting outcome for an unknown payment attempt returns not found.
    /// </summary>
    [Fact]
    public async Task PostPaymentOutcome_WithUnknownPaymentAttemptId_ReturnsNotFound()
    {
        var context = PaymentTestContext.Create(nameof(PostPaymentOutcome_WithUnknownPaymentAttemptId_ReturnsNotFound));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for record-payment-confirmation API tests");

        try
        {
            var request = BuildValidRequest(Guid.NewGuid(), context.ParkingSessionId);

            using var message = BuildOutcomeRequestMessage(
                correlationId: context.CorrelationId.ToString(),
                idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}",
                request: request);

            using var client = CreateClient();
            using var response = await client.SendAsync(message);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that reusing the same provider reference on a different fresh attempt returns conflict.
    /// </summary>
    [Fact]
    public async Task PostPaymentOutcome_WithDuplicateProviderReference_ReturnsConflict()
    {
        var firstContext = PaymentTestContext.Create(nameof(PostPaymentOutcome_WithDuplicateProviderReference_ReturnsConflict) + "_First");
        var secondContext = PaymentTestContext.Create(nameof(PostPaymentOutcome_WithDuplicateProviderReference_ReturnsConflict) + "_Second");

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            firstContext,
            "Seed data for duplicate provider reference tests");

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            secondContext,
            "Seed data for duplicate provider reference tests");

        try
        {
            var duplicateProviderReference = $"prov-{Guid.NewGuid():N}";

            var firstCreated = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                firstContext,
                $"idem-create-{Guid.NewGuid():N}",
                "record-payment-confirmation-test");

            var firstRequest = BuildValidRequest(
                paymentAttemptId: firstCreated.PaymentAttemptId,
                parkingSessionId: firstCreated.ParkingSessionId,
                providerReference: duplicateProviderReference);

            using var firstMessage = BuildOutcomeRequestMessage(
                correlationId: firstContext.CorrelationId.ToString(),
                idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}",
                request: firstRequest);

            using var client = CreateClient();
            using var firstResponse = await client.SendAsync(firstMessage);
            var firstRaw = await firstResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var secondCreated = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                secondContext,
                $"idem-create-{Guid.NewGuid():N}",
                "record-payment-confirmation-test");

            var secondRequest = BuildValidRequest(
                paymentAttemptId: secondCreated.PaymentAttemptId,
                parkingSessionId: secondCreated.ParkingSessionId,
                providerReference: duplicateProviderReference);

            using var secondMessage = BuildOutcomeRequestMessage(
                correlationId: secondContext.CorrelationId.ToString(),
                idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}",
                request: secondRequest);

            using var secondResponse = await client.SendAsync(secondMessage);
            var secondRaw = await secondResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
            Assert.Contains("PROVIDER_REFERENCE_ALREADY_RECORDED", secondRaw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, firstContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, secondContext);
        }
    }

    /// <summary>
    /// Builds a valid request for the current outcome endpoint contract.
    /// </summary>
    /// <param name="paymentAttemptId">Payment attempt identifier.</param>
    /// <param name="parkingSessionId">Parking session identifier linked to the attempt.</param>
    /// <param name="providerReference">Optional provider reference override.</param>
    /// <returns>Outcome request.</returns>
    private static ReportVerifiedPaymentOutcomeRequest BuildValidRequest(
        Guid paymentAttemptId,
        Guid parkingSessionId,
        string? providerReference = null)
    {
        return new ReportVerifiedPaymentOutcomeRequest(
            PaymentAttemptId: paymentAttemptId,
            ParkingSessionId: parkingSessionId,
            ProviderReference: providerReference ?? $"prov-{Guid.NewGuid():N}",
            ProviderStatus: "SUCCESS",
            FinalAttemptStatus: "CONFIRMED",
            RequestedBy: RequestedByActor,
            RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId);
    }

    /// <summary>
    /// Builds an HTTP request for the outcome endpoint.
    /// </summary>
    /// <param name="correlationId">Correlation id header.</param>
    /// <param name="idempotencyKey">Idempotency key header.</param>
    /// <param name="request">Request body.</param>
    /// <returns>Configured HTTP request message.</returns>
    private static HttpRequestMessage BuildOutcomeRequestMessage(
        string correlationId,
        string idempotencyKey,
        ReportVerifiedPaymentOutcomeRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, OutcomeRoute)
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("X-Correlation-Id", correlationId);
        message.Headers.Add("Idempotency-Key", idempotencyKey);

        return message;
    }

    /// <summary>
    /// Creates a configured HTTP client.
    /// </summary>
    /// <returns>Configured client.</returns>
    private static HttpClient CreateClient()
    {
        return new HttpClient
        {
            BaseAddress = ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Current payment outcome request contract.
    /// </summary>
    /// <param name="PaymentAttemptId">Payment attempt identifier.</param>
    /// <param name="ParkingSessionId">Parking session identifier linked to the attempt.</param>
    /// <param name="ProviderReference">Provider reference.</param>
    /// <param name="ProviderStatus">Provider status.</param>
    /// <param name="FinalAttemptStatus">Final attempt status.</param>
    /// <param name="RequestedBy">Caller identifier.</param>
    /// <param name="RequestedByUserId">Caller identity identifier.</param>
    private sealed record ReportVerifiedPaymentOutcomeRequest(
        Guid PaymentAttemptId,
        Guid ParkingSessionId,
        string ProviderReference,
        string ProviderStatus,
        string FinalAttemptStatus,
        string RequestedBy,
        Guid RequestedByUserId);

    /// <summary>
    /// Current payment outcome response contract.
    /// </summary>
    /// <param name="PaymentConfirmationId">Payment confirmation identifier.</param>
    /// <param name="PaymentAttemptId">Payment attempt identifier.</param>
    /// <param name="AttemptStatus">Final attempt status.</param>
    /// <param name="ExitAuthorizationId">Exit authorization identifier.</param>
    /// <param name="AuthorizationToken">Authorization token.</param>
    /// <param name="AuthorizationStatus">Authorization status.</param>
    /// <param name="VerifiedTimestamp">Verification timestamp.</param>
    private sealed record RecordPaymentOutcomeResponse(
        Guid PaymentConfirmationId,
        Guid PaymentAttemptId,
        string AttemptStatus,
        Guid? ExitAuthorizationId,
        string? AuthorizationToken,
        string? AuthorizationStatus,
        DateTimeOffset VerifiedTimestamp);
}
