using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExitPass.CentralPms.Application.StatutoryDiscounts;

public static class StatutoryDiscountParkingAvailabilityStatuses
{
    public const string Available = "AVAILABLE";
    public const string NoApplicableLocalOrdinance = "NO_APPLICABLE_LOCAL_ORDINANCE";
    public const string SiteNotResolved = "SITE_NOT_RESOLVED";
    public const string SiteJurisdictionNotConfigured = "SITE_JURISDICTION_NOT_CONFIGURED";
    public const string SiteJurisdictionAmbiguous = "SITE_JURISDICTION_AMBIGUOUS";
    public const string PolicyNotYetEffective = "POLICY_NOT_YET_EFFECTIVE";
    public const string PolicyExpired = "POLICY_EXPIRED";
    public const string PolicySuspended = "POLICY_SUSPENDED";
    public const string PolicyWithdrawn = "POLICY_WITHDRAWN";
    public const string PolicySupersededWithoutSuccessor = "POLICY_SUPERSEDED_WITHOUT_SUCCESSOR";
    public const string PolicyUnverified = "POLICY_UNVERIFIED";
    public const string PolicyNotPublished = "POLICY_NOT_PUBLISHED";
    public const string EntitlementNotCovered = "ENTITLEMENT_NOT_COVERED";
    public const string ParkingServiceNotCovered = "PARKING_SERVICE_NOT_COVERED";
    public const string ResidencyRequirementNotSatisfied = "RESIDENCY_REQUIREMENT_NOT_SATISFIED";
    public const string RequiredPolicyFactsIncomplete = "REQUIRED_POLICY_FACTS_INCOMPLETE";
    public const string PolicyConflict = "POLICY_CONFLICT";
    public const string BenefitEffectNotSupported = "BENEFIT_EFFECT_NOT_SUPPORTED";
    public const string TemporarilyUnavailable = "TEMPORARILY_UNAVAILABLE";
}

public static class StatutoryDiscountParkingAvailabilityRemediationActions
{
    public const string ContinueWithOrdinaryPayment = "CONTINUE_WITH_ORDINARY_PAYMENT";
    public const string ConfigureSiteJurisdiction = "CONFIGURE_SITE_JURISDICTION";
    public const string ResolveJurisdictionAmbiguity = "RESOLVE_JURISDICTION_AMBIGUITY";
    public const string PublishApplicablePolicy = "PUBLISH_APPLICABLE_POLICY";
    public const string ResolvePolicyConflict = "RESOLVE_POLICY_CONFLICT";
    public const string ProvideResidencyEvidence = "PROVIDE_RESIDENCY_EVIDENCE";
    public const string WaitAndRetry = "WAIT_AND_RETRY";
}

public sealed record StatutoryDiscountParkingAvailabilityRequest(
    Guid RequestReference,
    Guid ParkingSessionId,
    string? RequestedEntitlementType,
    bool? BeneficiaryResidencySatisfied,
    Guid CorrelationId);

public sealed record StatutoryDiscountParkingAvailabilityResult(
    Guid RequestReference,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid? JurisdictionId,
    string? JurisdictionCode,
    string? JurisdictionDisplayName,
    string AvailabilityStatus,
    bool StatutoryParkingBenefitAvailable,
    IReadOnlyList<string> CoveredEntitlementTypes,
    string? RequestedEntitlementType,
    Guid? SiteJurisdictionAssignmentId,
    Guid? PolicyVersionId,
    string? PolicyCode,
    string? PolicyVersion,
    string? OrdinanceNumber,
    string? OrdinanceTitle,
    string? PolicyDisplayName,
    string? VerificationStatus,
    string? PublicationStatus,
    string? DetailedRuleVerificationStatus,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? ResidencyRequirement,
    IReadOnlyList<StatutoryDiscountPolicyEvidenceRequirement> RequiredEvidenceTypes,
    string? ParkingServiceApplicability,
    string? BenefitEffectClassification,
    string? BenefitEffectSupportStatus,
    bool? OfficialSourceAvailable,
    bool? OrdinanceTextAvailable,
    bool? OrdinanceNumberAvailable,
    string? LegalBasisReference,
    string? SourceReference,
    string? SafeReasonCode,
    bool Retryable,
    string RemediationAction,
    DateTimeOffset? TransactionAt,
    string? PolicySemanticHash,
    Guid CorrelationId)
{
    public bool IsAvailable =>
        string.Equals(AvailabilityStatus, StatutoryDiscountParkingAvailabilityStatuses.Available, StringComparison.Ordinal);
}

