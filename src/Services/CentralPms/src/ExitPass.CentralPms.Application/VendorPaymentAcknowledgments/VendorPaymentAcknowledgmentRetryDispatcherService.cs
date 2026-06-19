using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;

/// <summary>
/// Sequential dispatcher for due Vendor PMS acknowledgment retries.
///
/// This dispatcher is intentionally single-record sequential. The current durable model has no
/// in-progress claim status, so concurrent dispatcher instances are not coordinated here.
/// </summary>
public sealed class VendorPaymentAcknowledgmentRetryDispatcherService : IVendorPaymentAcknowledgmentRetryDispatcherService
{
    private const int DefaultBatchSize = 25;
    private const int MaxBatchSize = 100;

    private readonly IVendorPaymentAcknowledgmentRepository _repository;
    private readonly IVendorPaymentAcknowledgmentWorkflow _workflow;
    private readonly ISystemClock _systemClock;
    private readonly ILogger<VendorPaymentAcknowledgmentRetryDispatcherService> _logger;

    /// <summary>
    /// Creates the retry dispatcher service.
    /// </summary>
    public VendorPaymentAcknowledgmentRetryDispatcherService(
        IVendorPaymentAcknowledgmentRepository repository,
        IVendorPaymentAcknowledgmentWorkflow workflow,
        ISystemClock systemClock,
        ILogger<VendorPaymentAcknowledgmentRetryDispatcherService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _systemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<VendorPaymentAcknowledgmentRetryDispatchResult> DispatchDueAsync(
        DispatchVendorPaymentAcknowledgmentRetriesCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var batchSize = NormalizeBatchSize(command.BatchSize);
        var now = _systemClock.UtcNow;
        var dueRecords = await _repository.FindDueRetryPendingAsync(now, batchSize, cancellationToken);
        var items = new List<VendorPaymentAcknowledgmentRetryDispatchItemResult>(dueRecords.Count);

        foreach (var dueRecord in dueRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await _repository.ReadAsync(dueRecord.VendorPaymentAcknowledgmentId, cancellationToken);
            if (current is null)
            {
                items.Add(new VendorPaymentAcknowledgmentRetryDispatchItemResult(
                    dueRecord.VendorPaymentAcknowledgmentId,
                    dueRecord.PaymentAttemptId,
                    dueRecord.PaymentConfirmationId,
                    dueRecord.ParkingSessionId,
                    dueRecord.VendorSystemCode,
                    dueRecord.AcknowledgmentStatus,
                    FinalStatus: null,
                    Succeeded: false,
                    Skipped: true,
                    FailureCode: "VENDOR_ACKNOWLEDGMENT_NOT_FOUND",
                    dueRecord.CorrelationId));
                continue;
            }

            if (ShouldSkip(current, now))
            {
                items.Add(Skipped(current));
                continue;
            }

            if (!current.ParkingSessionId.HasValue)
            {
                var failed = await _repository.MarkFailedAsync(
                    new MarkVendorPaymentAcknowledgmentFailedCommand(
                        current.VendorPaymentAcknowledgmentId,
                        "VENDOR_ACKNOWLEDGMENT_RETRY_BASIS_MISSING",
                        "Vendor PMS acknowledgment retry requires a parking session identifier.",
                        now,
                        NextRetryAt: null,
                        UpdatedAt: now),
                    cancellationToken);

                items.Add(Failed(current, failed.AcknowledgmentStatus, "VENDOR_ACKNOWLEDGMENT_RETRY_BASIS_MISSING"));
                continue;
            }

            try
            {
                await _workflow.ProcessAsync(
                    new VendorPaymentAcknowledgmentWorkflowCommand(
                        current.PaymentAttemptId,
                        current.PaymentConfirmationId,
                        current.ParkingSessionId.Value,
                        current.CorrelationId ?? Guid.Empty),
                    cancellationToken);

                var final = await _repository.ReadAsync(current.VendorPaymentAcknowledgmentId, cancellationToken);
                items.Add(Completed(current, final));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Vendor PMS acknowledgment retry dispatch item failed. acknowledgment_id={AcknowledgmentId} payment_attempt_id={PaymentAttemptId} payment_confirmation_id={PaymentConfirmationId} vendor_system_code={VendorSystemCode} status={Status} attempt_count={AttemptCount} next_retry_at={NextRetryAt} correlation_id={CorrelationId} failure_type={FailureType}",
                    current.VendorPaymentAcknowledgmentId,
                    current.PaymentAttemptId,
                    current.PaymentConfirmationId,
                    current.VendorSystemCode,
                    current.AcknowledgmentStatus,
                    current.AttemptCount,
                    current.NextRetryAt,
                    current.CorrelationId,
                    ex.GetType().Name);

                items.Add(Failed(current, current.AcknowledgmentStatus, ex.GetType().Name));
            }
        }

        return new VendorPaymentAcknowledgmentRetryDispatchResult(
            batchSize,
            dueRecords.Count,
            items.Count(item => !item.Skipped),
            items.Count(item => item.Succeeded),
            items.Count(item => item.Skipped),
            items.Count(item => !item.Succeeded && !item.Skipped),
            items);
    }

