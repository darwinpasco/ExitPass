namespace ExitPass.CentralPms.Contracts.Internal;

/// <summary>
/// Response payload returned after Central PMS records verified payment evidence,
/// finalizes the payment attempt, and optionally issues an exit authorization.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Payment confirmation evidence remains distinct from payment attempt state
/// - Exit authorization details are returned only when issuance actually occurred
/// </summary>
/// <param name="PaymentConfirmationId">Recorded payment confirmation identifier.</param>
/// <param name="PaymentAttemptId">Payment attempt identifier.</param>
/// <param name="AttemptStatus">Final payment attempt status.</param>
/// <param name="ExitAuthorizationId">Issued exit authorization identifier, when applicable.</param>
/// <param name="AuthorizationToken">Issued authorization token, when applicable.</param>
/// <param name="AuthorizationStatus">Issued authorization status, when applicable.</param>
/// <param name="VerifiedTimestamp">Timestamp when payment confirmation evidence was recorded.</param>
/// <param name="IssuedAt">Authorization issued timestamp, when applicable.</param>
/// <param name="ExpirationTimestamp">Authorization expiration timestamp, when applicable.</param>
public sealed record ReportVerifiedPaymentOutcomeResponse(
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    string AttemptStatus,
    Guid? ExitAuthorizationId,
    string? AuthorizationToken,
    string? AuthorizationStatus,
    DateTimeOffset VerifiedTimestamp,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? ExpirationTimestamp);
