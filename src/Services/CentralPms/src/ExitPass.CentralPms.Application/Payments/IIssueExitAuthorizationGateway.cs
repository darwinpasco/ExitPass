namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// DB-backed gateway for issuing exit authorizations through the canonical routine.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 9.7 Recommended Database Functions
///
/// Invariants Enforced:
/// - ExitAuthorization issuance is delegated to the canonical DB routine
/// - Application code must not mint authorization tokens outside the DB-enforced control path
/// </summary>
public interface IIssueExitAuthorizationGateway
{
    /// <summary>
    /// Issues an exit authorization through the canonical database routine.
    /// </summary>
    /// <param name="request">Issuance request metadata and identifiers.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative issuance result.</returns>
    Task<IssueExitAuthorizationDbResult> IssueAsync(
        IssueExitAuthorizationDbRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// DB request for issuing an exit authorization.
/// </summary>
public sealed record IssueExitAuthorizationDbRequest
{
    /// <summary>
    /// Canonical parking session identifier for which the authorization is issued.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Confirmed payment attempt identifier backing the authorization.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// User or actor identifier requesting issuance.
    /// </summary>
    public Guid RequestedByUserId { get; init; }

    /// <summary>
    /// Correlation identifier for end-to-end traceability.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Timestamp at which the issuance request is made.
    /// </summary>
    public DateTimeOffset RequestedAt { get; init; }
}

/// <summary>
/// DB-authoritative result returned after issuing an exit authorization.
/// </summary>
/// <param name="ExitAuthorizationId">Canonical identifier of the issued exit authorization.</param>
/// <param name="ParkingSessionId">Canonical parking session identifier bound to the authorization.</param>
/// <param name="PaymentAttemptId">Confirmed payment attempt backing the authorization.</param>
/// <param name="AuthorizationToken">Single-use authorization token minted by the DB routine.</param>
/// <param name="AuthorizationStatus">Authorization lifecycle status after issuance.</param>
/// <param name="IssuedAt">Timestamp at which the authorization was issued.</param>
/// <param name="ExpirationTimestamp">Timestamp at which the authorization expires.</param>
public sealed record IssueExitAuthorizationDbResult(
    Guid ExitAuthorizationId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    string AuthorizationToken,
    string AuthorizationStatus,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpirationTimestamp);
