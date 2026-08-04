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

public static class ManagementPlatformStatutoryEvidenceGovernanceEndpoints
{
    private const string GovernanceReadPolicy = ManagementPlatformStatutoryEvidenceGovernanceValues.PolicyName;
    private const string RoutePrefix = "/v1/ops/management-platform/statutory-discounts/evidence-governance";
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.ManagementPlatformStatutoryEvidenceGovernance");

    public static IEndpointRouteBuilder MapManagementPlatformStatutoryEvidenceGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/management-platform")
            .WithTags("ManagementPlatform");

        group.MapGet("/statutory-discounts/evidence-governance", GetGovernanceAsync)
            .WithName("GetManagementPlatformStatutoryEvidenceGovernance")
            .WithTags("ManagementPlatform", "StatutoryDiscounts", "StatutoryEvidence")
            .WithMetadata(new ReconciliationPolicyMetadata(GovernanceReadPolicy))
            .Produces<ManagementPlatformStatutoryEvidenceGovernanceResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Get Management Platform statutory evidence governance")
            .WithDescription("Returns browser-safe, read-only statutory evidence governance configuration and readiness for server-authorized Site and Site Group scopes. The endpoint does not return customer evidence metadata, evidence references, evidence bytes, signed URLs, object keys, checksums, provider internals, workflow records, or mutation authority.");

        group.MapGet("/statutory-discounts/evidence-governance/sites/{siteReference:guid}", GetSiteGovernanceAsync)
            .WithName("GetManagementPlatformStatutoryEvidenceGovernanceForSite")
            .WithTags("ManagementPlatform", "StatutoryDiscounts", "StatutoryEvidence")
            .WithMetadata(new ReconciliationPolicyMetadata(GovernanceReadPolicy))
            .Produces<ManagementPlatformStatutoryEvidenceGovernanceResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Get statutory evidence governance for a Site")
            .WithDescription("Returns browser-safe statutory evidence governance configuration for one server-authorized Site without exposing evidence records or storage internals.");

        group.MapGet("/statutory-discounts/evidence-governance/site-groups/{siteGroupReference:guid}", GetSiteGroupGovernanceAsync)
            .WithName("GetManagementPlatformStatutoryEvidenceGovernanceForSiteGroup")
            .WithTags("ManagementPlatform", "StatutoryDiscounts", "StatutoryEvidence")
            .WithMetadata(new ReconciliationPolicyMetadata(GovernanceReadPolicy))
            .Produces<ManagementPlatformStatutoryEvidenceGovernanceResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Get statutory evidence governance for a Site Group")
            .WithDescription("Returns browser-safe statutory evidence governance configuration for server-authorized Sites in one Site Group without collapsing Site-level authority.");