public sealed record StatutoryDiscountPolicyEvidenceRequirement(
    string EvidenceType,
    string RequirementStatus,
    string SafeRequirementLabel,
    string? SafeRequirementNotes);

public sealed record StatutoryDiscountDecisionPolicyAuthority(
    Guid StatutoryDiscountDecisionCommandId,
    Guid StatutoryDiscountPolicyVersionId,
    Guid JurisdictionId,
    string JurisdictionCode,
    string JurisdictionDisplayName,
    string PolicyCode,
    string PolicyVersion,
    string EntitlementType,
    string SourceVerificationStatus,
    string TransactionPublicationStatus,
    string DetailedRuleVerificationStatus,
    string ParkingServiceApplicability,
    string BenefitType,
    string BeneficiaryResidencyScope,
    bool? OfficialSourceAvailable,
    bool? OrdinanceTextAvailable,
    bool? OrdinanceNumberAvailable,
    string? OrdinanceNumber,
    string? OrdinanceTitle,
    string? LegalBasisReference,
    string SourceReference,
    DateTimeOffset? TransactionUseEffectiveFrom,
    DateTimeOffset? TransactionUseEffectiveTo,
    DateTimeOffset ResolvedAt,
    string PolicyAuthoritySemanticHash,
    Guid CorrelationId);

public interface IStatutoryDiscountParkingEligibilityResolver
{
    Task<StatutoryDiscountParkingAvailabilityResult> ResolveAsync(
        StatutoryDiscountParkingAvailabilityRequest request,
        CancellationToken cancellationToken);
}

public interface IStatutoryDiscountParkingEligibilityRepository
{
    Task<StatutoryDiscountParkingAvailabilityResult> ResolveAsync(
        StatutoryDiscountParkingAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task BindDecisionPolicyAuthorityAsync(
        Guid statutoryDiscountDecisionCommandId,
        StatutoryDiscountParkingAvailabilityResult availability,
        CancellationToken cancellationToken);

    Task<StatutoryDiscountDecisionPolicyAuthority?> GetDecisionPolicyAuthorityAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken);
}

public sealed class StatutoryDiscountParkingEligibilityResolver : IStatutoryDiscountParkingEligibilityResolver
{
    private readonly IStatutoryDiscountParkingEligibilityRepository _repository;

    public StatutoryDiscountParkingEligibilityResolver(IStatutoryDiscountParkingEligibilityRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<StatutoryDiscountParkingAvailabilityResult> ResolveAsync(
        StatutoryDiscountParkingAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RequestReference == Guid.Empty)
        {
            throw new ArgumentException("Request reference is required.", nameof(request));
        }

        if (request.ParkingSessionId == Guid.Empty)
        {
            throw new ArgumentException("Parking session id is required.", nameof(request));
        }

        if (request.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation id is required.", nameof(request));
        }

        return _repository.ResolveAsync(
            request with
            {
                RequestedEntitlementType = NormalizeOptional(request.RequestedEntitlementType)
            },
            cancellationToken);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

public static class StatutoryDiscountDecisionPolicyAuthorityHash
{
    private const string SourceVersion = "statutory-decision-policy-authority:sha256:v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(StatutoryDiscountParkingAvailabilityResult availability)
    {
        var source = new
        {
            version = SourceVersion,
            availability.PolicyVersionId,
            availability.JurisdictionId,
            availability.JurisdictionCode,
            availability.JurisdictionDisplayName,
            availability.PolicyCode,
            availability.PolicyVersion,
            availability.RequestedEntitlementType,
            availability.VerificationStatus,
            availability.PublicationStatus,
            availability.DetailedRuleVerificationStatus,
            availability.ParkingServiceApplicability,
            availability.BenefitEffectClassification,
            availability.ResidencyRequirement,
            availability.OfficialSourceAvailable,
            availability.OrdinanceTextAvailable,
            availability.OrdinanceNumberAvailable,
            availability.OrdinanceNumber,
            availability.OrdinanceTitle,
            availability.LegalBasisReference,
            availability.SourceReference,
            availability.EffectiveFrom,
            availability.EffectiveTo,
            availability.PolicySemanticHash
        };

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
