using ExitPass.CentralPms.Api.Validation;
using ExitPass.CentralPms.Application.PaymentAttempts;
using ExitPass.CentralPms.Application.PaymentAttempts.Commands;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Public.PaymentAttempts;
using ExitPass.CentralPms.Domain.PaymentAttempts.Exceptions;
using ExitPass.CentralPms.Domain.Sessions.Exceptions;
using ExitPass.CentralPms.Domain.Tariffs.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace ExitPass.CentralPms.Api.Controllers;

[ApiController]
[Route("v1/public/payment-attempts")]
public sealed class PaymentAttemptsController : ControllerBase
{
    private readonly ICreateOrReusePaymentAttemptUseCase _useCase;
    private readonly CreatePaymentAttemptRequestValidator _requestValidator;
    private readonly CreatePaymentAttemptHeadersValidator _headersValidator;

    public PaymentAttemptsController(
        ICreateOrReusePaymentAttemptUseCase useCase,
        CreatePaymentAttemptRequestValidator requestValidator,
        CreatePaymentAttemptHeadersValidator headersValidator)
    {
        _useCase = useCase;
        _requestValidator = requestValidator;
        _headersValidator = headersValidator;
    }

    /// <summary>
    /// BRD:
    /// - 9.9 Payment Initiation
    /// - 18.3 Payment Initiation
    ///
    /// SDD:
    /// - 10.2.4 Initiate Payment Attempt
    /// - 10.7.1 Idempotent APIs
    ///
    /// Invariants Enforced:
    /// - public create-payment-attempt requests must be validated before execution
    /// - missing idempotency or correlation context must fail closed
    /// - Central PMS remains the public owner of PaymentAttempt creation
    /// </summary>
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

        var requestErrors = _requestValidator.Validate(request);
        var headerErrors = _headersValidator.Validate(idempotencyKey, correlationIdRaw);
        var allErrors = requestErrors.Concat(headerErrors).ToArray();

        if (allErrors.Length > 0)
        {
            return BadRequest(new ErrorResponse
            {
                ErrorCode = "INVALID_REQUEST",
                Message = "The request is invalid.",
                CorrelationId = Guid.TryParse(correlationIdRaw, out var badRequestCorrelationId) ? badRequestCorrelationId : Guid.Empty,
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

            var result = await _useCase.ExecuteAsync(command, cancellationToken);
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

            return result.WasReused ? Ok(response) : StatusCode(StatusCodes.Status201Created, response);
        }
        catch (ParkingSessionNotFoundException ex)
        {
            return NotFound(BuildError("SESSION_NOT_FOUND", ex.Message, correlationIdRaw));
        }
        catch (TariffSnapshotNotFoundException ex)
        {
            return NotFound(BuildError("TARIFF_SNAPSHOT_NOT_FOUND", ex.Message, correlationIdRaw));
        }
        catch (TariffSnapshotNotEligibleException ex)
        {
            return Conflict(BuildError("TARIFF_SNAPSHOT_INVALID", ex.Message, correlationIdRaw));
        }
        catch (ActivePaymentAttemptAlreadyExistsException ex)
        {
            return Conflict(BuildError("ACTIVE_PAYMENT_ATTEMPT_EXISTS", ex.Message, correlationIdRaw));
        }
        catch (IdempotencyConflictException ex)
        {
            return Conflict(BuildError("IDEMPOTENCY_CONFLICT", ex.Message, correlationIdRaw));
        }
    }

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