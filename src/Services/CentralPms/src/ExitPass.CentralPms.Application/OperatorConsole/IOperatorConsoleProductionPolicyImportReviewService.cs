namespace ExitPass.CentralPms.Application.OperatorConsole;

public interface IOperatorConsoleProductionPolicyImportReviewService
{
    Task<ProductionPolicyImportReviewSubmitResult> SubmitForReviewAsync(
        ProductionPolicyImportReviewSubmitRequest request,
        CancellationToken cancellationToken);

    Task<ProductionPolicyImportReviewDecisionResult> DecideAsync(
        ProductionPolicyImportReviewDecisionRequest request,
        CancellationToken cancellationToken);

    Task<ProductionPolicyImportReviewListResult> ListAsync(
        ProductionPolicyImportReviewQuery query,
        Guid? correlationId,
        CancellationToken cancellationToken);

    Task<ProductionPolicyImportReviewSubmission?> GetAsync(
        Guid reviewId,
        CancellationToken cancellationToken);
}

public interface IOperatorConsoleProductionPolicyImportReviewQueue
{
    Task<ProductionPolicyImportReviewSubmission?> FindActiveByFingerprintAsync(
        Guid makerOperatorId,
        string submissionFingerprint,
        CancellationToken cancellationToken);

    Task<ProductionPolicyImportReviewSubmission?> GetAsync(
        Guid reviewId,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<ProductionPolicyImportReviewSubmission> Items, int TotalCount)> ListAsync(
        ProductionPolicyImportReviewQuery query,
        CancellationToken cancellationToken);

    Task SaveAsync(
        ProductionPolicyImportReviewSubmission submission,
        CancellationToken cancellationToken);
}
