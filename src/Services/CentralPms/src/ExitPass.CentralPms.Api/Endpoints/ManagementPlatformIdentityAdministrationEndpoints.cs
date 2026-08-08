using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using Microsoft.AspNetCore.Antiforgery;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class ManagementPlatformIdentityAdministrationEndpoints
{
    public const string RoutePrefix = "/v1/management-platform/identity";

    public static IEndpointRouteBuilder MapManagementPlatformIdentityAdministrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags("ManagementPlatformIdentityAdministration")
            .RequireAuthorization()
            .AddEndpointFilter(ValidateWebMutationAsync);

        group.MapGet("/users", async (HttpRequest request, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, string? status, string? query, int? offset, int? limit, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.ListUsersAsync(actor, new(offset ?? 0, limit ?? 50, status, query), correlation, ct)));
        group.MapPost("/users", async (HttpRequest request, CreateIdentityUserRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.CreateUserAsync(actor, new(body.Username, body.DisplayName, body.Email, body.MaskedMobileNumber, body.UserType, body.EffectiveFrom, body.EffectiveTo, body.ReasonCode, body.IdempotencyKey, correlation), ct)));
        group.MapGet("/users/{userReference:guid}", async (HttpRequest request, Guid userReference, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.GetUserAsync(actor, userReference, correlation, ct)));
        group.MapPatch("/users/{userReference:guid}", async (HttpRequest request, Guid userReference, UpdateIdentityUserRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.UpdateUserAsync(actor, new(userReference, body.DisplayName, body.Email, body.MaskedMobileNumber, body.EffectiveFrom, body.EffectiveTo, body.ExpectedRowVersion, body.ReasonCode, correlation), ct)));

        MapLifecycle(group, "activate", "ACTIVATE");
        MapLifecycle(group, "suspend", "SUSPEND");
        MapLifecycle(group, "inactivate", "INACTIVATE");
        MapLifecycle(group, "retire", "RETIRE");
        MapLifecycle(group, "lock", "LOCK");
        MapLifecycle(group, "unlock", "UNLOCK");

        group.MapPost("/users/{userReference:guid}/credential-reset-challenges", async (HttpRequest request, Guid userReference, CredentialResetChallengeRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.IssueCredentialChallengeAsync(actor, new(userReference, body.Purpose, body.ExpiresAt, body.ReasonCode, correlation), ct)));

        group.MapGet("/roles", async (HttpRequest request, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.ListRolesAsync(actor, correlation, ct)));
        group.MapGet("/permissions", async (HttpRequest request, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.ListPermissionsAsync(actor, correlation, ct)));

        group.MapPost("/users/{userReference:guid}/role-assignments", async (HttpRequest request, Guid userReference, AssignIdentityRoleRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.AssignRoleAsync(actor, new(userReference, body.RoleReference, body.EffectiveFrom, body.EffectiveTo, body.ReasonCode, body.IdempotencyKey, correlation), ct)));
        group.MapPost("/users/{userReference:guid}/role-assignments/{assignmentReference:guid}/revoke", async (HttpRequest request, Guid userReference, Guid assignmentReference, RevokeIdentityRoleRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.RevokeRoleAsync(actor, new(userReference, assignmentReference, body.ExpectedRowVersion, body.ReasonCode, correlation), ct)));
        group.MapPost("/users/{userReference:guid}/role-assignments/{assignmentReference:guid}/scope-grants", async (HttpRequest request, Guid userReference, Guid assignmentReference, GrantIdentityScopeRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.GrantScopeAsync(actor, new(userReference, assignmentReference, body.ScopeType, body.SiteReference, body.SiteGroupReference, body.EffectiveFrom, body.EffectiveTo, body.ReasonCode, body.IdempotencyKey, correlation), ct)));
        group.MapPost("/users/{userReference:guid}/role-assignments/{assignmentReference:guid}/scope-grants/{grantReference:guid}/revoke", async (HttpRequest request, Guid userReference, Guid assignmentReference, Guid grantReference, RevokeIdentityScopeRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.RevokeScopeAsync(actor, new(userReference, assignmentReference, grantReference, body.ExpectedRowVersion, body.ReasonCode, correlation), ct)));

        group.MapPost("/privileged-access-requests", async (HttpRequest request, CreatePrivilegedAccessRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.CreatePrivilegedAccessRequestAsync(actor, new(body.TargetUserReference, body.RoleReference, body.ScopeType, body.SiteReference, body.SiteGroupReference, body.EffectiveFrom, body.EffectiveTo, body.ExpiresAt, body.ReasonCode, correlation), ct)));
        group.MapGet("/privileged-access-requests/{requestReference:guid}", async (HttpRequest request, Guid requestReference, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.GetPrivilegedAccessRequestAsync(actor, requestReference, correlation, ct)));
        group.MapPost("/privileged-access-requests/{requestReference:guid}/decision", async (HttpRequest request, Guid requestReference, DecidePrivilegedAccessRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.DecidePrivilegedAccessAsync(actor, new(requestReference, body.Decision, body.ReasonCode, body.ExpectedRowVersion, correlation), ct)));

        group.MapPost("/users/{userReference:guid}/access-reviews", async (HttpRequest request, Guid userReference, ReviewIdentityAccessRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.ReviewAccessAsync(actor, new(userReference, body.AssignmentReferences, body.ScopeGrantReferences, body.Outcome, body.ReasonCode, correlation), ct)));

        group.MapGet("/users/{userReference:guid}/sessions", async (HttpRequest request, Guid userReference, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.ListSessionsAsync(actor, userReference, correlation, ct)));
        group.MapPost("/users/{userReference:guid}/sessions/{sessionReference:guid}/revoke", async (HttpRequest request, Guid userReference, Guid sessionReference, RevokeIdentitySessionRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.RevokeSessionsAsync(actor, new(userReference, sessionReference, body.ReasonCode, correlation), ct)));
        group.MapPost("/users/{userReference:guid}/sessions/revoke-all", async (HttpRequest request, Guid userReference, RevokeIdentitySessionRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.RevokeSessionsAsync(actor, new(userReference, null, body.ReasonCode, correlation), ct)));

        group.MapGet("/users/{userReference:guid}/mfa-status", async (HttpRequest request, Guid userReference, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.GetMfaStatusAsync(actor, userReference, correlation, ct)));
        group.MapPost("/users/{userReference:guid}/mfa-authenticators/reset", async (HttpRequest request, Guid userReference, ChangeIdentityMfaRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.ChangeMfaAsync(actor, new(userReference, "RESET", body.ExpectedRowVersion, body.ReasonCode, correlation), ct)));
        group.MapPost("/users/{userReference:guid}/mfa-authenticators/remove", async (HttpRequest request, Guid userReference, ChangeIdentityMfaRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.ChangeMfaAsync(actor, new(userReference, "REMOVE", body.ExpectedRowVersion, body.ReasonCode, correlation), ct)));

        group.MapGet("/users/{userReference:guid}/audit-events", async (HttpRequest request, Guid userReference, int? limit, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.ListAuditEventsAsync(actor, userReference, limit ?? 100, correlation, ct)));

        return app;
    }

    private static void MapLifecycle(RouteGroupBuilder group, string route, string transition) =>
        group.MapPost($"/users/{{userReference:guid}}/{route}", async (HttpRequest request, Guid userReference, IdentityLifecycleRequest body, IIdentityAdministrationActorAccessor actors, IManagementPlatformIdentityAdministrationService service, CancellationToken ct) =>
            await ExecuteAsync(request, actors, (actor, correlation) => service.ChangeUserLifecycleAsync(actor, new(userReference, transition, body.LockoutExpiresAt, body.ExpectedRowVersion, body.ReasonCode, correlation), ct)));

    private static async Task<IResult> ExecuteAsync<T>(
        HttpRequest request,
        IIdentityAdministrationActorAccessor actors,
        Func<IdentityAdministrationActor, Guid, Task<IdentityAdministrationResult<T>>> operation)
    {
        var correlationId = ResolveCorrelationId(request);
        var actor = actors.Current;
        if (actor is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "IDENTITY_ADMIN_UNAUTHENTICATED", "An authenticated Management Platform administrator is required.", correlationId);
        }

        try
        {
            var result = await operation(actor, correlationId);
            return result.Outcome switch
            {
                IdentityAdministrationOutcome.Success => Results.Ok(result.Value),
                IdentityAdministrationOutcome.NotFound => Error(StatusCodes.Status404NotFound, result.Classification, result.Message, result.CorrelationId),
                IdentityAdministrationOutcome.Forbidden => Error(StatusCodes.Status403Forbidden, result.Classification, result.Message, result.CorrelationId),
                IdentityAdministrationOutcome.Conflict => Error(StatusCodes.Status409Conflict, result.Classification, result.Message, result.CorrelationId),
                IdentityAdministrationOutcome.Invalid => Error(StatusCodes.Status400BadRequest, result.Classification, result.Message, result.CorrelationId),
                IdentityAdministrationOutcome.IntegrationUnavailable => Error(StatusCodes.Status503ServiceUnavailable, result.Classification, result.Message, result.CorrelationId, true),
                _ => Error(StatusCodes.Status500InternalServerError, "IDENTITY_ADMIN_FAILED", "The identity administration operation failed.", correlationId)
            };
        }
        catch (ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "IDENTITY_ADMIN_INVALID_REQUEST", "The identity administration request is invalid.", correlationId);
        }
        catch (Exception exception)
        {
            var logger = request.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ExitPass.CentralPms.Api.ManagementPlatformIdentityAdministrationEndpoints");
            logger.LogError(
                "Identity administration failed for correlation {CorrelationId} with exception type {ExceptionType}.",
                correlationId,
                exception.GetType().FullName);
            return Error(StatusCodes.Status500InternalServerError, "IDENTITY_ADMIN_FAILED", "The identity administration operation failed.", correlationId);
        }
    }

    private static IResult Error(int status, string classification, string message, Guid correlationId, bool retryable = false) =>
        Results.Json(new IdentityAdministrationErrorResponse(classification, message, correlationId, retryable), statusCode: status);

    private static async ValueTask<object?> ValidateWebMutationAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method))
        {
            return await next(context);
        }

        var correlationId = ResolveCorrelationId(request);
        var originValidator = context.HttpContext.RequestServices.GetRequiredService<IHumanAuthenticationOriginValidator>();
        if (!originValidator.IsAllowed(request))
        {
            return Error(StatusCodes.Status403Forbidden, "IDENTITY_ADMIN_ORIGIN_NOT_ALLOWED",
                "The identity administration request origin is not allowed.", correlationId);
        }

        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Error(StatusCodes.Status400BadRequest, "IDENTITY_ADMIN_CSRF_VALIDATION_FAILED",
                "The identity administration request could not be validated.", correlationId);
        }

        return await next(context);
    }

    private static Guid ResolveCorrelationId(HttpRequest request) =>
        request.Headers.TryGetValue("X-Correlation-Id", out var value) && Guid.TryParse(value.ToString(), out var parsed) && parsed != Guid.Empty
            ? parsed
            : Guid.NewGuid();
}
