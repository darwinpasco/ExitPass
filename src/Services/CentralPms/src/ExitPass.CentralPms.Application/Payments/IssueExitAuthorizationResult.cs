namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Represents the canonical result of a successful exit-authorization issuance.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 8.5 ExitAuthorization State Machine
///
/// Invariants Enforced:
/// - Issued authorizations are explicit, time-bounded, and traceable
/// - The application layer returns the DB-authoritative issued state, not a guessed projection
/// </summary>
public sealed record IssueExitAuthorizationResult(
    Guid ExitAuthorizationId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    string AuthorizationToken,
    string AuthorizationStatus,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpirationTimestamp);
