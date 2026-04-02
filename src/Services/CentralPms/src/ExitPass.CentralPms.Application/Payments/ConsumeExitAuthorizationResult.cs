namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Represents the canonical result of a successful exit-authorization consumption.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 8.5 ExitAuthorization State Machine
///
/// Invariants Enforced:
/// - Consumption result is DB-authoritative
/// - A consumed authorization must expose its consumed timestamp
/// </summary>
public sealed record ConsumeExitAuthorizationResult(
    Guid ExitAuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset ConsumedAt);
