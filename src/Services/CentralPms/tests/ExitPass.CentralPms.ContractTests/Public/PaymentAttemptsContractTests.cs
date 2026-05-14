using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Public.PaymentAttempts;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Public;

/// <summary>
/// Verifies the v1.2 public PaymentAttempt API contract against seeded canonical database state.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.4 One Active Payment Attempt Per Session
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 10.2.4 Initiate Payment Attempt
/// - 10.7.1 Idempotent APIs
///
/// Invariants Enforced:
/// - PaymentAttempt creation must be anchored to an existing v1.2 ParkingSession and TariffSnapshot.
/// - Same idempotency key must deterministically reuse the existing PaymentAttempt.
/// - Competing idempotency keys for the same active payment context must fail with the v1.2 error envelope.
/// </summary>
public sealed class PaymentAttemptsContractTests
{
    private const string PrimaryApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_API_BASE_URL";
    private const string AlternateApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_BASE_URL";
    private const string LegacyApiBaseUrlEnvVar = "CENTRAL_PMS_BASE_URL";
    private const string ContractApiBaseUrlEnvVar = "EXITPASS_TEST_API_BASE_URL";

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    private static Uri ApiBaseUri => new(
        Environment.GetEnvironmentVariable(PrimaryApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(ContractApiBaseUrlEnvVar)
        ?? "http://localhost:8080",
        UriKind.Absolute);

    /// <summary>
    /// Verifies BRD 9.9 and SDD 10.2.4 creation response shape for a valid v1.2 payment attempt request.
    /// </summary>
    [Fact]
    public async Task CreatePaymentAttempt_returns_201_created_for_first_request()
    {
        var context = PaymentTestContext.Create(
            nameof(CreatePaymentAttempt_returns_201_created_for_first_request));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for PaymentAttempt public contract tests");

        try
        {
            using var client = CreateClient();

            using var response = await PostCreatePaymentAttemptAsync(
                client,
                context,
                idempotencyKey: $"ctest-idem-{Guid.NewGuid():N}",
                correlationId: context.CorrelationId);

            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var payload = await response.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
            payload.Should().NotBeNull();
            payload!.PaymentAttemptId.Should().NotBe(Guid.Empty);
            payload.AttemptStatus.Should().Be("REQUESTED");
            payload.PaymentProvider.Should().Be("GCASH");
            payload.WasReused.Should().BeFalse();
            payload.ProviderHandoff.Should().NotBeNull();
            payload.ProviderHandoff.Type.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 9.13 and SDD 10.7.1 deterministic idempotent replay for the same v1.2 request.
    /// </summary>
    [Fact]
    public async Task CreatePaymentAttempt_returns_200_ok_and_reused_true_for_same_idempotency_key()
    {
        var context = PaymentTestContext.Create(
            nameof(CreatePaymentAttempt_returns_200_ok_and_reused_true_for_same_idempotency_key));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for PaymentAttempt public contract tests");

        try
        {
            using var client = CreateClient();
            var idempotencyKey = $"ctest-idem-{Guid.NewGuid():N}";

            using var first = await PostCreatePaymentAttemptAsync(
                client,
                context,
                idempotencyKey,
                context.CorrelationId);

            using var second = await PostCreatePaymentAttemptAsync(
                client,
                context,
                idempotencyKey,
                context.CorrelationId);

            first.StatusCode.Should().Be(HttpStatusCode.Created);
            second.StatusCode.Should().Be(HttpStatusCode.OK);

            var firstPayload = await first.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
            var secondPayload = await second.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();

            firstPayload.Should().NotBeNull();
            secondPayload.Should().NotBeNull();
            secondPayload!.PaymentAttemptId.Should().Be(firstPayload!.PaymentAttemptId);
            secondPayload.AttemptStatus.Should().Be(firstPayload.AttemptStatus);
            secondPayload.PaymentProvider.Should().Be("GCASH");
            secondPayload.WasReused.Should().BeTrue();
            secondPayload.ProviderHandoff.Should().NotBeNull();
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies BRD 10.7.4 and SDD 9.6 conflict behavior for competing active PaymentAttempt creation.
    /// </summary>
    [Fact]
    public async Task CreatePaymentAttempt_returns_409_conflict_for_competing_active_attempt()
    {
        var context = PaymentTestContext.Create(
            nameof(CreatePaymentAttempt_returns_409_conflict_for_competing_active_attempt));
        var conflictCorrelationId = Guid.NewGuid();

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for PaymentAttempt public contract tests");

        try
        {
            using var client = CreateClient();

            using var first = await PostCreatePaymentAttemptAsync(
                client,
                context,
                idempotencyKey: $"ctest-idem-{Guid.NewGuid():N}",
                correlationId: context.CorrelationId);

            using var second = await PostCreatePaymentAttemptAsync(
                client,
                context,
                idempotencyKey: $"ctest-idem-{Guid.NewGuid():N}",
                correlationId: conflictCorrelationId);

            first.StatusCode.Should().Be(HttpStatusCode.Created);
            second.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var payload = await second.Content.ReadFromJsonAsync<ErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be("ACTIVE_PAYMENT_ATTEMPT_EXISTS");
            payload.CorrelationId.Should().Be(conflictCorrelationId);
            payload.Retryable.Should().BeFalse();
            payload.Message.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient
        {
            BaseAddress = ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static async Task<HttpResponseMessage> PostCreatePaymentAttemptAsync(
        HttpClient client,
        PaymentTestContext context,
        string idempotencyKey,
        Guid correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/public/payment-attempts")
        {
            Content = JsonContent.Create(new CreatePaymentAttemptRequest
            {
                ParkingSessionId = context.ParkingSessionId,
                TariffSnapshotId = context.TariffSnapshotId,
                PaymentProvider = "GCASH"
            })
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        return await client.SendAsync(request);
    }
}
