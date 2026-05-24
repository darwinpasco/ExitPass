using System.Diagnostics;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Orchestrates the verified payment outcome path inside Central PMS.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 6.5 Issue Exit Authorization
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Only Central PMS may finalize PaymentAttempt state
/// - Payment confirmation evidence is recorded before exit authorization issuance
/// - Exit authorization is issued only after confirmed payment finality
/// </summary>
public sealed class ReportVerifiedPaymentOutcomeHandler : IReportVerifiedPaymentOutcomeUseCase
{
    /// <summary>
    /// Activity source for verified provider outcome reporting spans.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Application.Payments");

    private readonly IRecordPaymentConfirmationGateway _recordPaymentConfirmationGateway;
    private readonly IFinalizePaymentAttemptUseCase _finalizePaymentAttemptUseCase;
    private readonly IIssueExitAuthorizationUseCase _issueExitAuthorizationUseCase;
    private readonly IIntegrationEventPublisher _eventPublisher;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<ReportVerifiedPaymentOutcomeHandler> _logger;
    private readonly CentralPmsMetrics _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportVerifiedPaymentOutcomeHandler"/> class.
    /// </summary>
    /// <param name="recordPaymentConfirmationGateway">Gateway for recording canonical payment confirmation evidence.</param>
    /// <param name="finalizePaymentAttemptUseCase">Use case for finalizing the payment attempt.</param>
    /// <param name="issueExitAuthorizationUseCase">Use case for issuing exit authorization after confirmed payment.</param>
    /// <param name="eventPublisher">Best-effort integration event publisher for already-committed finality evidence.</param>
    /// <param name="systemClock">System clock used for authoritative timestamps.</param>
    /// <param name="logger">Application logger.</param>
    public ReportVerifiedPaymentOutcomeHandler(
        IRecordPaymentConfirmationGateway recordPaymentConfirmationGateway,
        IFinalizePaymentAttemptUseCase finalizePaymentAttemptUseCase,
        IIssueExitAuthorizationUseCase issueExitAuthorizationUseCase,
        IIntegrationEventPublisher eventPublisher,
        ISystemClock systemClock,
        ILogger<ReportVerifiedPaymentOutcomeHandler> logger,
        CentralPmsMetrics? metrics = null)
    {
        _recordPaymentConfirmationGateway = recordPaymentConfirmationGateway;
        _finalizePaymentAttemptUseCase = finalizePaymentAttemptUseCase;
        _issueExitAuthorizationUseCase = issueExitAuthorizationUseCase;
        _eventPublisher = eventPublisher;
        _systemClock = systemClock;
        _logger = logger;
        _metrics = metrics ?? new CentralPmsMetrics();
    }

