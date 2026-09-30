using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.FiscalReporting;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.ShiftManagement;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class FiscalReportingEndpoints
{
    public const string EjReadPolicy="FiscalReportingElectronicJournalRead";
    public const string EjExportPolicy="FiscalReportingElectronicJournalExport";
    public const string XReadPolicy="FiscalReportingXRead";
    public const string XGeneratePolicy="FiscalReportingXGenerate";
    public const string ZReadPolicy="FiscalReportingZRead";
    public const string ZGeneratePolicy="FiscalReportingZGenerate";
    public const string SiteCatalogPolicy="FiscalReportingSiteCatalogRead";

    private static readonly string[] FiscalPermissionCodes =
    [
        "fiscal-reporting.ej.read",
        "fiscal-reporting.ej.export",
        "fiscal-reporting.x.read",
        "fiscal-reporting.x.generate",
        "fiscal-reporting.z.read",
        "fiscal-reporting.z.generate"
    ];

    public static IEndpointRouteBuilder MapFiscalReportingEndpoints(this IEndpointRouteBuilder app)
    {
        Map(app.MapGroup("/v1/management-platform/fiscal-reporting/sites/{siteId:guid}"), operatorScoped:false);
        var operatorGroup = app.MapGroup("/v1/ops/operator-console/fiscal-reporting")
            .WithMetadata(OperatorConsoleOperatingContextRequirementMetadata.NotRequired);
        operatorGroup.MapGet("/authorized-sites", ListAuthorizedSitesAsync)
            .WithMetadata(new ReconciliationPolicyMetadata(SiteCatalogPolicy));
        Map(operatorGroup.MapGroup("/sites/{siteId:guid}"), operatorScoped:true);
        return app;
    }

    private static void Map(RouteGroupBuilder group,bool operatorScoped)
    {
        group.MapGet("/electronic-journal", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,DateTimeOffset? periodStart,DateTimeOffset? periodEnd,string? search,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.ElectronicJournalRead,periodStart,periodEnd,search,null,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(EjReadPolicy));
        group.MapGet("/electronic-journal/download", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,DateTimeOffset? periodStart,DateTimeOffset? periodEnd,string? search,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.ElectronicJournalDownload,periodStart,periodEnd,search,null,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(EjExportPolicy));
        group.MapGet("/x-readings", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.XHistory,null,null,null,null,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(XReadPolicy));
        group.MapPost("/x-readings", (Guid siteId,GenerateReadingRequest body,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.XGenerate,null,null,null,body.OperationKey,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(XGeneratePolicy));
        group.MapGet("/x-readings/{reportReference}/download", (Guid siteId,string reportReference,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.XDownload,null,null,null,null,reportReference,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(XReadPolicy));
        group.MapGet("/z-readings", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.ZHistory,null,null,null,null,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(ZReadPolicy));
        if (operatorScoped)
        {
            group.MapGet("/z-readings/closeable-periods", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
                Execute(siteId,true,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.ZCloseablePeriods,null,null,null,null,null,false,ct))
                .WithMetadata(new ReconciliationPolicyMetadata(ZReadPolicy));
            group.MapPost("/z-readings/closeable-periods/{fiscalReportingPeriodId:guid}/close", ClosePeriodAsync)
                .WithMetadata(new ReconciliationPolicyMetadata(ZGeneratePolicy));
            group.MapPost("/z-readings/closeable-periods/close-all", CloseAllPeriodsAsync)
                .WithMetadata(new ReconciliationPolicyMetadata(ZGeneratePolicy));
        }
        if (!operatorScoped)
        {
            group.MapPost("/z-readings", (Guid siteId,GenerateZReadingRequest body,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
                Execute(siteId,false,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.ZGenerate,null,null,null,body.OperationKey,null,body.ConfirmClose,ct))
                .WithMetadata(new ReconciliationPolicyMetadata(ZGeneratePolicy));
        }
        group.MapGet("/z-readings/{reportReference}/download", (Guid siteId,string reportReference,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.ZDownload,null,null,null,null,reportReference,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(ZReadPolicy));
    }

    private static async Task<IResult> ClosePeriodAsync(
        Guid siteId,
        Guid fiscalReportingPeriodId,
        CloseFiscalBusinessDateRequest body,
        HttpRequest request,
        IFiscalReportingPosServerGateway gateway,
        ICentralPmsRbacRepository rbacRepository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeCloseAsync(siteId, body.ConfirmClose, request, rbacRepository, cancellationToken).ConfigureAwait(false);
        if (authorization.Denial is not null) return authorization.Denial;

        var result = await gateway.SendAsync(new(
            siteId,
            FiscalReportingGatewayAction.ZClosePeriod,
            authorization.CorrelationId,
            authorization.Actor,
            OperationKey: body.OperationKey,
            FiscalReportingPeriodId: fiscalReportingPeriodId,
            ExpectedStateVersion: body.ExpectedStateVersion), cancellationToken).ConfigureAwait(false);
        loggerFactory.CreateLogger("ExitPass.CentralPms.FiscalReporting").Log(
            result.HttpStatusCode < 400 ? LogLevel.Information : LogLevel.Warning,
            "Site-scoped fiscal business date close completed. actor={Actor} site={Site} period={Period} result={Result} correlation_id={Correlation}",
            authorization.Actor, siteId, fiscalReportingPeriodId, result.Code, authorization.CorrelationId);
        return GatewayResult(result);
    }

    private static async Task<IResult> CloseAllPeriodsAsync(
        Guid siteId,
        CloseAllFiscalBusinessDatesRequest body,
        HttpRequest request,
        IFiscalReportingPosServerGateway gateway,
        ICentralPmsRbacRepository rbacRepository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeCloseAsync(siteId, body.ConfirmClose, request, rbacRepository, cancellationToken).ConfigureAwait(false);
        if (authorization.Denial is not null) return authorization.Denial;

        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.FiscalReporting");
        var snapshot = await gateway.SendAsync(new(
            siteId,
            FiscalReportingGatewayAction.ZCloseablePeriods,
            authorization.CorrelationId,
            authorization.Actor), cancellationToken).ConfigureAwait(false);
        if (snapshot.HttpStatusCode is < 200 or >= 300) return GatewayResult(snapshot);

        if (!TryReadCloseablePeriods(snapshot.Body, out var periods))
        {
            return Results.Json(new
            {
                succeeded = false,
                code = "fiscal_reporting_closeable_periods_response_invalid",
                message = "The authoritative closeable-period response was invalid.",
                correlationId = authorization.CorrelationId
            }, statusCode: StatusCodes.Status502BadGateway);
        }

        var closedCount = 0;
        foreach (var period in periods)
        {
            var close = await gateway.SendAsync(new(
                siteId,
                FiscalReportingGatewayAction.ZClosePeriod,
                authorization.CorrelationId,
                authorization.Actor,
                OperationKey: $"{body.OperationKey}:{period.FiscalReportingPeriodId:D}",
                FiscalReportingPeriodId: period.FiscalReportingPeriodId,
                ExpectedStateVersion: period.ExpectedStateVersion), cancellationToken).ConfigureAwait(false);
            if (close.HttpStatusCode is < 200 or >= 300)
            {
                _ = await gateway.SendAsync(new(
                    siteId,
                    FiscalReportingGatewayAction.ZCloseablePeriods,
                    authorization.CorrelationId,
                    authorization.Actor), cancellationToken).ConfigureAwait(false);
                logger.LogWarning(
                    "Site-scoped batch fiscal close stopped. actor={Actor} site={Site} closed_count={ClosedCount} stopped_period={Period} result={Result} correlation_id={Correlation}",
                    authorization.Actor, siteId, closedCount, period.FiscalBusinessDate, close.Code, authorization.CorrelationId);
                return Results.Json(new
                {
                    succeeded = false,
                    code = "fiscal_business_date_batch_close_stopped",
                    closedCount,
                    stoppedAt = period.FiscalBusinessDate,
                    message = $"{closedCount} business dates closed. Closing stopped at {period.FiscalBusinessDate}.",
                    correlationId = authorization.CorrelationId
                }, statusCode: close.HttpStatusCode >= 500 ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict);
            }

            closedCount++;
        }

        logger.LogInformation(
            "Site-scoped batch fiscal close completed. actor={Actor} site={Site} closed_count={ClosedCount} correlation_id={Correlation}",
            authorization.Actor, siteId, closedCount, authorization.CorrelationId);
        return Results.Ok(new
        {
            succeeded = true,
            code = "fiscal_business_dates_closed",
            closedCount,
            correlationId = authorization.CorrelationId
        });
    }

    private static async Task<CloseAuthorization> AuthorizeCloseAsync(
        Guid siteId,
        bool confirmClose,
        HttpRequest request,
        ICentralPmsRbacRepository rbacRepository,
        CancellationToken cancellationToken)
    {
        var correlation = Guid.TryParse(request.Headers["X-Correlation-Id"].ToString(), out var parsed) && parsed != Guid.Empty
            ? parsed
            : Guid.NewGuid();
        var actor = request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unavailable";
        if (!confirmClose)
        {
            return new(actor, correlation, Results.BadRequest(new
            {
                succeeded = false,
                code = "z_reading_close_confirmation_required",
                correlationId = correlation,
                message = "Explicit confirmation is required before closing a Fiscal Business Date."
            }));
        }
        if (!Guid.TryParse(actor, out var actorUserId) ||
            !await OperatorConsoleFiscalReportingAccess.IsOperationsSupervisorAsync(request.HttpContext.User, actorUserId, rbacRepository, cancellationToken).ConfigureAwait(false))
        {
            return new(actor, correlation, OperatorConsoleFiscalReportingAccess.Denied(correlation));
        }
        if (!InSiteScope(request.HttpContext.User, siteId, true))
        {
            return new(actor, correlation, Results.NotFound(new
            {
                succeeded = false,
                code = "fiscal_reporting_scope_not_found",
                correlationId = correlation,
                message = "The requested fiscal reporting scope is unavailable."
            }));
        }
        return new(actor, correlation, null);
    }

    private static bool TryReadCloseablePeriods(byte[] body, out IReadOnlyList<CloseablePeriod> periods)
    {
        periods = [];
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("fiscalBusinessDates", out var items) || items.ValueKind != JsonValueKind.Array) return false;
            var parsed = new List<CloseablePeriod>();
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("fiscalReportingPeriodId", out var periodIdValue) ||
                    !Guid.TryParse(periodIdValue.GetString(), out var periodId) || periodId == Guid.Empty ||
                    !item.TryGetProperty("fiscalBusinessDate", out var businessDateValue) ||
                    !DateOnly.TryParse(businessDateValue.GetString(), out var businessDate) ||
                    !item.TryGetProperty("expectedStateVersion", out var versionValue) ||
                    !versionValue.TryGetInt64(out var version) || version <= 0)
                {
                    return false;
                }
                parsed.Add(new(periodId, businessDate, version));
            }
            periods = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IResult GatewayResult(FiscalReportingGatewayResponse result)
    {
        if (result.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) &&
            IsValidJson(result.Body))
        {
            return Results.Content(
                Encoding.UTF8.GetString(result.Body),
                result.ContentType,
                Encoding.UTF8,
                result.HttpStatusCode);
        }

        var status = result.HttpStatusCode is >= 400 and < 500
            ? result.HttpStatusCode
            : StatusCodes.Status502BadGateway;
        return Results.Json(new
        {
            succeeded = false,
            code = result.HttpStatusCode is >= 200 and < 300
                ? "fiscal_reporting_upstream_response_invalid"
                : "fiscal_reporting_upstream_operation_failed",
            message = "The authoritative fiscal reporting operation could not be completed.",
            retryable = result.Retryable || result.HttpStatusCode >= 500
        }, statusCode: status);
    }

    private static bool IsValidJson(byte[] body)
    {
        try
        {
            using var _ = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<IResult> Execute(Guid siteId,bool operatorScoped,HttpRequest request,IFiscalReportingPosServerGateway gateway,
        IFiscalIssuanceReferenceRepository references,
        ICentralPmsRbacRepository rbacRepository,
        ILoggerFactory loggerFactory,FiscalReportingGatewayAction action,DateTimeOffset? start,DateTimeOffset? end,string? search,
        string? operationKey,string? reportReference,bool confirmClose,CancellationToken ct)
    {
        var correlation=Guid.TryParse(request.Headers["X-Correlation-Id"].ToString(),out var parsed)&&parsed!=Guid.Empty?parsed:Guid.NewGuid();
        var actor=request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)??"unavailable";
        var logger=loggerFactory.CreateLogger("ExitPass.CentralPms.FiscalReporting");
        var actorUserId=Guid.TryParse(actor,out var parsedActorUserId)?parsedActorUserId:Guid.Empty;
        if(operatorScoped&&!await OperatorConsoleFiscalReportingAccess.IsOperationsSupervisorAsync(request.HttpContext.User,actorUserId,rbacRepository,ct).ConfigureAwait(false))
        {
            logger.LogWarning("Operator Console fiscal reporting role denied. actor={Actor} site={Site} action={Action} result=denied correlation_id={Correlation}",actor,siteId,action,correlation);
            return OperatorConsoleFiscalReportingAccess.Denied(correlation);
        }
        if(!InSiteScope(request.HttpContext.User,siteId,operatorScoped))
        {
            logger.LogWarning("Fiscal reporting operation denied. actor={Actor} site={Site} action={Action} result=denied correlation_id={Correlation}",actor,siteId,action,correlation);
            return Results.NotFound(new{succeeded=false,code="fiscal_reporting_scope_not_found",correlationId=correlation,message="The requested fiscal reporting scope is unavailable."});
        }
        if(action==FiscalReportingGatewayAction.ZGenerate&&!confirmClose)
        {
            logger.LogWarning("Z Reading close denied because explicit confirmation was absent. actor={Actor} site={Site} result=denied correlation_id={Correlation}",actor,siteId,correlation);
            return Results.BadRequest(new{succeeded=false,code="z_reading_close_confirmation_required",correlationId=correlation,message="Explicit confirmation is required because Z Reading closes the authoritative fiscal period."});
        }
        var electronicJournal = action is FiscalReportingGatewayAction.ElectronicJournalRead or FiscalReportingGatewayAction.ElectronicJournalDownload;
        if(electronicJournal && (!start.HasValue || !end.HasValue || end<=start || end-start>TimeSpan.FromDays(31)))
            return Results.BadRequest(new{succeeded=false,code="fiscal_reporting_period_invalid",correlationId=correlation,message="The reporting period must be positive and no longer than 31 days."});

        var resolvedSearch=search;
        if(operatorScoped&&electronicJournal&&!string.IsNullOrWhiteSpace(search))
        {
            var matches=await references.FindByOperatorIdentifierAsync(search.Trim(),siteId,ct).ConfigureAwait(false);
            if(matches.Count>1)
                return Results.Json(new{succeeded=false,code="fiscal_reporting_identifier_ambiguous",correlationId=correlation,message="The exact identifier matched multiple fiscal records."},statusCode:StatusCodes.Status409Conflict);
            if(matches.Count==0||matches[0].Reference.FiscalDocumentNumber is null)
                return EmptyElectronicJournal(action);
            resolvedSearch=matches[0].Reference.FiscalDocumentNumber;
        }

        var result=await gateway.SendAsync(new(siteId,action,correlation,actor,start,end,resolvedSearch,operationKey,reportReference),ct).ConfigureAwait(false);
        logger.Log(result.HttpStatusCode<400?LogLevel.Information:LogLevel.Warning,
            "Fiscal reporting operation completed. actor={Actor} site={Site} action={Action} result={Result} report={Report} correlation_id={Correlation}",
            actor,siteId,action,result.Code,reportReference,correlation);
        if(result.HttpStatusCode is >=200 and <300 &&
            (result.ContentType.StartsWith("text/plain",StringComparison.OrdinalIgnoreCase) ||
             result.ContentType.StartsWith("application/pdf",StringComparison.OrdinalIgnoreCase)))
        {
            var fallbackFileName = result.ContentType.StartsWith("application/pdf",StringComparison.OrdinalIgnoreCase)
                ? "fiscal-report.pdf"
                : "fiscal-report.txt";
            request.HttpContext.Response.Headers.ContentDisposition=$"attachment; filename=\"{result.FileName??fallbackFileName}\"";
            request.HttpContext.Response.Headers.CacheControl="private, no-store";
            return Results.Bytes(result.Body,result.ContentType);
        }
        return GatewayResult(result);
    }

    private static bool InSiteScope(ClaimsPrincipal principal,Guid siteId,bool operatorScoped)
    {
        if (principal.Identity?.IsAuthenticated != true) return false;
        var expected=siteId.ToString("D");
        if (operatorScoped)
        {
            return principal.FindAll("site_id")
                .Any(c=>c.Value.Equals(expected,StringComparison.OrdinalIgnoreCase));
        }
        var global=principal.FindAll("has_global_scope").Concat(principal.FindAll("global_scope")).Any(c=>c.Value.Equals("true",StringComparison.OrdinalIgnoreCase));
        var sites=principal.FindAll("site_id").Concat(principal.FindAll("operator_effective_site_id"));
        return global||sites.Any(c=>c.Value=="*"||c.Value.Equals(expected,StringComparison.OrdinalIgnoreCase));
    }

    private static IResult EmptyElectronicJournal(FiscalReportingGatewayAction action) =>
        action == FiscalReportingGatewayAction.ElectronicJournalDownload
            ? Results.Bytes([], "text/plain; charset=utf-8")
            : Results.Json(new { invoices = Array.Empty<object>() });

    private static async Task<IResult> ListAuthorizedSitesAsync(
        HttpRequest request,
        IShiftManagementRepository repository,
        ICentralPmsRbacRepository rbacRepository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }
        if (!await OperatorConsoleFiscalReportingAccess.IsOperationsSupervisorAsync(
                request.HttpContext.User,
                userId,
                rbacRepository,
                cancellationToken).ConfigureAwait(false))
        {
            return OperatorConsoleFiscalReportingAccess.Denied(Guid.TryParse(request.Headers["X-Correlation-Id"], out var correlationId)
                ? correlationId
                : Guid.NewGuid());
        }

        var sessionSiteIds = request.HttpContext.User.FindAll("site_id")
            .Select(claim => Guid.TryParse(claim.Value, out var siteId) ? siteId : Guid.Empty)
            .Where(siteId => siteId != Guid.Empty)
            .ToHashSet();
        if (sessionSiteIds.Count == 0) return Results.Ok(Array.Empty<FiscalReportingAuthorizedSiteResponse>());

        var sites = new Dictionary<Guid, FiscalReportingAuthorizedSiteResponse>();
        foreach (var permissionCode in FiscalPermissionCodes)
        {
            var access = await repository.ReadAccessAsync(
                userId,
                permissionCode,
                timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            if (access is not { UserActive: true }) continue;

            foreach (var site in access.Sites.Where(site => sessionSiteIds.Contains(site.SiteId)))
            {
                sites[site.SiteId] = new FiscalReportingAuthorizedSiteResponse(
                    site.SiteId,
                    site.SiteCode,
                    site.SiteName);
            }
        }

        return Results.Ok(sites.Values
            .OrderBy(site => site.SiteName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.SiteId)
            .ToArray());
    }
}

public sealed record GenerateReadingRequest(string? OperationKey);
public sealed record GenerateZReadingRequest(string? OperationKey,bool ConfirmClose);
public sealed record CloseFiscalBusinessDateRequest(string? OperationKey,long ExpectedStateVersion,bool ConfirmClose);
public sealed record CloseAllFiscalBusinessDatesRequest(string OperationKey,bool ConfirmClose);
public sealed record FiscalReportingAuthorizedSiteResponse(Guid SiteId,string SiteCode,string SiteName);
internal sealed record CloseAuthorization(string Actor,Guid CorrelationId,IResult? Denial);
internal sealed record CloseablePeriod(Guid FiscalReportingPeriodId,DateOnly FiscalBusinessDate,long ExpectedStateVersion);
