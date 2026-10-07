using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using Npgsql;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class ManagementConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapManagementConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/management-platform/configuration")
            .WithTags("ManagementPlatformConfiguration")
            .RequireAuthorization();

        group.MapGet("/sites", (ManagementConfigurationService service, CancellationToken ct) => service.ListSitesAsync(ct))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteRead"));
        group.MapGet("/site-groups", (ManagementConfigurationService service, CancellationToken ct) => service.ListSiteGroupsAsync(ct))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteRead"));
        group.MapPut("/sites/{siteId:guid}", async (Guid siteId, ManagementSite body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("SITE_CONFIGURATION_CHANGED", "SITE", siteId, context, audit, ct, () => service.SaveSiteAsync(body with { SiteId = siteId }, Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteManage"));
        group.MapPost("/sites", async (ManagementSite body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("SITE_CONFIGURATION_CREATED", "SITE", null, context, audit, ct, () => service.SaveSiteAsync(body with { SiteId = Guid.Empty }, Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteManage"));

        group.MapGet("/lgus", (ManagementConfigurationService service, CancellationToken ct) => service.ListJurisdictionsAsync(ct))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementJurisdictionRead"));
        group.MapPut("/lgus/{jurisdictionId:guid}", async (Guid jurisdictionId, ManagementJurisdiction body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("JURISDICTION_CONFIGURATION_CHANGED", "JURISDICTION", jurisdictionId, context, audit, ct, () => service.SaveJurisdictionAsync(body with { JurisdictionId = jurisdictionId }, Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementJurisdictionManage"));
        group.MapPost("/lgus", async (ManagementJurisdiction body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("JURISDICTION_CONFIGURATION_CREATED", "JURISDICTION", null, context, audit, ct, () => service.SaveJurisdictionAsync(body with { JurisdictionId = Guid.Empty }, Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementJurisdictionManage"));

        group.MapGet("/statutory-discounts", (Guid? lguId, ManagementConfigurationService service, CancellationToken ct) => service.ListStatutoryPoliciesAsync(lguId, ct))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementStatutoryPolicyRead"));
        group.MapPost("/statutory-discounts", async (ManagementStatutoryPolicyDraft body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("STATUTORY_POLICY_DRAFT_CREATED", "STATUTORY_POLICY", null, context, audit, ct, () => service.CreateStatutoryPolicyDraftAsync(body, Actor(context), Correlation(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementStatutoryPolicyManage"));
        group.MapPost("/statutory-discounts/{policyId:guid}/activate", async (Guid policyId, PolicyLifecycleRequest body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("STATUTORY_POLICY_ACTIVATED", "STATUTORY_POLICY", policyId, context, audit, ct, () => service.ChangeStatutoryPolicyStatusAsync(policyId, body.ExpectedRowVersion, "ACTIVE", Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementStatutoryPolicyManage"));
        group.MapPost("/statutory-discounts/{policyId:guid}/retire", async (Guid policyId, PolicyLifecycleRequest body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("STATUTORY_POLICY_RETIRED", "STATUTORY_POLICY", policyId, context, audit, ct, () => service.ChangeStatutoryPolicyStatusAsync(policyId, body.ExpectedRowVersion, "RETIRED", Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementStatutoryPolicyManage"));

        group.MapGet("/site-tariffs", (Guid? siteId, string? vehicleType, ManagementConfigurationService service, CancellationToken ct) => service.ListTariffsAsync(siteId, vehicleType, ct))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteTariffRead"));
        group.MapPost("/site-tariffs", async (ManagementTariff body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("SITE_TARIFF_DRAFT_SAVED", "SITE_TARIFF", body.TariffId, context, audit, ct, () => service.SaveTariffDraftAsync(body, Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteTariffManage"));
        group.MapPut("/site-tariffs/{tariffId:guid}", async (Guid tariffId, ManagementTariff body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("SITE_TARIFF_DRAFT_SAVED", "SITE_TARIFF", tariffId, context, audit, ct, () => service.SaveTariffDraftAsync(body with { TariffId = tariffId }, Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteTariffManage"));
        group.MapPost("/site-tariffs/preview", (ManagementTariffPreviewRequest body, ManagementConfigurationService service, CancellationToken ct) => service.PreviewAsync(body, ct))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteTariffRead"));
        group.MapPost("/site-tariffs/{tariffId:guid}/verify", async (Guid tariffId, TariffVerifyRequest body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("SITE_TARIFF_VERIFIED", "SITE_TARIFF", tariffId, context, audit, ct, () => service.VerifyTariffAsync(tariffId, body.ExpectedRowVersion, body.EntryTimestamp, body.CalculationTimestamp, Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteTariffManage"));
        group.MapPost("/site-tariffs/{tariffId:guid}/activate", async (Guid tariffId, TariffLifecycleRequest body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("SITE_TARIFF_ACTIVATED", "SITE_TARIFF", tariffId, context, audit, ct, () => service.ActivateTariffAsync(tariffId, body.ExpectedRowVersion, Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteTariffManage"));
        group.MapPost("/site-tariffs/{tariffId:guid}/retire", async (Guid tariffId, TariffLifecycleRequest body, HttpContext context, ManagementConfigurationService service, ICentralPmsRbacRepository audit, CancellationToken ct) =>
            await ExecuteMutationAsync("SITE_TARIFF_RETIRED", "SITE_TARIFF", tariffId, context, audit, ct, () => service.RetireTariffAsync(tariffId, body.ExpectedRowVersion, Actor(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementSiteTariffManage"));
        return app;
    }

    private static async Task<IResult> ExecuteMutationAsync<T>(string eventType, string entity, Guid? id, HttpContext context, ICentralPmsRbacRepository audit, CancellationToken ct, Func<Task<T>> action)
    {
        var correlation = Correlation(context);
        Guid? actor = null;
        try
        {
            actor = Actor(context);
            var result = await action();
            await audit.RecordAuditEventAsync(eventType, "SUCCESS", "CONFIGURATION_PERSISTED", entity, id, actor, null, correlation, "Governed Management Platform configuration mutation completed.", ct);
            return Results.Ok(result);
        }
        catch (ManagementConfigurationException ex)
        {
            await audit.RecordAuditEventAsync(eventType, "REJECTED", ex.ErrorCode, entity, id, actor, null, correlation, "Governed configuration mutation was rejected.", ct);
            return Error(StatusCodes.Status409Conflict, ex.ErrorCode, correlation);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            await audit.RecordAuditEventAsync(eventType, "REJECTED", "CONFIGURATION_CONSTRAINT_REJECTED", entity, id, actor, null, correlation, "Canonical database constraints rejected the configuration mutation.", ct);
            return Error(StatusCodes.Status409Conflict, "CONFIGURATION_CONSTRAINT_REJECTED", correlation);
        }
    }

    private static Guid Actor(HttpContext context)
    {
        foreach (var name in new[] { ClaimTypes.NameIdentifier, "sub", "user_id" })
            if (Guid.TryParse(context.User.FindFirst(name)?.Value, out var id) && id != Guid.Empty) return id;
        throw new ManagementConfigurationException("MANAGEMENT_CONFIGURATION_ACTOR_REQUIRED");
    }
    private static Guid Correlation(HttpContext context) => Guid.TryParse(context.Request.Headers["X-Correlation-Id"].FirstOrDefault(), out var id) ? id : Guid.NewGuid();
    private static IResult Error(int status, string code, Guid correlation) => Results.Json(new ErrorResponse { ErrorCode = code, Message = "The requested configuration change could not be completed.", CorrelationId = correlation, Retryable = false, RecoveryClassification = code }, statusCode: status);
    public sealed record TariffLifecycleRequest(long ExpectedRowVersion);
    public sealed record TariffVerifyRequest(long ExpectedRowVersion, DateTimeOffset EntryTimestamp, DateTimeOffset CalculationTimestamp);
    public sealed record PolicyLifecycleRequest(long ExpectedRowVersion);
}
