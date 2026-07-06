namespace ExitPass.CentralPms.Application.FiscalIssuance;

public sealed class FiscalExceptionPosServerRetryContractReadinessService :
    IFiscalExceptionPosServerRetryContractReadinessService
{
    public const string PosServerSemanticHashAlgorithm = "sha256";
    public const string PosServerSemanticHashVersion = "sha256:v1";
    public const string PosServerIdempotencyKeySource = "payableBasis.upstreamFinalityRef";

    public FiscalExceptionPosServerRetryContractReadinessResult Evaluate(
        FiscalExceptionPosServerRetryContractReadinessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Detail);

        var summary = request.Detail.Summary;
        var semanticHashStatus = EvaluateSemanticHashCompatibility(summary);
        var idempotencyStatus = EvaluateIdempotencyMapping(summary, request.RequestedUpstreamFinalityReference);
        var readbackFieldStatus = FiscalExceptionPosServerRetryContractReadinessStatus.Ready;
        var fiscalNumberingStatus = FiscalExceptionPosServerRetryContractReadinessStatus.Ready;
        var conflictReplayStatus = FiscalExceptionPosServerRetryContractReadinessStatus.Ready;

        var blockReason = FirstBlockReason(semanticHashStatus, idempotencyStatus);
        var status = blockReason is null
            ? FiscalExceptionPosServerRetryContractReadinessStatus.Ready
            : ToOverallStatus(semanticHashStatus, idempotencyStatus);

        return new FiscalExceptionPosServerRetryContractReadinessResult(
            Status: status,
            SemanticHashCompatibilityStatus: semanticHashStatus,
            IdempotencyMappingStatus: idempotencyStatus,
            ReadbackFieldCompatibilityStatus: readbackFieldStatus,
            FiscalNumberingReadinessStatus: fiscalNumberingStatus,
            ConflictReplayBehaviorStatus: conflictReplayStatus,
            BlockReasonCode: blockReason,
            SafeSummary: ToSafeSummary(status, blockReason),
            RetryExecutionAvailable: false);
    }

    private static FiscalExceptionPosServerRetryContractReadinessStatus EvaluateSemanticHashCompatibility(
        FiscalExceptionQueueCaseSummary summary)
    {
        if (summary.SemanticRequestHashAvailabilityStatus !=
                FiscalExceptionSemanticRequestHashAvailabilityStatus.AvailableAndConfirmed ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashValue) ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashAlgorithm) ||
            string.IsNullOrWhiteSpace(summary.SemanticRequestHashSourceVersion))
        {
            return FiscalExceptionPosServerRetryContractReadinessStatus.Blocked;
        }

        if (!IsSha256Compatible(summary.SemanticRequestHashAlgorithm) ||
            !string.Equals(
                summary.SemanticRequestHashSourceVersion.Trim(),
                PosServerSemanticHashVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed;
        }

        return FiscalExceptionPosServerRetryContractReadinessStatus.Ready;
    }

    private static FiscalExceptionPosServerRetryContractReadinessStatus EvaluateIdempotencyMapping(
        FiscalExceptionQueueCaseSummary summary,
        string? requestedUpstreamFinalityReference)
    {
        if (string.IsNullOrWhiteSpace(summary.UpstreamFinalityReference))
        {
            return FiscalExceptionPosServerRetryContractReadinessStatus.Blocked;
        }

        if (!string.IsNullOrWhiteSpace(requestedUpstreamFinalityReference) &&
            !string.Equals(
                summary.UpstreamFinalityReference.Trim(),
                requestedUpstreamFinalityReference.Trim(),
                StringComparison.Ordinal))
        {
            return FiscalExceptionPosServerRetryContractReadinessStatus.Blocked;
        }

        if (summary.IdempotencyContextAvailabilityStatus is
            FiscalExceptionIdempotencyContextAvailabilityStatus.MissingUpstreamFinalityReference or
            FiscalExceptionIdempotencyContextAvailabilityStatus.NewUpstreamFinalityReferenceRejected)
        {
            return FiscalExceptionPosServerRetryContractReadinessStatus.Blocked;
        }

        return FiscalExceptionPosServerRetryContractReadinessStatus.Ready;
    }

    private static string? FirstBlockReason(
        FiscalExceptionPosServerRetryContractReadinessStatus semanticHashStatus,
        FiscalExceptionPosServerRetryContractReadinessStatus idempotencyStatus)
    {
        if (semanticHashStatus == FiscalExceptionPosServerRetryContractReadinessStatus.Blocked)
        {
            return "pos_server_semantic_hash_required_but_missing_or_unconfirmed";
        }

        if (semanticHashStatus == FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed)
        {
            return "pos_server_semantic_hash_compatibility_unconfirmed";
        }

        return idempotencyStatus == FiscalExceptionPosServerRetryContractReadinessStatus.Blocked
            ? "pos_server_idempotency_mapping_not_compatible"
            : null;
    }

    private static FiscalExceptionPosServerRetryContractReadinessStatus ToOverallStatus(
        FiscalExceptionPosServerRetryContractReadinessStatus semanticHashStatus,
        FiscalExceptionPosServerRetryContractReadinessStatus idempotencyStatus)
    {
        if (semanticHashStatus == FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed)
        {
            return FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed;
        }

        if (semanticHashStatus == FiscalExceptionPosServerRetryContractReadinessStatus.Blocked ||
            idempotencyStatus == FiscalExceptionPosServerRetryContractReadinessStatus.Blocked)
        {
            return FiscalExceptionPosServerRetryContractReadinessStatus.Blocked;
        }

        return FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed;
    }

    private static string ToSafeSummary(
        FiscalExceptionPosServerRetryContractReadinessStatus status,
        string? blockReason) =>
        status switch
        {
            FiscalExceptionPosServerRetryContractReadinessStatus.Ready =>
                "pos_server_retry_contract_readiness_ready_no_execution",
            FiscalExceptionPosServerRetryContractReadinessStatus.Unconfirmed =>
                blockReason ?? "pos_server_retry_contract_readiness_unconfirmed",
            FiscalExceptionPosServerRetryContractReadinessStatus.Blocked =>
                blockReason ?? "pos_server_retry_contract_readiness_blocked",
            _ => "pos_server_retry_contract_readiness_unavailable"
        };

    private static bool IsSha256Compatible(string value) =>
        string.Equals(value.Trim(), PosServerSemanticHashAlgorithm, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value.Trim(), "SHA-256", StringComparison.OrdinalIgnoreCase);
}
