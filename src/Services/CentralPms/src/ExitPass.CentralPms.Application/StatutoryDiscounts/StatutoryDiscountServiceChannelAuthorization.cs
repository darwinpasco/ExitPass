using ExitPass.CentralPms.Application.Security;

namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Authenticated, endpoint-derived service caller facts. This context is not part of any browser contract.
/// </summary>
public sealed record StatutoryDiscountServiceChannelCallerContext(
    Guid ServiceIdentityId,
    string SourceChannel,
    string ApplicationAudience,
    string PermissionCode);

public sealed record StatutoryDiscountServiceChannelAuthorizationResult(
    bool Allowed,
    string Decision,
    IReadOnlyList<string> DenialReasons,
    string? ErrorCode,
    bool AuditPersisted,
    Guid CorrelationId);

public interface IStatutoryDiscountServiceChannelAuthorizationService
{
    Task<StatutoryDiscountServiceChannelAuthorizationResult> AuthorizeAsync(
        StatutoryDiscountServiceChannelCallerContext caller,
        Guid? siteId,
        Guid correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Authorizes deferred statutory application for authenticated WebPay and APT service principals.
/// Operator Console device and shift readiness is intentionally outside this service-channel policy.
/// </summary>
public sealed class StatutoryDiscountServiceChannelAuthorizationService
    : IStatutoryDiscountServiceChannelAuthorizationService
{
    private static readonly IReadOnlyDictionary<string, string> RequiredPermissions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StatutoryDiscountSourceChannels.WebPay] = "statutory-discounts.decision.submit.webpay",
            [StatutoryDiscountSourceChannels.AssistedPaymentTerminal] =
                "statutory-discounts.decision.submit.assisted-payment-terminal"
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> CompatibleAudiences =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [StatutoryDiscountSourceChannels.WebPay] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "WEBPAY",
                "PAYMENT_ORCHESTRATOR"
            },
            [StatutoryDiscountSourceChannels.AssistedPaymentTerminal] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "APT",
                "ASSISTED_PAYMENT_TERMINAL"
            }
        };

    private readonly ICentralPmsRbacRepository _repository;

    public StatutoryDiscountServiceChannelAuthorizationService(ICentralPmsRbacRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<StatutoryDiscountServiceChannelAuthorizationResult> AuthorizeAsync(
        StatutoryDiscountServiceChannelCallerContext caller,
        Guid? siteId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var sourceChannel = StatutoryDiscountSourceChannels.Normalize(caller.SourceChannel);
        if (caller.ServiceIdentityId == Guid.Empty || !siteId.HasValue || siteId == Guid.Empty ||
            !RequiredPermissions.TryGetValue(sourceChannel, out var requiredPermission))
        {
            return await DenyAsync(caller.ServiceIdentityId, siteId, correlationId,
                "SERVICE_CHANNEL_CONTEXT_INVALID", "ACCESS_DENIED", cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(caller.ApplicationAudience, sourceChannel, StringComparison.Ordinal) ||
            !string.Equals(caller.PermissionCode, requiredPermission, StringComparison.Ordinal))
        {
            return await DenyAsync(caller.ServiceIdentityId, siteId, correlationId,
                "SERVICE_CHANNEL_AUDIENCE_OR_PERMISSION_MISMATCH", "ACCESS_DENIED", cancellationToken).ConfigureAwait(false);
        }

        var identity = await _repository.GetServiceIdentityAuthorizationAsync(
                caller.ServiceIdentityId,
                siteId.Value,
                cancellationToken)
            .ConfigureAwait(false);

        if (identity is null || !identity.Active)
        {
            return await DenyAsync(caller.ServiceIdentityId, siteId, correlationId,
                "SERVICE_IDENTITY_INACTIVE", "ACCESS_DENIED", cancellationToken).ConfigureAwait(false);
        }

        if (identity.IdentityType is not ("INTERNAL_SERVICE" or "ADAPTER" or "DEVICE" or "GATEWAY") ||
            !CompatibleAudiences[sourceChannel].Contains(identity.OwningServiceName))
        {
            return await DenyAsync(caller.ServiceIdentityId, siteId, correlationId,
                "SERVICE_CHANNEL_AUDIENCE_MISMATCH", "ACCESS_DENIED", cancellationToken).ConfigureAwait(false);
        }

        if (!identity.SiteAssigned)
        {
            return await DenyAsync(caller.ServiceIdentityId, siteId, correlationId,
                "SERVICE_IDENTITY_SITE_SCOPE_DENIED", "STATUTORY_DISCOUNT_DECISION_NOT_FOUND", cancellationToken)
                .ConfigureAwait(false);
        }

        var auditPersisted = await RecordAsync(
            "StatutoryDiscountServiceChannelApplicationAuthorized",
            "SUCCESS",
            "SERVICE_CHANNEL_AUTHORIZED",
            caller.ServiceIdentityId,
            siteId,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        return new StatutoryDiscountServiceChannelAuthorizationResult(
            Allowed: true,
            Decision: "SERVICE_CHANNEL_ALLOW",
            DenialReasons: [],
            ErrorCode: null,
            auditPersisted,
            correlationId);
    }

    private async Task<StatutoryDiscountServiceChannelAuthorizationResult> DenyAsync(
        Guid serviceIdentityId,
        Guid? siteId,
        Guid correlationId,
        string reason,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var auditPersisted = await RecordAsync(
            "StatutoryDiscountServiceChannelApplicationDenied",
            "DENIED",
            reason,
            serviceIdentityId == Guid.Empty ? null : serviceIdentityId,
            siteId,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        return new StatutoryDiscountServiceChannelAuthorizationResult(
            Allowed: false,
            Decision: "SERVICE_CHANNEL_DENY",
            DenialReasons: [reason],
            errorCode,
            auditPersisted,
            correlationId);
    }

    private async Task<bool> RecordAsync(
        string eventType,
        string result,
        string reason,
        Guid? serviceIdentityId,
        Guid? siteId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.RecordAuditEventAsync(
                eventType,
                result,
                reason,
                "Site",
                siteId,
                actorUserId: null,
                serviceIdentityId,
                correlationId,
                "Central PMS evaluated service-channel statutory payable-basis application authority.",
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
