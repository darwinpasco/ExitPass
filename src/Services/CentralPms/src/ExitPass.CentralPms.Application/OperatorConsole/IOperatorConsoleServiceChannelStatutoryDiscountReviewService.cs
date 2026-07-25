using ExitPass.CentralPms.Application.StatutoryDiscounts;

namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Operator Console review service for service-channel-originated statutory-discount decisions.
/// </summary>
public interface IOperatorConsoleServiceChannelStatutoryDiscountReviewService
{
    Task<StatutoryDiscountServiceChannelReviewQueueResult> ListAsync(
        StatutoryDiscountServiceChannelReviewQueueQuery query,
        OperatorConsoleReviewAccessContext accessContext,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountServiceChannelReviewDetail?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        OperatorConsoleReviewAccessContext accessContext,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountServiceChannelReviewDecisionResult> DecideAsync(
        StatutoryDiscountServiceChannelReviewDecisionCommand command,
        CancellationToken cancellationToken);
}

public sealed record OperatorConsoleReviewAccessContext(
    Guid UserId,
    Guid? OperatorDeviceBindingId,
    Guid? OperatorShiftId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid CorrelationId,
    string IdempotencyKey);
