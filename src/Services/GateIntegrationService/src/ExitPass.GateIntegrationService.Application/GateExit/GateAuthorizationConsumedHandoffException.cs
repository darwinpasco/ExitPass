namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Deterministic rejection raised while processing a consumed authorization handoff.
/// </summary>
public sealed class GateAuthorizationConsumedHandoffException : InvalidOperationException
{
    /// <summary>
    /// Creates a deterministic handoff rejection.
    /// </summary>
    /// <param name="errorCode">Stable error code.</param>
    /// <param name="message">Human-readable message.</param>
    public GateAuthorizationConsumedHandoffException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? "GATE_AUTHORIZATION_CONSUMED_HANDOFF_REJECTED"
            : errorCode;
    }

    /// <summary>
    /// Stable rejection code.
    /// </summary>
    public string ErrorCode { get; }
}
