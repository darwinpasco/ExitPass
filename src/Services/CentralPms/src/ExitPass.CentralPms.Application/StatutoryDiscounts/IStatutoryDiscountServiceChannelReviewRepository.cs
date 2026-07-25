namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Persistence boundary for service-channel statutory-discount review linkage.
/// </summary>
public interface IStatutoryDiscountServiceChannelReviewRepository
{
    Task UpsertIntakeAsync(
        StatutoryDiscountServiceChannelReviewIntakeCommand command,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountServiceChannelReviewQueueResult> ListAsync(
        StatutoryDiscountServiceChannelReviewQueueQuery query,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountServiceChannelReviewDetail?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountServiceChannelReviewDetail> RecordReviewCompletionAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid reviewerUserId,
        Guid? operatorDeviceBindingId,
        Guid? operatorShiftId,
        Guid accessEvaluationId,
        string decision,
        string? decisionReasonCode,
        Guid correlationId,
        CancellationToken cancellationToken);
}
