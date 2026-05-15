using System.Diagnostics;
using System.Text.Json;
using ExitPass.CentralPms.Api.Validation;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Public.VendorParking;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Controllers;

/// <summary>
/// Public API controller for provider-neutral vendor parking session and tariff resolution.
/// </summary>
[ApiController]
[Route("v1/vendor-parking/resolve")]
public sealed class VendorParkingResolutionController : ControllerBase
{
    private static readonly JsonSerializerOptions LogJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.VendorParking");

    private readonly IResolveVendorParkingUseCase _useCase;
    private readonly ResolveVendorParkingRequestValidator _validator;
    private readonly ILogger<VendorParkingResolutionController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VendorParkingResolutionController"/> class.
    /// </summary>
    /// <param name="useCase">Vendor parking resolution use case.</param>
    /// <param name="validator">Request validator.</param>
    /// <param name="logger">Application logger.</param>
    public VendorParkingResolutionController(
        IResolveVendorParkingUseCase useCase,
        ResolveVendorParkingRequestValidator validator,
        ILogger<VendorParkingResolutionController> logger)
    {
        _useCase = useCase;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Resolves provider-neutral vendor parking session and tariff data.
    /// </summary>
    /// <param name="request">Vendor parking resolution request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved parking session and tariff data or a deterministic error envelope.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ResolveVendorParkingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResolveAsync(
        [FromBody] ResolveVendorParkingRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ResolveVendorParking", ActivityKind.Server);

        activity?.SetTag("http.route", "POST /v1/vendor-parking/resolve");
        activity?.SetTag("correlation_id", request?.CorrelationId);
        activity?.SetTag("vendor_system_id", request?.VendorSystemId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlation_id"] = request?.CorrelationId,
            ["vendor_system_id"] = request?.VendorSystemId
        });

        var errors = _validator.Validate(request);
        if (errors.Count > 0)
        {
            var serializedErrors = JsonSerializer.Serialize(errors, LogJsonOptions);

            activity?.SetStatus(ActivityStatusCode.Error, "Validation failed");
            activity?.SetTag("lookup.outcome", "invalid_request");
            activity?.SetTag("validation_errors_json", serializedErrors);

            _logger.LogWarning(
                "ResolveVendorParking request validation failed. errors_json={ErrorsJson}",
                serializedErrors);

            return BadRequest(new ErrorResponse
            {
                ErrorCode = "INVALID_REQUEST",
                Message = "The request is invalid.",
                CorrelationId = request?.CorrelationId ?? Guid.Empty,
                Retryable = false,
                Details = new Dictionary<string, object?>
                {
                    ["errors"] = errors
                }
            });
        }

        var validRequest = request!;
        var result = await _useCase.ExecuteAsync(
            new ResolveVendorParkingCommand
            {
                SiteGroupId = validRequest.SiteGroupId,
                SiteId = validRequest.SiteId,
                PlateNumber = validRequest.PlateNumber,
                TicketReference = validRequest.TicketReference,
                CorrelationId = validRequest.CorrelationId
            },
            cancellationToken);

        activity?.SetTag("lookup.outcome", result.Outcome.ToString());
        activity?.SetTag("vendor_system_id", result.VendorSystemId ?? validRequest.VendorSystemId);
        activity?.SetTag("parking_session_id", result.ParkingSession?.ParkingSessionId);
        activity?.SetTag("tariff_snapshot_id", result.TariffSnapshot?.TariffSnapshotId);

        if (result.Outcome != ResolveVendorParkingOutcome.Resolved)
        {
            return MapFailure(result, validRequest);
        }

        if (result.ParkingSession is null || result.TariffSnapshot is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Malformed resolved result");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                BuildError("MALFORMED_VENDOR_RESPONSE", "Vendor response could not be mapped.", validRequest.CorrelationId, false));
        }

        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation(
            "ResolveVendorParking succeeded. vendor_system_id={VendorSystemId} parking_session_id={ParkingSessionId} tariff_snapshot_id={TariffSnapshotId} lookup_outcome={LookupOutcome}",
            result.VendorSystemId ?? validRequest.VendorSystemId,
            result.ParkingSession.ParkingSessionId,
            result.TariffSnapshot.TariffSnapshotId,
            result.Outcome);

        return Ok(new ResolveVendorParkingResponse
        {
            ParkingSessionId = result.ParkingSession.ParkingSessionId,
            TariffSnapshotId = result.TariffSnapshot.TariffSnapshotId,
            LookupOutcome = "resolved",
            PlateNumber = result.ParkingSession.PlateNumber,
            TicketReference = result.ParkingSession.TicketNumber,
            NetPayableMinorUnits = ToMinorUnits(result.TariffSnapshot.NetPayable),
            Currency = result.TariffSnapshot.CurrencyCode,
            TariffExpiresAt = result.TariffSnapshot.ExpiresAt,
            VendorSystemId = result.VendorSystemId ?? validRequest.VendorSystemId,
            CorrelationId = result.CorrelationId
        });
    }

    private IActionResult MapFailure(
        ResolveVendorParkingResult result,
        ResolveVendorParkingRequest request)
    {
        var errorCode = result.ErrorCode ?? result.Outcome.ToString().ToUpperInvariant();
        var vendorSystemId = result.VendorSystemId ?? request.VendorSystemId;

        _logger.LogWarning(
            "ResolveVendorParking failed. vendor_system_id={VendorSystemId} lookup_outcome={LookupOutcome} error_code={ErrorCode} retryable={Retryable}",
            vendorSystemId,
            result.Outcome,
            errorCode,
            result.Retryable);

        var error = BuildError(
            errorCode,
            ResolveMessage(result.Outcome),
            result.CorrelationId,
            result.Retryable);

        return result.Outcome switch
        {
            ResolveVendorParkingOutcome.SessionNotFound => NotFound(error),
            ResolveVendorParkingOutcome.RetryableUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, error),
            ResolveVendorParkingOutcome.MalformedVendorResponse => StatusCode(StatusCodes.Status502BadGateway, error),
            ResolveVendorParkingOutcome.InvalidRequest => BadRequest(error),
            ResolveVendorParkingOutcome.VendorRejected => Conflict(error),
            _ => StatusCode(StatusCodes.Status502BadGateway, error)
        };
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

    private static string ResolveMessage(ResolveVendorParkingOutcome outcome)
    {
        return outcome switch
        {
            ResolveVendorParkingOutcome.SessionNotFound => "Vendor parking session was not found.",
            ResolveVendorParkingOutcome.RetryableUnavailable => "Vendor parking resolution is temporarily unavailable.",
            ResolveVendorParkingOutcome.MalformedVendorResponse => "Vendor parking response was malformed.",
            ResolveVendorParkingOutcome.InvalidRequest => "Vendor parking resolution request is invalid.",
            ResolveVendorParkingOutcome.VendorRejected => "Vendor parking lookup was rejected.",
            _ => "Vendor parking resolution failed."
        };
    }

    private static long ToMinorUnits(decimal amount)
    {
        return decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }
}
