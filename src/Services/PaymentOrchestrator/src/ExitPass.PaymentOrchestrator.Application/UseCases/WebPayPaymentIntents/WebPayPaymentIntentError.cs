using ExitPass.PaymentOrchestrator.Contracts.WebPay;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;

/// <summary>
/// Deterministic WebPay payment intent error.
/// </summary>
/// <param name="StatusCode">HTTP status code for the API response.</param>
/// <param name="ErrorCode">Provider-neutral error code.</param>
/// <param name="Message">Provider-neutral error message.</param>
/// <param name="Retryable">Indicates whether the request can be retried.</param>
/// <param name="CorrelationId">Optional correlation identifier for support tracing.</param>
/// <param name="ParkingSessionId">Optional resolved parking session identifier.</param>
/// <param name="PaymentAttemptId">Optional payment attempt identifier.</param>
/// <param name="Status">Optional payment attempt status.</param>
/// <param name="Handoff">Optional resumable provider handoff details.</param>
/// <param name="PaymentMethod">Optional payment method code.</param>
/// <param name="AmountMinorUnits">Optional amount in minor currency units.</param>
/// <param name="Currency">Optional ISO currency code.</param>
/// <param name="SiteName">Optional parking site display name.</param>
/// <param name="TicketReference">Optional ticket reference.</param>
/// <param name="PlateNumber">Optional plate number.</param>
/// <param name="SelectedProviderCode">Optional selected payment provider code for diagnostics.</param>
/// <param name="FallbackProviderCode">Optional fallback payment provider code for diagnostics.</param>
/// <param name="ProviderProduct">Optional provider product code for diagnostics.</param>
public sealed record WebPayPaymentIntentError(
    int StatusCode,
    string ErrorCode,
    string Message,
    bool Retryable,
    Guid? CorrelationId = null,
    Guid? ParkingSessionId = null,
    Guid? PaymentAttemptId = null,
    string? Status = null,
    WebPayPaymentHandoffDto? Handoff = null,
    string? PaymentMethod = null,
    long? AmountMinorUnits = null,
    string? Currency = null,
    string? SiteName = null,
    string? TicketReference = null,
    string? PlateNumber = null,
    string? SelectedProviderCode = null,
    string? FallbackProviderCode = null,
    string? ProviderProduct = null);
