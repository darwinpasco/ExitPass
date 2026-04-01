using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the currently exposed Central PMS public payment-attempt API.
///
/// BRD:
/// - 9.9 Payment Initiation
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
///
/// Invariants Enforced:
/// - API request creates or reuses a payment attempt for a valid session/tariff pair
/// - API shape matches the currently published public contract
/// - Required request headers are present at the HTTP boundary
/// </summary>
public sealed class PaymentToExitFlowApiIntegrationTests
{
    private const string DbConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";
    private const string ApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_API_BASE_URL";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(DbConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{DbConnectionStringEnvVar}'.");

    private static Uri ApiBaseUri =>
        new(Environment.GetEnvironmentVariable(ApiBaseUrlEnvVar)
            ?? throw new InvalidOperationException(
                $"Missing environment variable '{ApiBaseUrlEnvVar}'."), UriKind.Absolute);

    [Fact]
    public async Task CreatePaymentAttempt_WhenSessionAndTariffAreValid_ReturnsOkOrCreated()
    {
        var context = PaymentTestContext.Create(
            nameof(CreatePaymentAttempt_WhenSessionAndTariffAreValid_ReturnsOkOrCreated));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for public payment-attempt API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostCreatePaymentAttemptAsync(
                client,
                new CreatePaymentAttemptRequest(
                    ParkingSessionId: context.ParkingSessionId,
                    TariffSnapshotId: context.TariffSnapshotId,
                    PaymentProvider: "GCASH"),
                idempotencyKey: $"idem-api-{Guid.NewGuid():N}",
                correlationId: context.CorrelationId);

            var raw = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                $"Unexpected status code: {response.StatusCode}. Body: {raw}");

            var body = await response.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
            Assert.NotNull(body);
            Assert.NotEqual(Guid.Empty, body!.PaymentAttemptId);
            Assert.False(string.IsNullOrWhiteSpace(body.AttemptStatus));
            Assert.Equal("GCASH", body.PaymentProvider);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task CreatePaymentAttempt_WhenSessionIsInvalid_ReturnsFailure()
    {
        var context = PaymentTestContext.Create(
            nameof(CreatePaymentAttempt_WhenSessionIsInvalid_ReturnsFailure));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for public payment-attempt API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostCreatePaymentAttemptAsync(
                client,
                new CreatePaymentAttemptRequest(
                    ParkingSessionId: Guid.NewGuid(),
                    TariffSnapshotId: context.TariffSnapshotId,
                    PaymentProvider: "GCASH"),
                idempotencyKey: $"idem-api-{Guid.NewGuid():N}",
                correlationId: context.CorrelationId);

            var raw = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict,
                $"Unexpected status code: {response.StatusCode}. Body: {raw}");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task CreatePaymentAttempt_WhenSameRequestIsRepeated_ReturnsDeterministicShape()
    {
        var context = PaymentTestContext.Create(
            nameof(CreatePaymentAttempt_WhenSameRequestIsRepeated_ReturnsDeterministicShape));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for public payment-attempt API tests");

        try
        {
            using var client = CreateClient();

            var request = new CreatePaymentAttemptRequest(
                ParkingSessionId: context.ParkingSessionId,
                TariffSnapshotId: context.TariffSnapshotId,
                PaymentProvider: "GCASH");

            var idempotencyKey = $"idem-api-{Guid.NewGuid():N}";

            var firstResponse = await PostCreatePaymentAttemptAsync(
                client,
                request,
                idempotencyKey,
                context.CorrelationId);

            var secondResponse = await PostCreatePaymentAttemptAsync(
                client,
                request,
                idempotencyKey,
                context.CorrelationId);

            var firstRaw = await firstResponse.Content.ReadAsStringAsync();
            var secondRaw = await secondResponse.Content.ReadAsStringAsync();

            Assert.True(
                firstResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                $"First call unexpected status: {firstResponse.StatusCode}. Body: {firstRaw}");

            Assert.True(
                secondResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                $"Second call unexpected status: {secondResponse.StatusCode}. Body: {secondRaw}");

            var first = await firstResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();
            var second = await secondResponse.Content.ReadFromJsonAsync<CreatePaymentAttemptResponse>();

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotEqual(Guid.Empty, first!.PaymentAttemptId);
            Assert.NotEqual(Guid.Empty, second!.PaymentAttemptId);
            Assert.False(string.IsNullOrWhiteSpace(first.AttemptStatus));
            Assert.False(string.IsNullOrWhiteSpace(second.AttemptStatus));
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
        CreatePaymentAttemptRequest request,
        string idempotencyKey,
        Guid correlationId)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.CreatePaymentAttempt)
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("Idempotency-Key", idempotencyKey);
        message.Headers.Add("X-Correlation-Id", correlationId.ToString());

        return await client.SendAsync(message);
    }

    private static class ApiRoutes
    {
        public const string CreatePaymentAttempt = "/v1/public/payment-attempts";
    }

    private sealed record CreatePaymentAttemptRequest(
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        string PaymentProvider);

    private sealed record CreatePaymentAttemptResponse(
        Guid PaymentAttemptId,
        string AttemptStatus,
        string PaymentProvider,
        bool WasReused,
        ProviderHandoffDto? ProviderHandoff);

    private sealed record ProviderHandoffDto(
        string? Type,
        string? Url,
        DateTimeOffset? ExpiresAt);

    private sealed record ErrorResponse(
        string? ErrorCode,
        string? Message,
        Guid CorrelationId,
        bool Retryable,
        Dictionary<string, object?>? Details);
}
