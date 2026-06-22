using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Internal;

/// <summary>
/// Verifies the v1.2 gate-facing ExitAuthorization consumption contract.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.4.2 Consume Exit Authorization
///
/// Invariants Enforced:
/// - A valid ExitAuthorization may be consumed exactly once.
/// - Unknown, expired, and already-consumed authorizations must fail deterministically.
/// - Gate consume requests require correlation metadata at the HTTP boundary.
/// </summary>
public sealed class ConsumeExitAuthorizationContractTests
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
    /// Verifies BRD 9.12 and SDD 6.6 not-found behavior for unknown authorizations.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_returns_404_not_found_for_unknown_authorization()
    {
        var context = PaymentTestContext.Create(nameof(ConsumeExitAuthorization_returns_404_not_found_for_unknown_authorization));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization contract tests");

        try
        {
            using var client = CreateClient();
            using var response = await PostConsumeAsync(
                client,
                exitAuthorizationId: Guid.NewGuid(),
                context: context,
                requestedByUserId: context.RequestedByUserId,
                correlationId: context.CorrelationId,
                includeCorrelationId: true);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("EXIT_AUTHORIZATION_NOT_FOUND");
            payload.CorrelationId.Should().Be(context.CorrelationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.12 and SDD 6.6 successful gate consumption response shape.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_returns_200_ok_for_valid_authorization()
    {
        var context = PaymentTestContext.Create(nameof(ConsumeExitAuthorization_returns_200_ok_for_valid_authorization));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization contract tests");

        try
        {
            var issued = await CreateIssuedAuthorizationAsync(context);

            using var client = CreateClient();
            using var response = await PostConsumeAsync(
                client,
                issued.ExitAuthorizationId,
                context,
                context.RequestedByUserId,
                context.CorrelationId,
                includeCorrelationId: true);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>();
            payload.Should().NotBeNull();
            payload!.ExitAuthorizationId.Should().Be(issued.ExitAuthorizationId);
            payload.AuthorizationStatus.Should().Be("CONSUMED");
            payload.ConsumedAt.Should().BeAfter(DateTimeOffset.MinValue);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 6.6 already-consumed conflict behavior.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_returns_409_conflict_for_already_consumed_authorization()
    {
        var context = PaymentTestContext.Create(nameof(ConsumeExitAuthorization_returns_409_conflict_for_already_consumed_authorization));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization contract tests");

        try
        {
            var issued = await CreateIssuedAuthorizationAsync(context);

            using var client = CreateClient();
            using var first = await PostConsumeAsync(
                client,
                issued.ExitAuthorizationId,
                context,
                context.RequestedByUserId,
                context.CorrelationId,
                includeCorrelationId: true);

            using var replay = await PostConsumeAsync(
                client,
                issued.ExitAuthorizationId,
                context,
                context.RequestedByUserId,
                context.CorrelationId,
                includeCorrelationId: true);

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            replay.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await replay.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("EXIT_AUTHORIZATION_ALREADY_CONSUMED");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 6.6 expired-authorization conflict behavior.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_returns_409_conflict_for_expired_authorization()
    {
        var context = PaymentTestContext.Create(nameof(ConsumeExitAuthorization_returns_409_conflict_for_expired_authorization));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization contract tests");

        try
        {
            var issued = await CreateIssuedAuthorizationAsync(context);
            await PaymentRoutineTestHelper.ExpireAuthorizationAsync(
                ConnectionString,
                issued.ExitAuthorizationId,
                context.RequestedByUserId);

            using var client = CreateClient();
            using var response = await PostConsumeAsync(
                client,
                issued.ExitAuthorizationId,
                context,
                context.RequestedByUserId,
                context.CorrelationId,
                includeCorrelationId: true);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("EXIT_AUTHORIZATION_EXPIRED");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies SDD 10.4.2 header validation for missing correlation metadata.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_returns_400_bad_request_without_correlation_id()
    {
        using var client = CreateClient();
        using var response = await PostConsumeAsync(
            client,
            exitAuthorizationId: Guid.NewGuid(),
            context: null,
            requestedByUserId: Guid.NewGuid(),
            correlationId: Guid.Empty,
            includeCorrelationId: false);

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

    private static async Task<PaymentRoutineTestHelper.IssueExitAuthorizationResult> CreateIssuedAuthorizationAsync(
        PaymentTestContext context)
    {
        var attempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
            ConnectionString,
            context,
            $"ctest-create-{Guid.NewGuid():N}",
            "consume-exit-authorization-contract-test");

        var finalized = await PaymentRoutineTestHelper.FinalizeAttemptAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            "CONFIRMED",
            "consume-exit-authorization-contract-test",
            context.CorrelationId);

        finalized.Should().NotBeNull();

        var confirmation = await PaymentRoutineTestHelper.RecordPaymentConfirmationAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            $"prov-{Guid.NewGuid():N}",
            "consume-exit-authorization-contract-test",
            context.CorrelationId);

        confirmation.Should().NotBeNull();

        var issued = await PaymentRoutineTestHelper.IssueExitAuthorizationAsync(
            ConnectionString,
            attempt.ParkingSessionId,
            attempt.PaymentAttemptId,
            context.RequestedByUserId,
            context.CorrelationId);

        issued.Should().NotBeNull();
        issued!.AuthorizationStatus.Should().Be("ISSUED");

        return issued;
    }

    private static async Task<HttpResponseMessage> PostConsumeAsync(
        HttpClient client,
        Guid exitAuthorizationId,
        PaymentTestContext? context,
        Guid requestedByUserId,
        Guid correlationId,
        bool includeCorrelationId)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume")
        {
            Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(requestedByUserId))
        };

        if (includeCorrelationId)
        {
            message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        }

        message.Headers.Add("X-Service-Identity-Id", requestedByUserId.ToString());

        if (context is not null)
        {
            message.Headers.Add("X-Gate-Device-Id", PaymentTestDataHelper.GateDeviceCode(context));
        }

        return await client.SendAsync(message);
    }

    private sealed record ConsumeExitAuthorizationRequest(Guid RequestedByUserId);

    private sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset ConsumedAt);
}
