namespace ExitPass.CentralPms.Domain.FiscalIssuance;

public enum FiscalIssuanceErrorPosture
{
    DoNotRetryWithoutRequestChange = 1,
    RetryAfterConfigurationCorrection = 2,
    RetryAfterServiceRecovery = 3
}
