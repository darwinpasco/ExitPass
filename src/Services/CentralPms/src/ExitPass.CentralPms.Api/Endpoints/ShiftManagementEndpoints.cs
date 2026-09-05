using System.Security.Claims;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.ShiftManagement;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.ShiftManagement;
using Microsoft.AspNetCore.Antiforgery;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class ShiftManagementEndpoints
{
    public static IEndpointRouteBuilder MapShiftManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var own = app.MapGroup("/v1/shift-management")
            .WithTags("ShiftManagement")
            .RequireAuthorization();
        own.MapGet("/authorized-sites", AuthorizedSitesAsync);
        own.MapGet("/own/current", CurrentOwnAsync);
        own.MapPost("/own/start", StartOwnAsync);
        own.MapPost("/own/{shiftId:guid}/resume", ResumeOwnAsync);
        own.MapPost("/own/{shiftId:guid}/close", CloseOwnAsync);

        var management = app.MapGroup("/v1/operator-console/shift-management")
            .WithTags("ShiftManagement")
            .RequireAuthorization();
        management.MapGet("/shifts", ListAsync);
        management.MapGet("/shifts/{shiftId:guid}", GetAsync);
        management.MapPost("/shifts/{shiftId:guid}/exception-close", ExceptionCloseAsync)
            .WithMetadata(new ReconciliationPolicyMetadata("ShiftManagementManage"));
        return app;
    }

    private static async Task<IResult> AuthorizedSitesAsync(HttpRequest request, IShiftManagementService service, CancellationToken cancellationToken)
    {
        var actor = Actor(request);
        var sites = await service.ListAuthorizedSitesAsync(actor, cancellationToken);
        return Results.Ok(sites.Select(site => new AuthorizedShiftSiteResponse(site.SiteId, site.SiteGroupId, site.SiteCode, site.SiteName, site.SiteGroupCode, site.SiteGroupName)));
    }

    private static async Task<IResult> CurrentOwnAsync(HttpRequest request, IShiftManagementService service, CancellationToken cancellationToken)
    {
        var shift = await service.GetCurrentOwnAsync(Actor(request), cancellationToken);
        return shift is null ? Results.NoContent() : Results.Ok(ToResponse(shift));
    }

    private static async Task<IResult> StartOwnAsync(StartOwnShiftRequest body, HttpRequest request, IShiftManagementService service, IAntiforgery antiforgery, CancellationToken cancellationToken)
    {
        var csrfFailure = await ValidateCsrfAsync(request, antiforgery);
        return csrfFailure ?? ToResult(await service.StartOwnAsync(new StartOwnShiftCommand(Actor(request), body.SiteId, body.TerminalReference), cancellationToken), StatusCodes.Status201Created);
    }

    private static async Task<IResult> ResumeOwnAsync(Guid shiftId, HttpRequest request, IShiftManagementService service, IAntiforgery antiforgery, CancellationToken cancellationToken)
    {
        var csrfFailure = await ValidateCsrfAsync(request, antiforgery);
        return csrfFailure ?? ToResult(await service.ResumeOwnAsync(Actor(request), shiftId, cancellationToken));
    }

    private static async Task<IResult> CloseOwnAsync(Guid shiftId, HttpRequest request, IShiftManagementService service, IAntiforgery antiforgery, CancellationToken cancellationToken)
    {
        var csrfFailure = await ValidateCsrfAsync(request, antiforgery);
        return csrfFailure ?? ToResult(await service.CloseOwnAsync(Actor(request), shiftId, cancellationToken));
    }

    private static async Task<IResult> ListAsync(string? view, Guid? siteId, Guid? staffUserId, int? limit, HttpRequest request, IShiftManagementService service, CancellationToken cancellationToken)
    {
        var actor = Actor(request);
        var items = await service.ListAsync(new ShiftListQuery(actor, NormalizeView(view), siteId, staffUserId, limit ?? 50), cancellationToken);
        return Results.Ok(new ShiftListResponse(items.Select(ToResponse).ToArray(), actor.CorrelationId));
    }

    private static async Task<IResult> GetAsync(Guid shiftId, HttpRequest request, IShiftManagementService service, CancellationToken cancellationToken) =>
        ToResult(await service.GetAsync(Actor(request), shiftId, cancellationToken));

    private static async Task<IResult> ExceptionCloseAsync(Guid shiftId, SupervisorCloseShiftRequest body, HttpRequest request, IShiftManagementService service, IAntiforgery antiforgery, CancellationToken cancellationToken)
    {
        var csrfFailure = await ValidateCsrfAsync(request, antiforgery);
        return csrfFailure ?? ToResult(await service.ExceptionCloseAsync(Actor(request), shiftId, body.Reason, cancellationToken));
    }

    private static async Task<IResult?> ValidateCsrfAsync(HttpRequest request, IAntiforgery antiforgery)
    {
        if (!string.Equals(request.HttpContext.User.Identity?.AuthenticationType, HumanSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal))
            return null;
        try
        {
            await antiforgery.ValidateRequestAsync(request.HttpContext);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new ErrorResponse
            {
                ErrorCode = "CSRF_VALIDATION_FAILED",
                Message = "The secure shift request could not be validated.",
                CorrelationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(request),
                Retryable = false
            });
        }
    }

    private static ShiftManagementActor Actor(HttpRequest request)
    {
        if (!Guid.TryParse(request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId == Guid.Empty)
            throw new BadHttpRequestException("Authenticated user identity is required.", StatusCodes.Status401Unauthorized);
        return new ShiftManagementActor(
            userId,
            HumanSessionAuthenticationHandler.ResolveGuid(request.HttpContext.User.FindFirstValue("device_service_identity_id")),
            HumanSessionAuthenticationHandler.ResolveGuid(request.HttpContext.User.FindFirstValue("operator_device_binding_id")),
            HumanSessionAuthenticationHandler.ResolveCorrelationId(request));
    }

    private static IResult ToResult(ShiftOperationResult result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.Succeeded) return Results.Json(new ShiftOperationResponse(true, ToResponse(result.Shift!), null, result.CorrelationId), statusCode: successStatus);
        var status = result.ErrorCode switch
        {
            ShiftManagementFailureCodes.ShiftNotFound => StatusCodes.Status404NotFound,
            ShiftManagementFailureCodes.ActiveShiftAlreadyExists or ShiftManagementFailureCodes.CloseBlockedByOpenCustody or ShiftManagementFailureCodes.ShiftNotActive => StatusCodes.Status409Conflict,
            ShiftManagementFailureCodes.CloseReasonRequired => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status403Forbidden
        };
        return Results.Json(new ErrorResponse
        {
            ErrorCode = result.ErrorCode ?? "SHIFT_OPERATION_FAILED",
            Message = Message(result.ErrorCode),
            CorrelationId = result.CorrelationId,
            Retryable = false
        }, statusCode: status);
    }

    private static ShiftSummaryResponse ToResponse(ShiftSummary shift)
    {
        var end = shift.ClosedAt ?? DateTimeOffset.UtcNow;
        return new ShiftSummaryResponse(
            shift.ShiftId, shift.ShiftReference, shift.OperatorUserId, shift.Username, shift.DisplayName, shift.UserType,
            shift.RoleCodes, shift.SiteId, shift.SiteGroupId, shift.SiteCode, shift.SiteName, shift.SiteGroupCode,
            shift.SiteGroupName, shift.OperatorDeviceBindingId, shift.DeviceName, shift.TerminalReference,
            shift.OpenedAt, shift.ClosedAt, Math.Max(0, (long)(end - shift.OpenedAt).TotalSeconds), shift.Status,
            shift.CashCustodyStatus, shift.OpeningCashMinorUnits, shift.CashTransactionCount, shift.CashCollectedMinorUnits,
            shift.CloseType, shift.ClosedByUserId, shift.ClosingActorName, shift.CloseReason, shift.CreatedAt, shift.UpdatedAt);
    }

    private static string NormalizeView(string? value) =>
        string.Equals(value, "recently-closed", StringComparison.OrdinalIgnoreCase) ? "RECENTLY_CLOSED" : "OPEN";

    private static string Message(string? errorCode) => errorCode switch
    {
        ShiftManagementFailureCodes.ActiveShiftAlreadyExists => "An active shift already exists for this user.",
        ShiftManagementFailureCodes.CloseBlockedByOpenCustody => "Close the cash custody session before closing this shift.",
        ShiftManagementFailureCodes.CloseReasonRequired => "A reason is required for supervisor exception close.",
        ShiftManagementFailureCodes.SiteNotAuthorized => "The selected Site is outside the user's current authorization.",
        ShiftManagementFailureCodes.DeviceSiteMismatch => "The device assignment does not match the shift Site.",
        ShiftManagementFailureCodes.ShiftNotFound => "The shift was not found in the authorized Site scope.",
        _ => "The shift operation is not authorized in the current context."
    };
}
