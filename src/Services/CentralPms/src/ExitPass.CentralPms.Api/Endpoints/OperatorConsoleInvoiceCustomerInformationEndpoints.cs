using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.SalesInvoiceCustomerInformation;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using Microsoft.AspNetCore.Antiforgery;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class OperatorConsoleInvoiceCustomerInformationEndpoints
{
    public const string ReadPolicy = "SalesInvoiceCustomerInformationRead";
    public const string ManagePolicy = "SalesInvoiceCustomerInformationManage";

    public static IEndpointRouteBuilder MapOperatorConsoleInvoiceCustomerInformationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console/sessions/{parkingSessionId:guid}/invoice-customer-information")
            .WithTags("OperatorConsole");

        group.MapGet(string.Empty, ReadAsync)
            .WithMetadata(new ReconciliationPolicyMetadata(ReadPolicy))
            .Produces<OperatorConsoleInvoiceCustomerInformationResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        group.MapPut(string.Empty, SaveAsync)
            .WithMetadata(new ReconciliationPolicyMetadata(ManagePolicy))
            .Accepts<SaveOperatorConsoleInvoiceCustomerInformationRequest>("application/json")
            .Produces<OperatorConsoleInvoiceCustomerInformationResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ReadAsync(
        Guid parkingSessionId,
        HttpRequest request,
        IParkingSessionInvoiceCustomerInformationService service,
        CancellationToken cancellationToken)
    {
        OperatorConsoleIdentityContext identity;
        try
        {
            identity = OperatorConsoleIdentityContext.Resolve(request);
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_OPERATOR_CONSOLE_IDENTITY", ex.Message, Correlation(request));
        }

        if (!identity.SiteId.HasValue)
        {
            return Error(StatusCodes.Status404NotFound, "PARKING_SESSION_NOT_FOUND", "The parking session is unavailable in the current Site scope.", identity.CorrelationId);
        }

        var result = await service.ReadAsync(
            parkingSessionId,
            new InvoiceCustomerInformationScope(identity.SiteId.Value, identity.SiteGroupId),
            cancellationToken);

        return result.Status switch
        {
            InvoiceCustomerInformationReadStatus.Found => Results.Ok(ToResponse(parkingSessionId, result.Record, identity.CorrelationId)),
            InvoiceCustomerInformationReadStatus.NotSupplied => Results.Ok(ToResponse(parkingSessionId, null, identity.CorrelationId)),
            InvoiceCustomerInformationReadStatus.ParkingSessionNotFound => Error(StatusCodes.Status404NotFound, "PARKING_SESSION_NOT_FOUND", "The parking session is unavailable in the current Site scope.", identity.CorrelationId),
            _ => Error(StatusCodes.Status503ServiceUnavailable, "CUSTOMER_INFORMATION_SOURCE_UNAVAILABLE", "Sales Invoice customer information is temporarily unavailable.", identity.CorrelationId, true)
        };
    }

    private static async Task<IResult> SaveAsync(
        Guid parkingSessionId,
        SaveOperatorConsoleInvoiceCustomerInformationRequest body,
        HttpRequest request,
        IParkingSessionInvoiceCustomerInformationService service,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(request.HttpContext.User.Identity?.AuthenticationType, HumanSessionAuthenticationHandler.SchemeName, StringComparison.Ordinal))
            {
                await antiforgery.ValidateRequestAsync(request.HttpContext);
            }
        }
        catch (AntiforgeryValidationException)
        {
            return Error(StatusCodes.Status403Forbidden, "HUMAN_SESSION_CSRF_INVALID", "The CSRF token is missing or invalid.", Correlation(request));
        }

        OperatorConsoleIdentityContext identity;
        try
        {
            identity = OperatorConsoleIdentityContext.Resolve(request);
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_OPERATOR_CONSOLE_IDENTITY", ex.Message, Correlation(request));
        }

        if (!identity.SiteId.HasValue)
        {
            return Error(StatusCodes.Status404NotFound, "PARKING_SESSION_NOT_FOUND", "The parking session is unavailable in the current Site scope.", identity.CorrelationId);
        }

        var result = await service.SaveAsync(
            new SaveParkingSessionInvoiceCustomerInformationCommand(
                parkingSessionId,
                body.CustomerName,
                body.Address,
                body.Tin,
                body.BusinessStyle,
                body.ExpectedVersion,
                new InvoiceCustomerInformationActor(identity.UserId, null, InvoiceCustomerInformationSourceChannels.OperatorConsole),
                new InvoiceCustomerInformationScope(identity.SiteId.Value, identity.SiteGroupId),
                identity.CorrelationId),
            cancellationToken);

        return result.Status switch
        {
            InvoiceCustomerInformationSaveStatus.Created or
            InvoiceCustomerInformationSaveStatus.Updated or
            InvoiceCustomerInformationSaveStatus.Unchanged => Results.Ok(ToResponse(parkingSessionId, result.Record, identity.CorrelationId)),
            InvoiceCustomerInformationSaveStatus.Invalid => Error(StatusCodes.Status400BadRequest, "CUSTOMER_INFORMATION_INVALID", "Sales Invoice customer information is invalid.", identity.CorrelationId, details: new Dictionary<string, object?> { ["validationErrors"] = result.ValidationErrors }),
            InvoiceCustomerInformationSaveStatus.ParkingSessionNotFound => Error(StatusCodes.Status404NotFound, "PARKING_SESSION_NOT_FOUND", "The parking session is unavailable in the current Site scope.", identity.CorrelationId),
            InvoiceCustomerInformationSaveStatus.VersionConflict => Conflict(identity.CorrelationId, result.Record),
            InvoiceCustomerInformationSaveStatus.FiscalFinality => Error(StatusCodes.Status409Conflict, "CUSTOMER_INFORMATION_FISCAL_FINALITY", "Sales Invoice customer information cannot be changed after fiscal issuance.", identity.CorrelationId),
            _ => Error(StatusCodes.Status503ServiceUnavailable, "CUSTOMER_INFORMATION_SOURCE_UNAVAILABLE", "Sales Invoice customer information is temporarily unavailable.", identity.CorrelationId, true)
        };
    }

    private static IResult Conflict(Guid correlationId, ParkingSessionInvoiceCustomerInformationRecord? current) =>
        Error(
            StatusCodes.Status409Conflict,
            "CUSTOMER_INFORMATION_VERSION_CONFLICT",
            "Sales Invoice customer information changed after it was loaded. Reload before saving.",
            correlationId,
            details: new Dictionary<string, object?>
            {
                ["currentRowVersion"] = current?.RowVersion
            });

    private static OperatorConsoleInvoiceCustomerInformationResponse ToResponse(
        Guid parkingSessionId,
        ParkingSessionInvoiceCustomerInformationRecord? record,
        Guid correlationId) =>
        new(
            parkingSessionId,
            record is not null,
            record?.CustomerName,
            record?.CustomerAddress,
            record?.CustomerTin,
            record?.BusinessStyle,
            record?.RowVersion,
            record?.UpdatedAt,
            correlationId);

    private static IResult Error(
        int statusCode,
        string code,
        string message,
        Guid correlationId,
        bool retryable = false,
        Dictionary<string, object?>? details = null) =>
        Results.Json(new ErrorResponse
        {
            ErrorCode = code,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable,
            Details = details
        }, statusCode: statusCode);

    private static Guid Correlation(HttpRequest request) =>
        Guid.TryParse(request.Headers["X-Correlation-Id"], out var value) && value != Guid.Empty
            ? value
            : Guid.NewGuid();
}
