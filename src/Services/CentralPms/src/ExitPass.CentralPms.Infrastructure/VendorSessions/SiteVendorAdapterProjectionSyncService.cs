using System.Net.Http.Json;
using ExitPass.CentralPms.Application.Auditing;
using ExitPass.CentralPms.Application.VendorParking.Routing;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Infrastructure.Auditing;
using ExitPass.VendorPmsAdapter.Contracts.Projection;
using ExitPass.VendorPmsAdapter.Contracts.Routing;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Infrastructure.VendorSessions;

/// <summary>Synchronizes provider-neutral passageway projections through the Site's bound adapter.</summary>
public sealed class SiteVendorAdapterProjectionSyncService(
    HttpClient httpClient,
    ISiteVendorAdapterRouteRegistry routes,
    ISiteAdapterCredentialResolver credentials,
    IVendorSessionProjectionRepository repository,
    ISystemClock clock,
    ILogger<SiteVendorAdapterProjectionSyncService> logger,
    Guid centralPmsServiceIdentityId,
    bool allowTaskOwnedHttp,
    IAuditEventPublisher auditEvents) : IVendorSessionProjectionSyncService
{
    public async Task<SyncVendorSessionProjectionsResult> SyncAsync(
        SyncVendorSessionProjectionsCommand command, CancellationToken cancellationToken)
    {
        if (!command.SiteId.HasValue || !command.SiteGroupId.HasValue || command.PageSize is < 1 or > 500 ||
            command.MaxPages < 1 || string.IsNullOrWhiteSpace(command.ParkingLotIndexCode))
            throw new VendorSessionProjectionException("SITE_ADAPTER_PROJECTION_SCOPE_INVALID", false);
        SiteVendorAdapterRoute route;
        try
        {
            route = await routes.ResolveAsync(command.SiteId.Value, command.SiteGroupId.Value,
                command.VendorSystemId, cancellationToken);
        }
        catch (SiteVendorAdapterRoutingException ex)
        {
            throw new VendorSessionProjectionException(ex.ErrorCode, false);
        }
        if (route.AdapterBaseUri.Scheme != Uri.UriSchemeHttps && !allowTaskOwnedHttp)
            throw new VendorSessionProjectionException("SITE_ADAPTER_PRIVATE_TLS_REQUIRED", false);
        var context = new VendorAdapterRequestContext(route.SiteId, route.SiteGroupId, route.VendorSystemId,
            route.AdapterIdentityId);
        var request = new VendorPassagewaySyncRequest(context, command.ParkingLotIndexCode, command.BeginTime,
            command.EndTime, command.PageSize, command.MaxPages, command.CorrelationId);
        using var message = new HttpRequestMessage(HttpMethod.Post,
            new Uri(route.AdapterBaseUri, "/v1/vendor/passageway-records/synchronize"))
        { Content = JsonContent.Create(request) };
        message.Headers.TryAddWithoutValidation("X-ExitPass-Service-Identity", centralPmsServiceIdentityId.ToString());
        message.Headers.TryAddWithoutValidation("X-ExitPass-Adapter-Key", credentials.Resolve(route.CredentialReference));
        VendorPassagewaySyncResponse response;
        try
        {
            using var httpResponse = await httpClient.SendAsync(message, cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
                throw new VendorSessionProjectionException("SITE_ADAPTER_UNAVAILABLE", true);
            response = await httpResponse.Content.ReadFromJsonAsync<VendorPassagewaySyncResponse>(
                cancellationToken: cancellationToken)
                ?? throw new VendorSessionProjectionException("SITE_ADAPTER_MALFORMED_RESPONSE", true);
        }
        catch (OperationCanceledException) { throw; }
        catch (VendorSessionProjectionException) { throw; }
        catch (Exception) { throw new VendorSessionProjectionException("SITE_ADAPTER_UNAVAILABLE", true); }

        if (!response.Succeeded)
            throw new VendorSessionProjectionException(response.ErrorCode ?? "SITE_ADAPTER_FAILURE", response.Retryable);
        if (!Matches(response.AdapterContext, route) || response.CorrelationId != command.CorrelationId)
            throw new VendorSessionProjectionException("SITE_ADAPTER_RESPONSE_BINDING_MISMATCH", false);
        var observedAt = clock.UtcNow;
        var projections = response.Records.Select(record => Map(record, route, command.CorrelationId, observedAt)).ToArray();
        await auditEvents.AppendAsync(new ApplicationAuditEvent(
            ProjectionAuditIdentity.For(route.SiteId, command.CorrelationId),
            "VENDOR_SESSION_PROJECTION_BATCH_RECEIVED", "INTEGRATION", "SUCCESS", null,
            route.SiteId, null, "CENTRAL_PMS_VENDOR_SESSION_PROJECTION",
            $"Validated provider-neutral projection batch with {projections.Length} usable records.",
            observedAt, command.CorrelationId, null), cancellationToken);
        try { await repository.UpsertBatchAsync(projections, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new VendorSessionProjectionException("PROJECTION_PERSISTENCE_FAILURE", true); }
        logger.LogInformation("Site Adapter projection completed. site_id={SiteId} vendor_system_id={VendorSystemId} adapter_identity_id={AdapterIdentityId} records={Records} correlation_id={CorrelationId}",
            route.SiteId, route.VendorSystemId, route.AdapterIdentityId, projections.Length, command.CorrelationId);
        return new(response.PagesPulled, response.RecordsSeen, projections.Length, response.RecordsSkipped,
            command.CorrelationId);
    }

    private static bool Matches(VendorAdapterResponseContext? context, SiteVendorAdapterRoute route) =>
        context is not null && context.SiteId == route.SiteId && context.SiteGroupId == route.SiteGroupId &&
        context.VendorSystemId == route.VendorSystemId && context.AdapterIdentityId == route.AdapterIdentityId;

    private static VendorSessionProjection Map(VendorPassagewayRecordDto record, SiteVendorAdapterRoute route,
        Guid correlationId, DateTimeOffset observedAt)
    {
        var identityType = "VENDOR_RECORD_GUID";
        var identityKey = $"{route.VendorSystemId:N}|GUID|{record.VendorRecordReference.ToUpperInvariant()}";
        return new VendorSessionProjection(Guid.NewGuid(), route.VendorSystemId, route.SiteId, route.SiteGroupId,
            record.ParkingLotReference, record.ParkingLotName, record.PassagewayReference, record.PassagewayName,
            record.LaneReference, record.LaneName, record.Direction, record.VendorRecordReference,
            record.CardReference, record.PlateNumber, record.EntryTime, record.ExitTime, record.AllowType,
            record.AllowResult, null, record.SourceApi, record.SourcePayloadHash,
            $"vendor:passageway-record:{record.VendorRecordReference}", record.SourceTimestamp,
            identityType, identityKey, observedAt, observedAt, observedAt,
            record.ExitTime.HasValue ? VendorSessionProjectionStatus.Exited : VendorSessionProjectionStatus.Active,
            correlationId, observedAt, observedAt) { SourceAdapterIdentityId = route.AdapterIdentityId };
    }
}
