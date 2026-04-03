using System.Diagnostics;
using System.Text.Json;
using ExitPass.CentralPms.Api.Validation;
using ExitPass.CentralPms.Application.PaymentAttempts;
using ExitPass.CentralPms.Application.PaymentAttempts.Commands;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Public.PaymentAttempts;
using ExitPass.CentralPms.Domain.PaymentAttempts.Exceptions;
using ExitPass.CentralPms.Domain.Sessions.Exceptions;
using ExitPass.CentralPms.Domain.Tariffs.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Controllers;

/// <summary>
/// Public API controller for payment-attempt creation and reuse.
/// </summary>
/// <remarks>
/// BRD:
/// - 9.9 Payment Initiation
/// - 18.3 Payment Initiation
/// - 9.21 Audit and Traceability
///
/// SDD:
/// - 10.2.4 Initiate Payment Attempt
/// - 10.7.1 Idempotent APIs
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - public create-payment-attempt requests must be validated before execution
/// - missing idempotency or correlation context must fail closed
/// - Central PMS remains the public owner of PaymentAttempt creation
/// </remarks>
[ApiController]
[Route("v1/public/payment-attempts")]
public sealed class PaymentAttemptsController : ControllerBase
{
    private static readonly JsonSerializerOptions LogJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.PaymentAttempts");

    private readonly ICreateOrReusePaymentAttemptUseCase _useCase;
    private readonly CreatePaymentAttemptRequestValidator _requestValidator;
    private readonly CreatePaymentAttemptHeadersValidator _headersValidator;
    private readonly ILogger<PaymentAttemptsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentAttemptsController"/> class.
    /// </summary>
    public PaymentAttemptsController(
        ICreateOrReusePaymentAttemptUseCase useCase,
        CreatePaymentAttemptRequestValidator requestValidator,
        CreatePaymentAttemptHeadersValidator headersValidator,
        ILogger<PaymentAttemptsController> logger)
    {
        _useCase = useCase;
        _requestValidator = requestValidator;
        _headersValidator = headersValidator;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new payment attempt or reuses an existing idempotent attempt.
    /// </summary>
    /// <param name="request">Incoming create-payment-attempt request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An HTTP result containing the created or reused payment attempt.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(CreatePaymentAttemptResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CreatePaymentAttemptResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreatePaymentAttemptRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var correlationIdRaw = Request.Headers["X-Correlation-Id"].FirstOrDefault();

        using var activity = ActivitySource.StartActivity("CreatePaymentAttempt", ActivityKind.Server);

        activity?.SetTag("http.route", "POST /v1/public/payment-attempts");
        activity?.SetTag("payment_provider", request?.PaymentProvider);
        activity?.SetTag("idempotency_key", idempotencyKey);
        activity?.SetTag("correlation_id", correlationIdRaw);
        activity?.SetTag("parking_session_id", request?.ParkingSessionId);
        activity?.SetTag("tariff_snapshot_id", request?.TariffSnapshotId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["parking_session_id"] = request?.ParkingSessionId,
            ["tariff_snapshot_id"] = request?.TariffSnapshotId,
            ["payment_provider"] = request?.PaymentProvider,
            ["idempotency_key"] = idempotencyKey,
            ["correlation_id_raw"] = correlationIdRaw
        });

        _logger.LogInformation("CreatePaymentAttempt request received.");

        var requestErrors = _requestValidator.Validate(request);
        var headerErrors = _headersValidator.Validate(idempotencyKey, correlationIdRaw);
        var allErrors = requestErrors.Concat(headerErrors).ToArray();

        if (allErrors.Length > 0)
        {
            var serializedErrors = JsonSerializer.Serialize(allErrors, LogJsonOptions);

            activity?.SetStatus(ActivityStatusCode.Error, "Validation failed");
            activity?.SetTag("validation_error_count", allErrors.Length);
            activity?.SetTag("validation_errors_json", serializedErrors);

            _logger.LogWarning(
                "CreatePaymentAttempt request validation failed. errors_json={ErrorsJson}",
                serializedErrors);

            return BadRequest(new ErrorResponse
            {
                ErrorCode = "INVALID_REQUEST",
                Message = "The request is invalid.",
                CorrelationId = Guid.TryParse(correlationIdRaw, out var badRequestCorrelationId)
                    ? badRequestCorrelationId
                    : Guid.Empty,
                Retryable = false,
                Details = new Dictionary<string, object?>
                {
                    ["errors"] = allErrors
                }
            });
        }

