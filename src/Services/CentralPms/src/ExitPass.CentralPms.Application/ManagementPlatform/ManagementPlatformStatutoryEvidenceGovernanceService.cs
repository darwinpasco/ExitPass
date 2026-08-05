using ExitPass.CentralPms.Application.StatutoryEvidence;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementPlatformStatutoryEvidenceGovernanceService
    : IManagementPlatformStatutoryEvidenceGovernanceService
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(24);

    private readonly IManagementPlatformStatutoryEvidenceGovernanceRepository _repository;
    private readonly StatutoryEvidenceUploadOptions _uploadOptions;
    private readonly StatutoryEvidenceScanWorkerOptions _scanWorkerOptions;
    private readonly TimeProvider _timeProvider;

    public ManagementPlatformStatutoryEvidenceGovernanceService(
        IManagementPlatformStatutoryEvidenceGovernanceRepository repository,
        StatutoryEvidenceUploadOptions uploadOptions,
        StatutoryEvidenceScanWorkerOptions scanWorkerOptions,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _uploadOptions = uploadOptions;
        _scanWorkerOptions = scanWorkerOptions;
        _timeProvider = timeProvider;
    }

    public async Task<ManagementPlatformStatutoryEvidenceGovernanceResult> ReadGovernanceAsync(
        ManagementPlatformStatutoryEvidenceGovernanceQuery query,
        CancellationToken cancellationToken)
    {
        var normalizedScopeType = NormalizeScopeType(query.ScopeType);
        if (normalizedScopeType is not null && !IsSupportedScopeType(normalizedScopeType))
        {
            return Fail(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.InvalidFilter,
                query.CorrelationId,
                ManagementPlatformStatutoryEvidenceGovernanceValues.InvalidFilter,
                "The requested evidence-governance scope filter is not supported.");
        }

        if (query.ScopeReference == Guid.Empty)
        {
            return Fail(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.InvalidFilter,
                query.CorrelationId,
                ManagementPlatformStatutoryEvidenceGovernanceValues.InvalidFilter,
                "The requested evidence-governance scope reference is invalid.");
        }

        if (!IsSupportedEntitlement(query.EntitlementType))
        {
            return Fail(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.InvalidFilter,
                query.CorrelationId,
                ManagementPlatformStatutoryEvidenceGovernanceValues.InvalidFilter,
                "The requested evidence-governance entitlement filter is not supported.");
        }

        var scope = await _repository.ResolveScopeAsync(
            new ManagementPlatformStatutoryEvidenceGovernanceActor(query.ActorUserId, query.ActorServiceIdentityId),
            normalizedScopeType,
            query.ScopeReference,
            cancellationToken);

        var scopeFailure = MapScopeFailure(scope.Status, query.CorrelationId, normalizedScopeType);
        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        var now = _timeProvider.GetUtcNow();
        var configuration = await _repository.ReadConfigurationAsync(scope.Sites, cancellationToken);
        var sites = scope.Sites
            .Select(site => BuildSite(site, configuration, query, now))
            .Where(site => MatchesFilters(site, query))
            .ToArray();

        var globalWarnings = sites.SelectMany(site => site.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var globalBlockers = sites.SelectMany(site => site.Blockers).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var stale = sites.Any(site => site.Stale);

        return ManagementPlatformStatutoryEvidenceGovernanceResult.Success(
            new ManagementPlatformStatutoryEvidenceGovernance(
                ManagementPlatformStatutoryEvidenceGovernanceValues.ContractVersion,
                normalizedScopeType,
                query.ScopeReference,
                query.CorrelationId,
                now,
                stale ? ManagementPlatformStatutoryEvidenceGovernanceValues.StaleFreshness : ManagementPlatformStatutoryEvidenceGovernanceValues.Fresh,
                stale,
                sites,
                globalWarnings,
                globalBlockers));
    }

    private ManagementPlatformStatutoryEvidenceGovernanceSite BuildSite(
        ManagementPlatformStatutoryEvidenceGovernanceScopeSite site,
        ManagementPlatformStatutoryEvidenceGovernanceConfiguration configuration,
        ManagementPlatformStatutoryEvidenceGovernanceQuery query,
        DateTimeOffset now)
    {
        var warnings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedMediaTypes = _uploadOptions.AllowedContentTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var captureEnabled = configuration.CaptureEnabledSiteIds.Contains(site.SiteId);
        var retentionReady = configuration.RetentionPolicies.Any(policy => policy.RetentionPolicyApproved);
        var uploadProfileReady = allowedMediaTypes.Length > 0 && _uploadOptions.MaxContentLengthBytes > 0 && _uploadOptions.AuthorizationTtlSeconds > 0;
        var storageReady = IsStorageConfigured();
        var checksumReady = _uploadOptions.RequireSha256Checksum;
        var uploadReady = configuration.HasUploadAuthorizationTable && uploadProfileReady && storageReady && checksumReady;
        var stale = configuration.LastConfigurationUpdatedAt is not null &&
            now - configuration.LastConfigurationUpdatedAt.Value > StaleThreshold;

        if (!captureEnabled)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningCaptureDisabled);
        }

        if (allowedMediaTypes.Length == 0)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningAllowedMediaNotConfigured);
        }

        if (_uploadOptions.MaxContentLengthBytes <= 0)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningMaximumSizeNotConfigured);
        }

        if (_uploadOptions.AuthorizationTtlSeconds <= 0)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningUploadTtlInvalid);
        }

        if (!storageReady)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningProtectedStorageNotConfigured);
        }

        if (!_uploadOptions.RequireSha256Checksum)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningChecksumConfigurationIncomplete);
        }

        if (!retentionReady)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningRetentionPolicyUnavailable);
        }

        if (_uploadOptions.RequireServerSideEncryptionMetadata)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningEncryptionPostureUnknown);
        }

        warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningStoragePrivacyUnverified);
        var scanWorkerReadiness = _scanWorkerOptions.Readiness();
        if (scanWorkerReadiness is not ManagementPlatformStatutoryEvidenceGovernanceValues.Ready)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningMalwareScanningNotImplemented);
        }

        warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningSecurePreviewNotImplemented);
        warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningRetentionWorkerNotImplemented);
        warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningDeletionWorkerNotImplemented);
        warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningObjectReconciliationNotImplemented);

        if (stale)
        {
            warnings.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningConfigurationStale);
        }

        if (!uploadProfileReady)
        {
            blockers.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningUploadProfileIncomplete);
        }

        if (!storageReady)
        {
            blockers.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningProtectedStorageNotConfigured);
        }

        if (!retentionReady)
        {
            blockers.Add(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningRetentionPolicyUnavailable);
        }

        var governanceStatus = ResolveGovernanceStatus(captureEnabled, uploadReady, retentionReady, blockers.Count);
        var readinessStatus = ResolveReadinessStatus(uploadReady, captureEnabled, blockers.Count);

        return new ManagementPlatformStatutoryEvidenceGovernanceSite(
            site.SiteId,
            site.SiteName,
            site.SiteGroupId,
            site.SiteGroupName,
            ResolveEntitlementTypes(query.EntitlementType),
            governanceStatus,
            readinessStatus,
            EvidenceCaptureConfigured: retentionReady || captureEnabled,
            EvidenceCaptureEnabled: captureEnabled,
            configuration.RetentionPolicies,
            allowedMediaTypes,
            _uploadOptions.MaxContentLengthBytes > 0 ? _uploadOptions.MaxContentLengthBytes : null,
            _uploadOptions.AuthorizationTtlSeconds > 0 ? _uploadOptions.AuthorizationTtlSeconds : null,
            Capability(uploadReady),
            Capability(uploadReady),
            string.IsNullOrWhiteSpace(_uploadOptions.ProviderType) ? ManagementPlatformStatutoryEvidenceGovernanceValues.Unknown : _uploadOptions.ProviderType.Trim().ToUpperInvariant(),
            Capability(storageReady),
            storageReady ? "PRIVATE_ACCESS_REQUIRED" : ManagementPlatformStatutoryEvidenceGovernanceValues.NotConfigured,
            _uploadOptions.RequireServerSideEncryptionMetadata ? "REQUIRED_UNVERIFIED" : "NOT_CONFIGURED",
            Capability(checksumReady),
            Capability(storageReady && checksumReady),
            Capability(configuration.HasMetadataTables),
            Capability(configuration.HasMetadataTables),
            Capability(configuration.HasMetadataTables),
            Capability(configuration.HasMetadataTables),
            Capability(configuration.HasMetadataTables),
            Capability(configuration.HasMetadataTables),
            Capability(configuration.HasMetadataTables),
            scanWorkerReadiness,
            ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented,
            Capability(retentionReady),
            ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented,
            ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented,
            ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented,
            now,
            configuration.LastConfigurationUpdatedAt,
            stale ? ManagementPlatformStatutoryEvidenceGovernanceValues.StaleFreshness : ManagementPlatformStatutoryEvidenceGovernanceValues.Fresh,
            stale,
            Retryable: !storageReady,
            $"I014-{query.CorrelationId:D}",
            warnings.ToArray(),
            blockers.ToArray());
    }

    private bool IsStorageConfigured() =>
        string.Equals(_uploadOptions.ProviderType, StatutoryEvidenceUploadConstants.ProviderTypeS3Compatible, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(_uploadOptions.Endpoint) &&
        !string.IsNullOrWhiteSpace(_uploadOptions.PublicUploadEndpoint) &&
        !string.IsNullOrWhiteSpace(_uploadOptions.Region) &&
        !string.IsNullOrWhiteSpace(_uploadOptions.BucketName) &&
        !string.IsNullOrWhiteSpace(_uploadOptions.BucketReference) &&
        !string.IsNullOrWhiteSpace(_uploadOptions.AccessKeyId) &&
        !string.IsNullOrWhiteSpace(_uploadOptions.SecretAccessKey);

    private static bool MatchesFilters(
        ManagementPlatformStatutoryEvidenceGovernanceSite site,
        ManagementPlatformStatutoryEvidenceGovernanceQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.GovernanceStatus) &&
            !string.Equals(site.GovernanceStatus, NormalizeCode(query.GovernanceStatus), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.ReadinessStatus) &&
            !string.Equals(site.ReadinessStatus, NormalizeCode(query.ReadinessStatus), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.CaptureEnabled is not null && site.EvidenceCaptureEnabled != query.CaptureEnabled.Value)
        {
            return false;
        }

        if (!query.IncludeStale && site.Stale)
        {
            return false;
        }

        return true;
    }

    private static string ResolveGovernanceStatus(bool captureEnabled, bool uploadReady, bool retentionReady, int blockerCount)
    {
        if (!captureEnabled)
        {
            return ManagementPlatformStatutoryEvidenceGovernanceValues.CaptureDisabled;
        }

        if (uploadReady && retentionReady && blockerCount == 0)
        {
            return ManagementPlatformStatutoryEvidenceGovernanceValues.ConfiguredPartiallyReady;
        }

        return blockerCount > 0
            ? ManagementPlatformStatutoryEvidenceGovernanceValues.ConfigurationIncomplete
            : ManagementPlatformStatutoryEvidenceGovernanceValues.ConfiguredPartiallyReady;
    }

    private static string ResolveReadinessStatus(bool uploadReady, bool captureEnabled, int blockerCount)
    {
        if (!captureEnabled)
        {
            return ManagementPlatformStatutoryEvidenceGovernanceValues.Disabled;
        }

        return uploadReady && blockerCount == 0
            ? ManagementPlatformStatutoryEvidenceGovernanceValues.PartiallyReady
            : ManagementPlatformStatutoryEvidenceGovernanceValues.NotConfigured;
    }

    private static string Capability(bool ready) =>
        ready
            ? ManagementPlatformStatutoryEvidenceGovernanceValues.Ready
            : ManagementPlatformStatutoryEvidenceGovernanceValues.NotConfigured;

    private static IReadOnlyList<string> ResolveEntitlementTypes(string? entitlementType) =>
        string.IsNullOrWhiteSpace(entitlementType)
            ? ManagementPlatformStatutoryEvidenceGovernanceValues.EntitlementTypes
            : [NormalizeCode(entitlementType)];

    private static bool IsSupportedEntitlement(string? entitlementType) =>
        string.IsNullOrWhiteSpace(entitlementType) ||
        ManagementPlatformStatutoryEvidenceGovernanceValues.EntitlementTypes.Contains(NormalizeCode(entitlementType), StringComparer.OrdinalIgnoreCase);

    private static string? NormalizeScopeType(string? scopeType) =>
        string.IsNullOrWhiteSpace(scopeType) ? null : NormalizeCode(scopeType);

    private static string NormalizeCode(string value) =>
        value.Trim().Replace('-', '_').ToUpperInvariant();

    private static bool IsSupportedScopeType(string normalizedScopeType) =>
        string.Equals(normalizedScopeType, ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSite, StringComparison.Ordinal) ||
        string.Equals(normalizedScopeType, ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSiteGroup, StringComparison.Ordinal);

    private static ManagementPlatformStatutoryEvidenceGovernanceResult? MapScopeFailure(
        ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus status,
        Guid correlationId,
        string? scopeType) =>
        status switch
        {
            ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Resolved => null,
            ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Empty => Fail(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.EmptyAuthorizedScope,
                correlationId,
                ManagementPlatformStatutoryEvidenceGovernanceValues.EmptyAuthorizedScope,
                "The caller has no authorized statutory evidence governance scope."),
            ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Denied => Fail(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.ScopeDenied,
                correlationId,
                string.Equals(scopeType, ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSiteGroup, StringComparison.Ordinal)
                    ? ManagementPlatformStatutoryEvidenceGovernanceValues.SiteGroupScopeDenied
                    : ManagementPlatformStatutoryEvidenceGovernanceValues.SiteScopeDenied,
                "The caller is not authorized for the requested statutory evidence governance scope."),
            ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.SourceUnavailable => Fail(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.ConfigurationUnavailable,
                correlationId,
                ManagementPlatformStatutoryEvidenceGovernanceValues.ConfigurationSourceUnavailable,
                "The statutory evidence governance configuration source is unavailable.",
                retryable: true),
            ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Malformed => Fail(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.MalformedCanonicalConfiguration,
                correlationId,
                ManagementPlatformStatutoryEvidenceGovernanceValues.MalformedCanonicalConfiguration,
                "The statutory evidence governance configuration source returned malformed data."),
            _ => Fail(
                ManagementPlatformStatutoryEvidenceGovernanceOutcome.UnexpectedFailure,
                correlationId,
                ManagementPlatformStatutoryEvidenceGovernanceValues.UnexpectedFailure,
                "The statutory evidence governance read failed.")
        };

    private static ManagementPlatformStatutoryEvidenceGovernanceResult Fail(
        ManagementPlatformStatutoryEvidenceGovernanceOutcome outcome,
        Guid correlationId,
        string errorCode,
        string errorMessage,
        bool retryable = false) =>
        ManagementPlatformStatutoryEvidenceGovernanceResult.Failed(outcome, correlationId, errorCode, errorMessage, retryable);
}
