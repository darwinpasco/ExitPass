using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Routing;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;

/// <summary>
/// Default post-finality Vendor PMS paid-state acknowledgment workflow.
/// </summary>
public sealed class VendorPaymentAcknowledgmentWorkflow : IVendorPaymentAcknowledgmentWorkflow
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    private readonly IVendorPaymentAcknowledgmentRepository _repository;
    private readonly IVendorPmsParkingResolutionClient _vendorClient;
    private readonly IVendorPaymentConfirmationGuard _confirmationGuard;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<VendorPaymentAcknowledgmentWorkflow> _logger;

    /// <summary>
    /// Creates the workflow.
    /// </summary>
    public VendorPaymentAcknowledgmentWorkflow(
        IVendorPaymentAcknowledgmentRepository repository,
        IVendorPmsParkingResolutionClient vendorClient,
        IVendorPaymentConfirmationGuard confirmationGuard,
        ISystemClock systemClock,
        ILogger<VendorPaymentAcknowledgmentWorkflow> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _vendorClient = vendorClient ?? throw new ArgumentNullException(nameof(vendorClient));
        _confirmationGuard = confirmationGuard ?? throw new ArgumentNullException(nameof(confirmationGuard));
        _systemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task ProcessAsync(
        VendorPaymentAcknowledgmentWorkflowCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PaymentAttemptId == Guid.Empty ||
            command.PaymentConfirmationId == Guid.Empty ||
            command.ParkingSessionId == Guid.Empty)
        {
            _logger.LogWarning(
                "Vendor PMS acknowledgment skipped because required identifiers are missing. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} parking_session_id={ParkingSessionId} correlation_id={CorrelationId}",
                command.PaymentAttemptId,
                command.PaymentConfirmationId,
                command.ParkingSessionId,
                command.CorrelationId);
            return;
        }

        var basis = await _repository.LoadBasisAsync(
            command.PaymentAttemptId,
            command.PaymentConfirmationId,
            command.ParkingSessionId,
            cancellationToken);
        if (basis is null)
        {
            _logger.LogWarning(
                "Vendor PMS acknowledgment skipped because confirmed payment basis could not be loaded. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} parking_session_id={ParkingSessionId} correlation_id={CorrelationId}",
                command.PaymentAttemptId,
                command.PaymentConfirmationId,
                command.ParkingSessionId,
                command.CorrelationId);
            return;
        }

        var acknowledgment = await CreateOrReuseAsync(basis, command.CorrelationId, cancellationToken);
        if (string.Equals(acknowledgment.AcknowledgmentStatus, VendorPaymentAcknowledgmentStatuses.Confirmed, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Vendor PMS acknowledgment already confirmed; skipping duplicate vendor confirm. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} vendor_system_code={VendorSystemCode} correlation_id={CorrelationId}",
                basis.PaymentAttemptId,
                basis.PaymentConfirmationId,
                basis.VendorSystemCode,
                command.CorrelationId);
            return;
        }

        if (!_confirmationGuard.IsConfirmPaymentEnabled(basis.VendorSystemCode))
        {
            var skipped = await _repository.MarkSkippedDisabledAsync(
                new MarkVendorPaymentAcknowledgmentSkippedDisabledCommand(
                    acknowledgment.VendorPaymentAcknowledgmentId,
                    _confirmationGuard.DisabledMessage(basis.VendorSystemCode),
                    _systemClock.UtcNow),
                cancellationToken);

            _logger.LogInformation(
                "Vendor PMS acknowledgment skipped because confirmation is disabled. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} vendor_system_code={VendorSystemCode} acknowledgment_status={AcknowledgmentStatus} correlation_id={CorrelationId}",
                basis.PaymentAttemptId,
                basis.PaymentConfirmationId,
                basis.VendorSystemCode,
                skipped.AcknowledgmentStatus,
                command.CorrelationId);
            return;
        }

        var ticketReference = Normalize(acknowledgment.CardNum) ??
            Normalize(acknowledgment.TicketNumber) ??
            Normalize(acknowledgment.VendorSessionRef);
        if (ticketReference is null)
        {
            await MarkFailureAsync(
                acknowledgment,
                "VENDOR_CONFIRMATION_IDENTIFIER_MISSING",
                "Vendor PMS confirmation requires a ticket or card reference.",
                retryable: false,
                cancellationToken);
            return;
        }

        VendorParkingFeeConfirmationResponse response;
        try
        {
            response = await _vendorClient.ConfirmParkingFeeAsync(
                new VendorParkingFeeConfirmationRequest(
                    PlateNumber: null,
                    TicketReference: ticketReference,
                    ImmediatelyLeave: 0,
                    AmountMinor: acknowledgment.RequestFeeMinorUnits,
                    Currency: acknowledgment.RequestCurrencyCode ?? basis.RequestCurrencyCode,
                    command.CorrelationId,
                    new VendorAdapterRequestContext(basis.SiteId, basis.SiteGroupId, basis.VendorSystemId,
                        basis.SourceAdapterIdentityId),
                    acknowledgment.IdempotencyKey),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Vendor PMS confirmation threw after ExitPass payment finality; recording retryable acknowledgment failure. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} vendor_system_code={VendorSystemCode} correlation_id={CorrelationId}",
                basis.PaymentAttemptId,
                basis.PaymentConfirmationId,
                basis.VendorSystemCode,
                command.CorrelationId);

            await MarkFailureAsync(
                acknowledgment,
                ex.GetType().Name,
                "Vendor PMS confirmation failed after ExitPass payment finality.",
                retryable: true,
                cancellationToken);
            return;
        }

        if (response.Status == VendorParkingLookupStatus.Confirmed)
        {
            var confirmed = await _repository.MarkConfirmedAsync(
                new MarkVendorPaymentAcknowledgmentConfirmedCommand(
                    acknowledgment.VendorPaymentAcknowledgmentId,
                    response.ErrorCode ?? VendorPaymentAcknowledgmentStatuses.Confirmed,
                    "Vendor PMS confirmed paid-state acknowledgment.",
                    response.Confirmation?.AmountMinor,
                    response.Confirmation?.FeeTime,
                    _systemClock.UtcNow),
                cancellationToken);

            _logger.LogInformation(
                "Vendor PMS acknowledgment confirmed. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} vendor_system_code={VendorSystemCode} vendor_code={VendorCode} acknowledgment_status={AcknowledgmentStatus} correlation_id={CorrelationId}",
                confirmed.PaymentAttemptId,
                confirmed.PaymentConfirmationId,
                confirmed.VendorSystemCode,
                confirmed.VendorCode,
                confirmed.AcknowledgmentStatus,
                command.CorrelationId);
            return;
        }

        await MarkFailureAsync(
            acknowledgment,
            response.ErrorCode ?? response.Status.ToString(),
            $"Vendor PMS confirmation returned {response.Status}.",
            response.Retryable,
            cancellationToken);
    }

    private async Task<VendorPaymentAcknowledgmentRecord> CreateOrReuseAsync(
        VendorPaymentAcknowledgmentBasis basis,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.ReadByPaymentConfirmationAsync(
            basis.PaymentConfirmationId,
            basis.VendorSystemCode,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _repository.CreatePendingAsync(
                new CreateVendorPaymentAcknowledgmentCommand(
                    basis.PaymentAttemptId,
                    basis.PaymentConfirmationId,
                    basis.ParkingSessionId,
                    basis.VendorSystemCode,
                    basis.VendorSessionRef,
                    basis.TicketNumber,
                    basis.CardNum,
                    basis.RequestFeeMinorUnits,
                    basis.RequestCurrencyCode,
                    $"vendor-ack-{basis.PaymentConfirmationId:N}-{NormalizeIdempotencySegment(basis.VendorSystemCode)}",
                    correlationId,
                    _systemClock.UtcNow),
                cancellationToken);
        }
        catch (VendorPaymentAcknowledgmentConflictException)
        {
            var conflictedRecord = await _repository.ReadByPaymentConfirmationAsync(
                basis.PaymentConfirmationId,
                basis.VendorSystemCode,
                cancellationToken);

            if (conflictedRecord is null)
            {
                throw;
            }

            return conflictedRecord;
        }
    }

    private async Task MarkFailureAsync(
        VendorPaymentAcknowledgmentRecord acknowledgment,
        string? vendorCode,
        string vendorMessage,
        bool retryable,
        CancellationToken cancellationToken)
    {
        var now = _systemClock.UtcNow;
        var failed = await _repository.MarkFailedAsync(
            new MarkVendorPaymentAcknowledgmentFailedCommand(
                acknowledgment.VendorPaymentAcknowledgmentId,
                vendorCode,
                vendorMessage,
                now,
                retryable ? now.Add(RetryDelay) : null,
                now),
            cancellationToken);

        _logger.LogWarning(
            "Vendor PMS acknowledgment failed after ExitPass payment finality. payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} vendor_system_code={VendorSystemCode} vendor_code={VendorCode} acknowledgment_status={AcknowledgmentStatus} correlation_id={CorrelationId}",
            failed.PaymentAttemptId,
            failed.PaymentConfirmationId,
            failed.VendorSystemCode,
            failed.VendorCode,
            failed.AcknowledgmentStatus,
            failed.CorrelationId);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeIdempotencySegment(string value)
    {
        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "vendor" : normalized.ToLowerInvariant();
    }
}
