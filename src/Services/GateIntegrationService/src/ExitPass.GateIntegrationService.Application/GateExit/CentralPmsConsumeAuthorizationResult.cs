namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Provider-neutral Central PMS consume outcome observed by Gate Integration Service.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.4.2 Consume Exit Authorization
///
/// Invariants Enforced:
/// - Central PMS remains authoritative for consume/finality state.
/// - Gate Integration Service only reacts to the consume outcome.
/// </summary>
public sealed record CentralPmsConsumeAuthorizationResult(
    CentralPmsConsumeAuthorizationStatus Status,
    Guid ExitAuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ConsumedAt,
    string? ErrorCode = null,
    string? Message = null)
{
    public static CentralPmsConsumeAuthorizationResult Consumed(
        Guid exitAuthorizationId,
        string authorizationStatus,
        DateTimeOffset consumedAt)
    {
        return new CentralPmsConsumeAuthorizationResult(
            CentralPmsConsumeAuthorizationStatus.Consumed,
            exitAuthorizationId,
            authorizationStatus,
            consumedAt);
    }

    public static CentralPmsConsumeAuthorizationResult Rejected(
        CentralPmsConsumeAuthorizationStatus status,
        Guid exitAuthorizationId,
        string errorCode,
        string? message = null)
    {
        return new CentralPmsConsumeAuthorizationResult(
            status,
            exitAuthorizationId,
            status.ToString().ToUpperInvariant(),
            null,
            errorCode,
            message);
    }
}