    private static bool ShouldSkip(VendorPaymentAcknowledgmentRecord record, DateTimeOffset now)
    {
        if (!string.Equals(record.AcknowledgmentStatus, VendorPaymentAcknowledgmentStatuses.RetryPending, StringComparison.Ordinal))
        {
            return true;
        }

        return record.NextRetryAt.HasValue && record.NextRetryAt.Value > now;
    }

    private static VendorPaymentAcknowledgmentRetryDispatchItemResult Completed(
        VendorPaymentAcknowledgmentRecord initial,
        VendorPaymentAcknowledgmentRecord? final)
    {
        var finalStatus = final?.AcknowledgmentStatus;
        var succeeded = string.Equals(finalStatus, VendorPaymentAcknowledgmentStatuses.Confirmed, StringComparison.Ordinal);
        var skipped = IsTerminalSkipped(finalStatus);

        return new VendorPaymentAcknowledgmentRetryDispatchItemResult(
            initial.VendorPaymentAcknowledgmentId,
            initial.PaymentAttemptId,
            initial.PaymentConfirmationId,
            initial.ParkingSessionId,
            initial.VendorSystemCode,
            initial.AcknowledgmentStatus,
            finalStatus,
            succeeded,
            skipped,
            succeeded || skipped ? null : final?.VendorCode,
            initial.CorrelationId);
    }

    private static VendorPaymentAcknowledgmentRetryDispatchItemResult Skipped(VendorPaymentAcknowledgmentRecord record) =>
        new(
            record.VendorPaymentAcknowledgmentId,
            record.PaymentAttemptId,
            record.PaymentConfirmationId,
            record.ParkingSessionId,
            record.VendorSystemCode,
            record.AcknowledgmentStatus,
            record.AcknowledgmentStatus,
            Succeeded: false,
            Skipped: true,
            FailureCode: null,
            record.CorrelationId);

    private static VendorPaymentAcknowledgmentRetryDispatchItemResult Failed(
        VendorPaymentAcknowledgmentRecord initial,
        string? finalStatus,
        string? failureCode) =>
        new(
            initial.VendorPaymentAcknowledgmentId,
            initial.PaymentAttemptId,
            initial.PaymentConfirmationId,
            initial.ParkingSessionId,
            initial.VendorSystemCode,
            initial.AcknowledgmentStatus,
            finalStatus,
            Succeeded: false,
            Skipped: false,
            failureCode,
            initial.CorrelationId);

    private static bool IsTerminalSkipped(string? status) =>
        string.Equals(status, VendorPaymentAcknowledgmentStatuses.Cancelled, StringComparison.Ordinal) ||
        string.Equals(status, VendorPaymentAcknowledgmentStatuses.SkippedDisabled, StringComparison.Ordinal);

    private static int NormalizeBatchSize(int batchSize)
    {
        if (batchSize <= 0)
        {
            return DefaultBatchSize;
        }

        return Math.Min(batchSize, MaxBatchSize);
    }
}
