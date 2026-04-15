using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Payments;

/// <summary>
/// Verifies the canonical payment-to-exit control path through Central PMS.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 6.6 Consume Exit Authorization
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.6 Internal Service APIs
///
/// Invariants Enforced:
/// - A confirmed verified payment outcome must issue a single-use exit authorization
/// - A valid exit authorization may be consumed exactly once
/// - A second consume attempt must be rejected deterministically
/// - The canonical control path must succeed without manual database intervention
/// </summary>
public sealed class PaymentToExitFlowIntegrationTests
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
    /// Verifies the full canonical control path from payment attempt creation
    /// to verified outcome reporting to exit authorization consumption.
    /// </summary>
    [Fact]
    public async Task PaymentToExitFlow_WhenVerifiedOutcomeIsConfirmed_IssuesAndConsumesAuthorizationExactlyOnce()
    {
        var context = PaymentTestContext.Create(
            nameof(PaymentToExitFlow_WhenVerifiedOutcomeIsConfirmed_IssuesAndConsumesAuthorizationExactlyOnce));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for payment-to-exit control-path integration tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "payment-to-exit-test");

            using var client = CreateClient();

            var outcomeResponse = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: new ReportVerifiedPaymentOutcomeRequest(
                    PaymentAttemptId: created.PaymentAttemptId,
                    ParkingSessionId: context.ParkingSessionId,
                    ProviderReference: $"prov-{Guid.NewGuid():N}",
                    ProviderStatus: "SUCCESS",
                    FinalAttemptStatus: "CONFIRMED",
                    RequestedBy: "payment-orchestrator",
                    RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-outcome-{Guid.NewGuid():N}");

            var outcomeBody = await outcomeResponse.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();

            Assert.Equal(HttpStatusCode.OK, outcomeResponse.StatusCode);
            Assert.NotNull(outcomeBody);
            Assert.Equal(created.PaymentAttemptId, outcomeBody!.PaymentAttemptId);
            Assert.Equal("CONFIRMED", outcomeBody.AttemptStatus);
            Assert.NotNull(outcomeBody.ExitAuthorizationId);
            Assert.Equal("ISSUED", outcomeBody.AuthorizationStatus);
            Assert.False(string.IsNullOrWhiteSpace(outcomeBody.AuthorizationToken));
            Assert.NotNull(outcomeBody.IssuedAt);
            Assert.NotNull(outcomeBody.ExpirationTimestamp);

            var exitAuthorizationId = outcomeBody.ExitAuthorizationId!.Value;

            var firstConsumeResponse = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: exitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                correlationId: context.CorrelationId);

            var firstConsumeBody = await firstConsumeResponse.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>();

            Assert.Equal(HttpStatusCode.OK, firstConsumeResponse.StatusCode);
            Assert.NotNull(firstConsumeBody);
            Assert.Equal(exitAuthorizationId, firstConsumeBody!.ExitAuthorizationId);
            Assert.Equal("CONSUMED", firstConsumeBody.AuthorizationStatus);
            Assert.NotNull(firstConsumeBody.ConsumedAt);

            var secondConsumeResponse = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: exitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                correlationId: context.CorrelationId);

            var secondConsumeRaw = await secondConsumeResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, secondConsumeResponse.StatusCode);
            Assert.Contains("EXIT_AUTHORIZATION_ALREADY_CONSUMED", secondConsumeRaw, StringComparison.OrdinalIgnoreCase);
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
    /// Sends a gate consume request.
    /// </summary>
    /// <param name="client">HTTP client.</param>
    /// <param name="exitAuthorizationId">Exit authorization identifier.</param>
    /// <param name="request">Consume request body.</param>
    /// <param name="correlationId">Correlation identifier.</param>
    /// <returns>HTTP response message.</returns>
    private static async Task<HttpResponseMessage> PostConsumeExitAuthorizationAsync(
        HttpClient client,
        Guid exitAuthorizationId,
        ConsumeExitAuthorizationRequest request,
        Guid correlationId)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume")
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("X-Correlation-Id", correlationId.ToString());

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

    /// <summary>
    /// Gate consume request contract.
    /// </summary>
    /// <param name="RequestedByUserId">Actor requesting authorization consumption.</param>
    private sealed record ConsumeExitAuthorizationRequest(Guid RequestedByUserId);

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
}
