namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Coordinates reconciliation exception lifecycle operations.
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
/// - Reconciliation exceptions are operational control records, not payment authority.
/// - Exception lifecycle actions must not create PaymentConfirmation, finalize PaymentAttempt, issue ExitAuthorization, or mutate provider outcome truth.
/// </summary>
public sealed class ReconciliationExceptionLifecycleService : IReconciliationExceptionLifecycleService
{
    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CLOSED",
        "CANCELLED"
    };

    private readonly IReconciliationExceptionLifecycleRepository _repository;

    /// <summary>
    /// Creates a reconciliation exception lifecycle service.
    /// </summary>
    public ReconciliationExceptionLifecycleService(IReconciliationExceptionLifecycleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public Task<ReconciliationExceptionDetailRecord> ReadAsync(
        ReadReconciliationExceptionQuery query,
        CancellationToken cancellationToken)
    {
        ValidateGuid(query.ReconciliationExceptionId, nameof(query.ReconciliationExceptionId));
        return _repository.ReadAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReconciliationExceptionLifecycleResult> AssignAsync(
        AssignReconciliationExceptionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.ReconciliationExceptionId, nameof(command.ReconciliationExceptionId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));
        ValidateRequired(command.ReasonCode, nameof(command.ReasonCode));

        if (!command.AssignedToUserId.HasValue && !command.AssignedToServiceIdentityId.HasValue)
        {
            throw new ArgumentException(
                "AssignedToUserId or AssignedToServiceIdentityId is required.",
                nameof(command.AssignedToUserId));
        }

        var current = await _repository.ReadAsync(
            new ReadReconciliationExceptionQuery(command.ReconciliationExceptionId),
            cancellationToken);
        EnsureNotTerminal(current.ExceptionStatus);

        var newStatus = current.ExceptionStatus.Equals("OPEN", StringComparison.OrdinalIgnoreCase)
            ? "ASSIGNED"
            : current.ExceptionStatus;

        return await _repository.AssignAsync(
            command with { ReasonCode = command.ReasonCode.Trim().ToUpperInvariant() },
            newStatus,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReconciliationExceptionLifecycleResult> UpdateStatusAsync(
        UpdateReconciliationExceptionStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.ReconciliationExceptionId, nameof(command.ReconciliationExceptionId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));
        ValidateRequired(command.ReasonCode, nameof(command.ReasonCode));
        ValidateStatus(command.NewStatus, nameof(command.NewStatus));

        var current = await _repository.ReadAsync(
            new ReadReconciliationExceptionQuery(command.ReconciliationExceptionId),
            cancellationToken);
        var newStatus = command.NewStatus.ToUpperInvariant();

        ValidateTransition(current.ExceptionStatus, newStatus, command.Action);

        return await _repository.UpdateStatusAsync(
            command with
            {
                NewStatus = newStatus,
                Action = command.Action.Trim().ToUpperInvariant(),
                ReasonCode = command.ReasonCode.Trim().ToUpperInvariant()
            },
            cancellationToken);
    }

    private static void ValidateTransition(string currentStatus, string newStatus, string action)
    {
        if (TerminalStatuses.Contains(currentStatus))
        {
            throw new ReconciliationWorkflowConflictException(
                "RECONCILIATION_EXCEPTION_TERMINAL",
                "Terminal reconciliation exceptions cannot be mutated by lifecycle actions.");
        }

        if (string.Equals(currentStatus, newStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReconciliationWorkflowConflictException(
                "RECONCILIATION_EXCEPTION_STATUS_UNCHANGED",
                "The requested reconciliation exception status is already current.");
        }

        var allowed = currentStatus.ToUpperInvariant() switch
        {
            "OPEN" => new[] { "ASSIGNED", "UNDER_REVIEW", "ESCALATED", "RESOLVED", "REJECTED", "CANCELLED" },
            "ASSIGNED" => new[] { "UNDER_REVIEW", "ESCALATED", "RESOLVED", "REJECTED", "CANCELLED" },
            "UNDER_REVIEW" => new[] { "ESCALATED", "RESOLVED", "REJECTED", "CANCELLED" },
            "ESCALATED" => new[] { "UNDER_REVIEW", "RESOLVED", "REJECTED", "CANCELLED" },
            "RESOLVED" => new[] { "CLOSED" },
            "REJECTED" => new[] { "CLOSED" },
            _ => Array.Empty<string>()
        };

        if (!allowed.Contains(newStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new ReconciliationWorkflowConflictException(
                "RECONCILIATION_EXCEPTION_INVALID_TRANSITION",
                $"Cannot transition reconciliation exception from {currentStatus} to {newStatus} for action {action}.");
        }
    }

    private static void EnsureNotTerminal(string currentStatus)
    {
        if (TerminalStatuses.Contains(currentStatus))
        {
            throw new ReconciliationWorkflowConflictException(
                "RECONCILIATION_EXCEPTION_TERMINAL",
                "Terminal reconciliation exceptions cannot be mutated by lifecycle actions.");
        }
    }

    private static void ValidateStatus(string? value, string parameterName)
    {
        ValidateRequired(value, parameterName);
        if (!Statuses.Contains(value!))
        {
            throw new ArgumentException(
                $"{parameterName} must be one of: {string.Join(", ", Statuses.OrderBy(static status => status))}.",
                parameterName);
        }
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
}
