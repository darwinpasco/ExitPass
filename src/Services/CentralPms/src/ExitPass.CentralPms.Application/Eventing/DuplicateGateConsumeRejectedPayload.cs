namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for duplicate gate consume rejection evidence.
/// </summary>
public sealed class DuplicateGateConsumeRejectedPayload
{
    /// <summary>
    /// Gets the exit authorization identifier that was presented again.
    /// </summary>
    public Guid ExitAuthorizationId { get; init; }

    /// <summary>
    /// Gets the deterministic rejection reason code.
    /// </summary>
    public string RejectionReasonCode { get; init; } = string.Empty;
}
