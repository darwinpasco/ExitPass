using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Contracts.Payments;
using Npgsql;
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
    private const string AuditActor = "integration-test";
    private const string RequestedByActor = "payment-orchestrator";
    private const string ExistingSiteGroupId = "SG-TEST-001";
    private const string ExistingSiteId = "SITE-TEST-001";
    private const string ExistingVendorSystemCode = "VENDOR-TEST-001";

    private readonly HttpClient _httpClient;
    private readonly string _dbConnectionString;

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

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

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

    [Fact]
    public async Task Finalize_WhenRepeatedForAlreadyFinalAttempt_ReturnsConflict()
    {
        var seed = await SeedPendingPaymentAttemptAsync();

        var request = new FinalizePaymentAttemptRequest(
            FinalAttemptStatus: "CONFIRMED",
            RequestedBy: RequestedByActor);

        using var firstMessage = BuildRequestMessage(
            paymentAttemptId: seed.PaymentAttemptId,
            correlationId: Guid.NewGuid().ToString(),
            idempotencyKey: Guid.NewGuid().ToString(),
            request: request);

        using var firstResponse = await _httpClient.SendAsync(firstMessage);

        Assert.True(
            firstResponse.StatusCode == HttpStatusCode.OK || firstResponse.StatusCode == HttpStatusCode.Created,
            $"Expected first finalization to succeed, got {(int)firstResponse.StatusCode} {firstResponse.StatusCode}. Body: {await firstResponse.Content.ReadAsStringAsync()}");

        using var secondMessage = BuildRequestMessage(
            paymentAttemptId: seed.PaymentAttemptId,
            correlationId: Guid.NewGuid().ToString(),
            idempotencyKey: Guid.NewGuid().ToString(),
            request: request);

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

        var parkingSessionId = Guid.NewGuid();
        var tariffSnapshotId = Guid.NewGuid();
        var paymentAttemptId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;
        var entryTimestamp = now.AddMinutes(-30);
        var expiresAt = now.AddMinutes(15);

        await using var connection = new NpgsqlConnection(_dbConnectionString);
        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                insert into core.parking_sessions
                (
                    parking_session_id,
                    site_group_id,
                    site_id,
                    vendor_system_code,
                    vendor_session_ref,
                    identifier_type,
                    plate_number,
                    ticket_number,
                    entry_timestamp,
                    session_status,
                    created_at,
                    created_by,
                    updated_at,
                    updated_by,
                    row_version
                )
                values
                (
                    @parking_session_id,
                    @site_group_id,
                    @site_id,
                    @vendor_system_code,
                    @vendor_session_ref,
                    @identifier_type,
                    @plate_number,
                    @ticket_number,
                    @entry_timestamp,
                    'OPEN',
                    @created_at,
                    @created_by,
                    @updated_at,
                    @updated_by,
                    1
                );
                """;

            cmd.Parameters.AddWithValue("parking_session_id", parkingSessionId);
            cmd.Parameters.AddWithValue("site_group_id", ExistingSiteGroupId);
            cmd.Parameters.AddWithValue("site_id", ExistingSiteId);
            cmd.Parameters.AddWithValue("vendor_system_code", ExistingVendorSystemCode);
            cmd.Parameters.AddWithValue("vendor_session_ref", $"vendor-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("identifier_type", "TICKET");
            cmd.Parameters.AddWithValue("plate_number", DBNull.Value);
            cmd.Parameters.AddWithValue("ticket_number", $"TICKET-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("entry_timestamp", entryTimestamp.UtcDateTime);
            cmd.Parameters.AddWithValue("created_at", now.UtcDateTime);
            cmd.Parameters.AddWithValue("created_by", AuditActor);
            cmd.Parameters.AddWithValue("updated_at", now.UtcDateTime);
            cmd.Parameters.AddWithValue("updated_by", AuditActor);

            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                insert into core.tariff_snapshots
                (
                    tariff_snapshot_id,
                    parking_session_id,
                    source_type,
                    gross_amount,
                    statutory_discount_amount,
                    coupon_discount_amount,
                    net_payable,
                    currency_code,
                    calculated_at,
                    expires_at,
                    snapshot_status,
                    created_at,
                    created_by,
                    updated_at,
                    updated_by,
                    row_version
                )
                values
                (
                    @tariff_snapshot_id,
                    @parking_session_id,
                    @source_type::core.tariff_snapshot_source_type_enum,
                    100.00,
                    0.00,
                    0.00,
                    100.00,
                    @currency_code,
                    @calculated_at,
                    @expires_at,
                    'ACTIVE',
                    @created_at,
                    @created_by,
                    @updated_at,
                    @updated_by,
                    1
                );
                """;

            cmd.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);
            cmd.Parameters.AddWithValue("parking_session_id", parkingSessionId);
            cmd.Parameters.AddWithValue("source_type", "BASE");
            cmd.Parameters.AddWithValue("currency_code", "PHP");
            cmd.Parameters.AddWithValue("calculated_at", now.UtcDateTime);
            cmd.Parameters.AddWithValue("expires_at", expiresAt.UtcDateTime);
            cmd.Parameters.AddWithValue("created_at", now.UtcDateTime);
            cmd.Parameters.AddWithValue("created_by", AuditActor);
            cmd.Parameters.AddWithValue("updated_at", now.UtcDateTime);
            cmd.Parameters.AddWithValue("updated_by", AuditActor);

            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                insert into core.payment_attempts
                (
                    payment_attempt_id,
                    parking_session_id,
                    tariff_snapshot_id,
                    payment_provider_code,
                    idempotency_key,
                    gross_amount_snapshot,
                    statutory_discount_snapshot,
                    coupon_discount_snapshot,
                    net_amount_due_snapshot,
                    currency_code,
                    attempt_status,
                    created_at,
                    created_by,
                    updated_at,
                    updated_by,
                    row_version
                )
                values
                (
                    @payment_attempt_id,
                    @parking_session_id,
                    @tariff_snapshot_id,
                    'GCASH',
                    @idempotency_key,
                    100.00,
                    0.00,
                    0.00,
                    100.00,
                    @currency_code,
                    'PENDING_PROVIDER',
                    @created_at,
                    @created_by,
                    @updated_at,
                    @updated_by,
                    1
                );
                """;

            cmd.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
            cmd.Parameters.AddWithValue("parking_session_id", parkingSessionId);
            cmd.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);
            cmd.Parameters.AddWithValue("idempotency_key", $"seed-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("currency_code", "PHP");
            cmd.Parameters.AddWithValue("created_at", now.UtcDateTime);
            cmd.Parameters.AddWithValue("created_by", AuditActor);
            cmd.Parameters.AddWithValue("updated_at", now.UtcDateTime);
            cmd.Parameters.AddWithValue("updated_by", AuditActor);

            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        return new PendingPaymentAttemptSeed(
            ParkingSessionId: parkingSessionId,
            TariffSnapshotId: tariffSnapshotId,
            PaymentAttemptId: paymentAttemptId);
    }

    private sealed record PendingPaymentAttemptSeed(
        Guid ParkingSessionId,
        Guid TariffSnapshotId,
        Guid PaymentAttemptId);
}
