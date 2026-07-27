using ExitPass.CentralPms.Contracts.TerminalCashPayments;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

public interface IAptPayableBasisReadinessService
{
    Task<AptPayableBasisReadinessResult> ResolveAsync(
        AptPayableBasisResolveRequest request,
        CancellationToken cancellationToken);

    Task<AptPayableBasisReadinessResult> RevalidateAsync(
        AptPayableBasisRevalidateRequest request,
        CancellationToken cancellationToken);
}

public sealed record AptPayableBasisReadinessResult(
    bool Succeeded,
    AptPayableBasisReadinessResponse? Response,
    string? ErrorCode,
    string? Message,
    int HttpStatusCode,
    bool Retryable,
    Guid CorrelationId);

public interface ITerminalCashPayableBasisEligibilityReader
{
    Task<TerminalCashPayableBasisEligibility> EvaluateAsync(
        TerminalCashPayableBasisEligibilityRequest request,
        CancellationToken cancellationToken);
}

public sealed record TerminalCashPayableBasisEligibilityRequest(
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    Guid SiteGroupId,
    Guid SiteId,
    string TerminalId,
    long ExpectedAmountMinorUnits,
    string ExpectedCurrency,
    DateTimeOffset RequestedAt);

public sealed record TerminalCashPayableBasisEligibility(
    bool Ready,
    string Status,
    string? BlockingReasonCode,
    bool Retryable,
    string Message);

public static class AptPayableBasisReadinessStatuses
{
    public const string Ready = "READY";
    public const string Pending = "PENDING";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class AptPayableBasisRevalidationOutcomes
{
    public const string PassedUnchanged = "PASSED_UNCHANGED";
    public const string AmountChanged = "AMOUNT_CHANGED";
    public const string TariffExpired = "TARIFF_EXPIRED";
    public const string SessionNotPayable = "SESSION_NOT_PAYABLE";
    public const string SessionAlreadyPaid = "SESSION_ALREADY_PAID";
    public const string TerminalCashUnavailable = "TERMINAL_CASH_UNAVAILABLE";
    public const string FiscalReadinessFailed = "FISCAL_READINESS_FAILED";
    public const string RevalidationFailed = "REVALIDATION_FAILED";
}
