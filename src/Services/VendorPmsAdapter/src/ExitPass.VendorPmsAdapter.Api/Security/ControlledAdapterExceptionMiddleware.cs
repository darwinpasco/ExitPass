using ExitPass.VendorPmsAdapter.Application.Routing;

namespace ExitPass.VendorPmsAdapter.Api.Security;

/// <summary>Returns stable sanitized errors for failures at the Site Adapter boundary.</summary>
public sealed class ControlledAdapterExceptionMiddleware(
    RequestDelegate next,
    ILogger<ControlledAdapterExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (SiteAdapterBindingException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, exception.ErrorCode,
                "The request is incompatible with this Site Integration Adapter.");
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning("Site Adapter request {CorrelationId} failed validation: {ExceptionType}.",
                CorrelationId(context), exception.GetType().Name);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "SITE_ADAPTER_REQUEST_INVALID",
                "The request is invalid.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Site Adapter request {CorrelationId} failed safely.", CorrelationId(context));
            await WriteProblemAsync(context, StatusCodes.Status502BadGateway, "SITE_ADAPTER_DEPENDENCY_FAILURE",
                "The Site Integration Adapter could not complete the request.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string code, string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            code,
            message,
            correlationId = CorrelationId(context)
        });
    }

    private static string CorrelationId(HttpContext context) =>
        context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? context.TraceIdentifier;
}
