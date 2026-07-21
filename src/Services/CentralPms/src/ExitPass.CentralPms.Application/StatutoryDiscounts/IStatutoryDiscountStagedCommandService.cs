namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

/// <summary>
/// Internal staged statutory-discount command boundary. No public route calls this in the current slice.
/// </summary>
public interface IStatutoryDiscountStagedCommandService
{
    Task<StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>> CreateOrResolveDecisionAsync(
        StatutoryDiscountDecisionV2Command command,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionV2Record?> GetDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionV2Record> MarkDecisionProcessingAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionV2Record> CompleteDecisionApprovedAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid? statutoryDiscountValidationId,
        Guid? originalTariffSnapshotId,
        Guid? appliedPolicyReferenceId,
        Guid? fallbackPolicyReferenceId,
        string? policyResolutionBasis,
        bool localOrdinanceApplied,
        StatutoryDiscountDecisionV2TariffFacts? tariffFacts,
        string? reasonCode,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionV2Record> CompleteDecisionRejectedAsync(
        Guid statutoryDiscountDecisionCommandId,
        string? reasonCode,
        string? safeErrorCode,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionV2Record> RecordDecisionFailureAsync(
        Guid statutoryDiscountDecisionCommandId,
        bool retryable,
        string safeErrorCode,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>> CreateOrResolveApplicationAsync(
        StatutoryDiscountPayableBasisApplicationV1Command command,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountPayableBasisApplicationV1Record> MarkApplicationProcessingAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountPayableBasisApplicationV1Record> CompleteApplicationAppliedAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        Guid? statutoryDiscountPayableBasisApplicationId,
        Guid? appliedTariffSnapshotId,
        Guid correlationId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountPayableBasisApplicationV1Record> RecordApplicationFailureAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        bool retryable,
        string safeErrorCode,
        Guid correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// PostgreSQL-backed persistence boundary for staged statutory-discount commands.
/// </summary>
public interface IStatutoryDiscountStagedCommandRepository
{
    Task<T> ExecuteWithDecisionLockAsync<T>(
        StatutoryDiscountDecisionV2RepositoryCommand command,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionV2BeginResult> BeginDecisionAsync(
        StatutoryDiscountDecisionV2RepositoryCommand command,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionV2Record?> GetDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionV2Record> UpdateDecisionAsync(
        StatutoryDiscountDecisionV2Record record,
        CancellationToken cancellationToken);

    Task<T> ExecuteWithApplicationLockAsync<T>(
        StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountPayableBasisApplicationV1BeginResult> BeginApplicationAsync(
        StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationByDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountPayableBasisApplicationV1Record> UpdateApplicationAsync(
        StatutoryDiscountPayableBasisApplicationV1Record record,
        CancellationToken cancellationToken);
}
