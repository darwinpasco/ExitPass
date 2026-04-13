namespace ExitPass.CentralPms.Contracts.Internal;

/// <summary>
/// Reports a verified provider outcome from the Payment Orchestrator into Central PMS.
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
/// - Only Central PMS may finalize PaymentAttempt state
/// - Verified provider evidence must be recorded before exit authorization issuance
/// - Central PMS must not reinterpret provider-specific raw outcomes
/// </summary>
/// <param name="PaymentAttemptId">Payment attempt identifier.</param>
/// <param name="ParkingSessionId">Parking session identifier.</param>
/// <param name="ProviderReference">Provider-side unique reference.</param>
/// <param name="ProviderStatus">Canonical provider outcome status.</param>
/// <param name="FinalAttemptStatus">Terminal Central PMS payment attempt status.</param>
/// <param name="RequestedBy">Logical actor label for audit traceability.</param>
/// <param name="RequestedByUserId">Typed actor identifier used by downstream issuance logic.</param>
public sealed record ReportVerifiedPaymentOutcomeRequest(
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    string ProviderReference,
    string ProviderStatus,
    string FinalAttemptStatus,
    string RequestedBy,
    Guid RequestedByUserId);
