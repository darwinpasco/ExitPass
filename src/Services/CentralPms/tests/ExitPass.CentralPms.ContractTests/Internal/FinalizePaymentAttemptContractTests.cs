using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Payments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Internal;

/// <summary>
/// Verifies the v1.2 internal PaymentAttempt finalization contract.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7.1 Idempotent APIs
///
/// Invariants Enforced:
/// - Unknown payment attempts must return a deterministic not-found envelope.
/// - Valid finalization requests must return canonical finalized PaymentAttempt state.
/// - Same-terminal retries must return existing finalized state.
/// - Conflicting terminal retries must return a deterministic conflict envelope.
/// </summary>
public sealed class FinalizePaymentAttemptContractTests
{
    private const string RequestedByActor = "payment-orchestrator";

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    private static Uri ApiBaseUri => new(
        Environment.GetEnvironmentVariable("EXITPASS_CENTRAL_PMS_API_BASE_URL")
        ?? Environment.GetEnvironmentVariable("EXITPASS_CENTRAL_PMS_BASE_URL")
        ?? Environment.GetEnvironmentVariable("CENTRAL_PMS_BASE_URL")
        ?? "http://localhost:8080",
        UriKind.Absolute);

    /// <summary>
    /// Verifies BRD 9.10 and SDD 6.4 unknown-payment-attempt behavior.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_returns_404_not_found_for_unknown_payment_attempt()
    {
        var correlationId = Guid.NewGuid();

        using var client = CreateClient();
        using var response = await PostFinalizeAsync(
            client,
            paymentAttemptId: Guid.NewGuid(),
            finalStatus: "CONFIRMED",
            correlationId: correlationId,
            idempotencyKey: $"ctest-finalize-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("PAYMENT_ATTEMPT_NOT_FOUND");
        payload.CorrelationId.Should().Be(correlationId);
        payload.Retryable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies BRD 9.10 and SDD 6.4 successful finalization response shape.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_returns_200_ok_for_valid_request()
    {
        var context = PaymentTestContext.Create(nameof(FinalizePaymentAttempt_returns_200_ok_for_valid_request));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalization contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "finalization-contract-test");

            using var client = CreateClient();
            using var response = await PostFinalizeAsync(
                client,
                created.PaymentAttemptId,
                "CONFIRMED",
                context.CorrelationId,
                $"ctest-finalize-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content.ReadFromJsonAsync<FinalizePaymentAttemptResponse>();
            payload.Should().NotBeNull();
            payload!.PaymentAttemptId.Should().Be(created.PaymentAttemptId);
            payload.AttemptStatus.Should().Be("CONFIRMED");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 10.7.1 same-terminal replay behavior.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_returns_200_ok_for_same_terminal_replay()
    {
        var context = PaymentTestContext.Create(nameof(FinalizePaymentAttempt_returns_200_ok_for_same_terminal_replay));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalization contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "finalization-contract-test");

            using var client = CreateClient();
            using var first = await PostFinalizeAsync(
                client,
                created.PaymentAttemptId,
                "CONFIRMED",
                context.CorrelationId,
                $"ctest-finalize-{Guid.NewGuid():N}");

            using var replay = await PostFinalizeAsync(
                client,
                created.PaymentAttemptId,
                "CONFIRMED",
                context.CorrelationId,
                $"ctest-finalize-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);

            var firstPayload = await first.Content.ReadFromJsonAsync<FinalizePaymentAttemptResponse>();
            var replayPayload = await replay.Content.ReadFromJsonAsync<FinalizePaymentAttemptResponse>();

            replayPayload.Should().NotBeNull();
            replayPayload!.PaymentAttemptId.Should().Be(firstPayload!.PaymentAttemptId);
            replayPayload.AttemptStatus.Should().Be("CONFIRMED");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 6.4 conflicting terminal replay behavior.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_returns_409_conflict_for_conflicting_terminal_replay()
    {
        var context = PaymentTestContext.Create(nameof(FinalizePaymentAttempt_returns_409_conflict_for_conflicting_terminal_replay));
        var conflictCorrelationId = Guid.NewGuid();
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for finalization contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "finalization-contract-test");

            using var client = CreateClient();
            using var first = await PostFinalizeAsync(
                client,
                created.PaymentAttemptId,
                "CONFIRMED",
                context.CorrelationId,
                $"ctest-finalize-{Guid.NewGuid():N}");

            using var conflict = await PostFinalizeAsync(
                client,
                created.PaymentAttemptId,
                "FAILED",
                conflictCorrelationId,
                $"ctest-finalize-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await conflict.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_ATTEMPT_ALREADY_FINAL");
            payload.CorrelationId.Should().Be(conflictCorrelationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies SDD 10.5.3 header validation for missing idempotency metadata.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_returns_400_bad_request_without_idempotency_key()
    {
        using var client = CreateClient();
        using var message = BuildFinalizeMessage(
            Guid.NewGuid(),
            "CONFIRMED",
            includeCorrelationId: true,
            correlationId: Guid.NewGuid(),
            includeIdempotencyKey: false,
            idempotencyKey: null);

        using var response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies SDD 10.5.3 header validation for missing correlation metadata.
    /// </summary>
    [Fact]
    public async Task FinalizePaymentAttempt_returns_400_bad_request_without_correlation_id()
    {
        using var client = CreateClient();
        using var message = BuildFinalizeMessage(
            Guid.NewGuid(),
            "CONFIRMED",
            includeCorrelationId: false,
            correlationId: Guid.Empty,
            includeIdempotencyKey: true,
            idempotencyKey: $"ctest-finalize-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(message);

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

    private static async Task<HttpResponseMessage> PostFinalizeAsync(
        HttpClient client,
        Guid paymentAttemptId,
        string finalStatus,
        Guid correlationId,
        string idempotencyKey)
    {
        using var message = BuildFinalizeMessage(
            paymentAttemptId,
            finalStatus,
            includeCorrelationId: true,
            correlationId: correlationId,
            includeIdempotencyKey: true,
            idempotencyKey: idempotencyKey);

        return await client.SendAsync(message);
    }

    private static HttpRequestMessage BuildFinalizeMessage(
        Guid paymentAttemptId,
        string finalStatus,
        bool includeCorrelationId,
        Guid correlationId,
        bool includeIdempotencyKey,
        string? idempotencyKey)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/internal/payment-attempts/{paymentAttemptId}/finalize")
        {
            Content = JsonContent.Create(new FinalizePaymentAttemptRequest(
                FinalAttemptStatus: finalStatus,
                RequestedBy: RequestedByActor))
        };

        if (includeCorrelationId)
        {
            message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        }

        if (includeIdempotencyKey)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return message;
    }
}
