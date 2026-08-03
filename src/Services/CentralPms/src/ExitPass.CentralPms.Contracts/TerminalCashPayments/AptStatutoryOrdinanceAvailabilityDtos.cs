namespace ExitPass.CentralPms.Contracts.TerminalCashPayments;

public sealed record AptStatutoryOrdinanceAvailabilityRequest(
    string SiteGroupId,
    string SiteId,
    string TerminalId,
    string? VendorSystemId,
    string? ParkingSessionId,
    string? TicketReference,
    string? PlateNumber,
    string EntitlementType,
    Guid CorrelationId);

public sealed record AptStatutoryOrdinanceAvailabilityResponse(
    string Operation,
    string? RevalidationOutcome,
    string Classification,
    string EntitlementType,
    bool OrdinanceCoverageAvailable,
    bool StatutoryRequestAllowed,
    bool PreCashRevalidationPassed,
    bool ReadyForStatutoryCashFlow,
    bool OrdinaryPaymentPreserved,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string ResolvedScopeType,
    string CoverageClassification,
    string PolicyStatusClassification,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? AuthorityClassification,
    string? JurisdictionDisplayName,
    string? SupportReference,
    Guid CorrelationId,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset? AuthoritativeUpdatedAt,
    bool Retryable,
    string SafeMessage);
