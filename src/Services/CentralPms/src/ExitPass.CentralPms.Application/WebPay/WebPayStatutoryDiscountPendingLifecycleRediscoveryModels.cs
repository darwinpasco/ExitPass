namespace ExitPass.CentralPms.Application.WebPay;

/// <summary>
/// WebPay statutory pending-lifecycle rediscovery contract values.
/// </summary>
public static class WebPayStatutoryDiscountPendingLifecycleRediscoveryValues
{
    public const string PolicyName = "WebPayStatutoryDiscountPendingLifecycleRediscover";
    public const string Permission = "statutory-discounts.pending-lifecycle.rediscover.webpay";

    public const string LookupModeParkingSessionId = "PARKING_SESSION_ID";
    public const string LookupModeTicketReference = "TICKET_REFERENCE";
    public const string LookupModePlateNumber = "PLATE_NUMBER";

    public const string Found = "FOUND";
    public const string NotFound = "NOT_FOUND";
    public const string NoActiveLifecycle = "NO_ACTIVE_LIFECYCLE";
    public const string AmbiguousSession = "AMBIGUOUS_SESSION";
    public const string SourceUnavailable = "SOURCE_UNAVAILABLE";
    public const string MalformedAuthoritativeState = "MALFORMED_AUTHORITATIVE_STATE";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string UnexpectedFailure = "UNEXPECTED_FAILURE";
}

public sealed record WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery(
    string LookupMode,
    Guid? ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    string? VendorSystemId,
    string? EntitlementType,
    Guid CorrelationId);

public sealed record WebPayStatutoryDiscountPendingLifecycleSession(
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string? TicketReference,
    string? PlateNumber,
    DateTimeOffset UpdatedAt);

public sealed record WebPayStatutoryDiscountPendingLifecycleSessionLookupResult(
    string Classification,
    WebPayStatutoryDiscountPendingLifecycleSession? Session,
    bool Retryable = false);

public sealed record WebPayStatutoryDiscountPendingLifecycleRecord(
    Guid StatutoryDecisionId,
    Guid StatutoryDecisionCommandId,
    Guid RequestReference,
    string EntitlementType,
    string DecisionStatus,
    string PayableBasisStatus,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string OpaqueContinuationReference,
    string? OpaqueContinuationUrl,
    string LifecycleState,
    bool Retryable,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ReviewedAt);

public sealed record WebPayStatutoryDiscountPendingLifecycleRediscoveryResult(
    string Classification,
    WebPayStatutoryDiscountPendingLifecycleRecord? Lifecycle,
    bool Retryable,
    Guid CorrelationId,
    string SafeMessage)
{
    public static WebPayStatutoryDiscountPendingLifecycleRediscoveryResult Found(
        WebPayStatutoryDiscountPendingLifecycleRecord lifecycle,
        Guid correlationId) =>
        new(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found, lifecycle, lifecycle.Retryable, correlationId, "Statutory parking privilege request found.");

    public static WebPayStatutoryDiscountPendingLifecycleRediscoveryResult NotFound(
        string classification,
        Guid correlationId,
        string safeMessage,
        bool retryable = false) =>
        new(classification, null, retryable, correlationId, safeMessage);
}

public interface IWebPayStatutoryDiscountPendingLifecycleRediscoveryService
{
    Task<WebPayStatutoryDiscountPendingLifecycleRediscoveryResult> RediscoverAsync(
        WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery query,
        CancellationToken cancellationToken);
}

public interface IWebPayStatutoryDiscountPendingLifecycleRediscoveryRepository
{
    Task<WebPayStatutoryDiscountPendingLifecycleSessionLookupResult> ResolveSessionAsync(
        WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery query,
        CancellationToken cancellationToken);

    Task<WebPayStatutoryDiscountPendingLifecycleRecord?> FindLatestLifecycleAsync(
        Guid parkingSessionId,
        Guid siteId,
        Guid siteGroupId,
        string? entitlementType,
        CancellationToken cancellationToken);
}

public sealed class WebPayStatutoryDiscountPendingLifecycleRediscoveryRejectedException : Exception
{
    public WebPayStatutoryDiscountPendingLifecycleRediscoveryRejectedException(
        string errorCode,
        string message,
        Guid correlationId)
        : base(message)
    {
        ErrorCode = errorCode;
        CorrelationId = correlationId;
    }

    public string ErrorCode { get; }
    public Guid CorrelationId { get; }
}
