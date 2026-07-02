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
    /// Gets the future fiscal gating enforcement decision computed in shadow-only mode.
    /// </summary>
    public string EnforcementDecision { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the future decision would allow normal ExitAuthorization.
    /// </summary>
    public bool WouldAllowNormalExitAuthorization { get; init; }

    /// <summary>
    /// Gets a value indicating whether the future decision would block normal ExitAuthorization.
    /// </summary>
    public bool WouldBlockNormalExitAuthorization { get; init; }

    /// <summary>
    /// Gets a value indicating whether the future decision is based on an explicit not-required-by-policy posture.
    /// </summary>
    public bool IsNotRequiredByPolicy { get; init; }

    /// <summary>
    /// Gets a value indicating whether the future decision is exception/manual-release only.
    /// </summary>
    public bool IsExceptionReleaseOnly { get; init; }

    /// <summary>
    /// Gets a value indicating whether the future decision requires manual review.
    /// </summary>
    public bool RequiresManualReview { get; init; }

    /// <summary>
    /// Gets a value indicating whether the future decision could not be evaluated from local fiscal context.
    /// </summary>
    public bool IsNotEvaluable { get; init; }

    /// <summary>
    /// Gets a value indicating whether fiscal-before-ExitAuthorization enforcement is configured.
    /// </summary>
    public bool EnforcementEnabled { get; init; }

    /// <summary>
    /// Gets a value indicating whether enforcement is wired to block production ExitAuthorization.
    /// </summary>
    public bool EnforcementWiredForBlocking { get; init; }

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
