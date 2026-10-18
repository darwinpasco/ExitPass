using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.FiscalReporting;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.ShiftManagement;
using Microsoft.AspNetCore.Antiforgery;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class FiscalReportingEndpoints
{
    public const string EjReadPolicy="FiscalReportingElectronicJournalRead";
    public const string EjExportPolicy="FiscalReportingElectronicJournalExport";
    public const string XReadPolicy="FiscalReportingXRead";
    public const string XGeneratePolicy="FiscalReportingXGenerate";
    public const string ZReadPolicy="FiscalReportingZRead";
    public const string ZGeneratePolicy="FiscalReportingZGenerate";
    public const string BirReadPolicy=ZReadPolicy;
    public const string BirGeneratePolicy=ZGeneratePolicy;
    public const string AnnexE1KnownZeroAttestPolicy="FiscalReportingAnnexE1KnownZeroAttest";
    public const string AnnexE1CorrectPolicy="FiscalReportingAnnexE1Correct";
    public const string SiteCatalogPolicy="FiscalReportingSiteCatalogRead";

    private static readonly HashSet<string> AnnexE1KnownZeroFactTypes = new(StringComparer.Ordinal)
    {
        "manual_si_or_net_income",
        "sales_overrun_overflow_net_income",
        "naac_discount",
        "solo_parent_discount",
        "other_vat_adjustment",
        "vat_on_returns",
        "residual_vat_adjustment"
    };

    private static readonly HashSet<string> AnnexE1CorrectionReasons = new(StringComparer.Ordinal)
    {
        "authorized_restatement",
        "source_correction"
    };

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
        group.MapGet("/audit-trail", (Guid siteId,DateTimeOffset? from,DateTimeOffset? to,string? actionCode,string? actor,string? result,string? reference,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.AuditTrailRead,from,to,null,null,null,false,ct,
                actionCode:actionCode,actorFilter:actor,resultFilter:result,referenceFilter:reference))
            .WithMetadata(new ReconciliationPolicyMetadata(ZReadPolicy));
        group.MapGet("/audit-trail/download", (Guid siteId,DateTimeOffset? from,DateTimeOffset? to,string? format,string? actionCode,string? actor,string? result,string? reference,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.AuditTrailDownload,from,to,null,null,null,false,ct,
                download:true,exportFormat:format,actionCode:actionCode,actorFilter:actor,resultFilter:result,referenceFilter:reference))
            .WithMetadata(new ReconciliationPolicyMetadata(ZReadPolicy));
        group.MapGet("/x-readings", (Guid siteId,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.XHistory,null,null,null,null,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(XReadPolicy));
        group.MapPost("/x-readings", (Guid siteId,GenerateReadingRequest body,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.XGenerate,null,null,null,body.OperationKey,null,false,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(XGeneratePolicy));
        group.MapGet("/x-readings/{reportReference}/download", (Guid siteId,string reportReference,string? format,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            DownloadReadingAsync(siteId,operatorScoped,reportReference,format,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.XDownload,ct))
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
        group.MapGet("/z-readings/{reportReference}/download", (Guid siteId,string reportReference,string? format,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            DownloadReadingAsync(siteId,operatorScoped,reportReference,format,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.ZDownload,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(ZReadPolicy));
        group.MapPost("/bir-sales-summaries", (Guid siteId,GenerateBirSalesSummaryRequest body,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,IAntiforgery antiforgery,CancellationToken ct)=>
            GenerateBirSalesSummaryAsync(siteId,operatorScoped,body,request,gateway,references,rbac,logs,antiforgery,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(BirGeneratePolicy));
        group.MapGet("/bir-sales-summaries/{summaryId:guid}", (Guid siteId,Guid summaryId,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.BirSalesSummaryRead,null,null,null,null,null,false,ct,summaryId:summaryId))
            .WithMetadata(new ReconciliationPolicyMetadata(BirReadPolicy));
        group.MapGet("/bir-sales-summaries/{summaryId:guid}/download", async (Guid siteId,Guid summaryId,string format,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
        {
            var action = format.ToLowerInvariant() switch
            {
                "json" => FiscalReportingGatewayAction.BirSalesSummaryDownloadJson,
                "csv" => FiscalReportingGatewayAction.BirSalesSummaryDownloadCsv,
                "pdf" => FiscalReportingGatewayAction.BirSalesSummaryDownloadPdf,
                _ => (FiscalReportingGatewayAction?)null
            };
            if (!action.HasValue) return Results.BadRequest(new { succeeded=false,code="bir_sales_summary_export_format_unsupported",message="The requested export format is unsupported." });
            return await Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,action.Value,
                null,null,null,null,null,false,ct,summaryId:summaryId,download:true).ConfigureAwait(false);
        })
            .WithMetadata(new ReconciliationPolicyMetadata(BirReadPolicy));
        group.MapPost("/annex-e/e1/workbooks", (Guid siteId,GenerateAnnexE1WorkbookRequest body,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,IAntiforgery antiforgery,CancellationToken ct)=>
            GenerateAnnexE1Async(siteId,operatorScoped,body,request,gateway,references,rbac,logs,antiforgery,ct));
        group.MapPost("/annex-e/e1/known-zero-attestations", (Guid siteId,AttestAnnexE1KnownZeroRequest body,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,IAntiforgery antiforgery,CancellationToken ct)=>
            AttestAnnexE1KnownZeroAsync(siteId,operatorScoped,body,request,gateway,references,rbac,logs,antiforgery,ct))
            .WithMetadata(new ReconciliationPolicyMetadata(AnnexE1KnownZeroAttestPolicy));
        group.MapGet("/annex-e/e1/workbooks/current", (Guid siteId,int calendarYear,int calendarMonth,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.AnnexE1CurrentRead,null,null,null,null,null,false,ct,calendarYear:calendarYear,calendarMonth:calendarMonth))
            .WithMetadata(new ReconciliationPolicyMetadata(BirReadPolicy));
        group.MapGet("/annex-e/e1/workbooks/{workbookId:guid}", (Guid siteId,Guid workbookId,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.AnnexE1Read,null,null,null,null,null,false,ct,workbookId:workbookId))
            .WithMetadata(new ReconciliationPolicyMetadata(BirReadPolicy));
        group.MapGet("/annex-e/e1/workbooks/{workbookId:guid}/download", async (Guid siteId,Guid workbookId,string? format,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
        {
            var action = (format??"xlsx").ToLowerInvariant() switch
            {
                "xlsx" => FiscalReportingGatewayAction.AnnexE1Download,
                "pdf" => FiscalReportingGatewayAction.AnnexE1DownloadPdf,
                _ => (FiscalReportingGatewayAction?)null
            };
            if(!action.HasValue)return Results.BadRequest(new{succeeded=false,code="annex_e1_export_format_unsupported",message="The requested export format is unsupported."});
            return await Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,action.Value,
                null,null,null,null,null,false,ct,workbookId:workbookId,download:true).ConfigureAwait(false);
        })
            .WithMetadata(new ReconciliationPolicyMetadata(BirReadPolicy));
        group.MapGet("/monthly-esales", (Guid siteId,int calendarYear,int calendarMonth,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
            Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,FiscalReportingGatewayAction.MonthlyESalesRead,null,null,null,null,null,false,ct,calendarYear:calendarYear,calendarMonth:calendarMonth))
            .WithMetadata(new ReconciliationPolicyMetadata(BirReadPolicy));
        group.MapGet("/monthly-esales/download", async (Guid siteId,int calendarYear,int calendarMonth,string format,HttpRequest request,IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,ICentralPmsRbacRepository rbac,ILoggerFactory logs,CancellationToken ct)=>
        {
            var action = format.ToLowerInvariant() switch
            {
                "json" => FiscalReportingGatewayAction.MonthlyESalesDownloadJson,
                "csv" => FiscalReportingGatewayAction.MonthlyESalesDownloadCsv,
                "pdf" => FiscalReportingGatewayAction.MonthlyESalesDownloadPdf,
                _ => (FiscalReportingGatewayAction?)null
            };
            if (!action.HasValue) return Results.BadRequest(new { succeeded=false,code="monthly_esales_export_format_unsupported",message="The requested export format is unsupported." });
            return await Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,action.Value,
                null,null,null,null,null,false,ct,calendarYear:calendarYear,calendarMonth:calendarMonth,download:true).ConfigureAwait(false);
        }).WithMetadata(new ReconciliationPolicyMetadata(BirReadPolicy));
    }

    private static async Task<IResult> GenerateBirSalesSummaryAsync(
        Guid siteId,bool operatorScoped,GenerateBirSalesSummaryRequest body,HttpRequest request,
        IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,
        ICentralPmsRbacRepository rbac,ILoggerFactory logs,IAntiforgery antiforgery,CancellationToken ct)
    {
        var csrfFailure=await ValidateCsrfAsync(request,antiforgery).ConfigureAwait(false);
        return csrfFailure??await Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,
            FiscalReportingGatewayAction.BirSalesSummaryGenerate,null,null,null,body.OperationKey,null,false,ct,
            fiscalReportingPeriodId:body.FiscalReportingPeriodId,
            governingZReadingReference:body.GoverningZReadingReference).ConfigureAwait(false);
    }

    private static async Task<IResult> GenerateAnnexE1Async(
        Guid siteId,bool operatorScoped,GenerateAnnexE1WorkbookRequest body,HttpRequest request,
        IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,
        ICentralPmsRbacRepository rbac,ILoggerFactory logs,IAntiforgery antiforgery,CancellationToken ct)
    {
        var csrfFailure=await ValidateCsrfAsync(request,antiforgery).ConfigureAwait(false);
        if (csrfFailure is not null) return csrfFailure;
        var validationFailure = ValidateAnnexE1Generation(body);
        if (validationFailure is not null) return validationFailure;
        var authorizationPolicy = body.SupersedesWorkbookId.HasValue ? AnnexE1CorrectPolicy : BirGeneratePolicy;
        var authorizationFailure = await AuthorizeSensitiveHumanAsync(request, rbac, authorizationPolicy, ct).ConfigureAwait(false);
        if (authorizationFailure is not null) return authorizationFailure;
        return await Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,
            FiscalReportingGatewayAction.AnnexE1Generate,null,null,null,body.OperationKey,null,false,ct,
            calendarYear:body.CalendarYear,calendarMonth:body.CalendarMonth,
            supersedesWorkbookId:body.SupersedesWorkbookId,correctionReason:body.CorrectionReason,
            correctionApprovalReference:body.CorrectionApprovalReference).ConfigureAwait(false);
    }

    private static async Task<IResult> AttestAnnexE1KnownZeroAsync(
        Guid siteId,bool operatorScoped,AttestAnnexE1KnownZeroRequest body,HttpRequest request,
        IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,
        ICentralPmsRbacRepository rbac,ILoggerFactory logs,IAntiforgery antiforgery,CancellationToken ct)
    {
        var csrfFailure=await ValidateCsrfAsync(request,antiforgery).ConfigureAwait(false);
        if (csrfFailure is not null) return csrfFailure;
        var validationFailure = ValidateAnnexE1KnownZero(body);
        if (validationFailure is not null) return validationFailure;
        var authorizationFailure = await AuthorizeSensitiveHumanAsync(request, rbac, AnnexE1KnownZeroAttestPolicy, ct).ConfigureAwait(false);
        if (authorizationFailure is not null) return authorizationFailure;
        return await Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,
            FiscalReportingGatewayAction.AnnexE1KnownZeroAttest,null,null,null,body.OperationKey,null,false,ct,
            fiscalReportingPeriodId:body.FiscalReportingPeriodId,factType:body.FactType,
            factStatus:body.FactStatus,amountMinorUnits:body.AmountMinorUnits,
            sourceDocumentCount:body.SourceDocumentCount,firstSourceReference:body.FirstSourceReference,
            lastSourceReference:body.LastSourceReference,sourceEventReference:body.SourceEventReference,
            approvalReference:body.ApprovalReference,supersedesFactId:body.SupersedesFactId,
            correctionReason:body.CorrectionReason).ConfigureAwait(false);
    }

    private static IResult? ValidateAnnexE1KnownZero(AttestAnnexE1KnownZeroRequest body)
    {
        if (!ValidReference(body.OperationKey, 200) || body.FiscalReportingPeriodId == Guid.Empty ||
            body.FactType is null || !AnnexE1KnownZeroFactTypes.Contains(body.FactType) ||
            !string.Equals(body.FactStatus, "attested_zero", StringComparison.Ordinal) ||
            body.AmountMinorUnits != 0 || body.SourceDocumentCount != 0 ||
            body.FirstSourceReference is not null || body.LastSourceReference is not null ||
            body.SourceEventReference is not null || body.SupersedesFactId is not null || body.CorrectionReason is not null ||
            !ValidApprovalReference(body.ApprovalReference))
        {
            return AnnexE1BadRequest(
                "annex_e1_known_zero_invalid",
                "The Annex E-1 known-zero attestation must contain an approved zero fact without source or correction values.");
        }
        return null;
    }

    private static IResult? ValidateAnnexE1Generation(GenerateAnnexE1WorkbookRequest body)
    {
        if (!ValidReference(body.OperationKey, 200) || body.CalendarYear is < 2000 or > 9999 || body.CalendarMonth is < 1 or > 12)
            return AnnexE1BadRequest("annex_e1_generation_invalid", "The Annex E-1 generation request is incomplete or invalid.");

        var hasCorrection = body.SupersedesWorkbookId.HasValue || body.CorrectionReason is not null || body.CorrectionApprovalReference is not null;
        var completeCorrection = body.SupersedesWorkbookId is { } workbookId && workbookId != Guid.Empty &&
            body.CorrectionReason is not null && AnnexE1CorrectionReasons.Contains(body.CorrectionReason) &&
            ValidApprovalReference(body.CorrectionApprovalReference);
        return hasCorrection && !completeCorrection
            ? AnnexE1BadRequest("annex_e1_correction_invalid", "An Annex E-1 correction requires a predecessor, controlled reason, and genuine approval reference.")
            : null;
    }

    private static async Task<IResult?> AuthorizeSensitiveHumanAsync(
        HttpRequest request,ICentralPmsRbacRepository repository,string policy,CancellationToken cancellationToken)
    {
        var principal = request.HttpContext.User;
        var correlation = Guid.TryParse(request.Headers["X-Correlation-Id"], out var parsed) && parsed != Guid.Empty ? parsed : Guid.NewGuid();
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId == Guid.Empty ||
            principal.HasClaim(claim => claim.Type is "service_identity_id" or "client_id"))
        {
            return Results.Json(new{succeeded=false,code="annex_e1_human_authorization_required",correlationId=correlation,message="An authenticated human approval is required."},statusCode:StatusCodes.Status401Unauthorized);
        }

        var required = CentralPmsRbacPolicyCatalog.ResolvePermissions(policy);
        var claims = principal.Claims
            .Where(claim => claim.Type.Equals(CentralPmsRbacPolicyCatalog.PermissionClaimType,StringComparison.OrdinalIgnoreCase) ||
                            claim.Type.Equals("permission",StringComparison.OrdinalIgnoreCase) ||
                            claim.Type.Equals("scope",StringComparison.OrdinalIgnoreCase))
            .SelectMany(claim => claim.Value.Split([' ', ','],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (required.Any(claims.Contains) || await repository.UserHasAnyPermissionAsync(userId,required,cancellationToken).ConfigureAwait(false))
            return null;

        await repository.RecordDeniedAsync(policy,userId,null,correlation,request.Path,cancellationToken).ConfigureAwait(false);
        return Results.Json(new{succeeded=false,code="annex_e1_sensitive_operation_forbidden",correlationId=correlation,message="The caller does not have the required Annex E-1 authority."},statusCode:StatusCodes.Status403Forbidden);
    }

    private static bool ValidReference(string? value,int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Contains('\r') && !value.Contains('\n');

    private static bool ValidApprovalReference(string? value)
    {
        if (!ValidReference(value,200)) return false;
        return value!.Trim().ToUpperInvariant() is not ("N/A" or "NONE" or "UNKNOWN" or "AUTO");
    }

    private static IResult AnnexE1BadRequest(string code,string message) =>
        Results.BadRequest(new{succeeded=false,code,message});

    private static async Task<IResult?> ValidateCsrfAsync(HttpRequest request,IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(request.HttpContext).ConfigureAwait(false);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new
            {
                succeeded=false,
                code="CSRF_VALIDATION_FAILED",
                message="The secure fiscal reporting request could not be validated."
            });
        }
    }

    private static async Task<IResult> DownloadReadingAsync(
        Guid siteId,bool operatorScoped,string reportReference,string? format,HttpRequest request,
        IFiscalReportingPosServerGateway gateway,IFiscalIssuanceReferenceRepository references,
        ICentralPmsRbacRepository rbac,ILoggerFactory logs,FiscalReportingGatewayAction action,CancellationToken ct)
    {
        return await Execute(siteId,operatorScoped,request,gateway,references,rbac,logs,action,
            null,null,null,null,reportReference,false,ct,download:true,exportFormat:format).ConfigureAwait(false);
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
        var birSalesSummaryPendingCount = 0;
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
            if (HasPendingBirSalesSummary(close.Body)) birSalesSummaryPendingCount++;
        }

        logger.LogInformation(
            "Site-scoped batch fiscal close completed. actor={Actor} site={Site} closed_count={ClosedCount} correlation_id={Correlation}",
            authorization.Actor, siteId, closedCount, authorization.CorrelationId);
        return Results.Ok(new
        {
            succeeded = true,
            code = "fiscal_business_dates_closed",
            closedCount,
            birSalesSummaryPendingCount,
            message = birSalesSummaryPendingCount > 0
                ? $"{closedCount} Fiscal Business Dates closed. {birSalesSummaryPendingCount} Daily BIR Sales Summary operation(s) are pending and can be retried from Daily BIR Sales Summary."
                : null,
            correlationId = authorization.CorrelationId
        });
    }

    private static bool HasPendingBirSalesSummary(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("birSalesSummaryCompletion", out var completion) &&
                   completion.ValueKind == JsonValueKind.Object &&
                   completion.TryGetProperty("status", out var status) &&
                   string.Equals(status.GetString(), "PENDING", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
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
        string? operationKey,string? reportReference,bool confirmClose,CancellationToken ct,
        Guid? fiscalReportingPeriodId=null,Guid? summaryId=null,Guid? workbookId=null,string? governingZReadingReference=null,
        int? calendarYear=null,int? calendarMonth=null,bool download=false,string? exportFormat=null,
        string? actionCode=null,string? actorFilter=null,string? resultFilter=null,string? referenceFilter=null,
        string? factType=null,string? factStatus=null,long? amountMinorUnits=null,long? sourceDocumentCount=null,
        string? firstSourceReference=null,string? lastSourceReference=null,string? sourceEventReference=null,
        string? approvalReference=null,Guid? supersedesFactId=null,string? correctionReason=null,
        Guid? supersedesWorkbookId=null,string? correctionApprovalReference=null)
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
        if(action is FiscalReportingGatewayAction.XDownload or FiscalReportingGatewayAction.ZDownload)
        {
            exportFormat=string.IsNullOrWhiteSpace(exportFormat)?"pdf":exportFormat.Trim().ToLowerInvariant();
            if(exportFormat is not ("pdf" or "json"))
                return Results.BadRequest(new{succeeded=false,code="fiscal_report_export_format_unsupported",correlationId=correlation,message="The requested export format is unsupported."});
        }
        if(action==FiscalReportingGatewayAction.AuditTrailDownload)
        {
            exportFormat=exportFormat?.Trim().ToLowerInvariant();
            if(exportFormat is not ("pdf" or "csv" or "json"))
                return Results.BadRequest(new{succeeded=false,code="audit_trail_export_format_unsupported",correlationId=correlation,message="The requested Audit Trail export format is unsupported."});
        }
        if(action==FiscalReportingGatewayAction.ZGenerate&&!confirmClose)
        {
            logger.LogWarning("Z Reading close denied because explicit confirmation was absent. actor={Actor} site={Site} result=denied correlation_id={Correlation}",actor,siteId,correlation);
            return Results.BadRequest(new{succeeded=false,code="z_reading_close_confirmation_required",correlationId=correlation,message="Explicit confirmation is required because Z Reading closes the authoritative fiscal period."});
        }
        var electronicJournal = action is FiscalReportingGatewayAction.ElectronicJournalRead or FiscalReportingGatewayAction.ElectronicJournalDownload;
        var boundedReport = electronicJournal ||
            action is FiscalReportingGatewayAction.AuditTrailRead or FiscalReportingGatewayAction.AuditTrailDownload;
        if(boundedReport && (!start.HasValue || !end.HasValue || end<=start || end-start>TimeSpan.FromDays(31)))
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

        var result=await gateway.SendAsync(new(siteId,action,correlation,actor,start,end,resolvedSearch,operationKey,reportReference,
            fiscalReportingPeriodId,SummaryId:summaryId,WorkbookId:workbookId,GoverningZReadingReference:governingZReadingReference,
            CalendarYear:calendarYear,CalendarMonth:calendarMonth,ExportFormat:exportFormat,ActionCode:actionCode,
            ActorFilter:actorFilter,ResultFilter:resultFilter,ReferenceFilter:referenceFilter,FactType:factType,
            FactStatus:factStatus,AmountMinorUnits:amountMinorUnits,SourceDocumentCount:sourceDocumentCount,
            FirstSourceReference:firstSourceReference,LastSourceReference:lastSourceReference,
            SourceEventReference:sourceEventReference,ApprovalReference:approvalReference,SupersedesFactId:supersedesFactId,
            CorrectionReason:correctionReason,SupersedesWorkbookId:supersedesWorkbookId,
            CorrectionApprovalReference:correctionApprovalReference),ct).ConfigureAwait(false);
        logger.Log(result.HttpStatusCode<400?LogLevel.Information:LogLevel.Warning,
            "Fiscal reporting operation completed. actor={Actor} site={Site} action={Action} result={Result} report={Report} correlation_id={Correlation}",
            actor,siteId,action,result.Code,reportReference,correlation);
        if (actorUserId != Guid.Empty && (action == FiscalReportingGatewayAction.AnnexE1KnownZeroAttest ||
            (action == FiscalReportingGatewayAction.AnnexE1Generate && supersedesWorkbookId.HasValue)))
        {
            try
            {
                await rbacRepository.RecordAuditEventAsync(
                    action == FiscalReportingGatewayAction.AnnexE1KnownZeroAttest
                        ? "ANNEX_E1_KNOWN_ZERO_ATTESTATION"
                        : "ANNEX_E1_WORKBOOK_CORRECTION",
                    result.HttpStatusCode is >=200 and <300 ? "SUCCESS" : "REJECTED",
                    result.Code,
                    action == FiscalReportingGatewayAction.AnnexE1KnownZeroAttest
                        ? "FiscalReportingPeriod"
                        : "AnnexE1Workbook",
                    action == FiscalReportingGatewayAction.AnnexE1KnownZeroAttest
                        ? fiscalReportingPeriodId
                        : supersedesWorkbookId,
                    actorUserId,null,correlation,
                    action == FiscalReportingGatewayAction.AnnexE1KnownZeroAttest
                        ? "Governed Annex E-1 known-zero attestation completed through POS authority."
                        : "Governed Annex E-1 correction request completed through POS authority.",ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Central Annex E-1 audit persistence failed after POS result. actor={Actor} site={Site} action={Action} correlation_id={Correlation}",
                    actor,siteId,action,correlation);
            }
        }
        if(result.HttpStatusCode is >=200 and <300 && (download ||
            (result.ContentType.StartsWith("text/plain",StringComparison.OrdinalIgnoreCase) ||
             result.ContentType.StartsWith("application/pdf",StringComparison.OrdinalIgnoreCase))))
        {
            var fallbackFileName = result.ContentType.StartsWith("application/pdf",StringComparison.OrdinalIgnoreCase) ? "fiscal-report.pdf"
                : result.ContentType.StartsWith("text/csv",StringComparison.OrdinalIgnoreCase) ? "bir-sales-summary.csv"
                : result.ContentType.StartsWith("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",StringComparison.OrdinalIgnoreCase) ? "annex-e1.xlsx"
                : result.ContentType.StartsWith("application/json",StringComparison.OrdinalIgnoreCase) ? "bir-sales-summary.json"
                : "fiscal-report.txt";
            request.HttpContext.Response.Headers.ContentDisposition=$"attachment; filename=\"{result.FileName??fallbackFileName}\"";
            request.HttpContext.Response.Headers.CacheControl="private, no-store";
            if(!string.IsNullOrWhiteSpace(result.ContentSha256)) request.HttpContext.Response.Headers["X-Content-SHA256"]=result.ContentSha256;
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
public sealed record GenerateBirSalesSummaryRequest(string? OperationKey,Guid FiscalReportingPeriodId,string? GoverningZReadingReference);
public sealed record GenerateAnnexE1WorkbookRequest(
    string? OperationKey,int CalendarYear,int CalendarMonth,Guid? SupersedesWorkbookId=null,
    string? CorrectionReason=null,string? CorrectionApprovalReference=null);
public sealed record AttestAnnexE1KnownZeroRequest(
    string? OperationKey,Guid FiscalReportingPeriodId,string? FactType,string? FactStatus,
    long? AmountMinorUnits,long? SourceDocumentCount,string? FirstSourceReference,
    string? LastSourceReference,string? SourceEventReference,string? ApprovalReference,
    Guid? SupersedesFactId=null,string? CorrectionReason=null);
public sealed record CloseFiscalBusinessDateRequest(string? OperationKey,long ExpectedStateVersion,bool ConfirmClose);
public sealed record CloseAllFiscalBusinessDatesRequest(string OperationKey,bool ConfirmClose);
public sealed record FiscalReportingAuthorizedSiteResponse(Guid SiteId,string SiteCode,string SiteName);
internal sealed record CloseAuthorization(string Actor,Guid CorrelationId,IResult? Denial);
internal sealed record CloseablePeriod(Guid FiscalReportingPeriodId,DateOnly FiscalBusinessDate,long ExpectedStateVersion);
