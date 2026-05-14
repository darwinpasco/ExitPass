using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Internal;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Internal;

/// <summary>
/// Verifies the v1.2 internal verified payment outcome contract.
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
///
/// Invariants Enforced:
/// - Verified provider outcomes must record payment confirmation evidence.
/// - Confirmed outcomes must finalize the PaymentAttempt and issue ExitAuthorization.
/// - Duplicate provider references must return deterministic conflict responses.
/// - Required idempotency and correlation headers must fail closed.
/// </summary>
public sealed class ReportVerifiedPaymentOutcomeContractTests
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
    /// Verifies BRD 9.10 and SDD 10.5.3 unknown-payment-attempt behavior.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_404_not_found_for_unknown_payment_attempt()
    {
        var context = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_404_not_found_for_unknown_payment_attempt));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for verified payment outcome contract tests");

        try
        {
            using var client = CreateClient();
            using var response = await PostOutcomeAsync(
                client,
                BuildRequest(Guid.NewGuid(), context.ParkingSessionId, context.RequestedByUserId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_ATTEMPT_NOT_FOUND");
            payload.CorrelationId.Should().Be(context.CorrelationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.10, BRD 9.12, SDD 6.4, and SDD 6.5 success response shape.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_200_ok_for_valid_confirmed_request()
    {
        var context = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_200_ok_for_valid_confirmed_request));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for verified payment outcome contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var response = await PostOutcomeAsync(
                client,
                BuildRequest(created.PaymentAttemptId, context.ParkingSessionId, context.RequestedByUserId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content.ReadFromJsonAsync<ReportVerifiedPaymentOutcomeResponse>();
            payload.Should().NotBeNull();
            payload!.PaymentAttemptId.Should().Be(created.PaymentAttemptId);
            payload.PaymentConfirmationId.Should().NotBe(Guid.Empty);
            payload.AttemptStatus.Should().Be("CONFIRMED");
            payload.ExitAuthorizationId.Should().NotBeNull();
            payload.AuthorizationStatus.Should().Be("ISSUED");
            payload.AuthorizationToken.Should().NotBeNullOrWhiteSpace();
            payload.VerifiedTimestamp.Should().BeAfter(DateTimeOffset.MinValue);
            payload.IssuedAt.Should().NotBeNull();
            payload.ExpirationTimestamp.Should().NotBeNull();
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 10.5.3 duplicate provider-reference behavior.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_409_conflict_for_duplicate_provider_reference()
    {
        var firstContext = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_409_conflict_for_duplicate_provider_reference) + "First");
        var secondContext = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_409_conflict_for_duplicate_provider_reference) + "Second");
        var providerReference = $"prov-{Guid.NewGuid():N}";

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            firstContext,
            "Seed first data set for duplicate provider-reference contract tests");
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            secondContext,
            "Seed second data set for duplicate provider-reference contract tests");

        try
        {
            var firstCreated = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                firstContext,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");
            var secondCreated = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                secondContext,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var first = await PostOutcomeAsync(
                client,
                BuildRequest(firstCreated.PaymentAttemptId, firstContext.ParkingSessionId, firstContext.RequestedByUserId, providerReference),
                includeCorrelationId: true,
                correlationId: firstContext.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            using var second = await PostOutcomeAsync(
                client,
                BuildRequest(secondCreated.PaymentAttemptId, secondContext.ParkingSessionId, secondContext.RequestedByUserId, providerReference),
                includeCorrelationId: true,
                correlationId: secondContext.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await second.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PROVIDER_REFERENCE_ALREADY_RECORDED");
            payload.CorrelationId.Should().Be(secondContext.CorrelationId);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, secondContext);
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, firstContext);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 10.5.3 conflict behavior when the attempt already has confirmation evidence.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_409_conflict_when_payment_confirmation_already_exists()
    {
        var context = PaymentTestContext.Create(nameof(ReportVerifiedPaymentOutcome_returns_409_conflict_when_payment_confirmation_already_exists));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for duplicate confirmation contract tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                $"ctest-create-{Guid.NewGuid():N}",
                "verified-outcome-contract-test");

            using var client = CreateClient();
            using var first = await PostOutcomeAsync(
                client,
                BuildRequest(created.PaymentAttemptId, context.ParkingSessionId, context.RequestedByUserId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            using var second = await PostOutcomeAsync(
                client,
                BuildRequest(created.PaymentAttemptId, context.ParkingSessionId, context.RequestedByUserId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId,
                includeIdempotencyKey: true,
                idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await second.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("PAYMENT_CONFIRMATION_ALREADY_EXISTS");
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
    public async Task ReportVerifiedPaymentOutcome_returns_400_bad_request_without_idempotency_key()
    {
        using var client = CreateClient();
        using var response = await PostOutcomeAsync(
            client,
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            includeCorrelationId: true,
            correlationId: Guid.NewGuid(),
            includeIdempotencyKey: false,
            idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies SDD 10.5.3 header validation for missing correlation metadata.
    /// </summary>
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_returns_400_bad_request_without_correlation_id()
    {
        using var client = CreateClient();
        using var response = await PostOutcomeAsync(
            client,
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            includeCorrelationId: false,
            correlationId: Guid.Empty,
            includeIdempotencyKey: true,
            idempotencyKey: $"ctest-outcome-{Guid.NewGuid():N}");

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

    private static ReportVerifiedPaymentOutcomeRequest BuildRequest(
        Guid paymentAttemptId,
        Guid parkingSessionId,
        Guid requestedByUserId,
        string? providerReference = null)
    {
        return new ReportVerifiedPaymentOutcomeRequest(
            PaymentAttemptId: paymentAttemptId,
            ParkingSessionId: parkingSessionId,
            ProviderReference: providerReference ?? $"prov-{Guid.NewGuid():N}",
            ProviderStatus: "SUCCESS",
            FinalAttemptStatus: "CONFIRMED",
            RequestedBy: "payment-orchestrator",
            RequestedByUserId: requestedByUserId);
    }

    private static async Task<HttpResponseMessage> PostOutcomeAsync(
        HttpClient client,
        ReportVerifiedPaymentOutcomeRequest request,
        bool includeCorrelationId,
        Guid correlationId,
        bool includeIdempotencyKey,
        string? idempotencyKey)
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
}
