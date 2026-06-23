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
    private const string PrimaryApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_API_BASE_URL";
    private const string AlternateApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_BASE_URL";
    private const string LegacyApiBaseUrlEnvVar = "CENTRAL_PMS_BASE_URL";

    /// <summary>
    /// Gets the configured integration-test database connection string.
    /// </summary>
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

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
            Assert.Equal(0, await PaymentRoutineTestHelper.CountPaymentAttemptsForParkingSessionAsync(
                ConnectionString,
                context.ParkingSessionId));
            Assert.Equal(0, await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: false));

            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "payment-to-exit-test");

            Assert.Equal(context.ParkingSessionId, created.ParkingSessionId);
            Assert.Equal(context.TariffSnapshotId, created.TariffSnapshotId);
            Assert.Equal("REQUESTED", created.AttemptStatus);
            Assert.Equal(1, await PaymentRoutineTestHelper.CountPaymentAttemptsForParkingSessionAsync(
                ConnectionString,
                context.ParkingSessionId));
            Assert.Equal(0, await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId));
            Assert.Equal(0, await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: false));

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
            Assert.NotEqual(Guid.Empty, outcomeBody.PaymentConfirmationId);
            Assert.NotNull(outcomeBody.ExitAuthorizationId);
            Assert.Equal("ISSUED", outcomeBody.AuthorizationStatus);
            Assert.False(string.IsNullOrWhiteSpace(outcomeBody.AuthorizationToken));
            Assert.NotNull(outcomeBody.IssuedAt);
            Assert.NotNull(outcomeBody.ExpirationTimestamp);

            var persistedAttempt = await PaymentRoutineTestHelper.GetPaymentAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var persistedConfirmation = await PaymentRoutineTestHelper.GetPaymentConfirmationByIdAsync(
                ConnectionString,
                outcomeBody.PaymentConfirmationId);

            Assert.NotNull(persistedAttempt);
            Assert.Equal("CONFIRMED", persistedAttempt!.AttemptStatus);
            Assert.NotNull(persistedAttempt.FinalizedAt);
            Assert.NotNull(persistedConfirmation);
            Assert.Equal(outcomeBody.PaymentConfirmationId, persistedConfirmation!.PaymentConfirmationId);
            Assert.Equal(created.PaymentAttemptId, persistedConfirmation.PaymentAttemptId);
            Assert.Equal("RECORDED", persistedConfirmation.ConfirmationStatus);
            Assert.Equal(100.00m, persistedConfirmation.AmountConfirmed);
            Assert.Equal("PHP", persistedConfirmation.CurrencyCode);
            Assert.Equal(1, await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId));

            var exitAuthorizationId = outcomeBody.ExitAuthorizationId!.Value;
            var persistedAuthorization = await PaymentRoutineTestHelper.GetExitAuthorizationByIdAsync(
                ConnectionString,
                exitAuthorizationId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            Assert.NotNull(persistedAuthorization);
            Assert.Equal(exitAuthorizationId, persistedAuthorization!.ExitAuthorizationId);
            Assert.Equal(context.ParkingSessionId, persistedAuthorization.ParkingSessionId);
            Assert.Equal(created.PaymentAttemptId, persistedAuthorization.PaymentAttemptId);
            Assert.Equal("ISSUED", persistedAuthorization.AuthorizationStatus);
            Assert.Equal(1, issuedAuthorizationCount);
            Assert.Equal(1, await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: false));

            var firstConsumeResponse = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: exitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(context.RequestedByUserId),
                correlationId: context.CorrelationId,
                context: context);

            var firstConsumeBody = await firstConsumeResponse.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>();

            Assert.Equal(HttpStatusCode.OK, firstConsumeResponse.StatusCode);
            Assert.NotNull(firstConsumeBody);
            Assert.Equal(exitAuthorizationId, firstConsumeBody!.ExitAuthorizationId);
            Assert.Equal("CONSUMED", firstConsumeBody.AuthorizationStatus);
            Assert.NotNull(firstConsumeBody.ConsumedAt);
            Assert.Equal(1, await PaymentRoutineTestHelper.CountGateAuthorizationConsumptionsAsync(
                ConnectionString,
                exitAuthorizationId));

            var secondConsumeResponse = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: exitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(context.RequestedByUserId),
                correlationId: context.CorrelationId,
                context: context);

            var secondConsumeRaw = await secondConsumeResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, secondConsumeResponse.StatusCode);
            Assert.Contains("EXIT_AUTHORIZATION_ALREADY_CONSUMED", secondConsumeRaw, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await PaymentRoutineTestHelper.CountGateAuthorizationConsumptionsAsync(
                ConnectionString,
                exitAuthorizationId));

            var consumedAuthorization = await PaymentRoutineTestHelper.GetExitAuthorizationByIdAsync(
                ConnectionString,
                exitAuthorizationId);

            Assert.NotNull(consumedAuthorization);
            Assert.Equal("CONSUMED", consumedAuthorization!.AuthorizationStatus);
            Assert.NotNull(consumedAuthorization.ConsumedAt);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies duplicate verified-provider delivery is idempotent and does not create duplicate active authorizations.
    /// </summary>
    [Fact]
    public async Task PaymentToExitFlow_WhenVerifiedOutcomeIsReplayed_ReusesSingleActiveAuthorization()
    {
        var context = PaymentTestContext.Create(
            nameof(PaymentToExitFlow_WhenVerifiedOutcomeIsReplayed_ReusesSingleActiveAuthorization));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for duplicate payment-to-exit replay integration tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "payment-to-exit-replay-test");

            using var client = CreateClient();

            var providerReference = $"prov-{Guid.NewGuid():N}";
            var request = new ReportVerifiedPaymentOutcomeRequest(
                PaymentAttemptId: created.PaymentAttemptId,
                ParkingSessionId: context.ParkingSessionId,
                ProviderReference: providerReference,
                ProviderStatus: "SUCCESS",
                FinalAttemptStatus: "CONFIRMED",
                RequestedBy: "payment-orchestrator",
                RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId);

            var firstResponse = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request,
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-outcome-first-{Guid.NewGuid():N}");
            var firstBody = await firstResponse.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();

            var replayResponse = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request,
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-outcome-replay-{Guid.NewGuid():N}");
            var replayBody = await replayResponse.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
            Assert.NotNull(firstBody);
            Assert.NotNull(replayBody);
            Assert.Equal(firstBody!.PaymentConfirmationId, replayBody!.PaymentConfirmationId);
            Assert.Equal(firstBody.ExitAuthorizationId, replayBody.ExitAuthorizationId);
            Assert.Equal("CONFIRMED", replayBody.AttemptStatus);
            Assert.Equal("ISSUED", replayBody.AuthorizationStatus);

            var confirmationCount = await PaymentRoutineTestHelper.CountPaymentConfirmationsAsync(
                ConnectionString,
                created.PaymentAttemptId);
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            Assert.Equal(1, confirmationCount);
            Assert.Equal(1, issuedAuthorizationCount);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies an unpaid parking session has no exit authorization.
    /// </summary>
    [Fact]
    public async Task PaymentToExitFlow_WhenSessionIsUnpaid_DoesNotHaveExitAuthorization()
    {
        var context = PaymentTestContext.Create(
            nameof(PaymentToExitFlow_WhenSessionIsUnpaid_DoesNotHaveExitAuthorization));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for unpaid payment-to-exit integration tests");

        try
        {
            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            Assert.Equal(0, issuedAuthorizationCount);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies failed provider evidence does not issue an exit authorization.
    /// </summary>
    [Fact]
    public async Task PaymentToExitFlow_WhenVerifiedOutcomeFails_DoesNotIssueExitAuthorization()
    {
        var context = PaymentTestContext.Create(
            nameof(PaymentToExitFlow_WhenVerifiedOutcomeFails_DoesNotIssueExitAuthorization));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for failed payment-to-exit integration tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-create-{Guid.NewGuid():N}",
                "payment-to-exit-failed-test");

            using var client = CreateClient();

            var outcomeResponse = await PostReportVerifiedPaymentOutcomeAsync(
                client,
                request: new ReportVerifiedPaymentOutcomeRequest(
                    PaymentAttemptId: created.PaymentAttemptId,
                    ParkingSessionId: context.ParkingSessionId,
                    ProviderReference: $"prov-failed-{Guid.NewGuid():N}",
                    ProviderStatus: "FAILED",
                    FinalAttemptStatus: "FAILED",
                    RequestedBy: "payment-orchestrator",
                    RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-outcome-failed-{Guid.NewGuid():N}");

            var outcomeBody = await outcomeResponse.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();

            Assert.Equal(HttpStatusCode.OK, outcomeResponse.StatusCode);
            Assert.NotNull(outcomeBody);
            Assert.Equal("FAILED", outcomeBody!.AttemptStatus);
            Assert.Null(outcomeBody.ExitAuthorizationId);
            Assert.Null(outcomeBody.AuthorizationStatus);
            Assert.Null(outcomeBody.AuthorizationToken);

            var issuedAuthorizationCount = await PaymentRoutineTestHelper.CountExitAuthorizationsAsync(
                ConnectionString,
                context.ParkingSessionId,
                issuedOnly: true);

            Assert.Equal(0, issuedAuthorizationCount);
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
        Guid correlationId,
        PaymentTestContext context)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume")
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        message.Headers.Add("X-Service-Identity-Id", request.RequestedByUserId.ToString());
        message.Headers.Add("X-Gate-Device-Id", PaymentTestDataHelper.GateDeviceCode(context));

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
