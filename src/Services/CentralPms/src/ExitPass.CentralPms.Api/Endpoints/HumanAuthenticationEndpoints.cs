using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.HumanAuthentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class HumanAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapHumanAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        var web = app.MapGroup("/v1/human-authentication").WithTags("HumanAuthentication");
        web.MapPost("/login", LoginAsync).DisableAntiforgery();
        web.MapGet("/session", CurrentSessionAsync);
        web.MapPost("/session/continue", ContinueAsync);
        web.MapPost("/logout", LogoutAsync);
        web.MapPost("/logout-all", LogoutAllAsync);
        web.MapPost("/reauthenticate", FreshAuthenticateAsync);
        web.MapPost("/password/change", ChangePasswordAsync);
        web.MapPost("/password-reset-requests", RequestPasswordResetAsync).DisableAntiforgery();
        web.MapPost("/password-resets", ResetPasswordAsync).DisableAntiforgery();
        web.MapPost("/activations", ActivateAsync).DisableAntiforgery();
        web.MapPost("/totp/enrollment", BeginTotpEnrollmentAsync);
        web.MapPost("/totp/enrollment/confirm", ConfirmTotpEnrollmentAsync);

        var apt = app.MapGroup("/v1/apt/human-sessions").WithTags("AptHumanAuthentication");
        apt.MapPost("", CreateAptSessionAsync).DisableAntiforgery().RequireInternalServiceMtls();
        apt.MapGet("/{sessionReference:guid}", GetAptSessionAsync).RequireInternalServiceMtls();
        apt.MapPost("/{sessionReference:guid}/continue", ContinueAptSessionAsync).DisableAntiforgery().RequireInternalServiceMtls();
        apt.MapPost("/{sessionReference:guid}/reauthenticate", ReauthenticateAptSessionAsync).DisableAntiforgery().RequireInternalServiceMtls();
        apt.MapPost("/{sessionReference:guid}/logout", LogoutAptSessionAsync).DisableAntiforgery().RequireInternalServiceMtls();
        return app;
    }

    private static async Task<IResult> LoginAsync(HumanLoginRequest request, HttpRequest httpRequest, HttpResponse response, IHumanAuthenticationService service, IHumanAuthenticationOriginValidator originValidator, IAntiforgery antiforgery, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, IOperatorConsoleOperatingContextService operatingContextService, CancellationToken cancellationToken)
    {
        if (!originValidator.IsAllowed(httpRequest) || !HumanSessionAudiences.IsWeb(NormalizeAudience(request.Audience))) return SafeFailure(400, "INVALID_LOGIN_REQUEST", HumanSessionAuthenticationHandler.ResolveCorrelationId(httpRequest));
        var context = BuildContext(httpRequest, options.Value, tokens, null, null);
        var result = await service.LoginAsync(request.Username, request.Password, request.Audience, request.TotpCode, context, cancellationToken);
        if (result.Response.Authenticated &&
            result.Response.Session is { Audience: HumanSessionAudiences.OperatorConsole } operatorSession &&
            result.InternalHumanSessionId.HasValue &&
            OperatorConsoleDeviceBindingCookie.Read(httpRequest) is not null)
        {
            var binding = await operatingContextService.BindSessionAsync(
                result.InternalHumanSessionId.Value,
                operatorSession.UserReference,
                operatorSession.SiteReferences,
                operatorSession.SiteGroupReferences,
                operatorSession.HasGlobalScope,
                OperatorConsoleDeviceBindingCookie.Read(httpRequest),
                result.Response.CorrelationId,
                cancellationToken);
            if (binding.Succeeded && binding.Context is { } bound)
            {
                var enrichedSession = operatorSession with
                {
                    OperatorDeviceBindingReference = bound.OperatorDeviceBindingId,
                    OperatorShiftReference = bound.OperatorShiftId,
                    EffectiveSiteReference = bound.SiteId,
                    EffectiveSiteGroupReference = bound.SiteGroupId,
                    AuthorizationEpoch = bound.AuthorizationEpoch,
                    CredentialVersion = bound.CredentialVersion
                };
                result = result with { Response = result.Response with { Session = enrichedSession } };
            }
        }
        if (result.Response.Authenticated && result.Credential is not null)
        {
            SetWebSessionCookie(response, result.Credential.SerializedToken, options.Value);
            httpRequest.HttpContext.User = HumanSessionAuthenticationHandler.CreatePrincipal(result.Response.Session!, result.InternalHumanSessionId);
            SetAntiforgeryResponseToken(httpRequest.HttpContext, response, antiforgery);
        }
        SetNoStore(response);
        return Results.Json(result.Response with { AptSessionToken = null }, statusCode: result.HttpStatusCode);
    }

    private static async Task<IResult> CurrentSessionAsync(HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, IAntiforgery antiforgery, IOperatorConsoleOperatingContextService operatingContextService, CancellationToken cancellationToken)
    {
        var token = HumanSessionAuthenticationHandler.ResolveToken(request, options.Value);
        if (token is null)
        {
            return SafeFailure(StatusCodes.Status401Unauthorized, "SESSION_REQUIRED", HumanSessionAuthenticationHandler.ResolveCorrelationId(request));
        }

        var authContext = BuildContext(request, options.Value, tokens, null, null);
        var result = await service.ResolveSessionAsync(token, null, null, authContext, true, cancellationToken);
        result = await EnrichOperatorSessionAsync(result, request, operatingContextService, cancellationToken);
        if (result.HttpStatusCode == StatusCodes.Status403Forbidden && !result.Response.Authenticated)
        {
            return SafeFailure(StatusCodes.Status403Forbidden, result.Response.ErrorCode ?? OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked, result.Response.CorrelationId);
        }
        SetNoStore(response);
        SetAntiforgeryResponseToken(request.HttpContext, response, antiforgery);
        return Results.Json(result.Response with { AptSessionToken = null }, statusCode: result.HttpStatusCode);
    }

    private static Task<IResult> ContinueAsync(HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, IAntiforgery antiforgery, IOperatorConsoleOperatingContextService operatingContextService, CancellationToken cancellationToken) =>
        ExecuteWebMutationAsync(request, response, service, tokens, options.Value, antiforgery, async (token, context) =>
        {
            var result = await service.ContinueSessionAsync(token, context, cancellationToken);
            result = await BindRotatedOperatorSessionAsync(result, request, operatingContextService, cancellationToken);
            if (result.Response.Authenticated && result.Credential is not null) SetWebSessionCookie(response, result.Credential.SerializedToken, options.Value);
            else
            {
                if (result.Credential is not null) await service.LogoutAsync(result.Credential.SerializedToken, context, cancellationToken);
                DeleteWebSessionCookie(response, options.Value);
            }
            return result;
        });

    private static Task<IResult> LogoutAsync(HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        ExecuteWebMutationAsync(request, response, service, tokens, options.Value, antiforgery, async (token, context) =>
        {
            var result = await service.LogoutAsync(token, context, cancellationToken);
            DeleteWebSessionCookie(response, options.Value);
            return result;
        });

    private static Task<IResult> LogoutAllAsync(HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        ExecuteWebMutationAsync(request, response, service, tokens, options.Value, antiforgery, async (token, context) =>
        {
            var result = await service.LogoutAllAsync(token, context, cancellationToken);
            DeleteWebSessionCookie(response, options.Value);
            return result;
        });

    private static Task<IResult> FreshAuthenticateAsync(HumanFreshAuthenticationRequest body, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, IAntiforgery antiforgery, IOperatorConsoleOperatingContextService operatingContextService, CancellationToken cancellationToken) =>
        ExecuteWebMutationAsync(request, response, service, tokens, options.Value, antiforgery, async (token, context) =>
        {
            var result = await service.FreshAuthenticateAsync(token, body.Password, body.TotpCode, context, cancellationToken);
            result = await BindRotatedOperatorSessionAsync(result, request, operatingContextService, cancellationToken);
            if (result.Response.Authenticated && result.Credential is not null) SetWebSessionCookie(response, result.Credential.SerializedToken, options.Value);
            else
            {
                if (result.Credential is not null) await service.LogoutAsync(result.Credential.SerializedToken, context, cancellationToken);
                DeleteWebSessionCookie(response, options.Value);
            }
            return result;
        });

    private static Task<IResult> ChangePasswordAsync(HumanPasswordChangeRequest body, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, IAntiforgery antiforgery, IOperatorConsoleOperatingContextService operatingContextService, CancellationToken cancellationToken) =>
        ExecuteWebMutationAsync(request, response, service, tokens, options.Value, antiforgery, async (token, context) =>
        {
            var result = await service.ChangePasswordAsync(token, body.CurrentPassword, body.NewPassword, body.TotpCode, context, cancellationToken);
            result = await BindRotatedOperatorSessionAsync(result, request, operatingContextService, cancellationToken);
            if (result.Response.Authenticated && result.Credential is not null) SetWebSessionCookie(response, result.Credential.SerializedToken, options.Value);
            else
            {
                if (result.Credential is not null) await service.LogoutAsync(result.Credential.SerializedToken, context, cancellationToken);
                DeleteWebSessionCookie(response, options.Value);
            }
            return result;
        });

    private static async Task<IResult> RequestPasswordResetAsync(HumanPasswordResetStartRequest body, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, IHumanAuthenticationOriginValidator originValidator, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, CancellationToken cancellationToken)
    {
        var correlationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(request);
        if (!originValidator.IsAllowed(request)) return SafeFailure(400, "INVALID_REQUEST_ORIGIN", correlationId);
        await service.RequestPasswordResetAsync(body.Username, BuildContext(request, options.Value, tokens, null, null), cancellationToken);
        SetNoStore(response);
        return Results.Accepted(value: new HumanChallengeAcceptedResponse("REQUEST_ACCEPTED", correlationId));
    }

    private static async Task<IResult> ResetPasswordAsync(HumanPasswordResetRequest body, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, IHumanAuthenticationOriginValidator originValidator, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, CancellationToken cancellationToken)
    {
        var correlationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(request);
        if (!originValidator.IsAllowed(request)) return SafeFailure(400, "INVALID_REQUEST_ORIGIN", correlationId);
        var result = await service.ResetPasswordAsync(body.ChallengeReference, body.ChallengeSecret, body.NewPassword, BuildContext(request, options.Value, tokens, null, null), cancellationToken);
        SetNoStore(response);
        return Results.Json(result.Response, statusCode: result.HttpStatusCode);
    }

    private static async Task<IResult> ActivateAsync(HumanActivationRequest body, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, IHumanAuthenticationOriginValidator originValidator, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, CancellationToken cancellationToken)
    {
        var correlationId = HumanSessionAuthenticationHandler.ResolveCorrelationId(request);
        if (!originValidator.IsAllowed(request)) return SafeFailure(400, "INVALID_REQUEST_ORIGIN", correlationId);
        var result = await service.ActivateAsync(body.ChallengeReference, body.ChallengeSecret, body.NewPassword, BuildContext(request, options.Value, tokens, null, null), cancellationToken);
        SetNoStore(response);
        return Results.Json(result.Response, statusCode: result.HttpStatusCode);
    }

    private static Task<IResult> BeginTotpEnrollmentAsync(HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        ExecuteWebTotpMutationAsync(request, response, tokens, options.Value, antiforgery, (token, context) => service.BeginTotpEnrollmentAsync(token, context, cancellationToken));

    private static Task<IResult> ConfirmTotpEnrollmentAsync(TotpEnrollmentConfirmRequest body, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        ExecuteWebTotpMutationAsync(request, response, tokens, options.Value, antiforgery, (token, context) => service.ConfirmTotpEnrollmentAsync(token, body.Code, context, cancellationToken));

    private static async Task<IResult> CreateAptSessionAsync(AptHumanSessionCreateRequest body, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, CancellationToken cancellationToken)
    {
        var deviceId = ResolveDeviceServiceIdentity(request);
        var context = BuildContext(request, options.Value, tokens, deviceId, body.SiteId);
        var result = await service.LoginAsync(body.Username, body.Password, HumanSessionAudiences.Apt, body.TotpCode, context, cancellationToken);
        SetNoStore(response);
        return Results.Json(result.Response, statusCode: result.HttpStatusCode);
    }

    private static async Task<IResult> GetAptSessionAsync(Guid sessionReference, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, CancellationToken cancellationToken)
    {
        var token = HumanSessionAuthenticationHandler.ResolveToken(request, options.Value);
        var deviceId = ResolveDeviceServiceIdentity(request);
        if (token is null || !_tokensMatchReference(tokens, token, sessionReference)) return SafeFailure(404, "SESSION_NOT_FOUND", HumanSessionAuthenticationHandler.ResolveCorrelationId(request));
        var result = await service.ResolveSessionAsync(token, HumanSessionAudiences.Apt, deviceId, BuildContext(request, options.Value, tokens, deviceId, null), true, cancellationToken);
        SetNoStore(response);
        return Results.Json(result.Response, statusCode: result.HttpStatusCode);
    }

    private static async Task<IResult> ContinueAptSessionAsync(Guid sessionReference, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, CancellationToken cancellationToken)
    {
        var token = HumanSessionAuthenticationHandler.ResolveToken(request, options.Value);
        var deviceId = ResolveDeviceServiceIdentity(request);
        if (token is null || !_tokensMatchReference(tokens, token, sessionReference)) return SafeFailure(404, "SESSION_NOT_FOUND", HumanSessionAuthenticationHandler.ResolveCorrelationId(request));
        var result = await service.ContinueSessionAsync(token, BuildContext(request, options.Value, tokens, deviceId, null), cancellationToken);
        SetNoStore(response);
        return Results.Json(result.Response, statusCode: result.HttpStatusCode);
    }

    private static async Task<IResult> ReauthenticateAptSessionAsync(Guid sessionReference, HumanFreshAuthenticationRequest body, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, CancellationToken cancellationToken)
    {
        var token = HumanSessionAuthenticationHandler.ResolveToken(request, options.Value);
        var deviceId = ResolveDeviceServiceIdentity(request);
        if (token is null || !_tokensMatchReference(tokens, token, sessionReference)) return SafeFailure(404, "SESSION_NOT_FOUND", HumanSessionAuthenticationHandler.ResolveCorrelationId(request));
        var result = await service.FreshAuthenticateAsync(token, body.Password, body.TotpCode, BuildContext(request, options.Value, tokens, deviceId, null), cancellationToken);
        SetNoStore(response);
        return Results.Json(result.Response, statusCode: result.HttpStatusCode);
    }

    private static async Task<IResult> LogoutAptSessionAsync(Guid sessionReference, HttpRequest request, HttpResponse response, IHumanAuthenticationService service, [FromServices] IHumanSessionTokenService tokens, IOptions<HumanAuthenticationOptions> options, CancellationToken cancellationToken)
    {
        var token = HumanSessionAuthenticationHandler.ResolveToken(request, options.Value);
        var deviceId = ResolveDeviceServiceIdentity(request);
        if (token is null || !_tokensMatchReference(tokens, token, sessionReference)) return SafeFailure(404, "SESSION_NOT_FOUND", HumanSessionAuthenticationHandler.ResolveCorrelationId(request));
        var result = await service.LogoutAsync(token, BuildContext(request, options.Value, tokens, deviceId, null), cancellationToken);
        SetNoStore(response);
        return Results.Json(result.Response, statusCode: result.HttpStatusCode);
    }

    private static async Task<IResult> ExecuteWebMutationAsync(HttpRequest request, HttpResponse response, IHumanAuthenticationService service, IHumanSessionTokenService tokens, HumanAuthenticationOptions options, IAntiforgery antiforgery, Func<string, HumanAuthenticationContext, Task<HumanAuthenticationResult>> action)
    {
        try { await antiforgery.ValidateRequestAsync(request.HttpContext); }
        catch (AntiforgeryValidationException) { return SafeFailure(400, "CSRF_VALIDATION_FAILED", HumanSessionAuthenticationHandler.ResolveCorrelationId(request)); }
        return await ExecuteWebSessionAsync(request, response, service, tokens, options, true, action);
    }

    private static async Task<IResult> ExecuteWebSessionAsync(HttpRequest request, HttpResponse response, IHumanAuthenticationService service, IHumanSessionTokenService tokens, HumanAuthenticationOptions options, bool mutation, Func<string, HumanAuthenticationContext, Task<HumanAuthenticationResult>> action)
    {
        var token = HumanSessionAuthenticationHandler.ResolveToken(request, options);
        if (token is null) return SafeFailure(401, "SESSION_REQUIRED", HumanSessionAuthenticationHandler.ResolveCorrelationId(request));
        var result = await action(token, BuildContext(request, options, tokens, null, null));
        SetNoStore(response);
        return Results.Json(result.Response with { AptSessionToken = null }, statusCode: result.HttpStatusCode);
    }

    private static async Task<HumanAuthenticationResult> EnrichOperatorSessionAsync(
        HumanAuthenticationResult result,
        HttpRequest request,
        IOperatorConsoleOperatingContextService operatingContextService,
        CancellationToken cancellationToken)
    {
        if (!result.Response.Authenticated ||
            result.Response.Session is not { Audience: HumanSessionAudiences.OperatorConsole } session ||
            !result.InternalHumanSessionId.HasValue)
        {
            return result;
        }

        var operating = await operatingContextService.ValidateSessionAsync(
            result.InternalHumanSessionId.Value,
            OperatorConsoleDeviceBindingCookie.Read(request),
            result.Response.CorrelationId,
            cancellationToken);
        if (!operating.Succeeded || operating.Context is null) return result;

        var context = operating.Context;
        return result with
        {
            Response = result.Response with
            {
                Session = session with
                {
                    OperatorDeviceBindingReference = context.OperatorDeviceBindingId,
                    OperatorShiftReference = context.OperatorShiftId,
                    EffectiveSiteReference = context.SiteId,
                    EffectiveSiteGroupReference = context.SiteGroupId,
                    AuthorizationEpoch = context.AuthorizationEpoch,
                    CredentialVersion = context.CredentialVersion
                }
            }
        };
    }

    private static async Task<HumanAuthenticationResult> BindRotatedOperatorSessionAsync(
        HumanAuthenticationResult result,
        HttpRequest request,
        IOperatorConsoleOperatingContextService operatingContextService,
        CancellationToken cancellationToken)
    {
        if (!result.Response.Authenticated ||
            result.Response.Session is not { Audience: HumanSessionAudiences.OperatorConsole } session ||
            !result.InternalHumanSessionId.HasValue)
        {
            return result;
        }

        if (OperatorConsoleDeviceBindingCookie.Read(request) is null) return result;

        var operating = await operatingContextService.BindSessionAsync(
            result.InternalHumanSessionId.Value,
            session.UserReference,
            session.SiteReferences,
            session.SiteGroupReferences,
            session.HasGlobalScope,
            OperatorConsoleDeviceBindingCookie.Read(request),
            result.Response.CorrelationId,
            cancellationToken);
        if (!operating.Succeeded || operating.Context is null) return result;

        var context = operating.Context;
        return result with
        {
            Response = result.Response with
            {
                Session = session with
                {
                    OperatorDeviceBindingReference = context.OperatorDeviceBindingId,
                    OperatorShiftReference = context.OperatorShiftId,
                    EffectiveSiteReference = context.SiteId,
                    EffectiveSiteGroupReference = context.SiteGroupId,
                    AuthorizationEpoch = context.AuthorizationEpoch,
                    CredentialVersion = context.CredentialVersion
                }
            }
        };
    }

    private static async Task<IResult> ExecuteWebTotpMutationAsync(HttpRequest request, HttpResponse response, IHumanSessionTokenService tokens, HumanAuthenticationOptions options, IAntiforgery antiforgery, Func<string, HumanAuthenticationContext, Task<TotpEnrollmentResult>> action)
    {
        try { await antiforgery.ValidateRequestAsync(request.HttpContext); }
        catch (AntiforgeryValidationException) { return SafeFailure(400, "CSRF_VALIDATION_FAILED", HumanSessionAuthenticationHandler.ResolveCorrelationId(request)); }
        var token = HumanSessionAuthenticationHandler.ResolveToken(request, options);
        if (token is null) return SafeFailure(401, "SESSION_REQUIRED", HumanSessionAuthenticationHandler.ResolveCorrelationId(request));
        var result = await action(token, BuildContext(request, options, tokens, null, null));
        SetNoStore(response);
        return Results.Json(result.Response, statusCode: result.HttpStatusCode);
    }

    private static HumanAuthenticationContext BuildContext(HttpRequest request, HumanAuthenticationOptions options, IHumanSessionTokenService tokens, Guid? deviceId, Guid? siteId) =>
        HumanSessionAuthenticationHandler.BuildContext(request, HumanSessionAuthenticationHandler.ResolveCorrelationId(request), deviceId, options, tokens) with { SiteId = siteId };

    private static Guid? ResolveDeviceServiceIdentity(HttpRequest request) => HumanSessionAuthenticationHandler.ResolveGuid(request.Headers["X-ExitPass-Service-Identity-Id"]);
    private static bool _tokensMatchReference(IHumanSessionTokenService tokens, string token, Guid reference) => tokens.TryParse(token, out var parsed) && parsed.SessionReference == reference;
    private static string NormalizeAudience(string value) => value.Trim().Replace('-', '_').ToUpperInvariant();

    private static void SetWebSessionCookie(HttpResponse response, string token, HumanAuthenticationOptions options) =>
        response.Cookies.Append(options.CookieName, token, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true });

    private static void DeleteWebSessionCookie(HttpResponse response, HumanAuthenticationOptions options) =>
        response.Cookies.Delete(options.CookieName, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });

    private static void SetNoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, private";
        response.Headers.Pragma = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static void SetAntiforgeryResponseToken(HttpContext context, HttpResponse response, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        if (!string.IsNullOrWhiteSpace(tokens.RequestToken)) response.Headers["X-CSRF-Token"] = tokens.RequestToken;
    }

    private static IResult SafeFailure(int status, string code, Guid correlationId) => Results.Json(new HumanAuthenticationResponse("REJECTED", false, null, null, code, false, correlationId), statusCode: status);
}
