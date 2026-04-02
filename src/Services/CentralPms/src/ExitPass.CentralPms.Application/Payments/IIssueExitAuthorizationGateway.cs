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
    Task<IssueExitAuthorizationDbResult> IssueAsync(
        IssueExitAuthorizationDbRequest request,
        CancellationToken cancellationToken);
}

public sealed record IssueExitAuthorizationDbRequest
{
    public Guid ParkingSessionId { get; init; }
    public Guid PaymentAttemptId { get; init; }
    public Guid RequestedByUserId { get; init; }
    public Guid CorrelationId { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
}

public sealed record IssueExitAuthorizationDbResult(
    Guid ExitAuthorizationId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    string AuthorizationToken,
    string AuthorizationStatus,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpirationTimestamp);
