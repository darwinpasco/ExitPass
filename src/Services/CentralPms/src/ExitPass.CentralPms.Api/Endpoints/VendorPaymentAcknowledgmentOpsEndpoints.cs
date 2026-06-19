using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Operations;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Read-only operations monitoring endpoints for Vendor PMS payment acknowledgments.
/// </summary>
public static class VendorPaymentAcknowledgmentOpsEndpoints
{
    private const string ViewerPolicy = "VendorPaymentAcknowledgmentViewer";
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.VendorPaymentAcknowledgments");

    /// <summary>
    /// Maps read-only Vendor PMS payment acknowledgment monitoring endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapVendorPaymentAcknowledgmentOpsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/vendor-payment-acknowledgments")
            .WithTags("OpsVendorPaymentAcknowledgments");

        group.MapPost("/search", SearchAsync)
            .WithName("SearchVendorPaymentAcknowledgments")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<VendorPaymentAcknowledgmentSearchResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{vendorPaymentAcknowledgmentId:guid}", GetAsync)
            .WithName("GetVendorPaymentAcknowledgment")
            .WithMetadata(new ReconciliationPolicyMetadata(ViewerPolicy))
            .Produces<VendorPaymentAcknowledgmentDetailResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> SearchAsync(
        VendorPaymentAcknowledgmentSearchRequest request,
        HttpRequest httpRequest,
        IVendorPaymentAcknowledgmentOpsService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP SearchVendorPaymentAcknowledgments", httpRequest);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.VendorPaymentAcknowledgmentOpsEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var result = await service.SearchAsync(
                new SearchVendorPaymentAcknowledgmentsQuery(
                    request.AcknowledgmentStatus,
                    request.VendorSystemCode,
                    request.PaymentAttemptId,
                    request.PaymentConfirmationId,
                    request.ParkingSessionId,
                    request.TicketNumber,
                    request.CardNum,
                    request.CorrelationId,
                    request.CreatedFrom,
                    request.CreatedTo,
                    request.LastAttemptedFrom,
                    request.LastAttemptedTo,
                    request.NextRetryDueOnly,
                    DateTimeOffset.UtcNow,
                    request.PageIndex ?? 0,
                    request.PageSize ?? 25),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("vendor_payment_acknowledgment.count", result.Items.Count);
            activity?.SetTag("vendor_payment_acknowledgment.page_index", result.PageIndex);
            activity?.SetTag("vendor_payment_acknowledgment.page_size", result.PageSize);

            logger.LogInformation(
                "Vendor payment acknowledgment search completed. item_count={ItemCount} page_index={PageIndex} page_size={PageSize} has_more={HasMore} correlation_id={CorrelationId}",
                result.Items.Count,
                result.PageIndex,
                result.PageSize,
                result.HasMore,
                correlationId);

            return Results.Ok(ToSearchResponse(result));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static async Task<IResult> GetAsync(
        Guid vendorPaymentAcknowledgmentId,
        HttpRequest httpRequest,
        IVendorPaymentAcknowledgmentOpsService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity("HTTP GetVendorPaymentAcknowledgment", httpRequest);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.VendorPaymentAcknowledgmentOpsEndpoints");
        var correlationId = ResolveCorrelationId(httpRequest);

        try
        {
            var record = await service.ReadAsync(vendorPaymentAcknowledgmentId, cancellationToken);
            if (record is null)
            {
                return Results.NotFound(BuildError(
                    "VENDOR_PAYMENT_ACKNOWLEDGMENT_NOT_FOUND",
                    "Vendor payment acknowledgment was not found.",
                    correlationId,
                    retryable: false));
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("vendor_payment_acknowledgment.id", record.VendorPaymentAcknowledgmentId);
            activity?.SetTag("vendor_payment_acknowledgment.status", record.AcknowledgmentStatus);

            logger.LogInformation(
                "Vendor payment acknowledgment detail read. vendor_payment_acknowledgment_id={VendorPaymentAcknowledgmentId} status={AcknowledgmentStatus} vendor_system_code={VendorSystemCode} correlation_id={CorrelationId}",
                record.VendorPaymentAcknowledgmentId,
                record.AcknowledgmentStatus,
                record.VendorSystemCode,
                record.CorrelationId ?? correlationId);

            return Results.Ok(ToDetailResponse(record, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            return MapException(ex, correlationId, activity, logger);
        }
    }

    private static Activity? StartActivity(string activityName, HttpRequest request)
    {
        var activity = ActivitySource.StartActivity(activityName, ActivityKind.Server);
        activity?.SetTag("url.path", request.Path.Value);
        activity?.SetTag("http.request.method", request.Method);
        return activity;
    }

    private static IResult MapException(
        Exception exception,
        Guid correlationId,
        Activity? activity,
        ILogger logger)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);

        return exception switch
        {
            ArgumentException ex => Results.BadRequest(BuildError(
                "INVALID_VENDOR_PAYMENT_ACKNOWLEDGMENT_SEARCH_REQUEST",
                ex.Message,
                correlationId,
                retryable: false)),
            _ => Unexpected(exception, correlationId, logger)
        };
    }

    private static IResult Unexpected(Exception exception, Guid correlationId, ILogger logger)
    {
        logger.LogError(exception, "Unexpected Vendor PMS payment acknowledgment ops API failure.");
        return Results.Json(
            BuildError(
                "VENDOR_PAYMENT_ACKNOWLEDGMENT_OPS_INTERNAL_ERROR",
                "An unexpected error occurred while reading Vendor PMS payment acknowledgments.",
                correlationId,
                retryable: false),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static ErrorResponse BuildError(
        string errorCode,
        string message,
        Guid correlationId,
        bool retryable)
    {
        return new ErrorResponse
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };
    }

    private static Guid ResolveCorrelationId(HttpRequest request)
    {
        return request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var correlationId)
            ? correlationId
            : Guid.Empty;
    }

    private static VendorPaymentAcknowledgmentSearchResponse ToSearchResponse(
        VendorPaymentAcknowledgmentSearchResult result)
    {
        return new VendorPaymentAcknowledgmentSearchResponse
        {
            Items = result.Items.Select(ToSummary).ToArray(),
            StatusBuckets = new VendorPaymentAcknowledgmentStatusBuckets
            {
                Pending = result.StatusBuckets.Pending,
                RetryPending = result.StatusBuckets.RetryPending,
                Failed = result.StatusBuckets.Failed,
                Confirmed = result.StatusBuckets.Confirmed,
                SkippedDisabled = result.StatusBuckets.SkippedDisabled,
                Cancelled = result.StatusBuckets.Cancelled
            },
            PageIndex = result.PageIndex,
            PageSize = result.PageSize,
            HasMore = result.HasMore
        };
    }

    private static VendorPaymentAcknowledgmentDetailResponse ToDetailResponse(
        VendorPaymentAcknowledgmentRecord record,
        DateTimeOffset utcNow)
    {
        var summary = ToSummary(record);
        return new VendorPaymentAcknowledgmentDetailResponse
        {
            VendorPaymentAcknowledgmentId = summary.VendorPaymentAcknowledgmentId,
            PaymentAttemptId = summary.PaymentAttemptId,
            PaymentConfirmationId = summary.PaymentConfirmationId,
            ParkingSessionId = summary.ParkingSessionId,
            VendorSystemCode = summary.VendorSystemCode,
            VendorSessionRef = summary.VendorSessionRef,
            TicketNumber = summary.TicketNumber,
            CardNum = summary.CardNum,
            AcknowledgmentStatus = summary.AcknowledgmentStatus,
            StatusBucket = summary.StatusBucket,
            VendorCode = summary.VendorCode,
            VendorMessage = summary.VendorMessage,
            RequestFeeMinorUnits = summary.RequestFeeMinorUnits,
            RequestCurrencyCode = summary.RequestCurrencyCode,
            ConfirmedFeeMinorUnits = summary.ConfirmedFeeMinorUnits,
            VendorConfirmedAt = summary.VendorConfirmedAt,
            AttemptCount = summary.AttemptCount,
            LastAttemptedAt = summary.LastAttemptedAt,
            NextRetryAt = summary.NextRetryAt,
            CorrelationId = summary.CorrelationId,
            CreatedAt = summary.CreatedAt,
            UpdatedAt = summary.UpdatedAt,
            Diagnostics = BuildDiagnostics(record, utcNow)
        };
    }

    private static VendorPaymentAcknowledgmentSummary ToSummary(VendorPaymentAcknowledgmentRecord record)
    {
        return new VendorPaymentAcknowledgmentSummary
        {
            VendorPaymentAcknowledgmentId = record.VendorPaymentAcknowledgmentId,
            PaymentAttemptId = record.PaymentAttemptId,
            PaymentConfirmationId = record.PaymentConfirmationId,
            ParkingSessionId = record.ParkingSessionId,
            VendorSystemCode = record.VendorSystemCode,
            VendorSessionRef = record.VendorSessionRef,
            TicketNumber = record.TicketNumber,
            CardNum = record.CardNum,
            AcknowledgmentStatus = record.AcknowledgmentStatus,
            StatusBucket = ToStatusBucket(record.AcknowledgmentStatus),
            VendorCode = record.VendorCode,
            VendorMessage = record.VendorMessage,
            RequestFeeMinorUnits = record.RequestFeeMinorUnits,
            RequestCurrencyCode = record.RequestCurrencyCode,
            ConfirmedFeeMinorUnits = record.ConfirmedFeeMinorUnits,
            VendorConfirmedAt = record.VendorConfirmedAt,
            AttemptCount = record.AttemptCount,
            LastAttemptedAt = record.LastAttemptedAt,
            NextRetryAt = record.NextRetryAt,
            CorrelationId = record.CorrelationId,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt
        };
    }

    private static IReadOnlyList<VendorPaymentAcknowledgmentDiagnosticDto> BuildDiagnostics(
        VendorPaymentAcknowledgmentRecord record,
        DateTimeOffset utcNow)
    {
        var diagnostics = new List<VendorPaymentAcknowledgmentDiagnosticDto>
        {
            new(
                "VENDOR_PAYMENT_ACKNOWLEDGMENT_STATUS_BUCKET",
                $"Status bucket: {ToStatusBucket(record.AcknowledgmentStatus)}.",
                "central-pms.vendor-payment-acknowledgments",
                Retryable: false,
                record.CorrelationId),
            new(
                "VENDOR_PAYMENT_ACKNOWLEDGMENT_AGE_SECONDS",
                $"Age seconds: {Math.Max(0, (long)(utcNow - record.CreatedAt.ToUniversalTime()).TotalSeconds)}.",
                "central-pms.vendor-payment-acknowledgments",
                Retryable: false,
                record.CorrelationId)
        };

        if (string.Equals(record.AcknowledgmentStatus, VendorPaymentAcknowledgmentStatuses.RetryPending, StringComparison.OrdinalIgnoreCase))
        {
            var retryDue = !record.NextRetryAt.HasValue || record.NextRetryAt.Value.ToUniversalTime() <= utcNow;
            diagnostics.Add(new VendorPaymentAcknowledgmentDiagnosticDto(
                retryDue ? "VENDOR_PAYMENT_ACKNOWLEDGMENT_RETRY_DUE" : "VENDOR_PAYMENT_ACKNOWLEDGMENT_RETRY_SCHEDULED",
                retryDue
                    ? "Retry-pending acknowledgment is due for dispatcher pickup."
                    : $"Retry is scheduled in {Math.Max(0, (long)(record.NextRetryAt!.Value.ToUniversalTime() - utcNow).TotalSeconds)} seconds.",
                "central-pms.vendor-payment-acknowledgments",
                Retryable: retryDue,
                record.CorrelationId));
        }

        return diagnostics;
    }

    private static string ToStatusBucket(string status)
    {
        return status switch
        {
            VendorPaymentAcknowledgmentStatuses.Pending => "pending",
            VendorPaymentAcknowledgmentStatuses.RetryPending => "retry_pending",
            VendorPaymentAcknowledgmentStatuses.Failed => "failed",
            VendorPaymentAcknowledgmentStatuses.Confirmed => "confirmed",
            VendorPaymentAcknowledgmentStatuses.SkippedDisabled => "skipped_disabled",
            VendorPaymentAcknowledgmentStatuses.Cancelled => "cancelled",
            _ => "unknown"
        };
    }
}
