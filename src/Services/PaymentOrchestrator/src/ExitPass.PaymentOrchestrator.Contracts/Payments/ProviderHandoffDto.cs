using System;
using System.Collections.Generic;

namespace ExitPass.PaymentOrchestrator.Contracts.Payments;

/// <summary>
/// Describes how the caller should continue the payment flow after a provider session is created.
///
/// BRD:
/// - 9.9 Payment Initiation
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - POA may return handoff instructions but must not finalize payment state.
/// </summary>
/// <param name="Type">The type of handoff required.</param>
/// <param name="RedirectUrl">The URL to redirect the parker to, when applicable.</param>
/// <param name="HttpMethod">The HTTP method to use for the handoff, when applicable.</param>
/// <param name="FormFields">The form fields to post for a form-based redirect, when applicable.</param>
/// <param name="QrPayload">The raw QR payload, when applicable.</param>
/// <param name="QrImageBase64">The QR image encoded as Base64, when applicable.</param>
/// <param name="ExpiresAtUtc">The provider handoff expiry timestamp, when applicable.</param>
public sealed record ProviderHandoffDto(
    ProviderHandoffType Type,
    string? RedirectUrl,
    string? HttpMethod,
    IReadOnlyDictionary<string, string>? FormFields,
    string? QrPayload,
    string? QrImageBase64,
    DateTimeOffset? ExpiresAtUtc);
