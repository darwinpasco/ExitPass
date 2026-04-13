using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the currently exposed Central PMS gate consume-exit-authorization API.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.6 Internal Service APIs
///
/// Invariants Enforced:
/// - HTTP boundary requires X-Correlation-Id before consume
/// - A valid authorization may be consumed exactly once
/// - Expired authorizations must be rejected deterministically
/// - Unknown authorizations must return not found
/// - Exit-authorization actor references must point to a seeded identity.service_identities record
/// - API shape matches the currently published gate-facing contract
/// </summary>
public sealed class GateExitAuthorizationConsumeEndpointsIntegrationTests
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
        ?? throw new InvalidOperationException(
            $"Integration test database connection string is missing. Set one of: {PrimaryDbConnectionStringEnvVar}, {AlternateDbConnectionStringEnvVar}, or {LegacyDbConnectionStringEnvVar}.");

    private static Uri ApiBaseUri => new(
        Environment.GetEnvironmentVariable(PrimaryApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyApiBaseUrlEnvVar)
        ?? throw new InvalidOperationException(
            $"Central PMS API base URL is missing. Set one of: {PrimaryApiBaseUrlEnvVar}, {AlternateApiBaseUrlEnvVar}, or {LegacyApiBaseUrlEnvVar}."),
        UriKind.Absolute);

    /// <summary>
    /// Verifies that the consume endpoint rejects requests when the X-Correlation-Id header is missing.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenCorrelationIdHeaderIsMissing_ReturnsBadRequest()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenCorrelationIdHeaderIsMissing_ReturnsBadRequest));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: Guid.NewGuid(),
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: false,
                correlationId: context.CorrelationId);

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
    /// Verifies that a valid authorization can be consumed successfully.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsValid_ReturnsOk()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsValid_ReturnsOk));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var issued = await CreateIssuedAuthorizationAsync(context);
            var exitAuthorizationId = issued.ExitAuthorizationId!.Value;

            using var client = CreateClient();

            var response = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: exitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            var body = await response.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(body);
            Assert.Equal(exitAuthorizationId, body!.ExitAuthorizationId);
            Assert.Equal("CONSUMED", body.AuthorizationStatus);
            Assert.NotNull(body.ConsumedAt);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the same authorization cannot be consumed twice successfully.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsAlreadyConsumed_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsAlreadyConsumed_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var issued = await CreateIssuedAuthorizationAsync(context);
            var exitAuthorizationId = issued.ExitAuthorizationId!.Value;

            using var client = CreateClient();

            var firstResponse = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: exitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            var secondResponse = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: exitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            var secondRaw = await secondResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
            Assert.Contains("EXIT_AUTHORIZATION_ALREADY_CONSUMED", secondRaw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that consuming a non-existent authorization fails deterministically.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationDoesNotExist_ReturnsNotFound()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationDoesNotExist_ReturnsNotFound));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: Guid.NewGuid(),
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("EXIT_AUTHORIZATION_NOT_FOUND", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that an expired authorization is rejected.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsExpired_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsExpired_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var issued = await CreateIssuedAuthorizationAsync(context);
            var exitAuthorizationId = issued.ExitAuthorizationId!.Value;

            await PaymentRoutineTestHelper.ExpireAuthorizationAsync(
                ConnectionString,
                exitAuthorizationId,
                KnownTestIdentityIds.ServiceIdentityId);

            using var client = CreateClient();

            var response = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: exitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("EXIT_AUTHORIZATION_EXPIRED", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Creates a fresh payment attempt, reports a verified successful outcome, and returns the issued authorization.
    /// </summary>
    /// <param name="context">Current payment test context.</param>
    /// <returns>The issued exit authorization response.</returns>
    private static async Task<ReportVerifiedPaymentOutcomeResponse> CreateIssuedAuthorizationAsync(PaymentTestContext context)
    {
        var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
            ConnectionString,
            context,
            $"idem-create-{Guid.NewGuid():N}",
            "consume-exit-auth-test");

        using var client = CreateClient();

        var outcomeResponse = await PostReportVerifiedPaymentOutcomeAsync(
            client,
            new ReportVerifiedPaymentOutcomeRequest(
                PaymentAttemptId: created.PaymentAttemptId,
                ParkingSessionId: context.ParkingSessionId,
                ProviderReference: $"prov-{Guid.NewGuid():N}",
                ProviderStatus: "SUCCESS",
                FinalAttemptStatus: "CONFIRMED",
                RequestedBy: "payment-orchestrator",
                RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
            correlationId: context.CorrelationId,
            idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}");

        var raw = await outcomeResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, outcomeResponse.StatusCode);

        var issued = await outcomeResponse.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
        Assert.NotNull(issued);
        Assert.Equal(created.PaymentAttemptId, issued!.PaymentAttemptId);
        Assert.Equal("CONFIRMED", issued.AttemptStatus);
        Assert.NotNull(issued.ExitAuthorizationId);
        Assert.Equal("ISSUED", issued.AuthorizationStatus);
        Assert.False(string.IsNullOrWhiteSpace(issued.AuthorizationToken));

        return issued;
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
    /// Sends a gate consume request.
    /// </summary>
    /// <param name="client">HTTP client.</param>
    /// <param name="exitAuthorizationId">Exit authorization identifier.</param>
    /// <param name="request">Consume request body.</param>
    /// <param name="includeCorrelationId">Whether to include the correlation header.</param>
    /// <param name="correlationId">Correlation identifier.</param>
    /// <returns>HTTP response message.</returns>
    private static async Task<HttpResponseMessage> PostConsumeExitAuthorizationAsync(
        HttpClient client,
        Guid exitAuthorizationId,
        ConsumeExitAuthorizationRequest request,
        bool includeCorrelationId,
        Guid correlationId)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume")
        {
            Content = JsonContent.Create(request)
        };

        if (includeCorrelationId)
        {
            message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        }

        return await client.SendAsync(message);
    }

    /// <summary>
    /// Sends an internal verified-payment-outcome request.
    /// </summary>
    /// <param name="client">HTTP client.</param>
    /// <param name="request">Outcome request body.</param>
    /// <param name="correlationId">Correlation identifier.</param>
    /// <param name="idempotencyKey">Idempotency key.</param>
    /// <returns>HTTP response message.</returns>
    private static async Task<HttpResponseMessage> PostReportVerifiedPaymentOutcomeAsync(
        HttpClient client,
        ReportVerifiedPaymentOutcomeRequest request,
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

    /// <summary>
    /// Gate consume request contract.
    /// </summary>
    /// <param name="RequestedByUserId">Actor requesting authorization consumption.</param>
    private sealed record ConsumeExitAuthorizationRequest(Guid RequestedByUserId);

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
    /// Gate consume response contract.
    /// </summary>
    /// <param name="ExitAuthorizationId">Exit authorization identifier.</param>
    /// <param name="AuthorizationStatus">Authorization status after consume.</param>
    /// <param name="ConsumedAt">Consume timestamp.</param>
    private sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset? ConsumedAt);

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
