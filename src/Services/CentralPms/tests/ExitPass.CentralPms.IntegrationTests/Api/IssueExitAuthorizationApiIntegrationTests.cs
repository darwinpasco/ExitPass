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
    private const string ConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    private readonly HttpClient _client;

    public IssueExitAuthorizationApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{ConnectionStringEnvVar}'. " +
            "Point it at the ExitPass integration database.");

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
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

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
            Assert.Equal("EXIT_AUTHORIZATION_ISSUANCE_CONFLICT", body!.ErrorCode);
            Assert.Equal(context.CorrelationId, body.CorrelationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

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
}
