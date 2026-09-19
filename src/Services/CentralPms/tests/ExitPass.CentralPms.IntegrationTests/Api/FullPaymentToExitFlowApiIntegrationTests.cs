using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the full happy-path API flow from payment attempt creation to exit authorization consumption.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 6.6 Consume Exit Authorization
///
/// Invariants Enforced:
/// - PaymentAttempt must exist before confirmation and finalization
/// - ExitAuthorization may only be issued after confirmed payment finality
/// - ExitAuthorization may only be consumed once through the canonical DB-backed path
/// </summary>
public sealed class FullPaymentToExitFlowApiIntegrationTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Creates the full payment-to-exit API flow integration test fixture.
    /// </summary>
    public FullPaymentToExitFlowApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    /// <summary>
    /// Verifies the canonical happy path from payment-attempt creation through exit-authorization consumption.
    /// </summary>
    [Fact]
    public async Task FullPaymentToExitFlow_HappyPath_CompletesSuccessfully()
    {
        var context = PaymentTestContext.Create(
            nameof(FullPaymentToExitFlow_HappyPath_CompletesSuccessfully));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for full payment-to-exit API flow tests");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                $"idem-full-payment-to-exit-api-{context.ParkingSessionId:N}",
                "full-payment-to-exit-api-test");

            var confirmation = await RecordPaymentConfirmationDirectAsync(
                attempt.PaymentAttemptId,
                providerReference: $"prov-{Guid.NewGuid():N}",
                providerStatus: "SUCCESS",
                requestedBy: "poa-webhook",
                correlationId: context.CorrelationId);

            Assert.NotNull(confirmation);
            Assert.Equal(attempt.PaymentAttemptId, confirmation!.PaymentAttemptId);

            await FinalizeAttemptAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                "CONFIRMED",
                "central-pms-finalizer",
                context.CorrelationId);

            await RecordReadyFiscalReferenceAsync(context, attempt, confirmation);

            using var issueRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/internal/payment-attempts/{attempt.PaymentAttemptId}/issue-exit-authorization");

            issueRequest.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());
            issueRequest.Headers.Add("Idempotency-Key", "idem-http-full-payment-to-exit-issue");

            issueRequest.Content = JsonContent.Create(new IssueExitAuthorizationRequest(
                ParkingSessionId: attempt.ParkingSessionId,
                RequestedByUserId: context.RequestedByUserId));

            using var issueResponse = await _client.SendAsync(issueRequest);

            Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);

            var issued = await issueResponse.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();
            Assert.NotNull(issued);
            Assert.Equal(attempt.PaymentAttemptId, issued!.PaymentAttemptId);
            Assert.Equal(attempt.ParkingSessionId, issued.ParkingSessionId);
            Assert.Equal("ISSUED", issued.AuthorizationStatus);
            Assert.False(string.IsNullOrWhiteSpace(issued.AuthorizationToken));

            using var consumeRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{issued.ExitAuthorizationId}/consume");

            consumeRequest.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());
            consumeRequest.Headers.Add("X-Service-Identity-Id", context.RequestedByUserId.ToString());
            consumeRequest.Headers.Add("X-Gate-Device-Id", PaymentTestDataHelper.GateDeviceCode(context));

            consumeRequest.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var consumeResponse = await _client.SendAsync(consumeRequest);

            Assert.Equal(HttpStatusCode.OK, consumeResponse.StatusCode);

            var consumed = await consumeResponse.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>();
            Assert.NotNull(consumed);
            Assert.Equal(issued.ExitAuthorizationId, consumed!.ExitAuthorizationId);
            Assert.Equal("CONSUMED", consumed.AuthorizationStatus);
            Assert.True(consumed.ConsumedAt > issued.IssuedAt);

            var persisted = await GetExitAuthorizationByIdAsync(ConnectionString, issued.ExitAuthorizationId);
            Assert.NotNull(persisted);
            Assert.Equal("CONSUMED", persisted!.AuthorizationStatus);
            Assert.NotNull(persisted.ConsumedAt);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static async Task<RecordPaymentConfirmationResult> RecordPaymentConfirmationDirectAsync(
        Guid paymentAttemptId,
        string providerReference,
        string providerStatus,
        string requestedBy,
        Guid correlationId)
    {
        const string sql = """
            SELECT
                payment_confirmation_id,
                payment_attempt_id,
                provider_reference,
                provider_status,
                'RECORDED'::character varying AS confirmation_status,
                verified_timestamp
            FROM core.record_payment_confirmation(
                @p_payment_attempt_id,
                @p_provider_reference,
                @p_provider_status,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p_payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("p_provider_reference", providerReference);
        command.Parameters.AddWithValue("p_provider_status", providerStatus);
        command.Parameters.AddWithValue("p_requested_by", requestedBy);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        return new RecordPaymentConfirmationResult(
            PaymentConfirmationId: reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ProviderReference: reader.GetString(reader.GetOrdinal("provider_reference")),
            ProviderStatus: reader.GetString(reader.GetOrdinal("provider_status")),
            ConfirmationStatus: reader.GetString(reader.GetOrdinal("confirmation_status")),
            VerifiedTimestamp: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("verified_timestamp")));
    }

    private static async Task RecordReadyFiscalReferenceAsync(
        PaymentTestContext context,
        PaymentRoutineTestHelper.CreateAttemptResult attempt,
        RecordPaymentConfirmationResult confirmation)
    {
        var repository = new PostgresFiscalIssuanceReferenceRepository(ConnectionString);
        var sequenceValue = Math.Abs(attempt.PaymentAttemptId.GetHashCode()) + 1L;

        await repository.CreateAsync(
            new CreateFiscalIssuanceReferenceRequest(
                PaymentConfirmationId: confirmation.PaymentConfirmationId,
                PaymentAttemptId: attempt.PaymentAttemptId,
                ParkingSessionId: attempt.ParkingSessionId,
                TariffSnapshotId: attempt.TariffSnapshotId,
                SiteId: context.SiteId,
                SitePosServerId: Guid.NewGuid(),
                SitePosServerRef: $"site-pos-server-{context.SiteCode}",
                FiscalDocumentTypeCodeId: null,
                FiscalDocumentTypeCodeKey: "SALES_INVOICE",
                PayableBasisRef: $"tariff-snapshot:{attempt.TariffSnapshotId:N}",
                UpstreamFinalityReference: confirmation.ProviderReference,
                PosServerFiscalDocumentId: Guid.NewGuid(),
                FiscalIdentityId: Guid.NewGuid(),
                FiscalSequencePolicyId: Guid.NewGuid(),
                FiscalSequenceValue: sequenceValue,
                FiscalDocumentNumber: $"SI-{sequenceValue:000000}-TEST",
                FiscalSeries: "SI",
                FiscalNumberPrefixText: "SI-",
                FiscalNumberSuffixText: "-TEST",
                FiscalNumberAssignedAt: DateTimeOffset.UtcNow,
                FiscalNumberAssignedByRef: "integration-test",
                FiscalDocumentStatusCodeId: Guid.NewGuid(),
                ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
                FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
                FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
                FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
                LatestExceptionReason: null,
                LatestErrorCode: null,
                LatestErrorPosture: null,
                CorrelationId: context.CorrelationId,
                PosServerResponseTimestamp: DateTimeOffset.UtcNow,
                RecordedByServiceIdentityId: context.RequestedByUserId),
            CancellationToken.None);
    }

    private sealed record RecordPaymentConfirmationResult(
        Guid PaymentConfirmationId,
        Guid PaymentAttemptId,
        string ProviderReference,
        string ProviderStatus,
        string ConfirmationStatus,
        DateTimeOffset VerifiedTimestamp);

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

    private sealed record ConsumeExitAuthorizationRequest(
        Guid RequestedByUserId);

    private sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset ConsumedAt);
}
