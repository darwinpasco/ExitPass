namespace ExitPass.PaymentOrchestrator.Contracts.WebPay;

/// <summary>
/// WebPay-facing statutory-discount request. Source channel and reviewer facts are server-controlled.
/// </summary>
public sealed class WebPayStatutoryDiscountDecisionRequest
{
    /// <summary>
    /// Browser-generated non-secret request reference used for safe recovery and support.
    /// </summary>
    public Guid RequestReference { get; set; }

    /// <summary>
    /// Canonical Central PMS parking session identifier.
    /// </summary>
    public Guid ParkingSessionId { get; set; }

    /// <summary>
    /// Optional site identifier from the resolved parking context.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Optional site group identifier from the resolved parking context.
    /// </summary>
    public Guid? SiteGroupId { get; set; }

    /// <summary>
    /// Parker-facing ticket reference.
    /// </summary>
    public string? TicketReference { get; set; }

    /// <summary>
    /// Parker-facing plate number.
    /// </summary>
    public string? PlateNumber { get; set; }

    /// <summary>
    /// Supported entitlement type, such as SENIOR_CITIZEN or PWD.
    /// </summary>
    public string? EntitlementType { get; set; }

    /// <summary>
    /// Safe document type label supplied for Operator Console review.
    /// </summary>
    public string? IdDocumentType { get; set; }

    /// <summary>
    /// Safe issuing authority label supplied for Operator Console review.
    /// </summary>
    public string? IssuingAuthority { get; set; }

    /// <summary>
    /// Optional entitlement document expiry date.
    /// </summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>
    /// Masked entitlement identifier reference. Full statutory identifiers are not accepted.
    /// </summary>
    public string? MaskedIdReference { get; set; }

    /// <summary>
    /// Indicates whether evidence references accompany this request.
    /// </summary>
    public bool EvidenceCaptureRequested { get; set; }

    /// <summary>
    /// Metadata-only evidence references. Raw evidence payloads are not accepted.
    /// </summary>
    public IReadOnlyList<WebPayStatutoryDiscountEvidenceReference>? EvidenceReferences { get; set; }

    /// <summary>
    /// Customer attestation that the submitted facts are correct.
    /// </summary>
    public bool RequesterAttestation { get; set; }

    /// <summary>
    /// Safe attestation notes for human review.
    /// </summary>
    public string? AttestationNotes { get; set; }

    /// <summary>
    /// Optional safe reason code.
    /// </summary>
    public string? ReasonCode { get; set; }

    /// <summary>
    /// Original tariff snapshot from the resolved pre-application payable basis.
    /// </summary>
    public Guid? OriginalTariffSnapshotId { get; set; }
}

/// <summary>
/// WebPay-facing metadata-only evidence reference.
/// </summary>
public sealed class WebPayStatutoryDiscountEvidenceReference
{
    /// <summary>
    /// Safe evidence type label.
    /// </summary>
    public string? EvidenceType { get; set; }

    /// <summary>
    /// Safe capture method label.
    /// </summary>
    public string? CaptureMethod { get; set; }

    /// <summary>
    /// Optional safe file name metadata.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Optional content type metadata.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Optional bounded evidence size metadata.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Safe storage reference for evidence already held by an approved evidence store.
    /// </summary>
    public string? StorageReference { get; set; }

    /// <summary>
    /// Masked document reference. Full statutory identifiers are not accepted.
    /// </summary>
    public string? ReferenceNumberMasked { get; set; }

    /// <summary>
    /// Safe pre-review verification posture when supplied.
    /// </summary>
    public string? VerificationStatus { get; set; }
}

/// <summary>
/// WebPay-facing statutory parking local-ordinance availability request.
/// </summary>
public sealed class WebPayStatutoryDiscountAvailabilityRequest
{
    /// <summary>
    /// Browser-generated non-secret request reference used for safe support correlation.
    /// </summary>
    public Guid RequestReference { get; set; }

    /// <summary>
    /// Canonical parking session identifier returned by WebPay parking-session resolution.
    /// </summary>
    public Guid ParkingSessionId { get; set; }

    /// <summary>
    /// Optional Site identifier returned by WebPay parking-session resolution.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Optional Site Group identifier returned by WebPay parking-session resolution.
    /// </summary>
    public Guid? SiteGroupId { get; set; }

    /// <summary>
    /// Optional entitlement filter, limited to Senior Citizen or PWD.
    /// </summary>
    public string? RequestedEntitlementType { get; set; }
}

/// <summary>
/// Browser-safe statutory parking local-ordinance availability readback.
/// </summary>
public sealed class WebPayStatutoryDiscountAvailabilityResponse
{
    /// <summary>
    /// Non-secret request reference used for safe support correlation.
    /// </summary>
    public Guid RequestReference { get; set; }

    /// <summary>
    /// Canonical parking session identifier.
    /// </summary>
    public Guid ParkingSessionId { get; set; }

    /// <summary>
    /// Resolved Site identifier when available.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Resolved Site Group identifier when available.
    /// </summary>
    public Guid? SiteGroupId { get; set; }

