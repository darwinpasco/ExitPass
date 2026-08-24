namespace ExitPass.CentralPms.Application.Payments;

public interface IDigitalPaymentFiscalRecoveryContextReader
{
    Task<DigitalPaymentFiscalRecoveryContext?> FindByPaymentAttemptIdAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken);
}

public sealed record DigitalPaymentFiscalRecoveryContext(
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    string AttemptStatus,
    Guid PaymentConfirmationId,
    string ProviderReference,
    string ConfirmationStatus,
    DateTimeOffset VerifiedTimestamp,
    Guid FiscalIssuanceReferenceId,
    string FiscalIssuanceState,
    string? LatestErrorPosture,
    bool HasCompleteFiscalEvidence)
{
    public bool PermitsServiceRecovery =>
        string.Equals(FiscalIssuanceState, "FISCAL_ISSUANCE_FAILED_SERVICE", StringComparison.Ordinal) &&
        string.Equals(LatestErrorPosture, "RETRY_AFTER_SERVICE_RECOVERY", StringComparison.Ordinal);

    public bool IsCompleted =>
        FiscalIssuanceState is "FISCAL_ISSUANCE_RECORDED" or "FISCAL_ISSUANCE_REPLAYED" or "FISCAL_ISSUANCE_RECONCILED" &&
        HasCompleteFiscalEvidence;
}
