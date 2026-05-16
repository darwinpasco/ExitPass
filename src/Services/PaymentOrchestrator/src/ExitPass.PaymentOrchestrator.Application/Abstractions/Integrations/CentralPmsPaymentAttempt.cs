namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Provider-neutral payment attempt state created or reused by Central PMS.
/// </summary>
/// <param name="PaymentAttemptId">Canonical Central PMS payment attempt identifier.</param>
/// <param name="AttemptStatus">Current payment attempt status.</param>
/// <param name="PaymentProvider">Provider or method code bound by Central PMS.</param>
/// <param name="WasReused">Indicates whether an existing idempotent attempt was reused.</param>
public sealed record CentralPmsPaymentAttempt(
    Guid PaymentAttemptId,
    string AttemptStatus,
    string PaymentProvider,
    bool WasReused);
