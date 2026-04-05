using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the currently exposed Central PMS internal issue-exit-authorization API.
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
/// - HTTP boundary requires X-Correlation-Id and Idempotency-Key before issuance
/// - A confirmed PaymentAttempt with recorded payment confirmation may produce an issued ExitAuthorization
/// - Exit-authorization actor references must point to a seeded identity.service_identities record
/// - API shape matches the currently published internal contract
/// </summary>
public sealed class InternalPaymentAttemptExitAuthorizationEndpointsIntegrationTests
{
    private const string PrimaryDbConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";
    private const string AlternateDbConnectionStringEnvVar = "EXITPASS_TEST_DB_CONNECTION_STRING";
    private const string LegacyDbConnectionStringEnvVar = "ConnectionStrings__MainDatabase";

    private const string PrimaryApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_API_BASE_URL";
    private const string AlternateApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_BASE_URL";
    private const string LegacyApiBaseUrlEnvVar = "CENTRAL_PMS_BASE_URL";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(PrimaryDbConnectionStringEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateDbConnectionStringEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyDbConnectionStringEnvVar)
        ?? "Host=localhost;Port=5432;Database=exitpass;Username=exitpass;Password=change_me";

    private static Uri ApiBaseUri => new(
        Environment.GetEnvironmentVariable(PrimaryApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyApiBaseUrlEnvVar)
        ?? "http://localhost:8080",
        UriKind.Absolute);

    /// <summary>
    /// Verifies that the endpoint rejects requests when the X-Correlation-Id header is missing.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenCorrelationIdHeaderIsMissing_ReturnsBadRequest()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenCorrelationIdHeaderIsMissing_ReturnsBadRequest));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostIssueExitAuthorizationAsync(
                client,
                paymentAttemptId: Guid.NewGuid(),
                request: new IssueExitAuthorizationRequest(
                    ParkingSessionId: context.ParkingSessionId,
                    RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: false,
                includeIdempotencyKey: true,
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-issue-{Guid.NewGuid():N}");

            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("INVALID_REQUEST", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests when the Idempotency-Key header is missing.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenIdempotencyKeyHeaderIsMissing_ReturnsBadRequest()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenIdempotencyKeyHeaderIsMissing_ReturnsBadRequest));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostIssueExitAuthorizationAsync(
                client,
                paymentAttemptId: Guid.NewGuid(),
                request: new IssueExitAuthorizationRequest(
                    ParkingSessionId: context.ParkingSessionId,
                    RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                includeIdempotencyKey: false,
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-issue-{Guid.NewGuid():N}");

            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("INVALID_REQUEST", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that issuance succeeds after a payment attempt has been confirmed and payment confirmation has been recorded.
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
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-issue-success",
                "issue-exit-auth-test");

            var finalized = await PaymentRoutineTestHelper.FinalizeAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId,
                "CONFIRMED",
                "payment-orchestrator",
                context.CorrelationId);

            Assert.NotNull(finalized);
            Assert.Equal("CONFIRMED", finalized!.AttemptStatus);

            using var client = CreateClient();

            var confirmationResponse = await PostRecordPaymentConfirmationAsync(
                client,
                new RecordPaymentConfirmationRequest(
                    PaymentAttemptId: created.PaymentAttemptId,
                    ProviderReference: $"prov-{Guid.NewGuid():N}",
                    ProviderStatus: "SUCCESS",
                    RequestedBy: "integration-test"),
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-confirm-{Guid.NewGuid():N}");

            Assert.Equal(HttpStatusCode.Created, confirmationResponse.StatusCode);

            var response = await PostIssueExitAuthorizationAsync(
                client,
                paymentAttemptId: created.PaymentAttemptId,
                request: new IssueExitAuthorizationRequest(
                    ParkingSessionId: context.ParkingSessionId,
                    RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                includeIdempotencyKey: true,
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-issue-{Guid.NewGuid():N}");

            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();

            Assert.NotNull(body);
            Assert.NotEqual(Guid.Empty, body!.ExitAuthorizationId);
            Assert.Equal(context.ParkingSessionId, body.ParkingSessionId);
            Assert.Equal(created.PaymentAttemptId, body.PaymentAttemptId);
            Assert.False(string.IsNullOrWhiteSpace(body.AuthorizationToken));
            Assert.False(string.IsNullOrWhiteSpace(body.AuthorizationStatus));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that issuance is rejected when the payment attempt does not exist.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_WhenPaymentAttemptDoesNotExist_ReturnsNotFoundOrConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(IssueExitAuthorization_WhenPaymentAttemptDoesNotExist_ReturnsNotFoundOrConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for issue-exit-authorization API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostIssueExitAuthorizationAsync(
                client,
                paymentAttemptId: Guid.NewGuid(),
                request: new IssueExitAuthorizationRequest(
                    ParkingSessionId: context.ParkingSessionId,
                    RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                includeIdempotencyKey: true,
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-issue-{Guid.NewGuid():N}");

            var raw = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict,
                $"Unexpected status code: {response.StatusCode}. Body: {raw}");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Creates an HTTP client for the live Central PMS API.
    /// </summary>
    /// <returns>A configured <see cref="HttpClient"/>.</returns>
    private static HttpClient CreateClient()
    {
        return new HttpClient
        {
            BaseAddress = ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Sends a POST request to the live issue-exit-authorization endpoint.
    /// </summary>
    private static async Task<HttpResponseMessage> PostIssueExitAuthorizationAsync(
        HttpClient client,
        Guid paymentAttemptId,
        IssueExitAuthorizationRequest request,
        bool includeCorrelationId,
        bool includeIdempotencyKey,
        Guid correlationId,
        string idempotencyKey)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization")
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
    /// Sends a POST request to the live internal payment-confirmation endpoint.
    /// </summary>
    private static async Task<HttpResponseMessage> PostRecordPaymentConfirmationAsync(
        HttpClient client,
        RecordPaymentConfirmationRequest request,
        Guid correlationId,
        string idempotencyKey)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/internal/payments/outcome")
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        message.Headers.Add("Idempotency-Key", idempotencyKey);

        return await client.SendAsync(message);
    }

    private sealed record IssueExitAuthorizationRequest(
        Guid ParkingSessionId,
        Guid RequestedByUserId);

    private sealed record RecordPaymentConfirmationRequest(
        Guid PaymentAttemptId,
        string ProviderReference,
        string ProviderStatus,
        string RequestedBy);

    private sealed record IssueExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken,
        string AuthorizationStatus,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpirationTimestamp);
}