    /// <summary>
    /// Records verified payment evidence, finalizes the payment attempt,
    /// and issues an exit authorization when the final attempt status is confirmed.
    /// </summary>
    /// <param name="command">Verified payment outcome command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authoritative verified outcome result.</returns>
    public async Task<ReportVerifiedPaymentOutcomeResult> ExecuteAsync(
        ReportVerifiedPaymentOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ActivitySource.StartActivity("ReportVerifiedPaymentOutcome", ActivityKind.Internal);

        activity?.SetTag("operation", "report_verified_payment_outcome");
        activity?.SetTag("correlation_id", command.CorrelationId);
        activity?.SetTag("payment_attempt_id", command.PaymentAttemptId);
        activity?.SetTag("parking_session_id", command.ParkingSessionId);
        activity?.SetTag("provider_reference", command.ProviderReference);
        activity?.SetTag("provider_status", command.ProviderStatus);
        activity?.SetTag("final_status", command.FinalAttemptStatus);
        activity?.SetTag("requested_by", command.RequestedBy);
        activity?.SetTag("requested_by_user_id", command.RequestedByUserId);

        ValidateCommand(command);
        _metrics.VerifiedPaymentOutcomeReceived(command.ProviderStatus, command.FinalAttemptStatus);

        _logger.LogInformation(
            "Reporting verified payment outcome for payment_attempt_id={PaymentAttemptId}, provider_reference={ProviderReference}, final_attempt_status={FinalAttemptStatus}.",
            command.PaymentAttemptId,
            command.ProviderReference,
            command.FinalAttemptStatus);

        var confirmation = await _recordPaymentConfirmationGateway.RecordAsync(
            new RecordPaymentConfirmationCommand(
                command.PaymentAttemptId,
                command.ProviderReference,
                command.ProviderStatus,
                command.RequestedBy,
                RawCallbackReference: null,
                ProviderSignatureValid: true,
                ProviderPayloadHash: null,
                AmountConfirmed: null,
                CurrencyCode: null,
                command.CorrelationId),
            _systemClock.UtcNow,
            cancellationToken);

        activity?.SetTag("payment_confirmation_id", confirmation.PaymentConfirmationId);
        activity?.SetTag("verified_timestamp", confirmation.VerifiedTimestamp);
        _metrics.PaymentConfirmationRecorded(command.ProviderStatus, command.FinalAttemptStatus);

        await PublishBestEffortAsync(
            CreatePaymentConfirmationRecordedEvent(command, confirmation),
            cancellationToken);

        var finalized = await _finalizePaymentAttemptUseCase.ExecuteAsync(
            new FinalizePaymentAttemptCommand(
                command.PaymentAttemptId,
                command.FinalAttemptStatus,
                command.RequestedBy,
                command.CorrelationId),
            cancellationToken);

        if (!string.Equals(finalized.AttemptStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
        {
            await PublishBestEffortAsync(
                CreatePaymentFinalityReportedEvent(command, confirmation, finalized),
                cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("attempt_status", finalized.AttemptStatus);
            activity?.SetTag("outcome", "finalized_without_exit_authorization");

            return new ReportVerifiedPaymentOutcomeResult(
                PaymentConfirmationId: confirmation.PaymentConfirmationId,
                PaymentAttemptId: finalized.PaymentAttemptId,
                AttemptStatus: finalized.AttemptStatus,
                ExitAuthorizationId: null,
                AuthorizationToken: null,
                AuthorizationStatus: null,
                VerifiedTimestamp: confirmation.VerifiedTimestamp,
                IssuedAt: null,
                ExpirationTimestamp: null);
        }

        await PublishBestEffortAsync(
            CreatePaymentAttemptConfirmedEvent(command, finalized),
            cancellationToken);

        var issued = await _issueExitAuthorizationUseCase.ExecuteAsync(
            new IssueExitAuthorizationCommand(
                command.ParkingSessionId,
                command.PaymentAttemptId,
                command.RequestedByUserId,
                command.CorrelationId),
            cancellationToken);

        await PublishBestEffortAsync(
            CreatePaymentFinalityReportedEvent(command, confirmation, finalized),
            cancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("attempt_status", finalized.AttemptStatus);
        activity?.SetTag("exit_authorization_id", issued.ExitAuthorizationId);
        activity?.SetTag("authorization_status", issued.AuthorizationStatus);
        activity?.SetTag("outcome", "exit_authorization_issued");

        return new ReportVerifiedPaymentOutcomeResult(
            PaymentConfirmationId: confirmation.PaymentConfirmationId,
            PaymentAttemptId: finalized.PaymentAttemptId,
            AttemptStatus: finalized.AttemptStatus,
            ExitAuthorizationId: issued.ExitAuthorizationId,
            AuthorizationToken: issued.AuthorizationToken,
            AuthorizationStatus: issued.AuthorizationStatus,
            VerifiedTimestamp: confirmation.VerifiedTimestamp,
            IssuedAt: issued.IssuedAt,
            ExpirationTimestamp: issued.ExpirationTimestamp);
    }

    /// <summary>
    /// Validates the verified payment outcome command.
    /// </summary>
    /// <param name="command">Command to validate.</param>
    /// <exception cref="ArgumentException">Thrown when the command is invalid.</exception>
    private static void ValidateCommand(ReportVerifiedPaymentOutcomeCommand command)
    {
        if (command.PaymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException("PaymentAttemptId is required.", nameof(command));
        }

        if (command.ParkingSessionId == Guid.Empty)
        {
            throw new ArgumentException("ParkingSessionId is required.", nameof(command));
        }

        if (command.RequestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("RequestedByUserId is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.ProviderReference))
        {
            throw new ArgumentException("ProviderReference is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.ProviderStatus))
        {
            throw new ArgumentException("ProviderStatus is required.", nameof(command));
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

    private IntegrationEventEnvelope CreatePaymentConfirmationRecordedEvent(
        ReportVerifiedPaymentOutcomeCommand command,
        RecordPaymentConfirmationResult confirmation)
    {
        return new IntegrationEventEnvelope
        {
            EventType = IntegrationEventTypes.PaymentConfirmationRecorded,
            OccurredAtUtc = _systemClock.UtcNow,
            CorrelationId = command.CorrelationId,
            AggregateId = confirmation.PaymentConfirmationId.ToString(),
            AggregateType = "PaymentConfirmation",
            Payload = new PaymentConfirmationRecordedPayload
            {
                PaymentConfirmationId = confirmation.PaymentConfirmationId,
                PaymentAttemptId = command.PaymentAttemptId,
                ProviderReference = command.ProviderReference,
                ProviderStatus = command.ProviderStatus,
                VerifiedAtUtc = confirmation.VerifiedTimestamp
            }
        };
    }

    private IntegrationEventEnvelope CreatePaymentAttemptConfirmedEvent(
        ReportVerifiedPaymentOutcomeCommand command,
        FinalizePaymentAttemptResult finalized)
    {
        return new IntegrationEventEnvelope
        {
            EventType = IntegrationEventTypes.PaymentAttemptConfirmed,
            OccurredAtUtc = _systemClock.UtcNow,
            CorrelationId = command.CorrelationId,
            AggregateId = finalized.PaymentAttemptId.ToString(),
            AggregateType = "PaymentAttempt",
            Payload = new PaymentAttemptConfirmedPayload
            {
                PaymentAttemptId = finalized.PaymentAttemptId,
                AttemptStatus = finalized.AttemptStatus,
                ProviderReference = command.ProviderReference
            }
        };
    }

    private IntegrationEventEnvelope CreatePaymentFinalityReportedEvent(
        ReportVerifiedPaymentOutcomeCommand command,
        RecordPaymentConfirmationResult confirmation,
        FinalizePaymentAttemptResult finalized)
    {
        return new IntegrationEventEnvelope
        {
            EventType = IntegrationEventTypes.PaymentFinalityReportedToCentralPms,
            OccurredAtUtc = _systemClock.UtcNow,
            CorrelationId = command.CorrelationId,
            AggregateId = command.PaymentAttemptId.ToString(),
            AggregateType = "PaymentAttempt",
            Payload = new PaymentFinalityReportedPayload
            {
                PaymentAttemptId = command.PaymentAttemptId,
                ParkingSessionId = command.ParkingSessionId,
                PaymentConfirmationId = confirmation.PaymentConfirmationId,
                AttemptStatus = finalized.AttemptStatus,
                ProviderReference = command.ProviderReference
            }
        };
    }

    private async Task PublishBestEffortAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        try
        {
            await _eventPublisher.PublishAsync(envelope, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Non-authoritative integration event publication failed after DB finality. event_type={EventType} event_id={EventId} correlation_id={CorrelationId}",
                envelope.EventType,
                envelope.EventId,
                envelope.CorrelationId);
        }
    }
}
