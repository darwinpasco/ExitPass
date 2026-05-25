namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Raised when a MoPS transaction record cannot be found.
/// </summary>
public sealed class MopsTransactionNotFoundException : KeyNotFoundException
{
    /// <summary>Creates a not-found exception.</summary>
    public MopsTransactionNotFoundException(Guid mopsTransactionRecordId)
        : base($"MoPS transaction record '{mopsTransactionRecordId}' was not found.")
    {
        MopsTransactionRecordId = mopsTransactionRecordId;
    }

    /// <summary>MoPS transaction record identifier.</summary>
    public Guid MopsTransactionRecordId { get; }
}

/// <summary>
/// Raised when a deterministic MoPS import validation failure occurs.
/// </summary>
public sealed class MopsImportRejectedException : InvalidOperationException
{
    /// <summary>Creates a deterministic rejection exception.</summary>
    public MopsImportRejectedException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Stable machine-readable error code.</summary>
    public string ErrorCode { get; }
}
