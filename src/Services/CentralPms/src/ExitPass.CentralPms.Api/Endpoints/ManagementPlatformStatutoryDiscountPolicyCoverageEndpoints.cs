using System.Diagnostics;
using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using Npgsql;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class ManagementPlatformStatutoryDiscountPolicyCoverageEndpoints
{
    private const string CoverageReadPolicy = ManagementPlatformStatutoryDiscountPolicyCoverageValues.PolicyName;
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.ManagementPlatformStatutoryDiscountPolicyCoverage");

    public static IEndpointRouteBuilder MapManagementPlatformStatutoryDiscountPolicyCoverageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/management-platform")
            .WithTags("ManagementPlatform");

        group.MapGet("/statutory-discounts/policy-coverage", GetPolicyCoverageAsync)
            .WithName("GetManagementPlatformStatutoryDiscountPolicyCoverage")
            .WithTags("ManagementPlatform", "StatutoryDiscounts")
            .WithMetadata(new ReconciliationPolicyMetadata(CoverageReadPolicy))
            .Produces<ManagementPlatformStatutoryDiscountPolicyCoverageResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Get Management Platform statutory policy coverage")
            .WithDescription("Returns a browser-safe, read-only statutory policy coverage read model for a server-resolved Site or Site Group scope. The endpoint does not create statutory requests, decisions, applications, evidence, payable-basis changes, payment state, fiscal state, policy mutations, or Operator Console workflow records.");

        return app;
    }

    private static async Task<IResult> GetPolicyCoverageAsync(
        HttpRequest httpRequest,
        IManagementPlatformStatutoryDiscountPolicyCoverageService service,
        ILoggerFactory loggerFactory,
        string? scopeType,
        Guid? scopeId,
        string? entitlementType,
        bool? includeInactive,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP GetManagementPlatformStatutoryDiscountPolicyCoverage", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ManagementPlatformStatutoryDiscountPolicyCoverageEndpoints");
        var correlationId = ResolveRequestCorrelationId(httpRequest);

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("statutory_policy_scope_type", scopeType ?? string.Empty);

        if (string.IsNullOrWhiteSpace(scopeType))
        {
            return SafeError(
                StatusCodes.Status400BadRequest,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.InvalidScopeType,
                "The policy-coverage scopeType query parameter is required.",
                correlationId,
                retryable: false);
        }

        if (!IsSupportedScopeType(scopeType))
        {
            return SafeError(
                StatusCodes.Status400BadRequest,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.InvalidScopeType,
                "The requested policy-coverage scope type is not supported.",
                correlationId,
                retryable: false);
        }

        if (scopeId is null || scopeId.Value == Guid.Empty)
        {
            return SafeError(
                StatusCodes.Status400BadRequest,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.InvalidScopeReference,
                "The policy-coverage scopeId query parameter is required.",
                correlationId,
                retryable: false);
        }

        try
        {
            var result = await service.ReadCoverageAsync(
                new ManagementPlatformStatutoryDiscountPolicyCoverageQuery(
                    scopeType,
                    scopeId.Value,
                    entitlementType,
                    includeInactive.GetValueOrDefault(false),
                    correlationId,
                    ResolveActorUserId(httpRequest)),
                cancellationToken);

            if (result.Outcome == ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.Success && result.Coverage is not null)
            {
                activity?.SetTag("coverage_row_count", result.Coverage.CoverageRows.Count);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Results.Ok(ToContract(result.Coverage));
            }

            return ToErrorResult(result);
        }
        catch (NpgsqlException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Policy source unavailable.");
            activity?.AddException(ex);
            logger.LogError(ex, "Management Platform statutory policy coverage source unavailable. CorrelationId: {CorrelationId}", correlationId);
            return SafeError(
                StatusCodes.Status503ServiceUnavailable,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.PolicySourceUnavailable,
                "The statutory policy source is unavailable.",
                correlationId,
                retryable: true);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unexpected policy coverage read failure.");
            activity?.AddException(ex);
            logger.LogError(ex, "Management Platform statutory policy coverage read failed. CorrelationId: {CorrelationId}", correlationId);
            return SafeError(
                StatusCodes.Status500InternalServerError,
                ManagementPlatformStatutoryDiscountPolicyCoverageValues.UnexpectedFailure,
                "The statutory policy coverage read failed.",
                correlationId,
                retryable: false);
        }
    }

    private static IResult ToErrorResult(ManagementPlatformStatutoryDiscountPolicyCoverageResult result)
    {
        var statusCode = result.Outcome switch
        {
            ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.InvalidScopeType => StatusCodes.Status400BadRequest,
            ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.InvalidScopeReference => StatusCodes.Status400BadRequest,
            ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.ScopeDenied => StatusCodes.Status403Forbidden,
            ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.ScopeNotFound => StatusCodes.Status404NotFound,
            ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.EmptyGovernedScope => StatusCodes.Status404NotFound,
            ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.PolicySourceUnavailable => StatusCodes.Status503ServiceUnavailable,
            ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.OrdinanceSourceUnavailable => StatusCodes.Status503ServiceUnavailable,
            ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.TransientDependencyFailure => StatusCodes.Status503ServiceUnavailable,
            ManagementPlatformStatutoryDiscountPolicyCoverageOutcome.MalformedAuthoritativeData => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        return SafeError(
            statusCode,
            result.ErrorCode ?? ManagementPlatformStatutoryDiscountPolicyCoverageValues.UnexpectedFailure,
            result.ErrorMessage ?? "The statutory policy coverage read failed.",
            result.CorrelationId,
            result.Retryable);
    }

    private static ManagementPlatformStatutoryDiscountPolicyCoverageResponse ToContract(
        ManagementPlatformStatutoryDiscountPolicyCoverage coverage) =>
        new(
            coverage.RequestedScopeType,
            coverage.RequestedScopeReference,
            coverage.ResolvedScopeType,
            coverage.ResolvedScopeReference,
            coverage.ScopeDisplayName,
            coverage.CorrelationId,
            coverage.EvaluatedAt,
            coverage.CoverageRows.Select(row => new ManagementPlatformStatutoryDiscountPolicyCoverageRowDto(
                row.SiteReference,
                row.SiteDisplayName,
                row.EntitlementType,
                row.CoverageClassification,
                row.PolicyStatusClassification,
                row.AuthoritativeCoverageAvailable,
                row.EffectiveFrom,
                row.EffectiveTo,
                row.PolicyReference,
                row.OrdinanceOrLegalAuthorityReference,
                row.JurisdictionOrLocalityReference,
                row.PolicyVersionOrRevisionReference,
                row.LastAuthoritativeUpdateTimestamp,
                row.DataQualityClassification,
                row.ReasonClassification,
                row.SourceClassification)).ToArray());

    private static bool IsSupportedScopeType(string scopeType)
    {
        var normalized = scopeType.Trim().Replace('-', '_').ToUpperInvariant();
        return string.Equals(normalized, ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSite, StringComparison.Ordinal) ||
            string.Equals(normalized, ManagementPlatformStatutoryDiscountPolicyCoverageValues.ScopeTypeSiteGroup, StringComparison.Ordinal);
    }

    private static Guid ResolveRequestCorrelationId(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var headerCorrelationId) &&
            headerCorrelationId != Guid.Empty)
        {
            return headerCorrelationId;
        }

        return Guid.NewGuid();
    }

    private static Guid? ResolveActorUserId(HttpRequest request)
    {
        if (request.Headers.TryGetValue(CentralPmsRbacPolicyCatalog.UserIdHeaderName, out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var headerUserId) &&
            headerUserId != Guid.Empty)
        {
            return headerUserId;
        }

        foreach (var claimType in new[] { ClaimTypes.NameIdentifier, "sub", "user_id" })
        {
            var value = request.HttpContext.User.FindFirst(claimType)?.Value;
            if (Guid.TryParse(value, out var claimUserId) && claimUserId != Guid.Empty)
            {
                return claimUserId;
            }
        }

        return null;
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

