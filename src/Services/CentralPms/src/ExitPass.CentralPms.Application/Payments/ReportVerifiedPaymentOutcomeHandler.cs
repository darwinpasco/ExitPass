using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging;

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
    private readonly IRecordPaymentConfirmationGateway _recordPaymentConfirmationGateway;
    private readonly IFinalizePaymentAttemptUseCase _finalizePaymentAttemptUseCase;
    private readonly IIssueExitAuthorizationUseCase _issueExitAuthorizationUseCase;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<ReportVerifiedPaymentOutcomeHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportVerifiedPaymentOutcomeHandler"/> class.
    /// </summary>
    /// <param name="recordPaymentConfirmationGateway">Gateway for recording canonical payment confirmation evidence.</param>
    /// <param name="finalizePaymentAttemptUseCase">Use case for finalizing the payment attempt.</param>
    /// <param name="issueExitAuthorizationUseCase">Use case for issuing exit authorization after confirmed payment.</param>
    /// <param name="systemClock">System clock used for authoritative timestamps.</param>
    /// <param name="logger">Application logger.</param>
    public ReportVerifiedPaymentOutcomeHandler(
        IRecordPaymentConfirmationGateway recordPaymentConfirmationGateway,
        IFinalizePaymentAttemptUseCase finalizePaymentAttemptUseCase,
        IIssueExitAuthorizationUseCase issueExitAuthorizationUseCase,
        ISystemClock systemClock,
        ILogger<ReportVerifiedPaymentOutcomeHandler> logger)
    {
        _recordPaymentConfirmationGateway = recordPaymentConfirmationGateway;
        _finalizePaymentAttemptUseCase = finalizePaymentAttemptUseCase;
        _issueExitAuthorizationUseCase = issueExitAuthorizationUseCase;
        _systemClock = systemClock;
        _logger = logger;
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

        ValidateCommand(command);

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

        var finalized = await _finalizePaymentAttemptUseCase.ExecuteAsync(
            new FinalizePaymentAttemptCommand(
                command.PaymentAttemptId,
                command.FinalAttemptStatus,
                command.RequestedBy,
                command.CorrelationId),
            cancellationToken);

        if (!string.Equals(finalized.AttemptStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
        {
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

        var issued = await _issueExitAuthorizationUseCase.ExecuteAsync(
            new IssueExitAuthorizationCommand(
                command.ParkingSessionId,
                command.PaymentAttemptId,
                command.RequestedByUserId,
                command.CorrelationId),
            cancellationToken);

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
}
