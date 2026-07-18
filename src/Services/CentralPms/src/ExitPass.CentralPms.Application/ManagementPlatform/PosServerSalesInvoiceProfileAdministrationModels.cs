namespace ExitPass.CentralPms.Application.ManagementPlatform;

public interface IPosServerSalesInvoiceProfileAdminClient
{
    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> CreateFiscalIdentityAsync(
        ManagementPlatformFiscalIdentityMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> GetFiscalIdentityAsync(
        Guid fiscalIdentityId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> UpdateFiscalIdentityAsync(
        Guid fiscalIdentityId,
        ManagementPlatformFiscalIdentityMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> CreateProfileAsync(
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> GetProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfile>>> ListProfilesAsync(
        ManagementPlatformSalesInvoiceHeaderProfileListRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> UpdateDraftProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileValidation>> ValidateProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> ApproveProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> RetireProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>> GetEffectiveReadinessAsync(
        ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileUsage>> GetProfileUsageAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);
}

public interface ISalesInvoiceProfileAdministrationService
{
    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> CreateFiscalIdentityAsync(
        ManagementPlatformFiscalIdentityMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> GetFiscalIdentityAsync(
        Guid fiscalIdentityId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> UpdateFiscalIdentityAsync(
        Guid fiscalIdentityId,
        ManagementPlatformFiscalIdentityMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> CreateProfileAsync(
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> GetProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfile>>> ListProfilesAsync(
        ManagementPlatformSalesInvoiceHeaderProfileListRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> UpdateDraftProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileValidation>> ValidateProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> ApproveProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> RetireProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>> GetEffectiveReadinessAsync(
        ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileUsage>> GetProfileUsageAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken);
}

public sealed class PosServerSalesInvoiceProfileAdministrationOptions
{
    public const string SectionName = "ManagementPlatform:PosServerSalesInvoiceProfileAdministration";

    public bool Enabled { get; set; }

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    public IReadOnlyList<string> Validate()
    {
        if (!Enabled)
        {
            return Array.Empty<string>();
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            errors.Add("pos_server_sales_invoice_profile_admin_base_url_required");
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("pos_server_sales_invoice_profile_admin_base_url_invalid");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add("pos_server_sales_invoice_profile_admin_api_key_required");
        }

        if (TimeoutSeconds <= 0)
        {
            errors.Add("pos_server_sales_invoice_profile_admin_timeout_seconds_must_be_positive");
        }

        return errors;
    }
}

public sealed record ManagementPlatformPosServerAdminRequestContext(Guid? CorrelationId)
{
    public Guid GetOrCreateCorrelationId() =>
        CorrelationId is { } correlationId && correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();
}

public sealed record ManagementPlatformFiscalIdentityMutationRequest(
    string RegisteredBusinessName,
    string RegisteredBusinessAddress,
    string Tin,
    string TaxpayerPosture,
    string RequestedByRef);

public sealed record ManagementPlatformFiscalIdentity(
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

public sealed record ManagementPlatformSalesInvoiceHeaderProfileMutationRequest(
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
    string RequestedByRef);

public sealed record ManagementPlatformSalesInvoiceHeaderProfile(
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

public sealed record ManagementPlatformSalesInvoiceHeaderProfileListRequest(
    Guid? SiteId,
    Guid? SitePosServerId,
    string? LifecycleState);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest(string ApprovedByRef);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest(
    string RetiredByRef,
    DateTimeOffset? RetireAt);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileValidation(
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

public sealed record ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest(
    Guid SiteId,
    Guid SitePosServerId,
    DateTimeOffset EffectiveAt);

public sealed record ManagementPlatformSalesInvoiceHeaderProfileReadiness(
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

public sealed record ManagementPlatformSalesInvoiceHeaderProfileUsage(
    Guid SalesInvoiceHeaderProfileId,
    int ProfileVersion,
    Guid FiscalIdentityId,
    DateTimeOffset? FirstSnapshotAt,
    DateTimeOffset? LatestSnapshotAt,
    long FiscalDocumentCount,
    IReadOnlyList<string> SafeFiscalDocumentIdentifiers,
    bool DestructiveMutationBlocked,
    Guid? CorrelationId);

public enum PosServerSalesInvoiceProfileAdminOutcome
{
    Succeeded = 1,
    Disabled = 2,
    InvalidConfiguration = 3,
    InvalidRequest = 4,
    AuthenticationFailed = 5,
    PermissionDenied = 6,
    NotFound = 7,
    Conflict = 8,
    ValidationFailure = 9,
    Throttled = 10,
    PosServerUnavailable = 11,
    Timeout = 12,
    NetworkFailure = 13,
    MalformedResponse = 14,
    UnknownFailure = 15
}

public sealed record PosServerSalesInvoiceProfileAdminError(
    string Code,
    string Message,
    PosServerSalesInvoiceProfileAdminOutcome Outcome,
    int? HttpStatusCode,
    Guid? CorrelationId);

public sealed record PosServerSalesInvoiceProfileAdminResult<T>(
    PosServerSalesInvoiceProfileAdminOutcome Outcome,
    bool Succeeded,
    T? Value,
    PosServerSalesInvoiceProfileAdminError? Error,
    Guid CorrelationId,
    int? HttpStatusCode,
    bool MutationSent,
    bool Retried)
{
    public static PosServerSalesInvoiceProfileAdminResult<T> Success(
        T value,
        Guid correlationId,
        int httpStatusCode,
        bool mutationSent = false,
        bool retried = false) =>
        new(PosServerSalesInvoiceProfileAdminOutcome.Succeeded, true, value, null, correlationId, httpStatusCode, mutationSent, retried);

    public static PosServerSalesInvoiceProfileAdminResult<T> Failure(
        PosServerSalesInvoiceProfileAdminOutcome outcome,
        string code,
        string message,
        Guid correlationId,
        int? httpStatusCode = null,
        bool mutationSent = false,
        bool retried = false) =>
        new(
            outcome,
            false,
            default,
            new PosServerSalesInvoiceProfileAdminError(code, message, outcome, httpStatusCode, correlationId),
            correlationId,
            httpStatusCode,
            mutationSent,
            retried);
}

public static class ManagementPlatformSalesInvoiceProfileLifecycleStates
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Retired = "RETIRED";
}

public static class ManagementPlatformSalesInvoiceProfileReadinessStatuses
{
    public const string Ready = "READY";
    public const string NoEffectiveProfile = "NO_EFFECTIVE_PROFILE";
    public const string Incomplete = "INCOMPLETE";
    public const string Expired = "EXPIRED";
    public const string Ambiguous = "AMBIGUOUS";
    public const string UnsupportedVersion = "UNSUPPORTED_VERSION";
    public const string Retired = "RETIRED";
}
