namespace ExitPass.CentralPms.Application.WebPay;

/// <summary>
/// Read-only WebPay recovery service for existing statutory-discount pending lifecycles.
/// </summary>
public sealed class WebPayStatutoryDiscountPendingLifecycleRediscoveryService
    : IWebPayStatutoryDiscountPendingLifecycleRediscoveryService
{
    private static readonly string[] SupportedEntitlementTypes =
    [
        "SENIOR_CITIZEN",
        "PWD"
    ];

    private readonly IWebPayStatutoryDiscountPendingLifecycleRediscoveryRepository _repository;

    public WebPayStatutoryDiscountPendingLifecycleRediscoveryService(
        IWebPayStatutoryDiscountPendingLifecycleRediscoveryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<WebPayStatutoryDiscountPendingLifecycleRediscoveryResult> RediscoverAsync(
        WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query);

        try
        {
            var session = await _repository.ResolveSessionAsync(query, cancellationToken).ConfigureAwait(false);
            if (session.Classification is not WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.Found)
            {
                return WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.NotFound(
                    session.Classification,
                    query.CorrelationId,
                    SafeMessageFor(session.Classification),
                    session.Retryable);
            }

            if (session.Session is null)
            {
                return WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.NotFound(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.MalformedAuthoritativeState,
                    query.CorrelationId,
                    SafeMessageFor(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.MalformedAuthoritativeState));
            }

            var lifecycle = await _repository.FindLatestLifecycleAsync(
                    session.Session.ParkingSessionId,
                    query.SiteId,
                    query.SiteGroupId,
                    NormalizeOptional(query.EntitlementType),
                    cancellationToken)
                .ConfigureAwait(false);

            if (lifecycle is null)
            {
                return WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.NotFound(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NoActiveLifecycle,
                    query.CorrelationId,
                    SafeMessageFor(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NoActiveLifecycle));
            }

            if (lifecycle.DecisionStatus is WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable)
            {
                return WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.NotFound(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable,
                    query.CorrelationId,
                    SafeMessageFor(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable),
                    retryable: true);
            }

            if (!IsAuthoritativeStateComplete(lifecycle))
            {
                return WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.NotFound(
                    WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.MalformedAuthoritativeState,
                    query.CorrelationId,
                    SafeMessageFor(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.MalformedAuthoritativeState));
            }

            return WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.Found(lifecycle, query.CorrelationId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return WebPayStatutoryDiscountPendingLifecycleRediscoveryResult.NotFound(
                WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.UnexpectedFailure,
                query.CorrelationId,
                SafeMessageFor(WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.UnexpectedFailure));
        }
    }

    private static void Validate(WebPayStatutoryDiscountPendingLifecycleRediscoveryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.CorrelationId == Guid.Empty)
        {
            throw Rejected("INVALID_REQUEST", "A valid correlation ID is required.", query.CorrelationId);
        }

        if (query.SiteId == Guid.Empty || query.SiteGroupId == Guid.Empty)
        {
            throw Rejected("INVALID_REQUEST", "Site and Site Group scope are required.", query.CorrelationId);
        }

        var lookupMode = Normalize(query.LookupMode);
        if (lookupMode is not WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId
            and not WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeTicketReference
            and not WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModePlateNumber)
        {
            throw Rejected("INVALID_REQUEST", "A supported lookup mode is required.", query.CorrelationId);
        }

        var suppliedContexts = 0;
        if (query.ParkingSessionId.HasValue && query.ParkingSessionId.Value != Guid.Empty)
        {
            suppliedContexts++;
        }

        if (!string.IsNullOrWhiteSpace(query.TicketReference))
        {
            suppliedContexts++;
        }

        if (!string.IsNullOrWhiteSpace(query.PlateNumber))
        {
            suppliedContexts++;
        }

        if (suppliedContexts != 1)
        {
            throw Rejected("INVALID_REQUEST", "Exactly one lookup context is required.", query.CorrelationId);
        }

        if (lookupMode is WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeParkingSessionId
            && (!query.ParkingSessionId.HasValue || query.ParkingSessionId.Value == Guid.Empty))
        {
            throw Rejected("INVALID_REQUEST", "Parking session ID lookup requires a valid parking session ID.", query.CorrelationId);
        }

        if (lookupMode is WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModeTicketReference
            && string.IsNullOrWhiteSpace(query.TicketReference))
        {
            throw Rejected("INVALID_REQUEST", "Ticket lookup requires a ticket reference.", query.CorrelationId);
        }

        if (lookupMode is WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.LookupModePlateNumber
            && string.IsNullOrWhiteSpace(query.PlateNumber))
        {
            throw Rejected("INVALID_REQUEST", "Plate lookup requires a plate reference.", query.CorrelationId);
        }

        var entitlementType = NormalizeOptional(query.EntitlementType);
        if (entitlementType is not null && !SupportedEntitlementTypes.Contains(entitlementType, StringComparer.Ordinal))
        {
            throw Rejected("INVALID_REQUEST", "The requested entitlement type is not supported for statutory parking recovery.", query.CorrelationId);
        }
    }

    private static bool IsAuthoritativeStateComplete(WebPayStatutoryDiscountPendingLifecycleRecord lifecycle) =>
        lifecycle.StatutoryDecisionId != Guid.Empty
        && lifecycle.StatutoryDecisionCommandId != Guid.Empty
        && lifecycle.RequestReference != Guid.Empty
        && lifecycle.ParkingSessionId != Guid.Empty
        && lifecycle.SiteId != Guid.Empty
        && lifecycle.SiteGroupId != Guid.Empty
        && !string.IsNullOrWhiteSpace(lifecycle.EntitlementType)
        && !string.IsNullOrWhiteSpace(lifecycle.DecisionStatus)
        && !string.IsNullOrWhiteSpace(lifecycle.PayableBasisStatus)
        && !string.IsNullOrWhiteSpace(lifecycle.OpaqueContinuationReference)
        && !string.IsNullOrWhiteSpace(lifecycle.LifecycleState);

    private static string SafeMessageFor(string classification) =>
        classification switch
        {
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NotFound =>
                "No matching parking session was found.",
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.NoActiveLifecycle =>
                "No active statutory parking privilege request was found for this parking session.",
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AmbiguousSession =>
                "More than one matching parking session was found. Please re-check the ticket or plate details.",
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.SourceUnavailable =>
                "The parking privilege request could not be checked right now. Please try again.",
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.MalformedAuthoritativeState =>
                "The parking privilege request is not available for recovery right now. Please continue with ordinary payment or contact support.",
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.AccessDenied =>
                "The parking session is not available for this WebPay scope.",
            WebPayStatutoryDiscountPendingLifecycleRediscoveryValues.UnexpectedFailure =>
                "The parking privilege request could not be checked right now. Please try again.",
            _ => "The parking privilege request could not be recovered."
        };

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static WebPayStatutoryDiscountPendingLifecycleRediscoveryRejectedException Rejected(
        string errorCode,
        string message,
        Guid correlationId) =>
        new(errorCode, message, correlationId);
}
