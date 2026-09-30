using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Api.Services;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;

namespace ExitPass.CentralPms.Api.Endpoints;

public static class CustomerDigitalSalesInvoiceEndpoints
{
    private const string StatusReadPolicy = "FiscalIssuanceStatusRead";
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.CustomerDigitalSalesInvoice");

    public static IEndpointRouteBuilder MapCustomerDigitalSalesInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/v1/ops/operator-console/fiscal-issuance/references/{fiscalIssuanceReferenceId:guid}/digital-sales-invoice",
                ReadForOperatorAsync)
            .WithName("GetOperatorConsoleDigitalSalesInvoice")
            .WithTags("OperatorConsole")
            .WithMetadata(new ReconciliationPolicyMetadata(StatusReadPolicy))
            .WithMetadata(OperatorConsoleOperatingContextRequirementMetadata.NotRequired)
            .Produces<OperatorDigitalSalesInvoiceResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        app.MapPost(
                "/v1/webpay/digital-sales-invoices/presentation",
                ReadForCustomerAsync)
            .AllowAnonymous()
            .WithName("GetCustomerDigitalSalesInvoice")
            .WithTags("WebPay")
            .Produces<CustomerDigitalSalesInvoiceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ReadForOperatorAsync(
        Guid fiscalIssuanceReferenceId,
        HttpRequest request,
        IOperatorConsoleFiscalIssuanceStatusService statusService,
        ICustomerDigitalSalesInvoiceCapabilityService capabilityService,
        ICentralPmsRbacRepository rbacRepository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var correlationId = ReadCorrelationId(request);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.CustomerDigitalSalesInvoiceEndpoints");
        var identity = OperatorConsoleIdentityContext.Resolve(
            request,
            fallbackCorrelationId: correlationId);
        if (!await OperatorConsoleFiscalReportingAccess.IsOperationsSupervisorAsync(
                request.HttpContext.User,
                identity.UserId,
                rbacRepository,
                cancellationToken))
        {
            return OperatorConsoleFiscalReportingAccess.Denied(identity.CorrelationId);
        }
        var result = await statusService.GetAsync(
            new OperatorConsoleFiscalIssuanceStatusQuery(
                identity.UserId,
                identity.OperatorDeviceBindingId,
                identity.SiteId,
                identity.SiteGroupId,
                identity.OperatorShiftId,
                fiscalIssuanceReferenceId,
                identity.CorrelationId),
            cancellationToken);

        if (!result.AccessAllowed)
        {
            return Results.Json(
                Error("DIGITAL_SALES_INVOICE_ACCESS_DENIED", "Digital Sales Invoice access was denied.", result.CorrelationId),
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (result.Status?.SiteId is not Guid siteId || siteId == Guid.Empty)
        {
            return Results.NotFound(Error("DIGITAL_SALES_INVOICE_NOT_FOUND", "Digital Sales Invoice was not found.", result.CorrelationId));
        }

        var document = await capabilityService.ReadAsync(
            fiscalIssuanceReferenceId,
            siteId,
            cancellationToken);
        if (document is null)
        {
            return Results.NotFound(Error("DIGITAL_SALES_INVOICE_NOT_FOUND", "Digital Sales Invoice was not found.", result.CorrelationId));
        }

        var capability = capabilityService.Create(fiscalIssuanceReferenceId, siteId);
        var publicPath = $"{capability.PublicPagePath}#access={Uri.EscapeDataString(capability.Token)}";

        logger.LogInformation(
            "Customer Digital Sales Invoice capability generated. fiscal_issuance_reference_id={FiscalIssuanceReferenceId} site_id={SiteId} expires_at={ExpiresAt} correlation_id={CorrelationId}",
            fiscalIssuanceReferenceId,
            siteId,
            capability.ExpiresAt,
            result.CorrelationId);

        return Results.Ok(new OperatorDigitalSalesInvoiceResponse(
            document.Presentation,
            document.CanonicalText,
            publicPath,
            capability.ExpiresAt));
    }

    private static async Task<IResult> ReadForCustomerAsync(
        CustomerDigitalSalesInvoiceRequest request,
        ICustomerDigitalSalesInvoiceCapabilityService capabilityService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ReadCustomerDigitalSalesInvoice", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.CustomerDigitalSalesInvoiceEndpoints");
        var document = await capabilityService.ResolveAsync(request.AccessToken, cancellationToken);
        if (document is null)
        {
            logger.LogInformation("Customer Digital Sales Invoice retrieval failed safely.");
            return Results.NotFound();
        }

        activity?.SetTag("fiscal_issuance_reference_id", document.FiscalIssuanceReferenceId);
        logger.LogInformation(
            "Customer Digital Sales Invoice retrieval succeeded. fiscal_issuance_reference_id={FiscalIssuanceReferenceId} site_id={SiteId}",
            document.FiscalIssuanceReferenceId,
            document.SiteId);

        return Results.Ok(new CustomerDigitalSalesInvoiceResponse(
            document.Presentation,
            document.CanonicalText));
    }

    private static Guid? ReadCorrelationId(HttpRequest request) =>
        Guid.TryParse(request.Headers["X-Correlation-Id"].FirstOrDefault(), out var value) && value != Guid.Empty
            ? value
            : null;

    private static ErrorResponse Error(string code, string message, Guid correlationId) => new()
    {
        ErrorCode = code,
        Message = message,
        CorrelationId = correlationId,
        Retryable = false
    };
}

public sealed record CustomerDigitalSalesInvoiceRequest(string AccessToken);

public sealed record CustomerDigitalSalesInvoiceResponse(
    System.Text.Json.JsonElement Presentation,
    string CanonicalText);

public sealed record OperatorDigitalSalesInvoiceResponse(
    System.Text.Json.JsonElement Presentation,
    string CanonicalText,
    string CustomerDigitalSalesInvoicePath,
    DateTimeOffset CapabilityExpiresAt);
