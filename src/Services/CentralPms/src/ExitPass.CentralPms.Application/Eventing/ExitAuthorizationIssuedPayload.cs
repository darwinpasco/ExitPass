namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for the ExitAuthorizationIssued integration event.
/// </summary>
public sealed class ExitAuthorizationIssuedPayload
{
    /// <summary>
    /// Gets the issued exit authorization identifier.
    /// </summary>
    public Guid ExitAuthorizationId { get; init; }

    /// <summary>
    /// Gets the parking session identifier.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Gets the payment attempt identifier that authorized paid exit; null for statutory zero-payable completion.
    /// </summary>
    public Guid? PaymentAttemptId { get; init; }

    /// <summary>
    /// Gets the canonical completion basis used for issuance.
    /// </summary>
    public string CompletionBasis { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether monetary payment was required for this completion.
    /// </summary>
    public bool PaymentRequired { get; init; }

    /// <summary>
    /// Gets the authorization status.
    /// </summary>
    public string AuthorizationStatus { get; init; } = string.Empty;

    /// <summary>
    /// Gets the issuance timestamp.
    /// </summary>
    public DateTimeOffset IssuedAtUtc { get; init; }

    /// <summary>
    /// Gets the authorization expiry timestamp.
    /// </summary>
    public DateTimeOffset ExpirationTimestampUtc { get; init; }
}
