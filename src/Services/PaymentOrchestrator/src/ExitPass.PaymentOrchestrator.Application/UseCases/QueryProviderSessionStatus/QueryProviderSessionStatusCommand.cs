using System;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.QueryProviderSessionStatus;

/// <summary>
/// Requests a controlled provider status query for a known provider session.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Status queries must be scoped to a known provider code/product/session.
/// - The correlation identifier is preserved for traceability only, not payment finality.
/// </summary>
/// <param name="ProviderCode">The provider code.</param>
/// <param name="ProviderProduct">The provider product code.</param>
/// <param name="ProviderSessionId">The provider session identifier.</param>
/// <param name="CorrelationId">Optional cross-service correlation identifier.</param>
public sealed record QueryProviderSessionStatusCommand(
    string ProviderCode,
    string ProviderProduct,
    string ProviderSessionId,
    Guid? CorrelationId);
