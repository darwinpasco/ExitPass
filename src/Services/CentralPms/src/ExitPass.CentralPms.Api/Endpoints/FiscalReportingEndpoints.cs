using System.Security.Claims;
using System.Text;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalReporting;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class FiscalReportingEndpoints
{
    public const string EjReadPolicy="FiscalReportingElectronicJournalRead";
    public const string EjExportPolicy="FiscalReportingElectronicJournalExport";
    public const string XReadPolicy="FiscalReportingXRead";
    public const string XGeneratePolicy="FiscalReportingXGenerate";
    public const string ZReadPolicy="FiscalReportingZRead";
    public const string ZGeneratePolicy="FiscalReportingZGenerate";

    public static IEndpointRouteBuilder MapFiscalReportingEndpoints(this IEndpointRouteBuilder app)
    {
        Map(app.MapGroup("/v1/management-platform/fiscal-reporting/sites/{siteId:guid}"), operatorScoped:false);
        Map(app.MapGroup("/v1/ops/operator-console/fiscal-reporting/sites/{siteId:guid}"), operatorScoped:true);
        return app;
    }

    private static void Map(RouteGroupBuilder group,bool operatorScoped)
    {
        group.MapGet("/electronic-journal", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,ILoggerFactory logs,DateTimeOffset? periodStart,DateTimeOffset? periodEnd,string? search,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,logs,FiscalReportingGatewayAction.ElectronicJournalRead,periodStart,periodEnd,search,null,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(EjReadPolicy));
        group.MapGet("/electronic-journal/download", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,ILoggerFactory logs,DateTimeOffset? periodStart,DateTimeOffset? periodEnd,string? search,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,logs,FiscalReportingGatewayAction.ElectronicJournalDownload,periodStart,periodEnd,search,null,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(EjExportPolicy));
        group.MapGet("/x-readings", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,logs,FiscalReportingGatewayAction.XHistory,null,null,null,null,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(XReadPolicy));
        group.MapPost("/x-readings", (Guid siteId,GenerateReadingRequest body,HttpRequest request,IFiscalReportingPosServerGateway gateway,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,logs,FiscalReportingGatewayAction.XGenerate,null,null,null,body.OperationKey,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(XGeneratePolicy));
        group.MapGet("/x-readings/{reportReference}/download", (Guid siteId,string reportReference,HttpRequest request,IFiscalReportingPosServerGateway gateway,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,logs,FiscalReportingGatewayAction.XDownload,null,null,null,null,reportReference,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(XReadPolicy));
        group.MapGet("/z-readings", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,logs,FiscalReportingGatewayAction.ZHistory,null,null,null,null,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(ZReadPolicy));
        group.MapPost("/z-readings", (Guid siteId,GenerateZReadingRequest body,HttpRequest request,IFiscalReportingPosServerGateway gateway,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,logs,FiscalReportingGatewayAction.ZGenerate,null,null,null,body.OperationKey,null,body.ConfirmClose,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(ZGeneratePolicy));
        group.MapGet("/z-readings/{reportReference}/download", (Guid siteId,string reportReference,HttpRequest request,IFiscalReportingPosServerGateway gateway,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,logs,FiscalReportingGatewayAction.ZDownload,null,null,null,null,reportReference,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(ZReadPolicy));
    }

    private static async Task<IResult> Execute(Guid siteId,bool operatorScoped,HttpRequest request,IFiscalReportingPosServerGateway gateway,
        ILoggerFactory loggerFactory,FiscalReportingGatewayAction action,DateTimeOffset? start,DateTimeOffset? end,string? search,
        string? operationKey,string? reportReference,bool confirmClose,CancellationToken ct)
    {
        var correlation=Guid.TryParse(request.Headers["X-Correlation-Id"].ToString(),out var parsed)&&parsed!=Guid.Empty?parsed:Guid.NewGuid();
        var actor=request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)??"unavailable";
        var logger=loggerFactory.CreateLogger("ExitPass.CentralPms.FiscalReporting");
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

        var result=await gateway.SendAsync(new(siteId,action,correlation,actor,start,end,search,operationKey,reportReference),ct).ConfigureAwait(false);
        logger.Log(result.HttpStatusCode<400?LogLevel.Information:LogLevel.Warning,
            "Fiscal reporting operation completed. actor={Actor} site={Site} action={Action} result={Result} report={Report} correlation_id={Correlation}",
            actor,siteId,action,result.Code,reportReference,correlation);
        if(result.HttpStatusCode is >=200 and <300 && result.ContentType.StartsWith("text/plain",StringComparison.OrdinalIgnoreCase))
        {
            request.HttpContext.Response.Headers.ContentDisposition=$"attachment; filename=\"{result.FileName??"fiscal-report.txt"}\"";
            request.HttpContext.Response.Headers.CacheControl="private, no-store";
            return Results.Bytes(result.Body,result.ContentType);
        }
        return Results.Content(Encoding.UTF8.GetString(result.Body),result.ContentType,statusCode:result.HttpStatusCode);
    }

    private static bool InSiteScope(ClaimsPrincipal principal,Guid siteId,bool operatorScoped)
    {
        var expected=siteId.ToString("D");
        var global=principal.FindAll("has_global_scope").Concat(principal.FindAll("global_scope")).Any(c=>c.Value.Equals("true",StringComparison.OrdinalIgnoreCase));
        var sites=principal.FindAll("site_id").Concat(principal.FindAll("operator_effective_site_id"));
        var allowed=global||sites.Any(c=>c.Value=="*"||c.Value.Equals(expected,StringComparison.OrdinalIgnoreCase));
        return allowed && (!operatorScoped || principal.FindAll("operator_effective_site_id").Any(c=>c.Value.Equals(expected,StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed record GenerateReadingRequest(string? OperationKey);
public sealed record GenerateZReadingRequest(string? OperationKey,bool ConfirmClose);
