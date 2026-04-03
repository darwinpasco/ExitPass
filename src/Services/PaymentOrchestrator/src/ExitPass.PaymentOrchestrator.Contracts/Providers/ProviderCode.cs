namespace ExitPass.PaymentOrchestrator.Contracts.Providers;

/// <summary>
/// Defines supported payment provider codes for the current Payment Orchestrator slice.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 4.2.7 Payment Orchestrator (POA)
///
/// Invariants Enforced:
/// - Provider selection must be explicit and deterministic.
/// </summary>
public static class ProviderCode
{
    /// <summary>
    /// PayMongo payment provider code.
    /// </summary>
    public const string PayMongo = "PAYMONGO";

    /// <summary>
    /// Asia United Bank payment provider code.
    /// </summary>
    public const string Aub = "AUB";
}
