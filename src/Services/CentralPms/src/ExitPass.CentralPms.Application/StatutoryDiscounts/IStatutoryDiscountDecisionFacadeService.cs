namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Shared Central PMS statutory-discount decision/readback facade.
/// </summary>
public interface IStatutoryDiscountDecisionFacadeService
{
    Task<StatutoryDiscountDecisionResult> SubmitAsync(
        StatutoryDiscountDecisionCommand command,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionResult?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Durable repository boundary for shared statutory-discount command idempotency and readback.
/// </summary>
public interface IStatutoryDiscountDecisionFacadeRepository
{
    Task<T> ExecuteWithCommandLockAsync<T>(
        StatutoryDiscountDecisionRepositoryCommand command,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionBeginResult> BeginAsync(
        StatutoryDiscountDecisionRepositoryCommand command,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionCommandRecord> CompleteAsync(
        StatutoryDiscountDecisionCommandRecord record,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionCommandRecord?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken);
}