    /// <summary>
    /// Authoritative Central PMS availability status.
    /// </summary>
    public string AvailabilityStatus { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether Central PMS reports an active statutory parking benefit.
    /// </summary>
    public bool StatutoryParkingBenefitAvailable { get; set; }

    /// <summary>
    /// Entitlement types currently covered by the authoritative result.
    /// </summary>
    public IReadOnlyList<string> CoveredEntitlementTypes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Requested entitlement filter echoed when supplied.
    /// </summary>
    public string? RequestedEntitlementType { get; set; }

    /// <summary>
    /// Browser-safe reason code.
    /// </summary>
    public string? SafeReasonCode { get; set; }

    /// <summary>
    /// Indicates whether the availability request can be retried.
    /// </summary>
    public bool Retryable { get; set; }

    /// <summary>
    /// Browser-safe remediation action.
    /// </summary>
    public string RemediationAction { get; set; } = string.Empty;

    /// <summary>
    /// Browser-safe evidence requirement metadata for active coverage.
    /// </summary>
    public IReadOnlyList<WebPayStatutoryDiscountAvailabilityEvidenceRequirement> RequiredEvidenceTypes { get; set; } =
        Array.Empty<WebPayStatutoryDiscountAvailabilityEvidenceRequirement>();

    /// <summary>
    /// Correlation identifier for safe diagnostics and support.
    /// </summary>
    public Guid CorrelationId { get; set; }
}

/// <summary>
/// Browser-safe evidence requirement metadata returned only when Central PMS reports active coverage.
/// </summary>
public sealed class WebPayStatutoryDiscountAvailabilityEvidenceRequirement
{
    /// <summary>
    /// Safe evidence type code.
    /// </summary>
    public string EvidenceType { get; set; } = string.Empty;

    /// <summary>
    /// Requirement status.
    /// </summary>
    public string RequirementStatus { get; set; } = string.Empty;

    /// <summary>
    /// Customer-safe requirement label.
    /// </summary>
    public string SafeRequirementLabel { get; set; } = string.Empty;

    /// <summary>
    /// Optional customer-safe requirement notes.
    /// </summary>
    public string? SafeRequirementNotes { get; set; }
}

/// <summary>
/// WebPay-facing request for rediscovering an existing statutory-discount pending lifecycle.
/// </summary>
public sealed class WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest
{
    /// <summary>
    /// Lookup mode. Must be PARKING_SESSION_ID, TICKET_REFERENCE, or PLATE_NUMBER.
    /// </summary>
    public string? LookupMode { get; set; }

    /// <summary>
    /// Canonical parking session identifier when lookup mode is PARKING_SESSION_ID.
    /// </summary>
    public Guid? ParkingSessionId { get; set; }

    /// <summary>
    /// Authoritative Site identifier from the resolved parking session.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Authoritative Site Group identifier from the resolved parking session.
    /// </summary>
    public Guid? SiteGroupId { get; set; }

    /// <summary>
    /// Ticket reference when lookup mode is TICKET_REFERENCE.
    /// </summary>
    public string? TicketReference { get; set; }

    /// <summary>
    /// Plate number when lookup mode is PLATE_NUMBER.
    /// </summary>
    public string? PlateNumber { get; set; }

    /// <summary>
    /// Optional vendor system identifier from the resolved parking session.
    /// </summary>
    public string? VendorSystemId { get; set; }

    /// <summary>
    /// Optional entitlement filter.
    /// </summary>
    public string? EntitlementType { get; set; }
}

/// <summary>
/// WebPay-safe response for rediscovering an existing statutory-discount pending lifecycle.
/// </summary>
public sealed class WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse
{
    /// <summary>
    /// Safe rediscovery classification.
    /// </summary>
    public string Classification { get; set; } = string.Empty;

    /// <summary>
    /// Canonical statutory decision identifier when found.
    /// </summary>
    public Guid? StatutoryDecisionId { get; set; }

    /// <summary>
    /// Canonical statutory decision command identifier when found.
    /// </summary>
    public Guid? StatutoryDecisionCommandId { get; set; }

    /// <summary>
    /// Existing request reference when found.
    /// </summary>
    public Guid? RequestReference { get; set; }

    /// <summary>
    /// Existing entitlement type when found.
    /// </summary>
    public string? EntitlementType { get; set; }

    /// <summary>
    /// Existing decision status when found.
    /// </summary>
    public string? DecisionStatus { get; set; }

    /// <summary>
    /// Existing payable-basis status when found.
    /// </summary>
    public string? PayableBasisStatus { get; set; }

    /// <summary>
    /// Canonical parking session identifier.
    /// </summary>
    public Guid? ParkingSessionId { get; set; }

    /// <summary>
    /// Canonical Site identifier.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Canonical Site Group identifier.
    /// </summary>
    public Guid? SiteGroupId { get; set; }

    /// <summary>
    /// Opaque continuation reference when found.
    /// </summary>
    public string? OpaqueContinuationReference { get; set; }

    /// <summary>
    /// Opaque continuation URL when found.
    /// </summary>
    public string? OpaqueContinuationUrl { get; set; }

