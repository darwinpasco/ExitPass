using System.Security.Claims;
using System.Security.Cryptography;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using Microsoft.AspNetCore.Antiforgery;
using Npgsql;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Exposes System Administrator device lifecycle APIs and the one-time device enrollment boundary.
/// </summary>
/// <remarks>
/// BRD v1.2 Sections 9.16 and 9.21; SDD v1.2 Sections 10 and 14.
/// Administrative APIs never authenticate a runtime device themselves; enrollment material is
/// consumed by Central PMS and projected into the existing runtime trust structures.
/// </remarks>
public static class DeviceAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapDeviceAdministrationEndpoints(this IEndpointRouteBuilder app)
    {
        var administration = app.MapGroup("/v1/management-platform/devices")
            .WithTags("DeviceAdministration")
            .RequireAuthorization()
            .AddEndpointFilter(ValidateManagementMutationAsync);

        administration.MapGet("", async (IDeviceAdministrationService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementDeviceRead"));

        administration.MapGet("/scopes", async (IDeviceAdministrationService service, CancellationToken ct) =>
                Results.Ok(await service.ListScopesAsync(ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementDeviceRead"));

        administration.MapPost("", async (RegisterDeviceRequest body, HttpContext context, IDeviceAdministrationService service, CancellationToken ct) =>
                await ExecuteAsync(context, () => service.RegisterAsync(body, Actor(context), Correlation(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementDeviceManage"));

        administration.MapGet("/{deviceId:guid}", async (Guid deviceId, HttpContext context, IDeviceAdministrationService service, CancellationToken ct) =>
                await ExecuteAsync(context, () => service.GetAsync(deviceId, ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementDeviceRead"));

        administration.MapPost("/{deviceId:guid}/provisioning", async (Guid deviceId, HttpContext context, IDeviceAdministrationService service, CancellationToken ct) =>
                await ExecuteAsync(context, () => service.ProvisionAsync(deviceId, Actor(context), Correlation(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementDeviceManage"));

        MapLifecycle(administration, "provisioning/cancel", (service, id, reason, actor, correlation, ct) =>
            service.CancelProvisioningAsync(id, reason, actor, correlation, ct));

        MapLifecycle(administration, "suspend", (service, id, reason, actor, correlation, ct) =>
            service.SuspendAsync(id, reason, actor, correlation, ct));
        MapLifecycle(administration, "reactivate", (service, id, reason, actor, correlation, ct) =>
            service.ReactivateAsync(id, reason, actor, correlation, ct));
        MapLifecycle(administration, "revoke", (service, id, reason, actor, correlation, ct) =>
            service.RevokeAsync(id, reason, actor, correlation, ct));

        administration.MapPost("/{deviceId:guid}/rotate", async (Guid deviceId, DeviceLifecycleRequest body, HttpContext context, IDeviceAdministrationService service, CancellationToken ct) =>
                await ExecuteAsync(context, () => service.RotateAsync(deviceId, body.ReasonCode, Actor(context), Correlation(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementDeviceManage"));

        administration.MapPost("/{deviceId:guid}/replace", async (Guid deviceId, ReplaceDeviceRequest body, HttpContext context, IDeviceAdministrationService service, CancellationToken ct) =>
                await ExecuteAsync(context, () => service.ReplaceAsync(deviceId, body, Actor(context), Correlation(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementDeviceManage"));

        app.MapPost("/v1/devices/enrollment", EnrollAsync)
            .WithTags("DeviceEnrollment")
            .DisableAntiforgery();

        return app;
    }

    private static void MapLifecycle(
        RouteGroupBuilder group,
        string route,
        Func<IDeviceAdministrationService, Guid, string, Guid, Guid, CancellationToken, Task<DeviceSummaryDto>> action)
    {
        group.MapPost($"/{{deviceId:guid}}/{route}", async (Guid deviceId, DeviceLifecycleRequest body, HttpContext context, IDeviceAdministrationService service, CancellationToken ct) =>
                await ExecuteAsync(context, () => action(service, deviceId, body.ReasonCode, Actor(context), Correlation(context), ct)))
            .WithMetadata(new ReconciliationPolicyMetadata("ManagementDeviceManage"));
    }

    private static async Task<IResult> EnrollAsync(
        EnrollDeviceRequest body,
        HttpContext context,
        IInternalClientCertificateAccessor certificates,
        IDeviceAdministrationService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var correlation = Correlation(context);
        string? certificateFingerprint = null;
        if (string.Equals(body.DeviceType?.Trim(), DeviceTypes.AssistedPaymentTerminal, StringComparison.OrdinalIgnoreCase))
        {
            var certificate = await certificates.GetClientCertificateAsync(context);
            if (certificate is not null)
                certificateFingerprint = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        }
        return await ExecuteAsync(context, () => service.EnrollAsync(body, certificateFingerprint, correlation, cancellationToken));
    }

    private static async ValueTask<object?> ValidateManagementMutationAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            try
            {
                await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Error(StatusCodes.Status400BadRequest, "CSRF_VALIDATION_FAILED", Correlation(context));
            }
        }
        return await next(invocation);
    }

    private static async Task<IResult> ExecuteAsync<T>(HttpContext context, Func<Task<T>> action)
    {
        try
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await action());
        }
        catch (DeviceAdministrationException exception)
        {
            var status = exception.ErrorCode switch
            {
                "DEVICE_NOT_FOUND" => StatusCodes.Status404NotFound,
                "SYSTEM_ADMINISTRATOR_REQUIRED" => StatusCodes.Status403Forbidden,
                "DEVICE_NAME_REQUIRED" or "DEVICE_NAME_INVALID" or "DEVICE_TYPE_REQUIRED" or
                "DEVICE_TYPE_UNSUPPORTED" or "DEVICE_SITE_REQUIRED" or "DEVICE_SITE_INVALID" or
                "DEVICE_SITE_GROUP_MISMATCH" or "DEVICE_DESCRIPTION_INVALID" or
                "TERMINAL_IDENTIFIER_INVALID" or "APT_TERMINAL_IDENTIFIER_REQUIRED" or
                "PROVISIONING_CREDENTIAL_REQUIRED" or "APT_CLIENT_CERTIFICATE_REQUIRED" => StatusCodes.Status400BadRequest,
                "PROVISIONING_CREDENTIAL_INVALID" => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status409Conflict
            };
            return Error(status, exception.ErrorCode, Correlation(context));
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation)
        {
            return Error(StatusCodes.Status409Conflict, "DEVICE_ADMINISTRATION_CONSTRAINT_REJECTED", Correlation(context));
        }
    }

    private static Guid Actor(HttpContext context)
    {
        foreach (var claim in new[] { ClaimTypes.NameIdentifier, "sub", "user_id" })
            if (Guid.TryParse(context.User.FindFirst(claim)?.Value, out var value) && value != Guid.Empty) return value;
        throw new DeviceAdministrationException("SYSTEM_ADMINISTRATOR_REQUIRED");
    }

    private static Guid Correlation(HttpContext context) =>
        Guid.TryParse(context.Request.Headers["X-Correlation-Id"].FirstOrDefault(), out var value) && value != Guid.Empty
            ? value
            : Guid.NewGuid();

    private static IResult Error(int status, string code, Guid correlation) => Results.Json(new ErrorResponse
    {
        ErrorCode = code,
        Message = "The device administration request could not be completed.",
        CorrelationId = correlation,
        Retryable = false,
        RecoveryClassification = code
    }, statusCode: status);
}
