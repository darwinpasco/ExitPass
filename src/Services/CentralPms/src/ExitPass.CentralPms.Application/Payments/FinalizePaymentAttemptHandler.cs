using System.Diagnostics;
using System.Diagnostics.Metrics;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Handles the finalization of a payment attempt by invoking the authoritative persistence gateway.
/// </summary>
/// <remarks>
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 10.5.3 Report Verified Payment Outcome
/// - 14.3 Distributed Tracing
/// - 14.4 Structured Logging
///
/// Invariants Enforced:
/// - PaymentAttemptId must be non-empty
/// - FinalAttemptStatus must be valid and non-empty
/// - Finalization must remain DB-backed and deterministic
/// - Gateway is the single authority for state transition
/// </remarks>
public sealed class FinalizePaymentAttemptHandler : IFinalizePaymentAttemptUseCase
{
    /// <summary>
    /// Activity source for payment attempt finalization spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Application.Payments");

    /// <summary>
    /// Metrics meter for payment attempt finalization.
    /// </summary>
    private static readonly Meter Meter =
        new("ExitPass.CentralPms.Application.Payments", "1.0.0");

    /// <summary>
    /// Counts successful payment attempt finalizations.
    /// </summary>
    private static readonly Counter<long> SuccessCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.payment_attempt.finalize.succeeded",
            unit: "{attempt}",
            description: "Counts successful payment attempt finalizations.");

    /// <summary>
    /// Counts rejected payment attempt finalizations caused by deterministic business or validation failures.
    /// </summary>
    private static readonly Counter<long> RejectedCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.payment_attempt.finalize.rejected",
            unit: "{attempt}",
            description: "Counts rejected payment attempt finalizations.");

    /// <summary>
    /// Counts unexpected payment attempt finalization failures.
    /// </summary>
    private static readonly Counter<long> FailureCounter =
        Meter.CreateCounter<long>(
            name: "exitpass.payment_attempt.finalize.failed",
            unit: "{attempt}",
            description: "Counts unexpected payment attempt finalization failures.");

    private readonly IFinalizePaymentAttemptGateway _gateway;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<FinalizePaymentAttemptHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinalizePaymentAttemptHandler"/> class.
    /// </summary>
    /// <param name="gateway">Gateway used to finalize the payment attempt through the authoritative DB path.</param>
    /// <param name="systemClock">System clock used to timestamp the request.</param>
    /// <param name="logger">Application logger.</param>
    public FinalizePaymentAttemptHandler(
        IFinalizePaymentAttemptGateway gateway,
        ISystemClock systemClock,
        ILogger<FinalizePaymentAttemptHandler> logger)
    {
        _gateway = gateway;
        _systemClock = systemClock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FinalizePaymentAttemptResult> ExecuteAsync(
        FinalizePaymentAttemptCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ActivitySource.StartActivity("FinalizePaymentAttempt", ActivityKind.Internal);

        activity?.SetTag("operation", "finalize_payment_attempt");
        activity?.SetTag("payment_attempt_id", command.PaymentAttemptId);
        activity?.SetTag("final_attempt_status", command.FinalAttemptStatus);
        activity?.SetTag("requested_by", command.RequestedBy);
        activity?.SetTag("correlation_id", command.CorrelationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["payment_attempt_id"] = command.PaymentAttemptId,
            ["final_attempt_status"] = command.FinalAttemptStatus,
            ["requested_by"] = command.RequestedBy,
            ["correlation_id"] = command.CorrelationId
        });

        _logger.LogInformation("FinalizePaymentAttempt started.");

        try
        {
            ValidateCommand(command);

            var dbStart = _systemClock.UtcNow;

            var dbResult = await _gateway.FinalizeAsync(
                new FinalizePaymentAttemptDbRequest
                {
                    PaymentAttemptId = command.PaymentAttemptId,
                    FinalAttemptStatus = command.FinalAttemptStatus,
                    RequestedBy = command.RequestedBy,
                    CorrelationId = command.CorrelationId,
                    RequestedAt = _systemClock.UtcNow
                },
                cancellationToken);

            var dbDuration = _systemClock.UtcNow - dbStart;

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("attempt_status", dbResult.AttemptStatus);
            activity?.SetTag("db.duration_ms", dbDuration.TotalMilliseconds);

            SuccessCounter.Add(
                1,
                new KeyValuePair<string, object?>("attempt_status", dbResult.AttemptStatus),
                new KeyValuePair<string, object?>("final_attempt_status", command.FinalAttemptStatus));

            _logger.LogInformation(
                "PaymentAttempt finalized successfully. payment_attempt_id={PaymentAttemptId} attempt_status={AttemptStatus}",
                dbResult.PaymentAttemptId,
                dbResult.AttemptStatus);

            return new FinalizePaymentAttemptResult(
                PaymentAttemptId: dbResult.PaymentAttemptId,
                AttemptStatus: dbResult.AttemptStatus);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("rejection_reason", "INVALID_REQUEST");

            RejectedCounter.Add(
                1,
                new KeyValuePair<string, object?>("reason", "INVALID_REQUEST"),
                new KeyValuePair<string, object?>("final_attempt_status", command.FinalAttemptStatus));

            _logger.LogWarning(
                ex,
                "FinalizePaymentAttempt rejected because the command is invalid.");

            throw;
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            activity?.SetTag("rejection_reason", ex.GetType().Name);

            RejectedCounter.Add(
                1,
                new KeyValuePair<string, object?>("reason", ex.GetType().Name),
                new KeyValuePair<string, object?>("final_attempt_status", command.FinalAttemptStatus));

            _logger.LogWarning(
                ex,
                "FinalizePaymentAttempt was rejected by deterministic business rules.");

            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            FailureCounter.Add(
                1,
                new KeyValuePair<string, object?>("exception_type", ex.GetType().Name),
                new KeyValuePair<string, object?>("final_attempt_status", command.FinalAttemptStatus));

            _logger.LogError(
                ex,
                "Failed to finalize PaymentAttempt. payment_attempt_id={PaymentAttemptId}",
                command.PaymentAttemptId);

            throw;
        }
    }

    /// <summary>
    /// Validates the incoming command.
    /// </summary>
    /// <param name="command">Command to validate.</param>
    /// <exception cref="ArgumentException">Thrown when required fields are invalid.</exception>
    private static void ValidateCommand(FinalizePaymentAttemptCommand command)
    {
        if (command.PaymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException("PaymentAttemptId is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.FinalAttemptStatus))
        {
            throw new ArgumentException("FinalAttemptStatus is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.RequestedBy))
        {
            throw new ArgumentException("RequestedBy is required.", nameof(command));
        }
    }
}
