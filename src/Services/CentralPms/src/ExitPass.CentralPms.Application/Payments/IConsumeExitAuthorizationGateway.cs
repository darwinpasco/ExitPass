namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// DB-backed gateway for consuming exit authorizations through the canonical routine.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 9.7 Recommended Database Functions
///
/// Invariants Enforced:
/// - ExitAuthorization consumption is delegated to the canonical DB routine
/// - Application code must not mutate authorization state outside the DB control path
/// </summary>
public interface IConsumeExitAuthorizationGateway
{
    /// <summary>
    /// Consumes an issued exit authorization through the canonical database routine.
    /// </summary>
    /// <param name="request">Consumption request metadata and identifiers.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative consume result.</returns>
    Task<ConsumeExitAuthorizationDbResult> ConsumeAsync(
        ConsumeExitAuthorizationDbRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// DB request for consuming an exit authorization.
/// </summary>
public sealed record ConsumeExitAuthorizationDbRequest
{
    /// <summary>
    /// Canonical identifier of the exit authorization to consume.
    /// </summary>
    public Guid ExitAuthorizationId { get; init; }

    /// <summary>
    /// User or actor identifier requesting the consume operation.
    /// </summary>
    public Guid RequestedByUserId { get; init; }

    /// <summary>
    /// Correlation identifier for end-to-end traceability.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Timestamp at which the consume request is issued.
    /// </summary>
    public DateTimeOffset RequestedAt { get; init; }
}

/// <summary>
/// DB-authoritative result returned after consuming an exit authorization.
/// </summary>
/// <param name="ExitAuthorizationId">Canonical identifier of the consumed authorization.</param>
/// <param name="AuthorizationStatus">Authorization status after consumption.</param>
/// <param name="ConsumedAt">Timestamp at which the authorization was consumed.</param>
public sealed record ConsumeExitAuthorizationDbResult(
    Guid ExitAuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset ConsumedAt);