        return app;
    }

    private static Task<IResult> GetGovernanceAsync(
        HttpRequest httpRequest,
        IManagementPlatformStatutoryEvidenceGovernanceService service,
        ILoggerFactory loggerFactory,
        Guid? siteReference,
        Guid? siteGroupReference,
        string? entitlementType,
        string? governanceStatus,
        string? readinessStatus,
        bool? captureEnabled,
        bool? includeStale,
        CancellationToken cancellationToken)
    {
        if (siteReference is not null && siteGroupReference is not null)
        {
            var correlationId = ResolveRequestCorrelationId(httpRequest);
            return Task.FromResult(SafeError(
                StatusCodes.Status400BadRequest,
                ManagementPlatformStatutoryEvidenceGovernanceValues.InvalidFilter,
                "Only one evidence-governance scope filter may be supplied.",
                correlationId,
                retryable: false));
        }

        var scopeType = siteReference is not null
            ? ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSite
            : siteGroupReference is not null
                ? ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSiteGroup
                : null;

        var scopeReference = siteReference ?? siteGroupReference;
        return ExecuteGovernanceReadAsync(httpRequest, service, loggerFactory, scopeType, scopeReference, entitlementType, governanceStatus, readinessStatus, captureEnabled, includeStale, cancellationToken);
    }

    private static Task<IResult> GetSiteGovernanceAsync(
        Guid siteReference,
        HttpRequest httpRequest,
        IManagementPlatformStatutoryEvidenceGovernanceService service,
        ILoggerFactory loggerFactory,
        string? entitlementType,
        string? governanceStatus,
        string? readinessStatus,
        bool? captureEnabled,
        bool? includeStale,
        CancellationToken cancellationToken) =>
        ExecuteGovernanceReadAsync(
            httpRequest,
            service,
            loggerFactory,
            ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSite,
            siteReference,
            entitlementType,
            governanceStatus,
            readinessStatus,
            captureEnabled,
            includeStale,
            cancellationToken);

    private static Task<IResult> GetSiteGroupGovernanceAsync(
        Guid siteGroupReference,
        HttpRequest httpRequest,
        IManagementPlatformStatutoryEvidenceGovernanceService service,
        ILoggerFactory loggerFactory,
        string? entitlementType,
        string? governanceStatus,
        string? readinessStatus,
        bool? captureEnabled,
        bool? includeStale,
        CancellationToken cancellationToken) =>
        ExecuteGovernanceReadAsync(
            httpRequest,
            service,
            loggerFactory,
            ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSiteGroup,
            siteGroupReference,
            entitlementType,
            governanceStatus,
            readinessStatus,
            captureEnabled,
            includeStale,
            cancellationToken);

    private static async Task<IResult> ExecuteGovernanceReadAsync(
        HttpRequest httpRequest,
        IManagementPlatformStatutoryEvidenceGovernanceService service,
        ILoggerFactory loggerFactory,
        string? scopeType,
        Guid? scopeReference,
        string? entitlementType,
        string? governanceStatus,
        string? readinessStatus,
        bool? captureEnabled,
        bool? includeStale,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP GetManagementPlatformStatutoryEvidenceGovernance", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ManagementPlatformStatutoryEvidenceGovernanceEndpoints");
        var correlationId = ResolveRequestCorrelationId(httpRequest);

        activity?.SetTag("url.path", httpRequest.Path.Value);
        activity?.SetTag("http.request.method", httpRequest.Method);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("statutory_evidence_governance_scope_type", scopeType ?? string.Empty);

        try
        {
            var result = await service.ReadGovernanceAsync(
                new ManagementPlatformStatutoryEvidenceGovernanceQuery(
                    scopeType,
                    scopeReference,
                    entitlementType,
                    governanceStatus,
                    readinessStatus,
                    captureEnabled,
                    includeStale.GetValueOrDefault(true),
                    correlationId,
                    ResolveActorUserId(httpRequest),
                    ResolveActorServiceIdentityId(httpRequest)),
                cancellationToken);

            if (result.Outcome == ManagementPlatformStatutoryEvidenceGovernanceOutcome.Success && result.Governance is not null)
            {
                activity?.SetTag("evidence_governance_site_count", result.Governance.Sites.Count);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return Results.Ok(ToContract(result.Governance));
            }

            return ToErrorResult(result);
        }
        catch (NpgsqlException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Evidence governance source unavailable.");
            activity?.AddException(ex);
            logger.LogError(ex, "Management Platform statutory evidence governance source unavailable. CorrelationId: {CorrelationId}", correlationId);
            return SafeError(
                StatusCodes.Status503ServiceUnavailable,
                ManagementPlatformStatutoryEvidenceGovernanceValues.ConfigurationSourceUnavailable,
                "The statutory evidence governance source is unavailable.",
                correlationId,
                retryable: true);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Unexpected evidence governance read failure.");
            activity?.AddException(ex);
            logger.LogError(ex, "Management Platform statutory evidence governance read failed. CorrelationId: {CorrelationId}", correlationId);
            return SafeError(
                StatusCodes.Status500InternalServerError,
                ManagementPlatformStatutoryEvidenceGovernanceValues.UnexpectedFailure,
                "The statutory evidence governance read failed.",
                correlationId,
                retryable: false);
        }
    }

    private static IResult ToErrorResult(ManagementPlatformStatutoryEvidenceGovernanceResult result)
    {
        var statusCode = result.Outcome switch
        {
            ManagementPlatformStatutoryEvidenceGovernanceOutcome.InvalidFilter => StatusCodes.Status400BadRequest,
            ManagementPlatformStatutoryEvidenceGovernanceOutcome.ScopeDenied => StatusCodes.Status403Forbidden,
            ManagementPlatformStatutoryEvidenceGovernanceOutcome.EmptyAuthorizedScope => StatusCodes.Status200OK,
            ManagementPlatformStatutoryEvidenceGovernanceOutcome.ConfigurationUnavailable => StatusCodes.Status503ServiceUnavailable,
            ManagementPlatformStatutoryEvidenceGovernanceOutcome.TransientDatabaseFailure => StatusCodes.Status503ServiceUnavailable,
            ManagementPlatformStatutoryEvidenceGovernanceOutcome.MalformedCanonicalConfiguration => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        if (result.Outcome == ManagementPlatformStatutoryEvidenceGovernanceOutcome.EmptyAuthorizedScope)
        {
            return Results.Ok(new ManagementPlatformStatutoryEvidenceGovernanceResponse(
                ManagementPlatformStatutoryEvidenceGovernanceValues.ContractVersion,
                RequestedScopeType: null,
                RequestedScopeReference: null,
                result.CorrelationId,
                DateTimeOffset.UtcNow,
                ManagementPlatformStatutoryEvidenceGovernanceValues.Fresh,
                Stale: false,
                Sites: [],
                Warnings: [ManagementPlatformStatutoryEvidenceGovernanceValues.EmptyAuthorizedScope],
                Blockers: []));
        }

        return SafeError(
            statusCode,
            result.ErrorCode ?? ManagementPlatformStatutoryEvidenceGovernanceValues.UnexpectedFailure,
            result.ErrorMessage ?? "The statutory evidence governance read failed.",
            result.CorrelationId,
            result.Retryable);
    }

    private static ManagementPlatformStatutoryEvidenceGovernanceResponse ToContract(
        ManagementPlatformStatutoryEvidenceGovernance governance) =>
        new(
            governance.ContractVersion,
            governance.RequestedScopeType,
            governance.RequestedScopeReference,
            governance.CorrelationId,
            governance.EvaluatedAt,
            governance.FreshnessStatus,
            governance.Stale,
            governance.Sites.Select(site => new ManagementPlatformStatutoryEvidenceGovernanceSiteDto(
                site.SiteReference,
                site.SiteDisplayName,
                site.SiteGroupReference,
                site.SiteGroupDisplayName,
                site.EntitlementTypesSupported,
                site.GovernanceStatus,
                site.ReadinessStatus,
                site.EvidenceCaptureConfigured,
                site.EvidenceCaptureEnabled,
                site.RequiredDocumentProfiles.Select(profile => new ManagementPlatformStatutoryEvidenceDocumentProfileDto(
                    profile.ProfileCode,
                    profile.ProfileVersion,
                    profile.RetentionClassCode,
                    profile.RetentionPolicyVersion,
                    profile.RetentionPolicyStatus,
                    profile.RetentionPolicyApproved)).ToArray(),
                site.AllowedMediaTypes,
                site.MaximumUploadSizeBytes,
                site.UploadAuthorizationTtlSeconds,
                site.UploadAuthorizationReadiness,
                site.UploadFinalizationReadiness,
                site.ProtectedStorageProviderClassification,
                site.ProtectedStorageReadiness,
                site.StoragePrivateAccessPosture,
                site.ServerSideEncryptionPosture,
                site.ChecksumVerificationReadiness,
                site.ProviderMetadataVerificationReadiness,
                site.UploadLifecycleReadiness,
                site.ValidationLifecycleReadiness,
                site.MalwareScanLifecycleReadiness,
                site.ReviewabilityLifecycleReadiness,
                site.BindingLifecycleReadiness,
                site.HoldLifecycleReadiness,
                site.DeletionRequestLifecycleReadiness,
                site.MalwareScanningExecutionReadiness,
                site.SecurePreviewReadiness,
                site.RetentionPolicyReadiness,
                site.RetentionWorkerReadiness,
                site.DeletionWorkerReadiness,
                site.ObjectReconciliationReadiness,
                site.LastEvaluatedAt,
                site.ConfigurationUpdatedAt,
                site.FreshnessStatus,
                site.Stale,
                site.Retryable,
                site.SupportReference,
                site.Warnings,
                site.Blockers)).ToArray(),
            governance.Warnings,
            governance.Blockers);

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

    private static Guid? ResolveActorServiceIdentityId(HttpRequest request)
    {
        if (request.Headers.TryGetValue(CentralPmsRbacPolicyCatalog.ServiceIdentityIdHeaderName, out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var headerServiceIdentityId) &&
            headerServiceIdentityId != Guid.Empty)
        {
            return headerServiceIdentityId;
        }

        foreach (var claimType in new[] { "service_identity_id", "client_id" })
        {
            var value = request.HttpContext.User.FindFirst(claimType)?.Value;
            if (Guid.TryParse(value, out var claimServiceIdentityId) && claimServiceIdentityId != Guid.Empty)
            {
                return claimServiceIdentityId;
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
