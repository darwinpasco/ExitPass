namespace ExitPass.CentralPms.Domain.FiscalIssuance;

public enum FiscalIssuanceIntegrationState
{
    NotRequired = 0,
    PendingFiscalIssuance = 1,
    FiscalIssuanceRequested = 2,
    FiscalIssuanceRecorded = 3,
    FiscalIssuanceReplayed = 4,
    FiscalIssuanceConflict = 5,
    FiscalIssuanceFailedRequest = 6,
    FiscalIssuanceFailedConfiguration = 7,
    FiscalIssuanceFailedService = 8,
    FiscalIssuanceUnknown = 9,
    FiscalIssuanceManualReview = 10,
    FiscalIssuanceExceptionReleased = 11,
    FiscalIssuanceReconciled = 12
}
