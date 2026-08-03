using ExitPass.CentralPms.Contracts.TerminalCashPayments;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

public static class AptStatutoryOrdinanceAvailabilityValues
{
    public const string PolicyName = "AptStatutoryOrdinanceAvailabilityRead";
    public const string Permission = "statutory-discounts.ordinance-availability.read.apt";

    public const string Available = "AVAILABLE";
    public const string NotAvailable = "NOT_AVAILABLE";
    public const string NoConfiguredPolicy = "NO_CONFIGURED_POLICY";
    public const string NotYetEffective = "NOT_YET_EFFECTIVE";
    public const string Expired = "EXPIRED";
    public const string Inactive = "INACTIVE";
    public const string AmbiguousScope = "AMBIGUOUS_SCOPE";
    public const string SessionNotFound = "SESSION_NOT_FOUND";
    public const string AmbiguousSession = "AMBIGUOUS_SESSION";
    public const string SourceUnavailable = "SOURCE_UNAVAILABLE";
    public const string MalformedAuthoritativeState = "MALFORMED_AUTHORITATIVE_STATE";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string UnexpectedFailure = "UNEXPECTED_FAILURE";

    public const string PassedUnchanged = "PASSED_UNCHANGED";
    public const string Failed = "FAILED";
}

public sealed record AptStatutoryOrdinanceAvailabilityResult(
    bool Succeeded,
    int HttpStatusCode,
    Guid CorrelationId,
    AptStatutoryOrdinanceAvailabilityResponse? Response,
    string? ErrorCode,
    string? Message,
    bool Retryable);

public interface IAptStatutoryOrdinanceAvailabilityService
{
    Task<AptStatutoryOrdinanceAvailabilityResult> ResolveAsync(
        AptStatutoryOrdinanceAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<AptStatutoryOrdinanceAvailabilityResult> RevalidateAsync(
        AptStatutoryOrdinanceAvailabilityRequest request,
        CancellationToken cancellationToken);
}