        try
        {
            var command = new CreateOrReusePaymentAttemptCommand
            {
                ParkingSessionId = request.ParkingSessionId,
                TariffSnapshotId = request.TariffSnapshotId,
                PaymentProviderCode = request.PaymentProvider,
                IdempotencyKey = idempotencyKey!,
                CorrelationId = Guid.Parse(correlationIdRaw!),
                RequestedBy = "public-api"
            };

            _logger.LogInformation("Dispatching CreateOrReusePaymentAttempt use case.");

            var result = await _useCase.ExecuteAsync(command, cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("payment_attempt_id", result.PaymentAttemptId);
            activity?.SetTag("was_reused", result.WasReused);
            activity?.SetTag("attempt_status", result.AttemptStatus);

            var response = new CreatePaymentAttemptResponse
            {
                PaymentAttemptId = result.PaymentAttemptId,
                AttemptStatus = result.AttemptStatus,
                PaymentProvider = result.PaymentProviderCode,
                WasReused = result.WasReused,
                ProviderHandoff = new ProviderHandoffDto
                {
                    Type = result.ProviderHandoff.Type,
                    Url = result.ProviderHandoff.Url,
                    ExpiresAt = result.ProviderHandoff.ExpiresAt
                }
            };

            if (result.WasReused)
            {
                _logger.LogInformation(
                    "CreatePaymentAttempt completed by reusing existing attempt. payment_attempt_id={PaymentAttemptId}",
                    result.PaymentAttemptId);

                return Ok(response);
            }

            _logger.LogInformation(
                "CreatePaymentAttempt completed by creating new attempt. payment_attempt_id={PaymentAttemptId}",
                result.PaymentAttemptId);

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (ParkingSessionNotFoundException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            _logger.LogWarning(ex, "CreatePaymentAttempt failed because parking session was not found.");
            return NotFound(BuildError("SESSION_NOT_FOUND", ex.Message, correlationIdRaw));
        }
        catch (TariffSnapshotNotFoundException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            _logger.LogWarning(ex, "CreatePaymentAttempt failed because tariff snapshot was not found.");
            return NotFound(BuildError("TARIFF_SNAPSHOT_NOT_FOUND", ex.Message, correlationIdRaw));
        }
        catch (TariffSnapshotNotEligibleException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            _logger.LogWarning(ex, "CreatePaymentAttempt failed because tariff snapshot was not eligible.");
            return Conflict(BuildError("TARIFF_SNAPSHOT_INVALID", ex.Message, correlationIdRaw));
        }
        catch (ActivePaymentAttemptAlreadyExistsException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            _logger.LogWarning(ex, "CreatePaymentAttempt failed because an active payment attempt already exists.");
            return Conflict(BuildError("ACTIVE_PAYMENT_ATTEMPT_EXISTS", ex.Message, correlationIdRaw));
        }
        catch (IdempotencyConflictException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            _logger.LogWarning(ex, "CreatePaymentAttempt failed because idempotency conflict was detected.");
            return Conflict(BuildError("IDEMPOTENCY_CONFLICT", ex.Message, correlationIdRaw));
        }
    }

    /// <summary>
    /// Builds a standardized error response.
    /// </summary>
    /// <param name="errorCode">Application error code.</param>
    /// <param name="message">Error message.</param>
    /// <param name="correlationIdRaw">Raw correlation ID header.</param>
    /// <returns>A standardized error response.</returns>
    private static ErrorResponse BuildError(string errorCode, string message, string? correlationIdRaw)
    {
        return new ErrorResponse
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = Guid.TryParse(correlationIdRaw, out var correlationId) ? correlationId : Guid.Empty,
            Retryable = false
        };
    }
}
