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
    /// <param name="paymentProvider">Payment provider rail code recorded by Central PMS.</param>
    /// <param name="paymentMethod">User-selected payment method code.</param>
    /// <param name="idempotencyKey">Idempotency key for safe retries.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created or reused payment attempt, or a deterministic Central PMS error.</returns>
    Task<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> CreateOrReusePaymentAttemptAsync(
        Guid parkingSessionId,
        Guid tariffSnapshotId,
        string paymentProvider,
        string paymentMethod,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Requests Central PMS to finalize a non-resumable payment attempt.
    /// </summary>
    /// <param name="paymentAttemptId">Canonical payment attempt identifier.</param>
    /// <param name="finalAttemptStatus">Terminal Central PMS payment attempt status.</param>
    /// <param name="requestedBy">Authenticated internal actor requesting finalization.</param>
    /// <param name="idempotencyKey">Idempotency key for safe recovery retries.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Finalized payment attempt, or a deterministic Central PMS error.</returns>
    Task<CentralPmsWebPayResult<CentralPmsPaymentAttempt>> FinalizePaymentAttemptAsync(
        Guid paymentAttemptId,
        string finalAttemptStatus,
        string requestedBy,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the POS Server-owned Sales Invoice presentation linked to a WebPay payment attempt.
    /// </summary>
    /// <param name="paymentAttemptId">Canonical payment attempt identifier.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authoritative presentation readback, or a deterministic Central PMS error.</returns>
    Task<CentralPmsWebPayResult<CentralPmsWebPayReceiptPresentation>> GetReceiptPresentationAsync(
        Guid paymentAttemptId,
        Guid correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves Central PMS statutory parking local-ordinance availability for a WebPay parking session.
    /// </summary>
    Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability>> ResolveStatutoryDiscountAvailabilityAsync(
        CentralPmsStatutoryDiscountAvailabilityRequest request,
        Guid correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rediscovers an existing Central PMS statutory-discount pending lifecycle for WebPay.
    /// </summary>
    Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery>> RediscoverStatutoryDiscountPendingLifecycleAsync(
        CentralPmsStatutoryDiscountPendingLifecycleRediscoveryRequest request,
        Guid correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or reuses a Central PMS statutory-discount decision for WebPay.
    /// </summary>
    /// <param name="request">Server-normalized WebPay statutory-discount request.</param>
    /// <param name="idempotencyKey">Original decision idempotency key.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Durable statutory-discount decision readback, or a deterministic Central PMS error.</returns>
    Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> SubmitStatutoryDiscountDecisionAsync(
        CentralPmsStatutoryDiscountDecisionRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a durable Central PMS statutory-discount decision.
    /// </summary>
    /// <param name="statutoryDiscountDecisionCommandId">Canonical decision command identifier.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Durable statutory-discount decision readback, or a deterministic Central PMS error.</returns>
    Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> GetStatutoryDiscountDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Requests Central PMS to create or reuse the payable-basis application for an approved WebPay decision.
    /// </summary>
    /// <param name="request">Server-normalized WebPay statutory-discount request matching the canonical decision.</param>
    /// <param name="idempotencyKey">Original application-intent idempotency key.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Durable statutory-discount decision/application readback, or a deterministic Central PMS error.</returns>
    Task<CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision>> ApplyStatutoryDiscountPayableBasisAsync(
        CentralPmsStatutoryDiscountDecisionRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken);
}
