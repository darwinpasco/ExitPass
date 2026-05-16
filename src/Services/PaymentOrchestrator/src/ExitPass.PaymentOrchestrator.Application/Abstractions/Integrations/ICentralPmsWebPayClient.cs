namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Calls Central PMS APIs required by the WebPay payment intent flow.
/// </summary>
public interface ICentralPmsWebPayClient
{
    /// <summary>
    /// Resolves the parker's parking session and tariff through Central PMS.
    /// </summary>
    /// <param name="siteGroupId">Optional site group identifier.</param>
    /// <param name="siteId">Optional site identifier.</param>
    /// <param name="vendorSystemId">Provider-neutral vendor system identifier.</param>
    /// <param name="plateNumber">Optional plate number.</param>
    /// <param name="ticketReference">Optional normalized ticket reference.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved parking data or a deterministic Central PMS error.</returns>
    Task<CentralPmsWebPayResult<CentralPmsResolvedParking>> ResolveVendorParkingAsync(
        Guid? siteGroupId,
        Guid? siteId,
        string vendorSystemId,
        string? plateNumber,
        string? ticketReference,
        Guid correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or reuses a Central PMS payment attempt for a resolved parking session.
    /// </summary>
    /// <param name="parkingSessionId">Canonical parking session identifier.</param>
    /// <param name="tariffSnapshotId">Canonical tariff snapshot identifier.</param>
    /// <param name="paymentProvider">Payment method or provider code recorded by Central PMS.</param>
    /// <param name="idempotencyKey">Idempotency key for safe retries.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created or reused payment attempt, or a deterministic Central PMS error.</returns>
    Task<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> CreateOrReusePaymentAttemptAsync(
        Guid parkingSessionId,
        Guid tariffSnapshotId,
        string paymentProvider,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken);
}
