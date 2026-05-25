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

    /// <inheritdoc />
    public async Task<ReconciliationRunEvaluationSummaryRecord> EvaluateRunAsync(
        EvaluateReconciliationRunCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.ReconciliationRunId, nameof(command.ReconciliationRunId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

        if (!await _repository.RunExistsAsync(command.ReconciliationRunId, cancellationToken))
        {
            throw new ReconciliationRunNotFoundException(command.ReconciliationRunId);
        }

        var currentItems = await _repository.ListRunItemsAsync(command.ReconciliationRunId, cancellationToken);
        if (currentItems.Count == 0)
        {
            return EmptyRunSummary(command.ReconciliationRunId, command.CorrelationId);
        }

        var beforeSummary = BuildSummary(
            command.ReconciliationRunId,
            currentItems.Select(ToEvaluationRecord).ToArray(),
            currentItems.FirstOrDefault()?.CorrelationId);

        var evaluations = new List<ReconciliationItemEvaluationRecord>(currentItems.Count);
        foreach (var item in currentItems)
        {
            evaluations.Add(await EvaluateAsync(
                new EvaluateReconciliationItemCommand(
                    item.ReconciliationItemId,
                    command.ActorUserId,
                    command.ServiceIdentityId,
                    command.CorrelationId),
                cancellationToken));
        }

        var afterSummary = BuildSummary(command.ReconciliationRunId, evaluations, command.CorrelationId);
        if (SummaryChanged(beforeSummary, afterSummary))
        {
            await _repository.PersistRunEvaluatedEventAsync(command, afterSummary, cancellationToken);
        }

        return afterSummary;
    }

    /// <inheritdoc />
    public async Task<ReconciliationRunEvaluationSummaryRecord> ReadRunEvaluationSummaryAsync(
        ReadReconciliationRunEvaluationSummaryQuery query,
        CancellationToken cancellationToken)
    {
        ValidateGuid(query.ReconciliationRunId, nameof(query.ReconciliationRunId));

        if (!await _repository.RunExistsAsync(query.ReconciliationRunId, cancellationToken))
        {
            throw new ReconciliationRunNotFoundException(query.ReconciliationRunId);
        }

        var items = await _repository.ListRunItemsAsync(query.ReconciliationRunId, cancellationToken);
        if (items.Count == 0)
        {
            return EmptyRunSummary(query.ReconciliationRunId, null);
        }

        var evaluations = items.Select(ToEvaluationRecord).ToArray();

        return BuildSummary(query.ReconciliationRunId, evaluations, evaluations.FirstOrDefault()?.CorrelationId);
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

    private static ReconciliationRunEvaluationSummaryRecord EmptyRunSummary(Guid runId, Guid? correlationId) =>
        new(
            runId,
            TotalItems: 0,
            EvaluatedItems: 0,
            MatchedItems: 0,
            MismatchedItems: 0,
            MissingSourceItems: 0,
            MissingTargetItems: 0,
            InconclusiveItems: 0,
            SkippedItems: 0,
            correlationId);

    private static ReconciliationRunEvaluationSummaryRecord BuildSummary(
        Guid runId,
        IReadOnlyCollection<ReconciliationItemEvaluationRecord> evaluations,
        Guid? correlationId)
    {
        var evaluated = evaluations.Count(evaluation => evaluation.MatchStatus != "NOT_EVALUATED");

        return new ReconciliationRunEvaluationSummaryRecord(
            runId,
            TotalItems: evaluations.Count,
            EvaluatedItems: evaluated,
            MatchedItems: evaluations.Count(evaluation => evaluation.MatchStatus == "MATCH"),
            MismatchedItems: evaluations.Count(evaluation => evaluation.MatchStatus == "AMOUNT_MISMATCH"),
            MissingSourceItems: evaluations.Count(evaluation => evaluation.MatchStatus == "MISSING_SOURCE"),
            MissingTargetItems: evaluations.Count(evaluation => evaluation.MatchStatus == "MISSING_TARGET"),
            InconclusiveItems: evaluations.Count(evaluation => evaluation.MatchStatus == "INCONCLUSIVE"),
            SkippedItems: evaluations.Count - evaluated,
            correlationId);
    }

    private static ReconciliationItemEvaluationRecord ToEvaluationRecord(ReconciliationItemRecord item)
    {
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

    private static bool SummaryChanged(
        ReconciliationRunEvaluationSummaryRecord before,
        ReconciliationRunEvaluationSummaryRecord after) =>
        before.EvaluatedItems != after.EvaluatedItems ||
        before.MatchedItems != after.MatchedItems ||
        before.MismatchedItems != after.MismatchedItems ||
        before.MissingSourceItems != after.MissingSourceItems ||
        before.MissingTargetItems != after.MissingTargetItems ||
        before.InconclusiveItems != after.InconclusiveItems ||
        before.SkippedItems != after.SkippedItems;

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
