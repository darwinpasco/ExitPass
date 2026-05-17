namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Describes a deterministic Central PMS error returned during WebPay orchestration.
/// </summary>
/// <param name="StatusCode">HTTP status code returned by Central PMS.</param>
/// <param name="ErrorCode">Provider-neutral error code.</param>
/// <param name="Message">Provider-neutral error message.</param>
/// <param name="Retryable">Indicates whether the failed operation is retryable.</param>
/// <param name="CorrelationId">Optional Central PMS correlation identifier.</param>
public sealed record CentralPmsWebPayError(
    int StatusCode,
    string ErrorCode,
    string Message,
    bool Retryable,
    Guid? CorrelationId = null);
