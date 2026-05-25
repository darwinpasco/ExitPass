namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Coordinates reconciliation run and item operations.
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
/// - Reconciliation runs and items are operational evidence, not payment authority.
/// - Creating a reconciliation run must not create PaymentConfirmation, finalize PaymentAttempt, issue ExitAuthorization, or mutate provider outcome truth.
/// </summary>
public sealed class ReconciliationRunItemService : IReconciliationRunItemService
{
    private static readonly HashSet<string> RunTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MOPS_RECONCILIATION",
        "MANUAL_GATE_REVIEW",
        "INCIDENT_RECONCILIATION",
        "COUPON_WALLET_RECONCILIATION",
        "PAYMENT_PROVIDER_RECONCILIATION",
        "VENDOR_PMS_RECONCILIATION"
    };

    private static readonly HashSet<string> RunStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "STARTED",
        "PROCESSING",
        "COMPLETED",
        "FAILED",
        "CANCELLED",
        "REPROCESSING"
    };

    private static readonly HashSet<string> ScopeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TIME_WINDOW",
        "SITE",
        "SITE_GROUP",
        "INCIDENT",
        "SOURCE_BATCH",
        "PAYMENT_RAIL",
        "VENDOR_SYSTEM",
        "MIXED"
    };

    private readonly IReconciliationRunItemRepository _repository;

    /// <summary>
    /// Creates a reconciliation run and item service.
    /// </summary>
    public ReconciliationRunItemService(IReconciliationRunItemRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public Task<ReconciliationRunCreateResult> CreateRunAsync(
        CreateReconciliationRunCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));
        ValidateEnum(command.RunType, RunTypes, nameof(command.RunType));
        ValidateEnum(command.ScopeType, ScopeTypes, nameof(command.ScopeType));
        ValidateEnum(command.RunStatus, RunStatuses, nameof(command.RunStatus));

        if (command.WindowStartAt.HasValue &&
            command.WindowEndAt.HasValue &&
            command.WindowEndAt.Value < command.WindowStartAt.Value)
        {
            throw new ArgumentException("WindowEndAt must be greater than or equal to WindowStartAt.", nameof(command.WindowEndAt));
        }

        return _repository.CreateRunAsync(
            command with
            {
                RunType = command.RunType.ToUpperInvariant(),
                RunStatus = command.RunStatus.ToUpperInvariant(),
                ScopeType = command.ScopeType.ToUpperInvariant(),
                RunCode = Normalize(command.RunCode),
                SourceBatchRef = Normalize(command.SourceBatchRef)
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ReconciliationRunDetailRecord> ReadRunAsync(
        ReadReconciliationRunQuery query,
        CancellationToken cancellationToken)
    {
        ValidateGuid(query.ReconciliationRunId, nameof(query.ReconciliationRunId));
        return _repository.ReadRunAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReconciliationItemRecord>> ListRunItemsAsync(
        ListReconciliationRunItemsQuery query,
        CancellationToken cancellationToken)
    {
        ValidateGuid(query.ReconciliationRunId, nameof(query.ReconciliationRunId));
        var limit = Math.Clamp(query.Limit, 1, 500);
        return _repository.ListRunItemsAsync(query with { Limit = limit }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ReconciliationItemRecord> ReadItemAsync(
        ReadReconciliationItemQuery query,
        CancellationToken cancellationToken)
    {
        ValidateGuid(query.ReconciliationItemId, nameof(query.ReconciliationItemId));
        return _repository.ReadItemAsync(query, cancellationToken);
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static void ValidateEnum(string? value, IReadOnlySet<string> allowedValues, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        if (!allowedValues.Contains(value))
        {
            throw new ArgumentException(
                $"{parameterName} must be one of: {string.Join(", ", allowedValues.OrderBy(static allowed => allowed))}.",
                parameterName);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