    /// <summary>
    /// Safe lifecycle state.
    /// </summary>
    public string LifecycleState { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether rediscovery can be retried.
    /// </summary>
    public bool Retryable { get; set; }

    /// <summary>
    /// Safe correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Safe lifecycle creation timestamp when found.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Safe lifecycle update timestamp when found.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Safe lifecycle submitted timestamp when found.
    /// </summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>
    /// Safe lifecycle decided timestamp when found.
    /// </summary>
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>
    /// Safe lifecycle reviewed timestamp when found.
    /// </summary>
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>
/// Browser-safe durable statutory-discount readback.
/// </summary>
public sealed class WebPayStatutoryDiscountDecisionResponse
{
    /// <summary>
    /// Canonical Central PMS statutory-discount decision command identifier.
    /// </summary>
    public Guid StatutoryDiscountDecisionCommandId { get; set; }

    /// <summary>
    /// Non-secret request reference supplied by WebPay.
    /// </summary>
    public Guid RequestReference { get; set; }

    /// <summary>
    /// Canonical payable-basis application command identifier when application was requested.
    /// </summary>
    public Guid? StatutoryDiscountPayableBasisApplicationCommandId { get; set; }

    /// <summary>
    /// Canonical statutory validation identifier when available.
    /// </summary>
    public Guid? StatutoryDiscountValidationId { get; set; }

    /// <summary>
    /// Canonical parking session identifier.
    /// </summary>
    public Guid ParkingSessionId { get; set; }

    /// <summary>
    /// Site identifier associated with the decision when returned.
    /// </summary>
    public Guid? SiteId { get; set; }

    /// <summary>
    /// Site group identifier associated with the decision when returned.
    /// </summary>
    public Guid? SiteGroupId { get; set; }

    /// <summary>
    /// Supported entitlement type.
    /// </summary>
    public string EntitlementType { get; set; } = string.Empty;

    /// <summary>
    /// Durable decision command status.
    /// </summary>
    public string DecisionCommandStatus { get; set; } = string.Empty;

    /// <summary>
    /// Durable decision result status.
    /// </summary>
    public string? DecisionResultStatus { get; set; }

    /// <summary>
    /// Durable payable-basis application command status.
    /// </summary>
    public string ApplicationCommandStatus { get; set; } = string.Empty;

    /// <summary>
    /// Durable payable-basis application result classification.
    /// </summary>
    public string ApplicationResultClassification { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether Central PMS has approved and applied the statutory payable basis.
    /// </summary>
    public bool PayableBasisReady { get; set; }

    /// <summary>
    /// Browser-safe payable-basis readiness status.
    /// </summary>
    public string PayableBasisReadinessStatus { get; set; } = string.Empty;

    /// <summary>
    /// Browser-safe next action for payable-basis readiness.
    /// </summary>
    public string? PayableBasisReadinessAction { get; set; }

    /// <summary>
    /// Original tariff snapshot identifier.
    /// </summary>
    public Guid? OriginalTariffSnapshotId { get; set; }

    /// <summary>
    /// Applied tariff snapshot identifier once approved and applied.
    /// </summary>
    public Guid? AppliedTariffSnapshotId { get; set; }

    /// <summary>
    /// Original amount in minor currency units.
    /// </summary>
    public long? OriginalAmountMinorUnits { get; set; }

    /// <summary>
    /// VAT-exclusive basis amount in minor currency units.
    /// </summary>
    public long? VatExclusiveBasisAmountMinorUnits { get; set; }

    /// <summary>
    /// VAT amount in minor currency units.
    /// </summary>
    public long? VatAmountMinorUnits { get; set; }

    /// <summary>
    /// Central PMS VAT treatment classification.
    /// </summary>
    public string? VatTreatment { get; set; }

    /// <summary>
    /// Statutory-discount amount in minor currency units.
    /// </summary>
    public long? StatutoryDiscountAmountMinorUnits { get; set; }

    /// <summary>
    /// Final payable amount in minor currency units after approved application.
    /// </summary>
    public long? FinalPayableAmountMinorUnits { get; set; }

    /// <summary>
    /// Currency code for returned amount fields.
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Indicates whether the current non-terminal result can be retried.
    /// </summary>
    public bool Retryable { get; set; }

    /// <summary>
    /// Browser-safe recovery classification.
    /// </summary>
    public string RecoveryClassification { get; set; } = string.Empty;

    /// <summary>
    /// Browser-safe recovery action.
    /// </summary>
    public string? RecoveryAction { get; set; }

    /// <summary>
    /// Browser-safe error code.
    /// </summary>
    public string? SafeErrorCode { get; set; }

    /// <summary>
    /// Overall command orchestration result classification.
    /// </summary>
    public string OverallResultClassification { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether Central PMS completed the requested one-shot path.
    /// </summary>
    public bool OneShotComplete { get; set; }

    /// <summary>
    /// Correlation identifier returned by Central PMS.
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Decision creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Decision timestamp when available.
    /// </summary>
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>
    /// Payable-basis application timestamp when available.
    /// </summary>
    public DateTimeOffset? AppliedAt { get; set; }
}
