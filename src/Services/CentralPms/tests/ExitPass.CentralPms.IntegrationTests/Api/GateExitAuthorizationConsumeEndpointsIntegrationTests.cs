using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the currently exposed Central PMS gate consume-exit-authorization API.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.6 Internal Service APIs
///
/// Invariants Enforced:
/// - HTTP boundary requires X-Correlation-Id before consume
/// - A valid authorization may be consumed exactly once
/// - Exit-authorization actor references must point to a seeded identity.service_identities record
/// - API shape matches the currently published gate-facing contract
/// </summary>
public sealed class GateExitAuthorizationConsumeEndpointsIntegrationTests
{
    private const string PrimaryDbConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";
    private const string AlternateDbConnectionStringEnvVar = "EXITPASS_TEST_DB_CONNECTION_STRING";
    private const string LegacyDbConnectionStringEnvVar = "ConnectionStrings__MainDatabase";

    private const string PrimaryApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_API_BASE_URL";
    private const string AlternateApiBaseUrlEnvVar = "EXITPASS_CENTRAL_PMS_BASE_URL";
    private const string LegacyApiBaseUrlEnvVar = "CENTRAL_PMS_BASE_URL";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(PrimaryDbConnectionStringEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateDbConnectionStringEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyDbConnectionStringEnvVar)
        ?? "Host=localhost;Port=5432;Database=exitpass;Username=postgres;Password=postgres";

