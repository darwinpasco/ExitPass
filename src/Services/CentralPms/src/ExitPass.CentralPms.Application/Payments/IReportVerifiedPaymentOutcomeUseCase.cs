namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Application use case for reporting a verified provider outcome into Central PMS.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Verified provider outcomes are handled through a single Central PMS application use case
/// - Only Central PMS may finalize PaymentAttempt state
/// </summary>
public interface IReportVerifiedPaymentOutcomeUseCase
{
    /// <summary>
    /// Records verified payment evidence, finalizes the payment attempt,
    /// and issues an exit authorization when the final status is confirmed.
    /// </summary>
    /// <param name="command">Verified payment outcome command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authoritative workflow result.</returns>
    Task<ReportVerifiedPaymentOutcomeResult> ExecuteAsync(
        ReportVerifiedPaymentOutcomeCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// Command for reporting a verified provider outcome into Central PMS.
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
/// - Central PMS receives canonical verified outcome input from the Payment Orchestrator
/// - Exit authorization issuance receives an explicit typed actor identifier
/// </summary>
/// <param name="PaymentAttemptId">Payment attempt identifier.</param>
/// <param name="ParkingSessionId">Parking session identifier.</param>
/// <param name="ProviderReference">Provider-side unique reference.</param>
/// <param name="ProviderStatus">Canonical provider outcome status.</param>
/// <param name="FinalAttemptStatus">Terminal Central PMS payment attempt status to apply.</param>
/// <param name="RequestedBy">Logical actor label for audit tracing.</param>
/// <param name="RequestedByUserId">Typed actor identifier used by exit authorization issuance.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record ReportVerifiedPaymentOutcomeCommand(
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    string ProviderReference,
    string ProviderStatus,
    string FinalAttemptStatus,
    string RequestedBy,
    Guid RequestedByUserId,
    Guid CorrelationId);

/// <summary>
/// Result of reporting a verified provider outcome into Central PMS.
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
/// - Payment confirmation evidence remains distinct from PaymentAttempt state
/// - Exit authorization details are returned only when issuance actually occurred
/// </summary>
/// <param name="PaymentConfirmationId">Recorded payment confirmation identifier.</param>
/// <param name="PaymentAttemptId">Payment attempt identifier.</param>
/// <param name="AttemptStatus">Final payment attempt status after processing.</param>
/// <param name="ExitAuthorizationId">Issued exit authorization identifier, when applicable.</param>
/// <param name="AuthorizationToken">Issued authorization token, when applicable.</param>
/// <param name="AuthorizationStatus">Issued authorization status, when applicable.</param>
/// <param name="VerifiedTimestamp">Timestamp when payment confirmation evidence was recorded.</param>
/// <param name="IssuedAt">Authorization issued timestamp, when applicable.</param>
/// <param name="ExpirationTimestamp">Authorization expiration timestamp, when applicable.</param>
public sealed record ReportVerifiedPaymentOutcomeResult(
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    string AttemptStatus,
    Guid? ExitAuthorizationId,
    string? AuthorizationToken,
    string? AuthorizationStatus,
    DateTimeOffset VerifiedTimestamp,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? ExpirationTimestamp);
