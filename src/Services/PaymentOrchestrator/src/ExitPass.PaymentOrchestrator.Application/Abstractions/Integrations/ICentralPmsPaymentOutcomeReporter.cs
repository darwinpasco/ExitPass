using System.Threading;
using System.Threading.Tasks;

namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;

/// <summary>
/// Reports verified provider outcomes from the Payment Orchestrator to Central PMS.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Only verified provider outcomes may be reported into Central PMS.
/// - POA reports verified outcomes but does not finalize PaymentAttempt state.
/// </summary>
public interface ICentralPmsPaymentOutcomeReporter
{
    /// <summary>
    /// Reports a verified provider payment outcome to Central PMS.
    /// </summary>
    /// <param name="report">The verified payment outcome report.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task ReportVerifiedOutcomeAsync(
        VerifiedPaymentOutcomeReport report,
        CancellationToken cancellationToken);
}
