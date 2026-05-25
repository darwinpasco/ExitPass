namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Raised when a reconciliation run cannot be found.
/// </summary>
public sealed class ReconciliationRunNotFoundException : KeyNotFoundException
{
    /// <summary>Creates a not-found exception.</summary>
    public ReconciliationRunNotFoundException(Guid reconciliationRunId)
        : base($"Reconciliation run '{reconciliationRunId}' was not found.")
    {
        ReconciliationRunId = reconciliationRunId;
    }

    /// <summary>Reconciliation run identifier.</summary>
    public Guid ReconciliationRunId { get; }
}

/// <summary>
/// Raised when a reconciliation item cannot be found.
/// </summary>
public sealed class ReconciliationItemNotFoundException : KeyNotFoundException
{
    /// <summary>Creates a not-found exception.</summary>
    public ReconciliationItemNotFoundException(Guid reconciliationItemId)
        : base($"Reconciliation item '{reconciliationItemId}' was not found.")
    {
        ReconciliationItemId = reconciliationItemId;
    }

    /// <summary>Reconciliation item identifier.</summary>
    public Guid ReconciliationItemId { get; }
}

/// <summary>
/// Raised when a deterministic reconciliation run or item validation failure occurs.
/// </summary>
public sealed class ReconciliationRunItemRejectedException : InvalidOperationException
{
    /// <summary>Creates a deterministic rejection exception.</summary>
    public ReconciliationRunItemRejectedException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Stable machine-readable error code.</summary>
    public string ErrorCode { get; }
}
