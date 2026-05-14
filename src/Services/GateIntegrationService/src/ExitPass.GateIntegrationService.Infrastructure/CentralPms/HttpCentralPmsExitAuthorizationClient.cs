using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ExitPass.GateIntegrationService.Application.GateExit;

namespace ExitPass.GateIntegrationService.Infrastructure.CentralPms;

/// <summary>
/// HTTP client for the Central PMS gate-facing exit authorization consume API.
/// </summary>
public sealed class HttpCentralPmsExitAuthorizationClient : ICentralPmsExitAuthorizationClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpCentralPmsExitAuthorizationClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client configured for the Central PMS service boundary.</param>
    public HttpCentralPmsExitAuthorizationClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<CentralPmsConsumeAuthorizationResult> ConsumeAsync(
        Guid exitAuthorizationId,
        Guid requestedByUserId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/gate/authorizations/{exitAuthorizationId}/consume")
        {
            Content = JsonContent.Create(new ConsumeExitAuthorizationRequest(requestedByUserId))
        };

        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId.ToString());

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadFromJsonAsync<ConsumeExitAuthorizationResponse>(
                    cancellationToken: cancellationToken);

                if (body is null ||
                    body.ExitAuthorizationId == Guid.Empty ||
                    !string.Equals(body.AuthorizationStatus, "CONSUMED", StringComparison.OrdinalIgnoreCase) ||
                    !body.ConsumedAt.HasValue)
                {
                    return CentralPmsConsumeAuthorizationResult.Rejected(
                        CentralPmsConsumeAuthorizationStatus.Rejected,
                        exitAuthorizationId,
                        "CENTRAL_PMS_INVALID_CONSUME_RESPONSE");
                }

                return CentralPmsConsumeAuthorizationResult.Consumed(
                    body.ExitAuthorizationId,
                    body.AuthorizationStatus,
                    body.ConsumedAt.Value);
            }

            var error = await TryReadErrorAsync(response, cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => CentralPmsConsumeAuthorizationResult.Rejected(
                    CentralPmsConsumeAuthorizationStatus.NotFound,
                    exitAuthorizationId,
                    "EXIT_AUTHORIZATION_NOT_FOUND",
                    error?.Message),

                HttpStatusCode.Conflict when string.Equals(error?.ErrorCode, "EXIT_AUTHORIZATION_ALREADY_CONSUMED", StringComparison.OrdinalIgnoreCase) =>
                    CentralPmsConsumeAuthorizationResult.Rejected(
                        CentralPmsConsumeAuthorizationStatus.AlreadyConsumed,
                        exitAuthorizationId,
                        "EXIT_AUTHORIZATION_ALREADY_CONSUMED",
                        error?.Message),

                HttpStatusCode.Conflict when string.Equals(error?.ErrorCode, "EXIT_AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) =>
                    CentralPmsConsumeAuthorizationResult.Rejected(
                        CentralPmsConsumeAuthorizationStatus.Expired,
                        exitAuthorizationId,
                        "EXIT_AUTHORIZATION_EXPIRED",
                        error?.Message),

                HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout =>
                    CentralPmsConsumeAuthorizationResult.Rejected(
                        CentralPmsConsumeAuthorizationStatus.Unavailable,
                        exitAuthorizationId,
                        "CENTRAL_PMS_UNAVAILABLE",
                        error?.Message),

                _ => CentralPmsConsumeAuthorizationResult.Rejected(
                    CentralPmsConsumeAuthorizationStatus.Rejected,
                    exitAuthorizationId,
                    string.IsNullOrWhiteSpace(error?.ErrorCode) ? "EXIT_AUTHORIZATION_REJECTED" : error.ErrorCode,
                    error?.Message)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CentralPmsConsumeAuthorizationResult.Rejected(
                CentralPmsConsumeAuthorizationStatus.Unavailable,
                exitAuthorizationId,
                "CENTRAL_PMS_UNAVAILABLE");
        }
        catch (HttpRequestException ex)
        {
            return CentralPmsConsumeAuthorizationResult.Rejected(
                CentralPmsConsumeAuthorizationStatus.Unavailable,
                exitAuthorizationId,
                "CENTRAL_PMS_UNAVAILABLE",
                ex.Message);
        }
    }

    private static async Task<ErrorResponse?> TryReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(
                cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private sealed record ConsumeExitAuthorizationRequest(
        [property: JsonPropertyName("requestedByUserId")] Guid RequestedByUserId);

    private sealed record ConsumeExitAuthorizationResponse(
        [property: JsonPropertyName("exitAuthorizationId")] Guid ExitAuthorizationId,
        [property: JsonPropertyName("authorizationStatus")] string AuthorizationStatus,
        [property: JsonPropertyName("consumedAt")] DateTimeOffset? ConsumedAt);

    private sealed record ErrorResponse(
        [property: JsonPropertyName("error_code")] string? ErrorCode,
        [property: JsonPropertyName("message")] string? Message);
}
