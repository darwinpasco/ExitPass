using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Internal;

/// <summary>
/// Verifies the v1.2 internal ExitAuthorization issuance contract.
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
/// - ExitAuthorization issuance must be anchored to a confirmed PaymentAttempt.
/// - Unknown PaymentAttempt identifiers must return deterministic not-found responses.
/// - Unconfirmed PaymentAttempts must return deterministic conflict responses.
/// - Replayed issuance for the same confirmed PaymentAttempt must return the existing authorization.
/// </summary>
public sealed class IssueExitAuthorizationContractTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    private static Uri ApiBaseUri => new(
        Environment.GetEnvironmentVariable("EXITPASS_CENTRAL_PMS_API_BASE_URL")
        ?? Environment.GetEnvironmentVariable("EXITPASS_CENTRAL_PMS_BASE_URL")
        ?? Environment.GetEnvironmentVariable("CENTRAL_PMS_BASE_URL")
        ?? "http://localhost:8080",
        UriKind.Absolute);

    /// <summary>
    /// Verifies BRD 9.12 and SDD 6.5 not-found behavior for unknown PaymentAttempts.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_returns_404_not_found_for_unknown_payment_attempt()
    {
        var correlationId = Guid.NewGuid();

        using var client = CreateClient();
        using var response = await PostIssueAsync(
            client,
            paymentAttemptId: Guid.NewGuid(),
            parkingSessionId: Guid.NewGuid(),
            requestedByUserId: Guid.NewGuid(),
            correlationId: correlationId,
            includeCorrelationId: true,
            idempotencyKey: $"ctest-issue-{Guid.NewGuid():N}",
            includeIdempotencyKey: true);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("PAYMENT_ATTEMPT_NOT_FOUND");
        payload.CorrelationId.Should().Be(correlationId);
    }

    /// <summary>
    /// Verifies BRD 9.12 and SDD 6.5 conflict behavior for unconfirmed PaymentAttempts.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_returns_409_conflict_for_unconfirmed_payment_attempt()
    {
        var context = PaymentTestContext.Create(nameof(IssueExitAuthorization_returns_409_conflict_for_unconfirmed_payment_attempt));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for exit-authorization issuance contract tests");

        try
        {
            var attempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "issue-exit-authorization-contract-test");

            using var client = CreateClient();
            using var response = await PostIssueAsync(
                client,
                attempt.PaymentAttemptId,
                attempt.ParkingSessionId,
                context.RequestedByUserId,
                context.CorrelationId,
                includeCorrelationId: true,
                idempotencyKey: $"ctest-issue-{Guid.NewGuid():N}",
                includeIdempotencyKey: true);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_ATTEMPT_NOT_CONFIRMED");
            payload.CorrelationId.Should().Be(context.CorrelationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.12 and SDD 6.5 successful issuance from confirmed payment finality.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_returns_200_ok_for_confirmed_payment_attempt()
    {
        var context = PaymentTestContext.Create(nameof(IssueExitAuthorization_returns_200_ok_for_confirmed_payment_attempt));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for exit-authorization issuance contract tests");

        try
        {
            var attempt = await CreateConfirmedAttemptWithPaymentEvidenceAsync(context);

            using var client = CreateClient();
            using var response = await PostIssueAsync(
                client,
                attempt.PaymentAttemptId,
                attempt.ParkingSessionId,
                context.RequestedByUserId,
                context.CorrelationId,
                includeCorrelationId: true,
                idempotencyKey: $"ctest-issue-{Guid.NewGuid():N}",
                includeIdempotencyKey: true);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();
            payload.Should().NotBeNull();
            payload!.PaymentAttemptId.Should().Be(attempt.PaymentAttemptId);
            payload.ParkingSessionId.Should().Be(attempt.ParkingSessionId);
            payload.ExitAuthorizationId.Should().NotBe(Guid.Empty);
            payload.AuthorizationStatus.Should().Be("ISSUED");
            payload.AuthorizationToken.Should().NotBeNullOrWhiteSpace();
            payload.ExpirationTimestamp.Should().BeAfter(payload.IssuedAt);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 deterministic issuance replay for a confirmed PaymentAttempt.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_returns_existing_authorization_for_replayed_issuance()
    {
        var context = PaymentTestContext.Create(nameof(IssueExitAuthorization_returns_existing_authorization_for_replayed_issuance));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for exit-authorization issuance contract tests");

        try
        {
            var attempt = await CreateConfirmedAttemptWithPaymentEvidenceAsync(context);

            using var client = CreateClient();
            using var first = await PostIssueAsync(
                client,
                attempt.PaymentAttemptId,
                attempt.ParkingSessionId,
                context.RequestedByUserId,
                context.CorrelationId,
                includeCorrelationId: true,
                idempotencyKey: $"ctest-issue-{Guid.NewGuid():N}",
                includeIdempotencyKey: true);

            using var replay = await PostIssueAsync(
                client,
                attempt.PaymentAttemptId,
                attempt.ParkingSessionId,
                context.RequestedByUserId,
                context.CorrelationId,
                includeCorrelationId: true,
                idempotencyKey: $"ctest-issue-{Guid.NewGuid():N}",
                includeIdempotencyKey: true);

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);

            var firstPayload = await first.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();
            var replayPayload = await replay.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();

            replayPayload.Should().NotBeNull();
            replayPayload!.ExitAuthorizationId.Should().Be(firstPayload!.ExitAuthorizationId);
            replayPayload.AuthorizationToken.Should().NotBeNullOrWhiteSpace();
            replayPayload.AuthorizationStatus.Should().Be("ISSUED");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies SDD 10.6 header validation for missing idempotency metadata.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_returns_400_bad_request_without_idempotency_key()
    {
        using var client = CreateClient();
        using var response = await PostIssueAsync(
            client,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            includeCorrelationId: true,
            idempotencyKey: null,
            includeIdempotencyKey: false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies SDD 10.6 header validation for missing correlation metadata.
    /// </summary>
    [Fact]
    public async Task IssueExitAuthorization_returns_400_bad_request_without_correlation_id()
    {
        using var client = CreateClient();
        using var response = await PostIssueAsync(
            client,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            includeCorrelationId: false,
            idempotencyKey: $"ctest-issue-{Guid.NewGuid():N}",
            includeIdempotencyKey: true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient
        {
            BaseAddress = ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static async Task<PaymentRoutineTestHelper.CreateAttemptResult> CreateConfirmedAttemptWithPaymentEvidenceAsync(
        PaymentTestContext context)
    {
        var attempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
            ConnectionString,
            context,
            $"ctest-create-{Guid.NewGuid():N}",
            "issue-exit-authorization-contract-test");

        var finalized = await PaymentRoutineTestHelper.FinalizeAttemptAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            "CONFIRMED",
            "issue-exit-authorization-contract-test",
            context.CorrelationId);

        finalized.Should().NotBeNull();
        finalized!.AttemptStatus.Should().Be("CONFIRMED");

        var confirmation = await PaymentRoutineTestHelper.RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            $"prov-{Guid.NewGuid():N}",
            "issue-exit-authorization-contract-test",
            context.CorrelationId);

        confirmation.Should().NotBeNull();

        return attempt;
    }

    private static async Task<HttpResponseMessage> PostIssueAsync(
        HttpClient client,
        Guid paymentAttemptId,
        Guid parkingSessionId,
        Guid requestedByUserId,
        Guid correlationId,
        bool includeCorrelationId,
        string? idempotencyKey,
        bool includeIdempotencyKey)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization")
        {
            Content = JsonContent.Create(new IssueExitAuthorizationRequest(
                ParkingSessionId: parkingSessionId,
                RequestedByUserId: requestedByUserId))
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
