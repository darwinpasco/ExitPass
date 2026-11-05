namespace ExitPass.CentralPms.Application.FiscalReporting;

public static class FiscalBusinessDateAdmissionCodes
{
    public const string NotOpen = "FISCAL_BUSINESS_DATE_NOT_OPEN";
    public const string Unavailable = "FISCAL_BUSINESS_DATE_READINESS_UNAVAILABLE";
    public const string UnsafeToClose = "FISCAL_BUSINESS_DATE_UNRESOLVED_PAYMENTS";
}

public sealed class FiscalBusinessDateAdmissionException(
    string code,
    string message,
    bool retryable = false) : Exception(message)
{
    public string Code { get; } = code;

    public bool Retryable { get; } = retryable;
}

public interface IFiscalBusinessDateAdmissionLease : IAsyncDisposable
{
    Task EnsureSafeToCloseAsync(CancellationToken cancellationToken);
}

public interface IFiscalBusinessDateAdmissionGate
{
    Task<IFiscalBusinessDateAdmissionLease> AcquireOpenAsync(
        Guid siteId,
        Guid correlationId,
        string actorReference,
        CancellationToken cancellationToken);

    Task<IFiscalBusinessDateAdmissionLease> AcquireTransitionAsync(
        Guid siteId,
        CancellationToken cancellationToken);

    Task<IFiscalBusinessDateAdmissionLease> AcquirePaymentSettlementAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken);
}
