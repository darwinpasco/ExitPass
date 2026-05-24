namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Raised when a reconciliation item has no persisted exception to review.
/// </summary>
public sealed class ReconciliationExceptionNotFoundException : KeyNotFoundException
{
    /// <summary>Creates a not-found exception for a reconciliation item.</summary>
    public ReconciliationExceptionNotFoundException(Guid reconciliationItemId)
        : base($"Reconciliation exception was not found for item '{reconciliationItemId}'.")
    {
        ReconciliationItemId = reconciliationItemId;
    }

    /// <summary>Reconciliation item identifier.</summary>
    public Guid ReconciliationItemId { get; }
}

/// <summary>
/// Raised when a reconciliation resolution request cannot be found.
/// </summary>
public sealed class ReconciliationResolutionRequestNotFoundException : KeyNotFoundException
{
    /// <summary>Creates a not-found exception for a resolution request.</summary>
    public ReconciliationResolutionRequestNotFoundException(Guid resolutionRequestId)
        : base($"Reconciliation resolution request '{resolutionRequestId}' was not found.")
    {
        ResolutionRequestId = resolutionRequestId;
    }

    /// <summary>Resolution request identifier.</summary>
    public Guid ResolutionRequestId { get; }
}

/// <summary>
/// Raised when a deterministic reconciliation workflow conflict occurs.
/// </summary>
public sealed class ReconciliationWorkflowConflictException : InvalidOperationException
{
    /// <summary>Creates a workflow conflict exception.</summary>
    public ReconciliationWorkflowConflictException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Stable machine-readable error code.</summary>
    public string ErrorCode { get; }
}
