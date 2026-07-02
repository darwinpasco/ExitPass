namespace ExitPass.CentralPms.Application.Eventing;

/// <summary>
/// Payload for the non-enforcing ExitAuthorization fiscal gating shadow observation event.
/// </summary>
public sealed class ExitAuthorizationFiscalGatingShadowObservedPayload
{
    /// <summary>
    /// Gets the parking session identifier from the ExitAuthorization command.
    /// </summary>
    public Guid ParkingSessionId { get; init; }

    /// <summary>
    /// Gets the payment attempt identifier from the ExitAuthorization command.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// Gets the payment confirmation identifier when a fiscal reference was available.
    /// </summary>
    public Guid? PaymentConfirmationId { get; init; }

    /// <summary>
    /// Gets the fiscal issuance reference identifier when a fiscal reference was available.
    /// </summary>
    public Guid? FiscalIssuanceReferenceId { get; init; }

    /// <summary>
    /// Gets the POS Server fiscal document identifier when available.
    /// </summary>
    public Guid? PosServerFiscalDocumentId { get; init; }

    /// <summary>
    /// Gets the POS Server fiscal document number when available.
    /// </summary>
    public string? FiscalDocumentNumber { get; init; }

    /// <summary>
    /// Gets the fiscal issuance integration state when available.
    /// </summary>
    public string? FiscalIssuanceState { get; init; }

    /// <summary>
    /// Gets the fiscal issuance evidence status when available.
    /// </summary>
    public string? FiscalIssuanceEvidenceStatus { get; init; }

    /// <summary>
    /// Gets the fiscal number assignment state when available.
    /// </summary>
    public string? FiscalNumberAssignmentState { get; init; }

    /// <summary>
    /// Gets the non-enforcing shadow evaluation status.
    /// </summary>
    public string ShadowEvaluationStatus { get; init; } = string.Empty;

    /// <summary>
    /// Gets the shadow evaluation blocked reason when available.
    /// </summary>
    public string? BlockedReason { get; init; }

    /// <summary>
    /// Gets the latest fiscal exception reason when available.
    /// </summary>
    public string? ExceptionReason { get; init; }

    /// <summary>
    /// Gets the latest fiscal error posture when available.
    /// </summary>
    public string? ErrorPosture { get; init; }

    /// <summary>
    /// Gets the Site identifier when available.
    /// </summary>
    public Guid? SiteId { get; init; }

    /// <summary>
    /// Gets the Site POS Server identifier when available.
    /// </summary>
    public Guid? SitePosServerId { get; init; }

    /// <summary>
    /// Gets the Site POS Server reference when available.
    /// </summary>
    public string? SitePosServerRef { get; init; }

    /// <summary>
    /// Gets the correlation identifier for the triggering ExitAuthorization command.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Gets the component that produced the observation.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Gets the timestamp when the shadow observation was emitted.
    /// </summary>
    public DateTimeOffset ObservedAtUtc { get; init; }
}
