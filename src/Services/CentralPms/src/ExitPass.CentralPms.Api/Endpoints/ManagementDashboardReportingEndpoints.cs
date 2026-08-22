using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class ManagementDashboardReportingEndpoints
{
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.ManagementDashboardReporting");

    public static IEndpointRouteBuilder MapManagementDashboardReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/management-platform/dashboard")
            .WithTags("ManagementPlatform", "DashboardReporting");

        group.MapGet("/catalog", GetCatalogAsync)
            .WithName("GetManagementDashboardReportCatalog")
            .WithMetadata(new ReconciliationPolicyMetadata(ManagementDashboardReportingValues.CatalogPolicy))
            .Produces<ManagementDashboardCatalogResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Get the Management Dashboard report catalog")
            .WithDescription("Returns the controlled phase-1 report catalog and explicit availability classifications. It does not execute unavailable reports or expose mutation authority.");

        group.MapGet("/operational-overview", GetOperationalOverviewAsync)
            .WithName("GetManagementDashboardOperationalOverview")
            .WithMetadata(new ReconciliationPolicyMetadata(ManagementDashboardReportingValues.OverviewPolicy))
            .Produces<ManagementDashboardOperationalOverviewResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Get a scoped Management Dashboard operational overview")
            .WithDescription("Returns read-only Site status and vendor projection health aggregates for an explicit server-authorized SITE or SITE_GROUP scope.");

        group.MapGet("/payment-reconciliation-summary", GetPaymentReconciliationSummaryAsync)
            .WithName("GetManagementPaymentReconciliationSummary")
            .WithMetadata(new ReconciliationPolicyMetadata(ManagementPaymentReconciliationReportingValues.Policy))
            .Produces<ManagementPaymentReconciliationSummaryResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Get a scoped payment and reconciliation summary")
            .WithDescription("Returns canonical Central PMS payment aggregates and internally provable consistency conditions for an explicit authorized scope and half-open UTC period. It does not prove settlement, payout, cash custody, or fiscal remittance.");

        return app;
    }

    private static async Task<IResult> GetCatalogAsync(
        HttpRequest request,
        IIdentityAdministrationActorAccessor actorAccessor,
        IManagementDashboardReportingService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetManagementDashboardReportCatalog", request);
        var correlationId = ResolveCorrelationId(request);
        var actor = ResolveActor(actorAccessor);
        if (actor is null)
        {
            return SafeError(
                StatusCodes.Status401Unauthorized,
                "HUMAN_SESSION_REQUIRED",
                "An authenticated Management Platform human session is required.",
                correlationId,
                false);
        }

        try
        {
            var result = await service.GetCatalogAsync(actor, correlationId, cancellationToken);
            if (result.Outcome == ManagementDashboardReportingOutcome.Success && result.Value is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Results.Ok(ToContract(result.Value));
            }

            return ToError(result);
        }
        catch (ManagementDashboardSourceUnavailableException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Dashboard source unavailable.");
            activity?.AddException(ex);
            loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ManagementDashboardReportingEndpoints")
                .LogError(ex, "Management Dashboard catalog source unavailable. CorrelationId: {CorrelationId}", correlationId);
            return SafeError(
                StatusCodes.Status503ServiceUnavailable,
                ManagementDashboardReportingValues.SourceUnavailable,
                "A required Management Dashboard source is temporarily unavailable.",
                correlationId,
                true);
        }
        catch (Exception ex)
        {
            return Unexpected(ex, activity, loggerFactory, correlationId);
        }
    }

    private static async Task<IResult> GetOperationalOverviewAsync(
        HttpRequest request,
        IIdentityAdministrationActorAccessor actorAccessor,
        IManagementDashboardReportingService service,
        ILoggerFactory loggerFactory,
        string? scopeType,
        Guid? scopeReference,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetManagementDashboardOperationalOverview", request);
        var correlationId = ResolveCorrelationId(request);
        var actor = ResolveActor(actorAccessor);
        if (actor is null)
        {
            return SafeError(
                StatusCodes.Status401Unauthorized,
                "HUMAN_SESSION_REQUIRED",
                "An authenticated Management Platform human session is required.",
                correlationId,
                false);
        }

        try
        {
            var result = await service.GetOperationalOverviewAsync(
                actor,
                new ManagementDashboardOperationalOverviewQuery(scopeType, scopeReference, correlationId),
                cancellationToken);
            if (result.Outcome == ManagementDashboardReportingOutcome.Success && result.Value is not null)
            {
                activity?.SetTag("dashboard.scope_type", result.Value.EffectiveScope.ScopeType);
                activity?.SetTag("dashboard.availability", result.Value.Availability);
                activity?.SetTag("dashboard.freshness", result.Value.Freshness);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Results.Ok(ToContract(result.Value));
            }

            return ToError(result);
        }
        catch (ManagementDashboardSourceUnavailableException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Dashboard source unavailable.");
            activity?.AddException(ex);
            loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ManagementDashboardReportingEndpoints")
                .LogError(ex, "Management Dashboard overview source unavailable. CorrelationId: {CorrelationId}", correlationId);
            return SafeError(
                StatusCodes.Status503ServiceUnavailable,
                ManagementDashboardReportingValues.SourceUnavailable,
                "A required Management Dashboard source is temporarily unavailable.",
                correlationId,
                true);
        }
        catch (Exception ex)
        {
            return Unexpected(ex, activity, loggerFactory, correlationId);
        }
    }

    private static async Task<IResult> GetPaymentReconciliationSummaryAsync(
        HttpRequest request,
        IIdentityAdministrationActorAccessor actorAccessor,
        IManagementPaymentReconciliationReportingService service,
        ILoggerFactory loggerFactory,
        string? scopeType,
        Guid? scopeReference,
        string? periodStart,
        string? periodEnd,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetManagementPaymentReconciliationSummary", request);
        var correlationId = ResolveCorrelationId(request);
        var actor = ResolveActor(actorAccessor);
        if (actor is null)
        {
            return SafeError(
                StatusCodes.Status401Unauthorized,
                "HUMAN_SESSION_REQUIRED",
                "An authenticated Management Platform human session is required.",
                correlationId,
                false);
        }

        try
        {
            var result = await service.GetSummaryAsync(
                actor,
                new ManagementPaymentReconciliationQuery(
                    scopeType,
                    scopeReference,
                    periodStart,
                    periodEnd,
                    correlationId),
                cancellationToken);
            if (result.Outcome == ManagementPaymentReconciliationOutcome.Success && result.Value is not null)
            {
                activity?.SetTag("dashboard.report_id", ManagementPaymentReconciliationReportingValues.ReportId);
                activity?.SetTag("dashboard.scope_type", result.Value.EffectiveScope.ScopeType);
                activity?.SetTag("dashboard.availability", result.Value.Availability);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Results.Ok(ToContract(result.Value));
            }

            return ToPaymentReconciliationError(result);
        }
        catch (ManagementDashboardSourceUnavailableException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Payment reporting source unavailable.");
            activity?.AddException(ex);
            loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ManagementDashboardReportingEndpoints")
                .LogError(ex, "Payment reconciliation reporting source unavailable. CorrelationId: {CorrelationId}", correlationId);
            return SafeError(
                StatusCodes.Status503ServiceUnavailable,
                ManagementPaymentReconciliationReportingValues.SourceUnavailable,
                "The canonical payment reporting source is temporarily unavailable.",
                correlationId,
                true);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unexpected payment reporting failure.");
            activity?.AddException(ex);
            loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ManagementDashboardReportingEndpoints")
                .LogError(ex, "Unexpected payment reconciliation reporting failure. CorrelationId: {CorrelationId}", correlationId);
            return SafeError(
                StatusCodes.Status500InternalServerError,
                ManagementPaymentReconciliationReportingValues.UnexpectedFailure,
                "The payment and reconciliation report failed unexpectedly.",
                correlationId,
                false);
        }
    }

    private static IResult ToPaymentReconciliationError(
        ManagementPaymentReconciliationResult<ManagementPaymentReconciliationReport> result)
    {
        var statusCode = result.Outcome switch
        {
            ManagementPaymentReconciliationOutcome.FeatureDisabled => StatusCodes.Status503ServiceUnavailable,
            ManagementPaymentReconciliationOutcome.InvalidScope => StatusCodes.Status400BadRequest,
            ManagementPaymentReconciliationOutcome.InvalidPeriod => StatusCodes.Status400BadRequest,
            ManagementPaymentReconciliationOutcome.SessionInvalid => StatusCodes.Status401Unauthorized,
            ManagementPaymentReconciliationOutcome.ScopeNotFoundOrDenied => StatusCodes.Status404NotFound,
            ManagementPaymentReconciliationOutcome.SourceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return SafeError(
            statusCode,
            result.ErrorCode ?? ManagementPaymentReconciliationReportingValues.UnexpectedFailure,
            result.ErrorMessage ?? "The payment and reconciliation report request failed.",
            result.CorrelationId,
            result.Retryable);
    }

    private static IResult ToError<T>(ManagementDashboardReportingResult<T> result)
    {
        var statusCode = result.Outcome switch
        {
            ManagementDashboardReportingOutcome.FeatureDisabled => StatusCodes.Status503ServiceUnavailable,
            ManagementDashboardReportingOutcome.InvalidScope => StatusCodes.Status400BadRequest,
            ManagementDashboardReportingOutcome.SessionInvalid => StatusCodes.Status401Unauthorized,
            ManagementDashboardReportingOutcome.ScopeNotFoundOrDenied => StatusCodes.Status404NotFound,
            ManagementDashboardReportingOutcome.SourceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return SafeError(
            statusCode,
            result.ErrorCode ?? ManagementDashboardReportingValues.UnexpectedFailure,
            result.ErrorMessage ?? "The Management Dashboard request failed.",
            result.CorrelationId,
            result.Retryable);
    }

    private static ManagementDashboardCatalogResponse ToContract(ManagementDashboardCatalog catalog) =>
        new(
            catalog.ContractVersion,
            catalog.GeneratedAt,
            catalog.Reports.Select(report => new ManagementDashboardCatalogEntryDto(
                report.ReportId,
                report.ContractVersion,
                report.DisplayTitle,
                report.FunctionalDomain,
                report.Description,
                report.SupportedScopeTypes,
                report.RequiredPermission,
                report.Availability,
                report.SourceAuthority,
                report.PrivacyClassification,
                report.SupportedFilters,
                report.FreshnessSemantics,
                report.Warnings,
                report.Limitations)).ToArray());

    private static ManagementDashboardOperationalOverviewResponse ToContract(
        ManagementDashboardOperationalOverview overview) =>
        new(
            overview.ContractVersion,
            overview.ReportId,
            ToContract(overview.RequestedScope),
            ToContract(overview.EffectiveScope),
            overview.GeneratedAt,
            overview.DataAsOf,
            overview.Availability,
            overview.Freshness,
            overview.CorrelationId,
            overview.Sections.Select(section => new ManagementDashboardOverviewSectionDto(
                section.SectionId,
                section.DisplayTitle,
                section.Availability,
                section.Freshness,
                section.SourceAuthority,
                section.DataAsOf,
                section.Metrics.Select(metric => new ManagementDashboardMetricDto(
                    metric.MetricId,
                    metric.DisplayLabel,
                    metric.Value,
                    metric.Unit)).ToArray(),
                section.Warnings,
                section.Limitations)).ToArray(),
            overview.Warnings,
            overview.Limitations);

    private static ManagementPaymentReconciliationSummaryResponse ToContract(
        ManagementPaymentReconciliationReport report) =>
        new(
            report.ContractVersion,
            report.ReportId,
            ToContract(report.RequestedScope),
            ToContract(report.EffectiveScope),
            report.PeriodStart,
            report.PeriodEnd,
            report.GeneratedAt,
            report.DataAsOf,
            report.Availability,
            report.Freshness,
            report.CorrelationId,
            report.CurrencySummaries.Select(summary => new ManagementPaymentCurrencySummaryDto(
                summary.CurrencyCode,
                summary.AttemptCount,
                summary.AttemptedAmount,
                summary.ConfirmedCount,
                summary.ConfirmedAmount)).ToArray(),
            report.PaymentAttemptSummaries.Select(summary => new ManagementPaymentStatusSummaryDto(
                summary.Status,
                summary.CurrencyCode,
                summary.Count,
                summary.Amount)).ToArray(),
            report.ConfirmedPaymentSummaries.Select(summary => new ManagementPaymentStatusSummaryDto(
                summary.Status,
                summary.CurrencyCode,
                summary.Count,
                summary.Amount)).ToArray(),
            report.CanonicalStatusSummaries.Select(summary => new ManagementPaymentCanonicalStatusSummaryDto(
                summary.RecordType,
                summary.Status,
                summary.CurrencyCode,
                summary.Count,
                summary.Amount)).ToArray(),
            report.ChannelSummaries.Select(summary => new ManagementPaymentChannelSummaryDto(
                summary.ChannelCode,
                summary.ChannelType,
                summary.CurrencyCode,
                summary.AttemptCount,
                summary.AttemptedAmount,
                summary.ConfirmedCount,
                summary.ConfirmedAmount)).ToArray(),
            report.ProviderSummaries.Select(summary => new ManagementPaymentProviderSummaryDto(
                summary.ProviderCode,
                summary.CurrencyCode,
                summary.AttemptCount,
                summary.AttemptedAmount,
                summary.ConfirmedCount,
                summary.ConfirmedAmount,
                summary.VerifiedOutcomeCount,
                summary.VerifiedOutcomeAmount)).ToArray(),
            report.InternalReconciliationSummaries.Select(summary => new ManagementInternalReconciliationSummaryDto(
                summary.CategoryId,
                summary.Availability,
                summary.Count,
                summary.Amounts.Select(amount => new ManagementReconciliationAmountSummaryDto(
                    amount.CurrencyCode,
                    amount.Amount)).ToArray(),
                summary.Definition,
                summary.MonetaryTreatment,
                summary.Limitations)).ToArray(),
            report.Warnings,
            report.Limitations,
            report.SourceAuthority);

    private static ManagementDashboardScopeDto ToContract(ManagementDashboardScope scope) =>
        new(scope.ScopeType, scope.ScopeReference, scope.DisplayName);

    private static ManagementDashboardActor? ResolveActor(IIdentityAdministrationActorAccessor accessor) =>
        accessor.Current is { } actor
            ? new ManagementDashboardActor(actor.UserId, actor.HumanSessionId)
            : null;

    private static Activity? StartActivity(string name, HttpRequest request)
    {
        var activity = ActivitySource.StartActivity(name, ActivityKind.Server);
        activity?.SetTag("url.path", request.Path.Value);
        activity?.SetTag("http.request.method", request.Method);
        return activity;
    }

    private static Guid ResolveCorrelationId(HttpRequest request) =>
        request.Headers.TryGetValue("X-Correlation-Id", out var value) &&
        Guid.TryParse(value.ToString(), out var correlationId) &&
        correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();

    private static IResult Unexpected(
        Exception ex,
        Activity? activity,
        ILoggerFactory loggerFactory,
        Guid correlationId)
    {
        activity?.SetStatus(ActivityStatusCode.Error, "Unexpected dashboard failure.");
        activity?.AddException(ex);
        loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ManagementDashboardReportingEndpoints")
            .LogError(ex, "Unexpected Management Dashboard failure. CorrelationId: {CorrelationId}", correlationId);
        return SafeError(
            StatusCodes.Status500InternalServerError,
            ManagementDashboardReportingValues.UnexpectedFailure,
            "The Management Dashboard request failed unexpectedly.",
            correlationId,
            false);
    }

    private static IResult SafeError(
        int statusCode,
        string errorCode,
        string message,
        Guid correlationId,
        bool retryable) =>
        Results.Json(
            new ErrorResponse
            {
                ErrorCode = errorCode,
                Message = message,
                CorrelationId = correlationId,
                Retryable = retryable,
                RecoveryClassification = errorCode
            },
            statusCode: statusCode);
}
