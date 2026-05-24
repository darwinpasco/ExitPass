namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Coordinates reconciliation exception workflow operations.
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
/// - Reconciliation workflow records are operational evidence, not payment authority.
/// - Reconciliation review must not mutate payment finality, provider outcome truth, exit authorization, or gate consumption.
/// </summary>
public sealed class ReconciliationWorkflowService : IReconciliationWorkflowService
{
    private static readonly HashSet<string> NoteTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "REVIEW_NOTE",
        "PROVIDER_CHECK_NOTE",
        "INTERNAL_CHECK_NOTE",
        "FINANCIAL_IMPACT_NOTE",
        "SYSTEM_NOTE"
    };

    private static readonly HashSet<string> ResolutionActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "RESOLVE_NO_ADJUSTMENT",
        "RESOLVE_WITH_OPERATIONAL_NOTE",
        "REQUEST_FINANCIAL_ADJUSTMENT",
        "ACCEPT_PROVIDER_EVIDENCE",
        "OVERRIDE_RECONCILIATION_STATUS",
        "REOPEN_EXCEPTION",
        "CLOSE_EXCEPTION",
        "CANCEL_EXCEPTION"
    };

    private static readonly HashSet<string> FinancialImpacts = new(StringComparer.OrdinalIgnoreCase)
    {
        "NONE",
        "POSSIBLE",
        "DEFINITE",
        "CONTROL_ONLY"
    };

    private static readonly HashSet<string> ExceptionStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "OPEN",
        "ASSIGNED",
        "UNDER_REVIEW",
        "RESOLVED",
        "REJECTED",
        "ESCALATED",
        "CLOSED",
        "CANCELLED"
    };

    private readonly IReconciliationWorkflowRepository _repository;

    /// <summary>
    /// Creates a reconciliation workflow service.
    /// </summary>
    public ReconciliationWorkflowService(IReconciliationWorkflowRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public Task<ReconciliationNoteResult> AddNoteAsync(
        AddReconciliationNoteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.ReconciliationItemId, nameof(command.ReconciliationItemId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));
        ValidateRequired(command.NoteText, nameof(command.NoteText));
        ValidateEnum(command.NoteType, NoteTypes, nameof(command.NoteType));

        return _repository.AddNoteAsync(
            command with { NoteType = command.NoteType.ToUpperInvariant() },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ReconciliationResolutionRequestResult> SubmitResolutionRequestAsync(
        SubmitReconciliationResolutionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.ReconciliationItemId, nameof(command.ReconciliationItemId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));
        ValidateEnum(command.ResolutionAction, ResolutionActions, nameof(command.ResolutionAction));
        ValidateRequired(command.ResolutionReason, nameof(command.ResolutionReason));
        ValidateEnum(command.FinancialImpact, FinancialImpacts, nameof(command.FinancialImpact));
        ValidateEnum(command.ProposedExceptionStatus, ExceptionStatuses, nameof(command.ProposedExceptionStatus));

        var financialImpact = command.FinancialImpact.ToUpperInvariant();
        if (command.AdjustmentRequired && financialImpact is not ("POSSIBLE" or "DEFINITE"))
        {
            throw new ArgumentException(
                "Adjustment-required resolution requests must use POSSIBLE or DEFINITE financial impact.",
                nameof(command.FinancialImpact));
        }

        if (command.ResolutionAction.Equals("REQUEST_FINANCIAL_ADJUSTMENT", StringComparison.OrdinalIgnoreCase) &&
            financialImpact is not ("POSSIBLE" or "DEFINITE"))
        {
            throw new ArgumentException(
                "Financial adjustment requests must use POSSIBLE or DEFINITE financial impact.",
                nameof(command.FinancialImpact));
        }

        return _repository.SubmitResolutionRequestAsync(
            command with
            {
                ResolutionAction = command.ResolutionAction.ToUpperInvariant(),
                FinancialImpact = financialImpact,
                ProposedExceptionStatus = command.ProposedExceptionStatus.ToUpperInvariant()
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ReconciliationResolutionDecisionResult> DecideResolutionRequestAsync(
        DecideReconciliationResolutionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.ResolutionRequestId, nameof(command.ResolutionRequestId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));

        var decision = command.Decision.ToUpperInvariant();
        if (decision is not ("APPROVED" or "REJECTED"))
        {
            throw new ArgumentException("Decision must be APPROVED or REJECTED.", nameof(command.Decision));
        }

        ValidateRequired(command.Reason, nameof(command.Reason));

        return _repository.DecideResolutionRequestAsync(
            command with { Decision = decision },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReconciliationWorkflowHistoryRecord>> ReadWorkflowHistoryAsync(
        ReadReconciliationWorkflowHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ValidateGuid(query.ReconciliationItemId, nameof(query.ReconciliationItemId));
        return _repository.ReadWorkflowHistoryAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReconciliationRunRecord>> ListRunsAsync(
        ListReconciliationRunsQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 100);
        return _repository.ListRunsAsync(query with { Limit = limit }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReconciliationExceptionRecord>> ListExceptionsAsync(
        ListReconciliationExceptionsQuery query,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            ValidateEnum(query.Status, ExceptionStatuses, nameof(query.Status));
        }

        var limit = Math.Clamp(query.Limit, 1, 100);
        return _repository.ListExceptionsAsync(query with { Limit = limit }, cancellationToken);
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static void ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static void ValidateEnum(string value, IReadOnlySet<string> allowedValues, string parameterName)
    {
        ValidateRequired(value, parameterName);

        if (!allowedValues.Contains(value))
        {
            throw new ArgumentException(
                $"{parameterName} must be one of: {string.Join(", ", allowedValues.OrderBy(static value => value))}.",
                parameterName);
        }
    }
}
