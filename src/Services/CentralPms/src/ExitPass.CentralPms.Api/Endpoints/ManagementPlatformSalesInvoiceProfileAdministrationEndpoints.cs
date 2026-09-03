using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.ManagementPlatform;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Browser-safe Management Platform API boundary for POS Server-owned Sales Invoice profile administration.
/// </summary>
public static class ManagementPlatformSalesInvoiceProfileAdministrationEndpoints
{
    public const string ReadPolicy = "SalesInvoiceProfileRead";
    public const string ManagePolicy = "SalesInvoiceProfileManage";
    public const string ApprovePolicy = "SalesInvoiceProfileApprove";
    private const string CorrelationHeaderName = "X-Correlation-Id";
    private const string SiteIdHeaderName = "X-Site-Id";
    private const string ActorPrefix = "central-pms-user:";

    public static IEndpointRouteBuilder MapManagementPlatformSalesInvoiceProfileAdministrationEndpoints(this IEndpointRouteBuilder app)
    {
        var fiscalIdentities = app.MapGroup("/v1/management-platform/fiscal-identities")
            .WithTags("ManagementPlatformSalesInvoiceProfiles")
            .AddEndpointFilter(RequestValidationFilterAsync);

        fiscalIdentities.MapPost(string.Empty, CreateFiscalIdentityAsync)
            .WithName("CreateManagementPlatformFiscalIdentity")
            .Accepts<ManagementPlatformFiscalIdentityMutationRequestDto>("application/json")
            .Produces<ManagementPlatformFiscalIdentityDto>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithMetadata(new ReconciliationPolicyMetadata(ManagePolicy));

        fiscalIdentities.MapGet("/{fiscalIdentityId:guid}", GetFiscalIdentityAsync)
            .WithName("GetManagementPlatformFiscalIdentity")
            .Produces<ManagementPlatformFiscalIdentityDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy));

        fiscalIdentities.MapPatch("/{fiscalIdentityId:guid}", UpdateFiscalIdentityAsync)
            .WithName("UpdateManagementPlatformFiscalIdentity")
            .Accepts<ManagementPlatformFiscalIdentityMutationRequestDto>("application/json")
            .Produces<ManagementPlatformFiscalIdentityDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithMetadata(new ReconciliationPolicyMetadata(ManagePolicy));

        var profiles = app.MapGroup("/v1/management-platform/sales-invoice-header-profiles")
            .WithTags("ManagementPlatformSalesInvoiceProfiles")
            .AddEndpointFilter(RequestValidationFilterAsync);

        profiles.MapGet("/effective-readiness", GetEffectiveReadinessAsync)
            .WithName("GetManagementPlatformSalesInvoiceHeaderProfileEffectiveReadiness")
            .Produces<ManagementPlatformSalesInvoiceHeaderProfileReadinessDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy));

        profiles.MapPost(string.Empty, CreateProfileAsync)
            .WithName("CreateManagementPlatformSalesInvoiceHeaderProfile")
            .Accepts<ManagementPlatformSalesInvoiceHeaderProfileMutationRequestDto>("application/json")
            .Produces<ManagementPlatformSalesInvoiceHeaderProfileDto>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithMetadata(new ReconciliationPolicyMetadata(ManagePolicy));

        profiles.MapGet(string.Empty, ListProfilesAsync)
            .WithName("ListManagementPlatformSalesInvoiceHeaderProfiles")
            .Produces<IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfileDto>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy));

        profiles.MapGet("/{salesInvoiceHeaderProfileId:guid}", GetProfileAsync)
            .WithName("GetManagementPlatformSalesInvoiceHeaderProfile")
            .Produces<ManagementPlatformSalesInvoiceHeaderProfileDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy));

        profiles.MapPatch("/{salesInvoiceHeaderProfileId:guid}", UpdateDraftProfileAsync)
            .WithName("UpdateManagementPlatformSalesInvoiceHeaderProfileDraft")
            .Accepts<ManagementPlatformSalesInvoiceHeaderProfileMutationRequestDto>("application/json")
            .Produces<ManagementPlatformSalesInvoiceHeaderProfileDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithMetadata(new ReconciliationPolicyMetadata(ManagePolicy));

        profiles.MapPost("/{salesInvoiceHeaderProfileId:guid}/validate", ValidateProfileAsync)
            .WithName("ValidateManagementPlatformSalesInvoiceHeaderProfile")
            .Produces<ManagementPlatformSalesInvoiceHeaderProfileValidationDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy));

        profiles.MapPost("/{salesInvoiceHeaderProfileId:guid}/approve", ApproveProfileAsync)
            .WithName("ApproveManagementPlatformSalesInvoiceHeaderProfile")
            .Produces<ManagementPlatformSalesInvoiceHeaderProfileDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithMetadata(new ReconciliationPolicyMetadata(ApprovePolicy));

        profiles.MapPost("/{salesInvoiceHeaderProfileId:guid}/retire", RetireProfileAsync)
            .WithName("RetireManagementPlatformSalesInvoiceHeaderProfile")
            .Accepts<ManagementPlatformSalesInvoiceHeaderProfileRetirementRequestDto>("application/json")
            .Produces<ManagementPlatformSalesInvoiceHeaderProfileDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithMetadata(new ReconciliationPolicyMetadata(ApprovePolicy));

        profiles.MapGet("/{salesInvoiceHeaderProfileId:guid}/usage", GetProfileUsageAsync)
            .WithName("GetManagementPlatformSalesInvoiceHeaderProfileUsage")
            .Produces<ManagementPlatformSalesInvoiceHeaderProfileUsageDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy));

        return app;
    }

    private static async ValueTask<object?> RequestValidationFilterAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ArgumentException)
        {
            var correlationId = TryResolveCorrelationId(context.HttpContext.Request) ?? Guid.Empty;
            return BadRequest(
                "SALES_INVOICE_PROFILE_REQUEST_INVALID",
                "Management Platform Sales Invoice profile request is invalid.",
                correlationId);
        }
    }

    private static async Task<IResult> CreateFiscalIdentityAsync(
        ManagementPlatformFiscalIdentityMutationRequestDto request,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service,
        ILoggerFactory loggerFactory)
    {
        var context = ResolveContext(httpRequest);
        var result = await service.CreateFiscalIdentityAsync(ToApplication(request, context.ActorRef), context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        LogMutation(loggerFactory, "FiscalIdentityCreate", result, context, siteId: null, sitePosServerId: null, resourceId: result.Value?.FiscalIdentityId);
        return ToHttpResult(result, ToDto, createdRoute: $"/v1/management-platform/fiscal-identities/{result.Value?.FiscalIdentityId:D}");
    }

    private static async Task<IResult> GetFiscalIdentityAsync(
        Guid fiscalIdentityId,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service)
    {
        var context = ResolveContext(httpRequest);
        var result = await service.GetFiscalIdentityAsync(fiscalIdentityId, context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        return ToHttpResult(result, ToDto);
    }

    private static async Task<IResult> UpdateFiscalIdentityAsync(
        Guid fiscalIdentityId,
        ManagementPlatformFiscalIdentityMutationRequestDto request,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service,
        ILoggerFactory loggerFactory)
    {
        var context = ResolveContext(httpRequest);
        var result = await service.UpdateFiscalIdentityAsync(fiscalIdentityId, ToApplication(request, context.ActorRef), context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        LogMutation(loggerFactory, "FiscalIdentityUpdate", result, context, siteId: null, sitePosServerId: null, resourceId: fiscalIdentityId);
        return ToHttpResult(result, ToDto);
    }

    private static async Task<IResult> CreateProfileAsync(
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequestDto request,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service,
        ILoggerFactory loggerFactory)
    {
        var context = ResolveContext(httpRequest);
        if (!SiteScopeAllowed(context, request.SiteId))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        var result = await service.CreateProfileAsync(ToApplication(request, context.ActorRef), context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        LogMutation(loggerFactory, "SalesInvoiceHeaderProfileCreate", result, context, request.SiteId, request.SitePosServerId, result.Value?.SalesInvoiceHeaderProfileId);
        return ToHttpResult(result, ToDto, createdRoute: $"/v1/management-platform/sales-invoice-header-profiles/{result.Value?.SalesInvoiceHeaderProfileId:D}");
    }

    private static async Task<IResult> ListProfilesAsync(
        Guid? siteId,
        Guid? sitePosServerId,
        string? lifecycleState,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service)
    {
        var context = ResolveContext(httpRequest);
        if (siteId is { } requestedSiteId && !SiteScopeAllowed(context, requestedSiteId))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        var result = await service.ListProfilesAsync(
            new ManagementPlatformSalesInvoiceHeaderProfileListRequest(siteId, sitePosServerId, lifecycleState),
            context.AdminRequestContext,
            httpRequest.HttpContext.RequestAborted);

        if (result.Succeeded && result.Value is not null && !result.Value.All(profile => SiteScopeAllowed(context, profile.SiteId)))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        return ToHttpResult(result, value => value.Select(ToDto).ToArray());
    }

    private static async Task<IResult> GetProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service)
    {
        var context = ResolveContext(httpRequest);
        var result = await service.GetProfileAsync(salesInvoiceHeaderProfileId, context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        if (result.Succeeded && result.Value is not null && !SiteScopeAllowed(context, result.Value.SiteId))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        return ToHttpResult(result, ToDto);
    }

    private static async Task<IResult> UpdateDraftProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequestDto request,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service,
        ILoggerFactory loggerFactory)
    {
        var context = ResolveContext(httpRequest);
        if (!SiteScopeAllowed(context, request.SiteId))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        var current = await service.GetProfileAsync(salesInvoiceHeaderProfileId, context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        if (!current.Succeeded)
        {
            return ToHttpResult(current, ToDto);
        }

        if (current.Value is not null && !SiteScopeAllowed(context, current.Value.SiteId))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        var result = await service.UpdateDraftProfileAsync(salesInvoiceHeaderProfileId, ToApplication(request, context.ActorRef), context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        LogMutation(loggerFactory, "SalesInvoiceHeaderProfileDraftUpdate", result, context, request.SiteId, request.SitePosServerId, salesInvoiceHeaderProfileId);
        return ToHttpResult(result, ToDto);
    }

    private static async Task<IResult> ValidateProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service)
    {
        var context = ResolveContext(httpRequest);
        var current = await service.GetProfileAsync(salesInvoiceHeaderProfileId, context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        if (!current.Succeeded)
        {
            return ToHttpResult(current, ToDto);
        }

        if (current.Value is not null && !SiteScopeAllowed(context, current.Value.SiteId))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        var result = await service.ValidateProfileAsync(salesInvoiceHeaderProfileId, context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        return ToHttpResult(result, ToDto);
    }

    private static async Task<IResult> ApproveProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service,
        ILoggerFactory loggerFactory)
    {
        var context = ResolveContext(httpRequest);
        var current = await service.GetProfileAsync(salesInvoiceHeaderProfileId, context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        if (!current.Succeeded)
        {
            return ToHttpResult(current, ToDto);
        }

        if (current.Value is not null && !SiteScopeAllowed(context, current.Value.SiteId))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        var result = await service.ApproveProfileAsync(salesInvoiceHeaderProfileId, new ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest(context.ActorRef), context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        LogMutation(loggerFactory, "SalesInvoiceHeaderProfileApprove", result, context, current.Value?.SiteId, current.Value?.SitePosServerId, salesInvoiceHeaderProfileId);
        return ToHttpResult(result, ToDto);
    }

    private static async Task<IResult> RetireProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileRetirementRequestDto? request,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service,
        ILoggerFactory loggerFactory)
    {
        var context = ResolveContext(httpRequest);
        var current = await service.GetProfileAsync(salesInvoiceHeaderProfileId, context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        if (!current.Succeeded)
        {
            return ToHttpResult(current, ToDto);
        }

        if (current.Value is not null && !SiteScopeAllowed(context, current.Value.SiteId))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        var result = await service.RetireProfileAsync(
            salesInvoiceHeaderProfileId,
            new ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest(context.ActorRef, request?.RetireAt),
            context.AdminRequestContext,
            httpRequest.HttpContext.RequestAborted);
        LogMutation(loggerFactory, "SalesInvoiceHeaderProfileRetire", result, context, current.Value?.SiteId, current.Value?.SitePosServerId, salesInvoiceHeaderProfileId);
        return ToHttpResult(result, ToDto);
    }

    private static async Task<IResult> GetEffectiveReadinessAsync(
        Guid? siteId,
        Guid? sitePosServerId,
        DateTimeOffset? effectiveAt,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service)
    {
        var context = ResolveContext(httpRequest);
        if (siteId is null || siteId.Value == Guid.Empty ||
            sitePosServerId is null || sitePosServerId.Value == Guid.Empty ||
            effectiveAt is null)
        {
            return BadRequest("SALES_INVOICE_PROFILE_REQUEST_INVALID", "Site, Site POS Server, and effective timestamp are required.", context.CorrelationId);
        }

        if (!SiteScopeAllowed(context, siteId.Value))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        var result = await service.GetEffectiveReadinessAsync(
            new ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest(siteId.Value, sitePosServerId.Value, effectiveAt.Value),
            context.AdminRequestContext,
            httpRequest.HttpContext.RequestAborted);
        return ToHttpResult(result, ToDto);
    }

    private static async Task<IResult> GetProfileUsageAsync(
        Guid salesInvoiceHeaderProfileId,
        HttpRequest httpRequest,
        ISalesInvoiceProfileAdministrationService service)
    {
        var context = ResolveContext(httpRequest);
        var current = await service.GetProfileAsync(salesInvoiceHeaderProfileId, context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        if (!current.Succeeded)
        {
            return ToHttpResult(current, ToDto);
        }

        if (current.Value is not null && !SiteScopeAllowed(context, current.Value.SiteId))
        {
            return ForbiddenSiteScope(context.CorrelationId);
        }

        var result = await service.GetProfileUsageAsync(salesInvoiceHeaderProfileId, context.AdminRequestContext, httpRequest.HttpContext.RequestAborted);
        return ToHttpResult(result, ToDto);
    }

    private static ManagementPlatformRequestContext ResolveContext(HttpRequest request)
    {
        var correlationId = ResolveGuid(request, CorrelationHeaderName, required: false) ?? Guid.NewGuid();
        var userId = ResolveGuid(request, CentralPmsRbacPolicyCatalog.UserIdHeaderName, required: false) ??
            ResolveClaimGuid(request, System.Security.Claims.ClaimTypes.NameIdentifier, "sub", "user_id");
        var siteId = ResolveGuid(request, SiteIdHeaderName, required: false) ??
            ResolveClaimGuid(request, "site_id");

        if (userId is null)
        {
            throw new ArgumentException("Authenticated Management Platform user identity is required.", CentralPmsRbacPolicyCatalog.UserIdHeaderName);
        }

        return new ManagementPlatformRequestContext(
            ActorRef: $"{ActorPrefix}{userId.Value:D}",
            AuthorizedSiteId: siteId,
            CorrelationId: correlationId,
            AdminRequestContext: new ManagementPlatformPosServerAdminRequestContext(correlationId));
    }

    private static Guid? ResolveGuid(HttpRequest request, string headerName, bool required)
    {
        if (!request.Headers.TryGetValue(headerName, out var value) || string.IsNullOrWhiteSpace(value.ToString()))
        {
            if (required)
            {
                throw new ArgumentException($"{headerName} header is required.", headerName);
            }

            return null;
        }

        if (!Guid.TryParse(value.ToString(), out var parsed) || parsed == Guid.Empty)
        {
            throw new ArgumentException($"{headerName} header must be a valid GUID.", headerName);
        }

        return parsed;
    }

    private static Guid? TryResolveCorrelationId(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(CorrelationHeaderName, out var value) || string.IsNullOrWhiteSpace(value.ToString()))
        {
            return null;
        }

        return Guid.TryParse(value.ToString(), out var parsed) && parsed != Guid.Empty ? parsed : null;
    }

    private static Guid? ResolveClaimGuid(HttpRequest request, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            if (Guid.TryParse(request.HttpContext.User.FindFirst(claimType)?.Value, out var value) && value != Guid.Empty)
            {
                return value;
            }
        }

        return null;
    }

    private static bool SiteScopeAllowed(ManagementPlatformRequestContext context, Guid siteId) =>
        context.AuthorizedSiteId is null || context.AuthorizedSiteId.Value == siteId;

    private static IResult ToHttpResult<TValue, TDto>(
        PosServerSalesInvoiceProfileAdminResult<TValue> result,
        Func<TValue, TDto> map,
        string? createdRoute = null)
    {
        if (result.Succeeded && result.Value is not null)
        {
            var dto = map(result.Value);
            return createdRoute is not null
                ? Results.Created(createdRoute, dto)
                : Results.Ok(dto);
        }

        return Failure(result);
    }

    private static IResult Failure<T>(PosServerSalesInvoiceProfileAdminResult<T> result)
    {
        var statusCode = result.Outcome switch
        {
            PosServerSalesInvoiceProfileAdminOutcome.InvalidRequest => StatusCodes.Status400BadRequest,
            PosServerSalesInvoiceProfileAdminOutcome.Disabled => StatusCodes.Status503ServiceUnavailable,
            PosServerSalesInvoiceProfileAdminOutcome.InvalidConfiguration => StatusCodes.Status503ServiceUnavailable,
            PosServerSalesInvoiceProfileAdminOutcome.NotFound => StatusCodes.Status404NotFound,
            PosServerSalesInvoiceProfileAdminOutcome.Conflict => StatusCodes.Status409Conflict,
            PosServerSalesInvoiceProfileAdminOutcome.ValidationFailure => StatusCodes.Status422UnprocessableEntity,
            PosServerSalesInvoiceProfileAdminOutcome.Throttled => StatusCodes.Status429TooManyRequests,
            PosServerSalesInvoiceProfileAdminOutcome.Timeout => StatusCodes.Status504GatewayTimeout,
            PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable => StatusCodes.Status503ServiceUnavailable,
            PosServerSalesInvoiceProfileAdminOutcome.NetworkFailure => StatusCodes.Status503ServiceUnavailable,
            PosServerSalesInvoiceProfileAdminOutcome.AuthenticationFailed => StatusCodes.Status502BadGateway,
            PosServerSalesInvoiceProfileAdminOutcome.PermissionDenied => StatusCodes.Status502BadGateway,
            PosServerSalesInvoiceProfileAdminOutcome.MalformedResponse => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status502BadGateway
        };

        var error = result.Error;
        var code = result.Outcome == PosServerSalesInvoiceProfileAdminOutcome.Disabled
            ? "SALES_INVOICE_PROFILE_ADMINISTRATION_DISABLED"
            : NormalizeErrorCode(error?.Code);

        return Results.Json(
            new ErrorResponse
            {
                ErrorCode = code,
                Message = SafeErrorMessage(result.Outcome, error?.Message),
                CorrelationId = result.CorrelationId,
                Retryable = result.Outcome is PosServerSalesInvoiceProfileAdminOutcome.Throttled
                    or PosServerSalesInvoiceProfileAdminOutcome.Timeout
                    or PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable
                    or PosServerSalesInvoiceProfileAdminOutcome.NetworkFailure
            },
            statusCode: statusCode);
    }

    private static IResult BadRequest(string code, string message, Guid correlationId) =>
        Results.BadRequest(new ErrorResponse
        {
            ErrorCode = code,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        });

    private static IResult ForbiddenSiteScope(Guid correlationId) =>
        Results.Json(
            new ErrorResponse
            {
                ErrorCode = "SALES_INVOICE_PROFILE_SITE_SCOPE_FORBIDDEN",
                Message = "The caller is not authorized for the requested Site scope.",
                CorrelationId = correlationId,
                Retryable = false
            },
            statusCode: StatusCodes.Status403Forbidden);

    private static string NormalizeErrorCode(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? "SALES_INVOICE_PROFILE_ADMINISTRATION_FAILED"
            : code.ToUpperInvariant();

    private static string SafeErrorMessage(PosServerSalesInvoiceProfileAdminOutcome outcome, string? downstreamMessage) =>
        outcome switch
        {
            PosServerSalesInvoiceProfileAdminOutcome.Disabled => "Sales Invoice profile administration is disabled.",
            PosServerSalesInvoiceProfileAdminOutcome.InvalidConfiguration => "Sales Invoice profile administration is unavailable.",
            PosServerSalesInvoiceProfileAdminOutcome.AuthenticationFailed => "The downstream administration service rejected Central PMS authentication.",
            PosServerSalesInvoiceProfileAdminOutcome.PermissionDenied => "The downstream administration service rejected Central PMS authorization.",
            PosServerSalesInvoiceProfileAdminOutcome.MalformedResponse => "The downstream administration response could not be mapped safely.",
            PosServerSalesInvoiceProfileAdminOutcome.PosServerUnavailable => "The downstream administration service is unavailable.",
            PosServerSalesInvoiceProfileAdminOutcome.NetworkFailure => "The downstream administration service could not be reached.",
            PosServerSalesInvoiceProfileAdminOutcome.Timeout => "The downstream administration service timed out.",
            _ => string.IsNullOrWhiteSpace(downstreamMessage) ? "Sales Invoice profile administration request failed." : downstreamMessage
        };

    private static ManagementPlatformFiscalIdentityMutationRequest ToApplication(
        ManagementPlatformFiscalIdentityMutationRequestDto request,
        string actorRef) =>
        new(
            request.RegisteredBusinessName,
            request.RegisteredBusinessAddress,
            request.Tin,
            request.TaxpayerPosture,
            actorRef);

    private static ManagementPlatformSalesInvoiceHeaderProfileMutationRequest ToApplication(
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequestDto request,
        string actorRef) =>
        new(
            request.FiscalIdentityId,
            request.SiteId,
            request.SitePosServerId,
            request.ProfileVersion,
            request.TemplateVersion,
            request.PresentationVersion,
            request.PosSerialNumber,
            request.MachineIdentificationNumber,
            request.ParkingLocationDisplay,
            request.BirAccreditationNumber,
            request.BirAccreditationIssuedDate,
            request.BirAccreditationValidUntil,
            request.PtuNumber,
            request.PtuIssuedDate,
            request.SalesInvoiceLegalStatement,
            request.CustomerServiceFooter,
            request.EffectiveFrom,
            request.EffectiveTo,
            actorRef,
            request.SupplierDeveloperRegisteredName,
            request.SupplierDeveloperAddress,
            request.SupplierDeveloperTin);

    private static ManagementPlatformFiscalIdentityDto ToDto(ManagementPlatformFiscalIdentity value) =>
        new(
            value.FiscalIdentityId,
            value.RegisteredBusinessName,
            value.RegisteredBusinessAddress,
            value.Tin,
            value.TaxpayerPosture,
            value.LifecycleStatus,
            value.CreatedAt,
            value.UpdatedAt,
            value.CreatedByRef,
            value.UpdatedByRef);

    private static ManagementPlatformSalesInvoiceHeaderProfileDto ToDto(ManagementPlatformSalesInvoiceHeaderProfile value) =>
        new(
            value.SalesInvoiceHeaderProfileId,
            value.FiscalIdentityId,
            value.SiteId,
            value.SitePosServerId,
            value.ProfileVersion,
            value.TemplateVersion,
            value.PresentationVersion,
            value.PosSerialNumber,
            value.MachineIdentificationNumber,
            value.ParkingLocationDisplay,
            value.BirAccreditationNumber,
            value.BirAccreditationIssuedDate,
            value.BirAccreditationValidUntil,
            value.PtuNumber,
            value.PtuIssuedDate,
            value.SalesInvoiceLegalStatement,
            value.CustomerServiceFooter,
            value.EffectiveFrom,
            value.EffectiveTo,
            value.LifecycleState,
            value.ApprovedAt,
            value.ApprovedByRef,
            value.RetiredAt,
            value.CreatedAt,
            value.UpdatedAt,
            value.SupplierDeveloperRegisteredName,
            value.SupplierDeveloperAddress,
            value.SupplierDeveloperTin);

    private static ManagementPlatformSalesInvoiceHeaderProfileValidationDto ToDto(ManagementPlatformSalesInvoiceHeaderProfileValidation value) =>
        new(
            value.SalesInvoiceHeaderProfileId,
            value.LifecycleState,
            value.IsComplete,
            value.MissingOrInvalidFieldCodes,
            value.ValidationMessages,
            value.TemplateVersionPosture,
            value.PresentationVersionPosture,
            value.EffectiveWindowPosture,
            value.OverlapPosture,
            value.FiscalIdentityPosture,
            value.ValidatedAt,
            value.CorrelationId);

    private static ManagementPlatformSalesInvoiceHeaderProfileReadinessDto ToDto(ManagementPlatformSalesInvoiceHeaderProfileReadiness value) =>
        new(
            value.SiteId,
            value.SitePosServerId,
            value.EffectiveAt,
            value.ResolutionStatus,
            value.EffectiveProfileId,
            value.ProfileVersion,
            value.FiscalIdentityId,
            value.LifecycleState,
            value.IsComplete,
            value.EnforcementRequired,
            value.MissingOrInvalidFieldCodes,
            value.BirAccreditationValidityPosture,
            value.PtuCompletenessPosture,
            value.SupportedVersionPosture,
            value.OverlapOrAmbiguityPosture,
            value.LastUpdatedAt,
            value.CorrelationId);

    private static ManagementPlatformSalesInvoiceHeaderProfileUsageDto ToDto(ManagementPlatformSalesInvoiceHeaderProfileUsage value) =>
        new(
            value.SalesInvoiceHeaderProfileId,
            value.ProfileVersion,
            value.FiscalIdentityId,
            value.FirstSnapshotAt,
            value.LatestSnapshotAt,
            value.FiscalDocumentCount,
            value.SafeFiscalDocumentIdentifiers,
            value.DestructiveMutationBlocked,
            value.CorrelationId);

    private static void LogMutation<T>(
        ILoggerFactory loggerFactory,
        string operation,
        PosServerSalesInvoiceProfileAdminResult<T> result,
        ManagementPlatformRequestContext context,
        Guid? siteId,
        Guid? sitePosServerId,
        Guid? resourceId)
    {
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.ManagementPlatformSalesInvoiceProfileAdministration");
        logger.LogInformation(
            "Management Platform Sales Invoice profile mutation {Operation} completed with outcome {Outcome}. ResourceId={ResourceId}; SiteId={SiteId}; SitePosServerId={SitePosServerId}; CorrelationId={CorrelationId}; ActorRef={ActorRef}",
            operation,
            result.Outcome,
            resourceId,
            siteId,
            sitePosServerId,
            context.CorrelationId,
            context.ActorRef);
    }

    private sealed record ManagementPlatformRequestContext(
        string ActorRef,
        Guid? AuthorizedSiteId,
        Guid CorrelationId,
        ManagementPlatformPosServerAdminRequestContext AdminRequestContext);
}
