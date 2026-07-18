namespace ExitPass.CentralPms.Contracts.ManagementPlatform;

public sealed record ManagementPlatformFiscalIdentityMutationRequestDto(
    string RegisteredBusinessName,
    string RegisteredBusinessAddress,
    string Tin,
    string TaxpayerPosture);

public sealed record ManagementPlatformFiscalIdentityDto(
    Guid FiscalIdentityId,
    string RegisteredBusinessName,
    string RegisteredBusinessAddress,
    string Tin,
    string TaxpayerPosture,
    string LifecycleStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string CreatedByRef,
    string? UpdatedByRef);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileMutationRequestDto(
    Guid FiscalIdentityId,
    Guid SiteId,
    Guid SitePosServerId,
    int ProfileVersion,
    string TemplateVersion,
    string PresentationVersion,
    string PosSerialNumber,
    string MachineIdentificationNumber,
    string ParkingLocationDisplay,
    string BirAccreditationNumber,
    DateOnly? BirAccreditationIssuedDate,
    DateOnly? BirAccreditationValidUntil,
    string PtuNumber,
    DateOnly? PtuIssuedDate,
    string SalesInvoiceLegalStatement,
    string CustomerServiceFooter,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileDto(
    Guid SalesInvoiceHeaderProfileId,
    Guid FiscalIdentityId,
    Guid SiteId,
    Guid SitePosServerId,
    int ProfileVersion,
    string TemplateVersion,
    string PresentationVersion,
    string PosSerialNumber,
    string MachineIdentificationNumber,
    string ParkingLocationDisplay,
    string BirAccreditationNumber,
    DateOnly? BirAccreditationIssuedDate,
    DateOnly? BirAccreditationValidUntil,
    string PtuNumber,
    DateOnly? PtuIssuedDate,
    string SalesInvoiceLegalStatement,
    string CustomerServiceFooter,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string LifecycleState,
    DateTimeOffset? ApprovedAt,
    string? ApprovedByRef,
    DateTimeOffset? RetiredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileRetirementRequestDto(DateTimeOffset? RetireAt);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileValidationDto(
    Guid SalesInvoiceHeaderProfileId,
    string LifecycleState,
    bool IsComplete,
    IReadOnlyList<string> MissingOrInvalidFieldCodes,
    IReadOnlyList<string> ValidationMessages,
    string TemplateVersionPosture,
    string PresentationVersionPosture,
    string EffectiveWindowPosture,
    string OverlapPosture,
    string FiscalIdentityPosture,
    DateTimeOffset ValidatedAt,
    Guid? CorrelationId);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileReadinessDto(
    Guid SiteId,
    Guid SitePosServerId,
    DateTimeOffset EffectiveAt,
    string ResolutionStatus,
    Guid? EffectiveProfileId,
    int? ProfileVersion,
    Guid? FiscalIdentityId,
    string? LifecycleState,
    bool IsComplete,
    bool EnforcementRequired,
    IReadOnlyList<string> MissingOrInvalidFieldCodes,
    string BirAccreditationValidityPosture,
    string PtuCompletenessPosture,
    string SupportedVersionPosture,
    string OverlapOrAmbiguityPosture,
    DateTimeOffset? LastUpdatedAt,
    Guid? CorrelationId);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileUsageDto(
    Guid SalesInvoiceHeaderProfileId,
    int ProfileVersion,
    Guid FiscalIdentityId,
    DateTimeOffset? FirstSnapshotAt,
    DateTimeOffset? LatestSnapshotAt,
    long FiscalDocumentCount,
    IReadOnlyList<string> SafeFiscalDocumentIdentifiers,
    bool DestructiveMutationBlocked,
    Guid? CorrelationId);

public sealed record ManagementPlatformPosServerSalesInvoiceProfileAdministrationErrorDto(
    string Code,
    string Message,
    string Outcome,
    int? HttpStatusCode,
    Guid? CorrelationId);
