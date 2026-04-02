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
    Task<ConsumeExitAuthorizationDbResult> ConsumeAsync(
        ConsumeExitAuthorizationDbRequest request,
        CancellationToken cancellationToken);
}

public sealed record ConsumeExitAuthorizationDbRequest
{
    public Guid ExitAuthorizationId { get; init; }
    public Guid RequestedByUserId { get; init; }
    public Guid CorrelationId { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
}

public sealed record ConsumeExitAuthorizationDbResult(
    Guid ExitAuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset ConsumedAt);
