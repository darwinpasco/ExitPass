namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Represents the provider-neutral Central PMS consume outcome observed by Gate Integration Service.
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
/// <param name="Status">Central PMS consume status mapped for Gate Integration Service fail-closed handling.</param>
/// <param name="ExitAuthorizationId">Exit authorization identifier returned or rejected by Central PMS.</param>
/// <param name="AuthorizationStatus">Central PMS authorization status, such as <c>CONSUMED</c> on success.</param>
/// <param name="ConsumedAt">Central PMS consume timestamp when the authorization was consumed.</param>
/// <param name="ErrorCode">Provider-neutral Central PMS error code when consume is rejected.</param>
/// <param name="Message">Optional Central PMS error detail.</param>
public sealed record CentralPmsConsumeAuthorizationResult(
    CentralPmsConsumeAuthorizationStatus Status,
    Guid ExitAuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ConsumedAt,
    string? ErrorCode = null,
    string? Message = null)
{
    /// <summary>
    /// Creates a successful Central PMS consume result that allows the gate-open workflow to proceed.
    /// </summary>
    /// <param name="exitAuthorizationId">Exit authorization identifier consumed by Central PMS.</param>
    /// <param name="authorizationStatus">Central PMS authorization status; must be <c>CONSUMED</c> for gate opening.</param>
    /// <param name="consumedAt">Timestamp supplied by Central PMS for the consume operation.</param>
    /// <returns>A consumed Central PMS result.</returns>
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

    /// <summary>
    /// Creates a rejected Central PMS consume result that keeps the gate closed.
    /// </summary>
    /// <param name="status">Central PMS rejection status.</param>
    /// <param name="exitAuthorizationId">Exit authorization identifier rejected by Central PMS.</param>
    /// <param name="errorCode">Provider-neutral rejection code.</param>
    /// <param name="message">Optional rejection detail.</param>
    /// <returns>A rejected Central PMS result.</returns>
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
