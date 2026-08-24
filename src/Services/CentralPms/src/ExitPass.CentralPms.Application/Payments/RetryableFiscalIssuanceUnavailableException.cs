namespace ExitPass.CentralPms.Application.Payments;

public sealed class RetryableFiscalIssuanceUnavailableException : Exception
{
    public const string StableErrorCode = "FISCAL_ISSUANCE_TEMPORARILY_UNAVAILABLE";
    public const string SafeMessage = "Fiscal issuance is temporarily unavailable. Retry the original request.";

    public RetryableFiscalIssuanceUnavailableException(Guid fiscalIssuanceReferenceId)
        : base(SafeMessage)
    {
        FiscalIssuanceReferenceId = fiscalIssuanceReferenceId;
    }

    public Guid FiscalIssuanceReferenceId { get; }
}
