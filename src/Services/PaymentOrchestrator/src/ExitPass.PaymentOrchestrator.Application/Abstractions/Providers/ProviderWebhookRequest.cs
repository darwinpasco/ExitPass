using System.Collections.Generic;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;

/// <summary>
/// Represents the raw inbound webhook request received from a payment provider.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
///
/// Invariants Enforced:
/// - Provider webhook verification must operate on the raw provider request.
/// </summary>
/// <param name="Headers">The inbound request headers.</param>
/// <param name="RawBody">The raw request body.</param>
public sealed record ProviderWebhookRequest(
    IReadOnlyDictionary<string, string> Headers,
    string RawBody);
