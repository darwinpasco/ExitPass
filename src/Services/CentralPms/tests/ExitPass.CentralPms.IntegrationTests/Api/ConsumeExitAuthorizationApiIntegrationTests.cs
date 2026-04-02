using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the HTTP contract for consuming ExitAuthorization through the gate-facing endpoint.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.4 Gate / Site Integration APIs
///
/// Invariants Enforced:
/// - ExitAuthorization consume is the hard control point before physical exit
/// - A valid authorization may be consumed only once
/// - Expired or replayed authorizations must fail closed
/// </summary>
public sealed class ConsumeExitAuthorizationApiIntegrationTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private const string ConnectionStringEnvVar = "EXITPASS_INTEGRATION_DB";

    private readonly HttpClient _client;

    /// <summary>
    /// Creates the consume-authorization API integration test fixture.
    /// </summary>
    public ConsumeExitAuthorizationApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
        ?? throw new InvalidOperationException(
            $"Missing environment variable '{ConnectionStringEnvVar}'. " +
            "Point it at the ExitPass integration database.");

    /// <summary>
    /// Verifies that a valid issued authorization can be consumed successfully.
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
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-api-success");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{authorization.ExitAuthorizationId}/consume");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());

            request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>();
            Assert.NotNull(body);
            Assert.Equal(authorization.ExitAuthorizationId, body!.ExitAuthorizationId);
            Assert.Equal("CONSUMED", body.AuthorizationStatus);
            Assert.True(body.ConsumedAt > DateTimeOffset.MinValue);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests without a correlation header.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenCorrelationHeaderIsMissing_ReturnsBadRequest()
    {
        var exitAuthorizationId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume");

        request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
            RequestedByUserId: Guid.NewGuid()));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("INVALID_REQUEST", body!.ErrorCode);
        Assert.Contains("X-Correlation-Id", body.Message);
    }

    /// <summary>
    /// Verifies that the endpoint rejects requests with an empty requesting-user identifier.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenRequestedByUserIdIsEmpty_ReturnsBadRequest()
    {
        var correlationId = Guid.NewGuid();
        var exitAuthorizationId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume");

        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
            RequestedByUserId: Guid.Empty));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("INVALID_REQUEST", body!.ErrorCode);
        Assert.Contains("RequestedByUserId", body.Message);
    }

    /// <summary>
    /// Verifies that consume returns not found for a non-existent authorization identifier.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationDoesNotExist_ReturnsNotFound()
    {
        var correlationId = Guid.NewGuid();
        var exitAuthorizationId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume");

        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
            RequestedByUserId: Guid.NewGuid()));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("EXIT_AUTHORIZATION_NOT_FOUND", body!.ErrorCode);
        Assert.Equal(correlationId, body.CorrelationId);
    }

    /// <summary>
    /// Verifies that replaying consume for an already consumed authorization returns conflict.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsAlreadyConsumed_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsAlreadyConsumed_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-api-replay");

            await ConsumeAuthorizationDirectAsync(
                authorization.ExitAuthorizationId,
                context.RequestedByUserId,
                context.CorrelationId);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{authorization.ExitAuthorizationId}/consume");

            request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());

            request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(body);
            Assert.Equal("EXIT_AUTHORIZATION_CONSUME_CONFLICT", body!.ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    /// <summary>
    /// Verifies that consume returns conflict for an expired authorization.
    /// </summary>
    [Fact]
    public async Task ConsumeExitAuthorization_WhenAuthorizationIsExpired_ReturnsConflict()
    {
        var context = PaymentTestContext.Create(
            nameof(ConsumeExitAuthorization_WhenAuthorizationIsExpired_ReturnsConflict));

        await PaymentTestDataHelper.ResetAndSeedAsync(
            ConnectionString,
            context,
            "Seed data for consume-exit-authorization API tests");

        try
        {
            var authorization = await CreateConfirmedIssuedAuthorizationAsync(
                context,
                "idem-consume-api-expired");

            await ExpireAuthorizationDirectAsync(authorization.ExitAuthorizationId);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/gate/authorizations/{authorization.ExitAuthorizationId}/consume");

            request.Headers.Add("X-Correlation-Id", context.CorrelationId.ToString());

            request.Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(
                RequestedByUserId: context.RequestedByUserId));

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(body);
            Assert.Equal("EXIT_AUTHORIZATION_CONSUME_CONFLICT", body!.ErrorCode);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    private static async Task<IssuedAuthorizationContext> CreateConfirmedIssuedAuthorizationAsync(
        PaymentTestContext context,
        string idempotencyKey)
    {
        var attempt = await CreateAttemptAsync(
            ConnectionString,
            context,
            idempotencyKey,
            "consume-auth-api-test");

        await FinalizeAttemptAsync(
            ConnectionString,
            attempt.PaymentAttemptId,
            "CONFIRMED",
            "central-pms-finalizer",
            context.CorrelationId);

        var authorization = await IssueExitAuthorizationAsync(
            ConnectionString,
            attempt.ParkingSessionId,
            attempt.PaymentAttemptId,
            context.RequestedByUserId,
            context.CorrelationId);

        Assert.NotNull(authorization);

        return new IssuedAuthorizationContext(
            authorization!.ExitAuthorizationId,
            authorization.ParkingSessionId,
            authorization.PaymentAttemptId,
            authorization.AuthorizationToken);
    }

    private static async Task ConsumeAuthorizationDirectAsync(
        Guid exitAuthorizationId,
        Guid requestedByUserId,
        Guid correlationId)
    {
        const string sql = """
            SELECT *
            FROM core.consume_exit_authorization(
                @p_exit_authorization_id,
                @p_requested_by,
                @p_correlation_id,
                @p_now
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p_exit_authorization_id", exitAuthorizationId);
        command.Parameters.AddWithValue("p_requested_by", requestedByUserId);
        command.Parameters.AddWithValue("p_correlation_id", correlationId);
        command.Parameters.AddWithValue("p_now", DateTimeOffset.UtcNow);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
    }

    private static async Task ExpireAuthorizationDirectAsync(Guid exitAuthorizationId)
    {
        const string sql = """
            UPDATE core.exit_authorizations
            SET issued_at = now() - interval '16 minutes',
                expiration_timestamp = now() - interval '1 minute',
                updated_at = now(),
                row_version = row_version + 1
            WHERE exit_authorization_id = @p_exit_authorization_id;
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p_exit_authorization_id", exitAuthorizationId);

        var affected = await command.ExecuteNonQueryAsync();
        Assert.Equal(1, affected);
    }

    private sealed record ConsumeExitAuthorizationRequest(
        Guid RequestedByUserId);

    private sealed record ConsumeExitAuthorizationResponse(
        Guid ExitAuthorizationId,
        string AuthorizationStatus,
        DateTimeOffset ConsumedAt);

    private sealed record IssuedAuthorizationContext(
        Guid ExitAuthorizationId,
        Guid ParkingSessionId,
        Guid PaymentAttemptId,
        string AuthorizationToken);
}
