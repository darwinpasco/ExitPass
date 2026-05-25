namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Coordinates conservative reconciliation item evaluation.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - Reconciliation evaluation is operational evidence only, not payment authority.
/// - Item evaluation must not create PaymentConfirmation, finalize PaymentAttempt, issue ExitAuthorization, mutate provider outcome truth, or infer payment finality.
/// </summary>
public sealed class ReconciliationEvaluationService : IReconciliationEvaluationService
{
    private const string ExceptionHandlingDeferred =
        "Exception creation/update is deferred in this slice because the live schema has no uniqueness constraint for one evaluation exception per item.";

    private readonly IReconciliationEvaluationRepository _repository;

    /// <summary>
    /// Creates a reconciliation evaluation service.
    /// </summary>
    public ReconciliationEvaluationService(IReconciliationEvaluationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public async Task<ReconciliationItemEvaluationRecord> EvaluateAsync(
        EvaluateReconciliationItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.ReconciliationItemId, nameof(command.ReconciliationItemId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

        var item = await _repository.ReadItemAsync(command.ReconciliationItemId, cancellationToken);
        var decision = Classify(item);
        return await _repository.SaveEvaluationAsync(command, decision, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReconciliationItemEvaluationRecord> ReadEvaluationAsync(
        ReadReconciliationItemEvaluationQuery query,
        CancellationToken cancellationToken)
    {
        ValidateGuid(query.ReconciliationItemId, nameof(query.ReconciliationItemId));
        var item = await _repository.ReadItemAsync(query.ReconciliationItemId, cancellationToken);
        var decision = ToCurrentEvaluation(item);

        return new ReconciliationItemEvaluationRecord(
            item.ReconciliationItemId,
            item.ReconciliationRunId,
            item.ComparisonBasis,
            item.ItemStatus,
            item.MatchStatus,
            decision.EvaluationClassification,
            decision.EvaluationReason,
            item.ExpectedAmount,
            item.ActualAmount,
            item.VarianceAmount,
            item.ExceptionReasonCode,
            ExceptionCreatedOrUpdated: false,
            ExceptionHandlingDeferred,
            item.UpdatedAt,
            item.CorrelationId);
    }

    private static ReconciliationEvaluationDecision Classify(ReconciliationItemRecord item)
    {
        var hasSource = HasSource(item);
        var hasTarget = HasTarget(item);

        if (!hasSource)
        {
            return Decision(
                "EXCEPTION",
                "MISSING_SOURCE",
                "MISSING_SOURCE",
                "No schema-supported source evidence is linked to the reconciliation item.",
                null,
                "MISSING_SOURCE");
        }

        if (!hasTarget)
        {
            return Decision(
                "EXCEPTION",
                "MISSING_TARGET",
                "MISSING_TARGET",
                "No schema-supported target evidence is linked to the reconciliation item.",
                null,
                "MISSING_TARGET");
        }

        if (item.ExpectedAmount.HasValue && item.ActualAmount.HasValue)
        {
            var variance = Math.Abs(item.ExpectedAmount.Value - item.ActualAmount.Value);
            if (variance == 0m)
            {
                return Decision(
                    "MATCHED",
                    "MATCH",
                    "MATCH",
                    "Expected and actual amounts match exactly.",
                    0m,
                    null);
            }

            return Decision(
                "MISMATCHED",
                "AMOUNT_MISMATCH",
                "AMOUNT_MISMATCH",
                "Expected and actual amounts are both present but differ.",
                variance,
                "AMOUNT_MISMATCH");
        }

        return Decision(
            "PENDING",
            "INCONCLUSIVE",
            "INCONCLUSIVE",
            "Source and target evidence are present, but comparable expected and actual amounts are not both present.",
            null,
            null);
    }

    private static ReconciliationEvaluationDecision ToCurrentEvaluation(ReconciliationItemRecord item)
    {
        var classification = item.MatchStatus;
        var reason = item.MatchStatus switch
        {
            "MATCH" => "Current item state is an exact match.",
            "AMOUNT_MISMATCH" => "Current item state is an amount mismatch.",
            "MISSING_SOURCE" => "Current item state is missing source evidence.",
            "MISSING_TARGET" => "Current item state is missing target evidence.",
            "INCONCLUSIVE" => "Current item state is inconclusive.",
            _ => "Current item state has not been evaluated by the conservative evaluator."
        };

        return Decision(
            item.ItemStatus,
            item.MatchStatus,
            classification,
            reason,
            item.VarianceAmount,
            item.ExceptionReasonCode);
    }

    private static bool HasSource(ReconciliationItemRecord item)
    {
        return item.ComparisonBasis switch
        {
            "MOPS_TO_CORE" or "MOPS_TO_SETTLEMENT" => item.MopsTransactionRecordId.HasValue,
            "MANUAL_GATE_TO_CORE" => item.ManualGateLogId.HasValue,
            "PROVIDER_TO_CORE" => item.ProviderOutcomeId.HasValue,
            _ => item.MopsTransactionRecordId.HasValue ||
                 item.ManualGateLogId.HasValue ||
                 item.ProviderOutcomeId.HasValue
        };
    }

    private static bool HasTarget(ReconciliationItemRecord item)
    {
        return item.ComparisonBasis switch
        {
            "MOPS_TO_CORE" or
            "MANUAL_GATE_TO_CORE" or
            "PROVIDER_TO_CORE" => item.PaymentAttemptId.HasValue ||
                                  item.PaymentConfirmationId.HasValue ||
                                  item.TargetEntityId.HasValue,
            "SETTLEMENT_TO_CONFIRMATION" => item.PaymentConfirmationId.HasValue || item.TargetEntityId.HasValue,
            _ => item.TargetEntityId.HasValue ||
                 item.PaymentAttemptId.HasValue ||
                 item.PaymentConfirmationId.HasValue
        };
    }

    private static ReconciliationEvaluationDecision Decision(
        string itemStatus,
        string matchStatus,
        string classification,
        string reason,
        decimal? variance,
        string? exceptionReasonCode) =>
        new(itemStatus, matchStatus, classification, reason, variance, exceptionReasonCode);

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
