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
