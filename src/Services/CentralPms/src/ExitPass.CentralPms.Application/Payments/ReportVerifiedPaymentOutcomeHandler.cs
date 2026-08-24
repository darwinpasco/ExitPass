using System.Diagnostics;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
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
    private readonly IVendorPaymentAcknowledgmentWorkflow? _vendorPaymentAcknowledgmentWorkflow;
    private readonly IDigitalPaymentFiscalIssuanceService? _digitalPaymentFiscalIssuanceService;
    private readonly IDigitalPaymentFiscalRecoveryContextReader? _digitalPaymentFiscalRecoveryContextReader;
    private readonly FiscalIssuancePosServerIntegrationOptions _posServerOptions;
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
    /// <param name="metrics">Optional Central PMS metrics recorder.</param>
    /// <param name="vendorPaymentAcknowledgmentWorkflow">Optional post-finality Vendor PMS payment acknowledgment workflow.</param>
    public ReportVerifiedPaymentOutcomeHandler(
        IRecordPaymentConfirmationGateway recordPaymentConfirmationGateway,
        IFinalizePaymentAttemptUseCase finalizePaymentAttemptUseCase,
        IIssueExitAuthorizationUseCase issueExitAuthorizationUseCase,
        IIntegrationEventPublisher eventPublisher,
        ISystemClock systemClock,
        ILogger<ReportVerifiedPaymentOutcomeHandler> logger,
        CentralPmsMetrics? metrics = null,
        IVendorPaymentAcknowledgmentWorkflow? vendorPaymentAcknowledgmentWorkflow = null,
        IDigitalPaymentFiscalIssuanceService? digitalPaymentFiscalIssuanceService = null,
        FiscalIssuancePosServerIntegrationOptions? posServerOptions = null,
        IDigitalPaymentFiscalRecoveryContextReader? digitalPaymentFiscalRecoveryContextReader = null)
    {
        _recordPaymentConfirmationGateway = recordPaymentConfirmationGateway;
        _finalizePaymentAttemptUseCase = finalizePaymentAttemptUseCase;
        _issueExitAuthorizationUseCase = issueExitAuthorizationUseCase;
        _eventPublisher = eventPublisher;
        _vendorPaymentAcknowledgmentWorkflow = vendorPaymentAcknowledgmentWorkflow;
        _digitalPaymentFiscalIssuanceService = digitalPaymentFiscalIssuanceService;
        _digitalPaymentFiscalRecoveryContextReader = digitalPaymentFiscalRecoveryContextReader;
        _posServerOptions = posServerOptions ?? new FiscalIssuancePosServerIntegrationOptions();
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

        var recovered = await TryResumeFiscalProcessingAsync(command, cancellationToken);
        if (recovered is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("attempt_status", recovered.AttemptStatus);
            activity?.SetTag("exit_authorization_id", recovered.ExitAuthorizationId);
            activity?.SetTag("authorization_status", recovered.AuthorizationStatus);
            activity?.SetTag("outcome", "fiscal_recovery_resumed");
            return recovered;
        }

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

        await EnsureDigitalFiscalIssuanceAsync(command, confirmation, cancellationToken);

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

        await ProcessVendorPaymentAcknowledgmentBestEffortAsync(
            command,
            confirmation,
            finalized,
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

    private async Task EnsureDigitalFiscalIssuanceAsync(
        ReportVerifiedPaymentOutcomeCommand command,
        RecordPaymentConfirmationResult confirmation,
        CancellationToken cancellationToken)
    {
        if (!_posServerOptions.EnableLiveFiscalIssuanceFromPaymentFlow)
        {
            return;
        }

        if (_digitalPaymentFiscalIssuanceService is null)
        {
            throw new InvalidOperationException("digital_payment_fiscal_issuance_service_required");
        }

        var result = await _digitalPaymentFiscalIssuanceService.IssueOrReadAsync(
            new DigitalPaymentFiscalIssuanceCommand(
                command.PaymentAttemptId,
                confirmation.PaymentConfirmationId,
                command.ParkingSessionId,
                command.ProviderReference,
                command.CorrelationId,
                null),
            cancellationToken);

        if (!result.ReadyForExitAuthorization)
        {
            if (result.RetryableAfterServiceRecovery)
            {
                throw new RetryableFiscalIssuanceUnavailableException(
                    result.FiscalIssuanceReferenceId);
            }

            throw new InvalidOperationException(
                result.SafeErrorCode ?? "digital_payment_fiscal_issuance_not_ready");
        }
    }

    private async Task<ReportVerifiedPaymentOutcomeResult?> TryResumeFiscalProcessingAsync(
        ReportVerifiedPaymentOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        if (!_posServerOptions.EnableLiveFiscalIssuanceFromPaymentFlow ||
            _digitalPaymentFiscalRecoveryContextReader is null ||
            _digitalPaymentFiscalIssuanceService is null)
        {
            return null;
        }

        var recovery = await _digitalPaymentFiscalRecoveryContextReader.FindByPaymentAttemptIdAsync(
            command.PaymentAttemptId,
            cancellationToken);
        if (recovery is null)
        {
            return null;
        }

        EnsureRecoveryRequestMatches(command, recovery);
        if (!recovery.PermitsServiceRecovery && !recovery.IsCompleted)
        {
            throw new InvalidOperationException("payment_attempt_has_no_retryable_fiscal_recovery_context");
        }

        _logger.LogInformation(
            "Resuming post-payment fiscal processing. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} fiscal_issuance_reference_id={FiscalIssuanceReferenceId} correlation_id={CorrelationId}",
            recovery.PaymentAttemptId,
            recovery.PaymentConfirmationId,
            recovery.FiscalIssuanceReferenceId,
            command.CorrelationId);

        var confirmation = new RecordPaymentConfirmationResult(
            recovery.PaymentConfirmationId,
            recovery.PaymentAttemptId,
            recovery.ProviderReference,
            command.ProviderStatus,
            recovery.ConfirmationStatus,
            recovery.VerifiedTimestamp);
        var finalized = new FinalizePaymentAttemptResult(recovery.PaymentAttemptId, recovery.AttemptStatus);

        await EnsureDigitalFiscalIssuanceAsync(command, confirmation, cancellationToken);

        var issued = await _issueExitAuthorizationUseCase.ExecuteAsync(
            new IssueExitAuthorizationCommand(
                command.ParkingSessionId,
                command.PaymentAttemptId,
                command.RequestedByUserId,
                command.CorrelationId),
            cancellationToken);

        if (!recovery.IsCompleted)
        {
            await PublishBestEffortAsync(
                CreatePaymentFinalityReportedEvent(command, confirmation, finalized),
                cancellationToken);
        }

        await ProcessVendorPaymentAcknowledgmentBestEffortAsync(
            command,
            confirmation,
            finalized,
            cancellationToken);

        return new ReportVerifiedPaymentOutcomeResult(
            confirmation.PaymentConfirmationId,
            finalized.PaymentAttemptId,
            finalized.AttemptStatus,
            issued.ExitAuthorizationId,
            issued.AuthorizationToken,
            issued.AuthorizationStatus,
            confirmation.VerifiedTimestamp,
            issued.IssuedAt,
            issued.ExpirationTimestamp);
    }

    private static void EnsureRecoveryRequestMatches(
        ReportVerifiedPaymentOutcomeCommand command,
        DigitalPaymentFiscalRecoveryContext recovery)
    {
        if (recovery.PaymentAttemptId != command.PaymentAttemptId ||
            recovery.ParkingSessionId != command.ParkingSessionId ||
            !string.Equals(recovery.ProviderReference, command.ProviderReference, StringComparison.Ordinal) ||
            !string.Equals(recovery.AttemptStatus, "CONFIRMED", StringComparison.Ordinal) ||
            !string.Equals(recovery.ConfirmationStatus, "RECORDED", StringComparison.Ordinal) ||
            !string.Equals(command.ProviderStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(command.FinalAttemptStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentFinalityConflictException(
                "PAYMENT_ATTEMPT_ALREADY_FINAL",
                "The final payment request does not match its persisted fiscal recovery context.");
        }
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

    private async Task ProcessVendorPaymentAcknowledgmentBestEffortAsync(
        ReportVerifiedPaymentOutcomeCommand command,
        RecordPaymentConfirmationResult confirmation,
        FinalizePaymentAttemptResult finalized,
        CancellationToken cancellationToken)
    {
        if (_vendorPaymentAcknowledgmentWorkflow is null ||
            !string.Equals(finalized.AttemptStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await _vendorPaymentAcknowledgmentWorkflow.ProcessAsync(
                new VendorPaymentAcknowledgmentWorkflowCommand(
                    finalized.PaymentAttemptId,
                    confirmation.PaymentConfirmationId,
                    command.ParkingSessionId,
                    command.CorrelationId),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Vendor PMS payment acknowledgment failed after ExitPass payment finality and will not roll back finality. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} correlation_id={CorrelationId}",
                finalized.PaymentAttemptId,
                confirmation.PaymentConfirmationId,
                command.CorrelationId);
        }
    }
}