    private static Uri ApiBaseUri => new(
        Environment.GetEnvironmentVariable(PrimaryApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(AlternateApiBaseUrlEnvVar)
        ?? Environment.GetEnvironmentVariable(LegacyApiBaseUrlEnvVar)
        ?? "http://localhost:8080",
        UriKind.Absolute);

    /// <summary>
    /// Verifies that the consume endpoint rejects requests when the X-Correlation-Id header is missing.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenCorrelationIdHeaderIsMissing_ReturnsBadRequest()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenCorrelationIdHeaderIsMissing_ReturnsBadRequest));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: Guid.NewGuid(),
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: false,
                correlationId: context.CorrelationId);

            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("INVALID_REQUEST", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that a valid authorization can be consumed successfully.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsValid_ReturnsOk()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsValid_ReturnsOk));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-consume-success",
                "consume-exit-auth-test");

            var finalized = await PaymentRoutineTestHelper.FinalizeAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId,
                "CONFIRMED",
                "payment-orchestrator",
                context.CorrelationId);

            Assert.NotNull(finalized);
            Assert.Equal("CONFIRMED", finalized!.AttemptStatus);

            using var client = CreateClient();

            var confirmationResponse = await PostRecordPaymentConfirmationAsync(
                client,
                new RecordPaymentConfirmationRequest(
                    PaymentAttemptId: created.PaymentAttemptId,
                    ProviderReference: $"prov-{Guid.NewGuid():N}",
                    ProviderStatus: "SUCCESS",
                    RequestedBy: "integration-test"),
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-confirm-{Guid.NewGuid():N}");

            Assert.Equal(HttpStatusCode.Created, confirmationResponse.StatusCode);

            var issuedResponse = await PostIssueExitAuthorizationAsync(
                client,
                paymentAttemptId: created.PaymentAttemptId,
                request: new IssueExitAuthorizationRequest(
                    ParkingSessionId: context.ParkingSessionId,
                    RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                includeIdempotencyKey: true,
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-issue-{Guid.NewGuid():N}");

            var issuedRaw = await issuedResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, issuedResponse.StatusCode);

            var issued = await issuedResponse.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();
            Assert.NotNull(issued);

            var response = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: issued!.ExitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>();

            Assert.NotNull(body);
            Assert.Equal(issued.ExitAuthorizationId, body!.ExitAuthorizationId);
            Assert.False(string.IsNullOrWhiteSpace(body.AuthorizationStatus));
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the same authorization cannot be consumed twice successfully.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsAlreadyConsumed_ReturnsConflictOrBadRequestOrNotFound()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsAlreadyConsumed_ReturnsConflictOrBadRequestOrNotFound));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-consume-repeat",
                "consume-exit-auth-test");

            var finalized = await PaymentRoutineTestHelper.FinalizeAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId,
                "CONFIRMED",
                "payment-orchestrator",
                context.CorrelationId);

            Assert.NotNull(finalized);
            Assert.Equal("CONFIRMED", finalized!.AttemptStatus);

            using var client = CreateClient();

            var confirmationResponse = await PostRecordPaymentConfirmationAsync(
                client,
                new RecordPaymentConfirmationRequest(
                    PaymentAttemptId: created.PaymentAttemptId,
                    ProviderReference: $"prov-{Guid.NewGuid():N}",
                    ProviderStatus: "SUCCESS",
                    RequestedBy: "integration-test"),
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-confirm-{Guid.NewGuid():N}");

            Assert.Equal(HttpStatusCode.Created, confirmationResponse.StatusCode);

            var issuedResponse = await PostIssueExitAuthorizationAsync(
                client,
                paymentAttemptId: created.PaymentAttemptId,
                request: new IssueExitAuthorizationRequest(
                    ParkingSessionId: context.ParkingSessionId,
                    RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                includeIdempotencyKey: true,
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-issue-{Guid.NewGuid():N}");

            Assert.Equal(HttpStatusCode.OK, issuedResponse.StatusCode);

            var issued = await issuedResponse.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();
            Assert.NotNull(issued);

            var firstResponse = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: issued!.ExitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            var secondResponse = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: issued.ExitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            var secondRaw = await secondResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            Assert.True(
                secondResponse.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                $"Unexpected status code: {secondResponse.StatusCode}. Body: {secondRaw}");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that consuming a non-existent authorization fails deterministically.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationDoesNotExist_ReturnsNotFoundOrConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationDoesNotExist_ReturnsNotFoundOrConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            using var client = CreateClient();

            var response = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: Guid.NewGuid(),
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            var raw = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict,
                $"Unexpected status code: {response.StatusCode}. Body: {raw}");
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that an expired authorization is rejected.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsExpired_ReturnsConflictOrBadRequestOrNotFound()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsExpired_ReturnsConflictOrBadRequestOrNotFound));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var created = await PaymentRoutineTestHelper.CreateAttemptAsync(
                ConnectionString,
                context,
                "idem-consume-expired",
                "consume-exit-auth-test");

            var finalized = await PaymentRoutineTestHelper.FinalizeAttemptAsync(
                ConnectionString,
                created.PaymentAttemptId,
                "CONFIRMED",
                "payment-orchestrator",
                context.CorrelationId);

            Assert.NotNull(finalized);
            Assert.Equal("CONFIRMED", finalized!.AttemptStatus);

            using var client = CreateClient();

            var confirmationResponse = await PostRecordPaymentConfirmationAsync(
                client,
                new RecordPaymentConfirmationRequest(
                    PaymentAttemptId: created.PaymentAttemptId,
                    ProviderReference: $"prov-{Guid.NewGuid():N}",
                    ProviderStatus: "SUCCESS",
                    RequestedBy: "integration-test"),
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-confirm-{Guid.NewGuid():N}");

            Assert.Equal(HttpStatusCode.Created, confirmationResponse.StatusCode);

            var issuedResponse = await PostIssueExitAuthorizationAsync(
                client,
                paymentAttemptId: created.PaymentAttemptId,
                request: new IssueExitAuthorizationRequest(
                    ParkingSessionId: context.ParkingSessionId,
                    RequestedByUserId: KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                includeIdempotencyKey: true,
                correlationId: context.CorrelationId,
                idempotencyKey: $"idem-issue-{Guid.NewGuid():N}");

            Assert.Equal(HttpStatusCode.OK, issuedResponse.StatusCode);

            var issued = await issuedResponse.Content.ReadFromJsonAsync<IssueExitAuthorizationResponse>();
            Assert.NotNull(issued);

            await PaymentRoutineTestHelper.ExpireAuthorizationAsync(
                ConnectionString,
                issued!.ExitAuthorizationId,
                KnownTestIdentityIds.ServiceIdentityId);

            var response = await PostConsumeExitAuthorizationAsync(
                client,
                exitAuthorizationId: issued.ExitAuthorizationId,
                request: new ConsumeExitAuthorizationRequest(KnownTestIdentityIds.ServiceIdentityId),
                includeCorrelationId: true,
                correlationId: context.CorrelationId);

            var raw = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                $"Unexpected status code: {response.StatusCode}. Body: {raw}");
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

    private static async Task<HttpResponseMessage> PostConsumeExitAuthorizationAsync(
        HttpClient client,
        Guid exitAuthorizationId,
        ConsumeExitAuthorizationRequest request,
        bool includeCorrelationId,
        Guid correlationId)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume")
        {
            Content = JsonContent.Create(request)
        };

        if (includeCorrelationId)
        {
            message.Headers.Add("X-Correlation-Id", correlationId.ToString());
        }

        return await client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> PostRecordPaymentConfirmationAsync(
        HttpClient client,
        RecordPaymentConfirmationRequest request,
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

    private static async Task<HttpResponseMessage> PostIssueExitAuthorizationAsync(
        HttpClient client,
        Guid paymentAttemptId,
        IssueExitAuthorizationRequest request,
        bool includeCorrelationId,
        bool includeIdempotencyKey,
        Guid correlationId,
        string idempotencyKey)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/internal/payment-attempts/{paymentAttemptId}/issue-exit-authorization")
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

    private sealed record ConsumeExitAuthorizationRequest(Guid RequestedByUserId);

    private sealed record RecordPaymentConfirmationRequest(
        Guid PaymentAttemptId,
        string ProviderReference,
        string ProviderStatus,
        string RequestedBy);

    private sealed record IssueExitAuthorizationRequest(
        Guid ParkingSessionId,
        Guid RequestedByUserId);

    private sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset? ConsumedAt);

    private sealed record IssueExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken,
        string AuthorizationStatus,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpirationTimestamp);
}
