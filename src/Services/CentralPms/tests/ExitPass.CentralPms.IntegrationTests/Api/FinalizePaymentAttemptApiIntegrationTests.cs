using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Contracts.Payments;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Live API integration tests for the Central PMS payment-attempt finalization endpoint.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 10.5.3 Report Verified Payment Outcome
/// - 11.8 Replay, Duplicate, and Abuse Controls
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state
/// - Finalization must remain a DB-backed control transition
/// - Already-final or invalid transitions must be rejected deterministically
/// </summary>
public sealed class FinalizePaymentAttemptApiIntegrationTests : IAsyncLifetime
{
    private const string RouteTemplate = "/v1/internal/payment-attempts/{0}/finalize";
    private const string RequestedByActor = "payment-orchestrator";

    private readonly HttpClient _httpClient;
    private readonly string _dbConnectionString;
    private readonly List<PaymentTestContext> _seededContexts = [];

    /// <summary>
    /// Creates the live API integration fixture for ExitPass v1.2 payment-attempt finalization.
    /// </summary>
    public FinalizePaymentAttemptApiIntegrationTests()
    {
        var baseUrl =
            Environment.GetEnvironmentVariable("EXITPASS_CENTRAL_PMS_BASE_URL")
            ?? Environment.GetEnvironmentVariable("EXITPASS_CENTRAL_PMS_API_BASE_URL")
            ?? Environment.GetEnvironmentVariable("CENTRAL_PMS_BASE_URL")
            ?? "http://localhost:8080";

        _dbConnectionString =
            Environment.GetEnvironmentVariable("EXITPASS_TEST_DB_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("EXITPASS_INTEGRATION_DB")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__MainDatabase")
            ?? string.Empty;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/'))
        };
    }

    /// <summary>
    /// Initializes the fixture; no shared data is created because each test owns its v1.2 rows.
    /// </summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Cleans up per-test v1.2 seed data created through the shared payment-chain helpers.
    /// </summary>
    public async Task DisposeAsync()
    {
        foreach (var context in _seededContexts)
        {
            await PaymentTestDataHelper.CleanupAsync(_dbConnectionString, context);
        }

        _httpClient.Dispose();
    }

    /// <summary>
    /// Verifies ExitPass v1.2 BRD 9.10, SDD 6.4, and the invariant that a pending attempt
    /// can be finalized through the HTTP boundary.
    /// </summary>
    [Fact]
    public async Task Finalize_WithValidRequest_ReturnsSuccessStatus()
    {
        var seed = await SeedPendingPaymentAttemptAsync();

        var request = new FinalizePaymentAttemptRequest(
            FinalAttemptStatus: "CONFIRMED",
            RequestedBy: RequestedByActor);

        using var message = BuildRequestMessage(
            paymentAttemptId: seed.PaymentAttemptId,
            correlationId: Guid.NewGuid().ToString(),
            idempotencyKey: Guid.NewGuid().ToString(),
            request: request);

        using var response = await _httpClient.SendAsync(message);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created,
            $"Expected 200 or 201 but got {(int)response.StatusCode} {response.StatusCode}. Body: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<FinalizePaymentAttemptResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(body);
        Assert.Equal(seed.PaymentAttemptId, body!.PaymentAttemptId);
        Assert.False(string.IsNullOrWhiteSpace(body.AttemptStatus));
    }

    /// <summary>
    /// Verifies ExitPass v1.2 SDD 10.5.3 and the invariant that idempotency metadata is required.
    /// </summary>
    [Fact]
    public async Task Finalize_WithoutIdempotencyKey_ReturnsBadRequest()
    {
        var request = new FinalizePaymentAttemptRequest(
            FinalAttemptStatus: "CONFIRMED",
            RequestedBy: RequestedByActor);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            string.Format(RouteTemplate, Guid.NewGuid()))
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());

        using var response = await _httpClient.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies ExitPass v1.2 SDD 10.5.3 and the invariant that correlation metadata is required.
    /// </summary>
    [Fact]
    public async Task Finalize_WithoutCorrelationId_ReturnsBadRequest()
    {
        var request = new FinalizePaymentAttemptRequest(
            FinalAttemptStatus: "CONFIRMED",
            RequestedBy: RequestedByActor);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            string.Format(RouteTemplate, Guid.NewGuid()))
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await _httpClient.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies ExitPass v1.2 BRD 9.10 and SDD 6.4 not-found behavior for unknown attempts.
    /// </summary>
    [Fact]
    public async Task Finalize_WithUnknownPaymentAttemptId_ReturnsNotFound()
    {
        var request = new FinalizePaymentAttemptRequest(
            FinalAttemptStatus: "CONFIRMED",
            RequestedBy: RequestedByActor);

        using var message = BuildRequestMessage(
            paymentAttemptId: Guid.NewGuid(),
            correlationId: Guid.NewGuid().ToString(),
            idempotencyKey: Guid.NewGuid().ToString(),
            request: request);

        using var response = await _httpClient.SendAsync(message);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Verifies ExitPass v1.2 BRD 10.7.2, SDD 8.3, and the invariant that a terminal
    /// PaymentAttempt cannot be re-finalized to a conflicting terminal status.
    /// </summary>
    [Fact]
    public async Task Finalize_WhenRepeatedForAlreadyFinalAttempt_ReturnsConflict()
    {
        var seed = await SeedPendingPaymentAttemptAsync();

        var firstRequest = new FinalizePaymentAttemptRequest(
            FinalAttemptStatus: "CONFIRMED",
            RequestedBy: RequestedByActor);

        using var firstMessage = BuildRequestMessage(
            paymentAttemptId: seed.PaymentAttemptId,
            correlationId: Guid.NewGuid().ToString(),
            idempotencyKey: Guid.NewGuid().ToString(),
            request: firstRequest);

        using var firstResponse = await _httpClient.SendAsync(firstMessage);

        Assert.True(
            firstResponse.StatusCode == HttpStatusCode.OK || firstResponse.StatusCode == HttpStatusCode.Created,
            $"Expected first finalization to succeed, got {(int)firstResponse.StatusCode} {firstResponse.StatusCode}. Body: {await firstResponse.Content.ReadAsStringAsync()}");

        var secondRequest = new FinalizePaymentAttemptRequest(
            FinalAttemptStatus: "FAILED",
            RequestedBy: RequestedByActor);

        using var secondMessage = BuildRequestMessage(
            paymentAttemptId: seed.PaymentAttemptId,
            correlationId: Guid.NewGuid().ToString(),
            idempotencyKey: Guid.NewGuid().ToString(),
            request: secondRequest);

        using var secondResponse = await _httpClient.SendAsync(secondMessage);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    private static HttpRequestMessage BuildRequestMessage(
        Guid paymentAttemptId,
        string correlationId,
        string idempotencyKey,
        FinalizePaymentAttemptRequest request)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post,
            string.Format(RouteTemplate, paymentAttemptId))
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.Add("X-Correlation-Id", correlationId);
        message.Headers.Add("Idempotency-Key", idempotencyKey);

        return message;
    }

    private async Task<PendingPaymentAttemptSeed> SeedPendingPaymentAttemptAsync()
    {
        if (string.IsNullOrWhiteSpace(_dbConnectionString))
        {
            throw new InvalidOperationException(
                "Missing DB connection string. Set EXITPASS_TEST_DB_CONNECTION_STRING, EXITPASS_INTEGRATION_DB, or ConnectionStrings__MainDatabase.");
        }

        var context = PaymentTestContext.Create(nameof(FinalizePaymentAttemptApiIntegrationTests));
        await PaymentTestDataHelper.ResetAndSeedAsync(
            _dbConnectionString,
            context,
            "Seed data for finalize-payment API tests");

        _seededContexts.Add(context);

        var attempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
            _dbConnectionString,
            context,
            $"idem-finalize-api-{Guid.NewGuid():N}",
            "finalize-api-test");

        return new PendingPaymentAttemptSeed(
            ParkingSessionId: attempt.ParkingSessionId,
            TariffSnapshotId: attempt.TariffSnapshotId,
            PaymentAttemptId: attempt.PaymentAttemptId);
    }

    private sealed record PendingPaymentAttemptSeed(
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        Guid PaymentAttemptId);
}
