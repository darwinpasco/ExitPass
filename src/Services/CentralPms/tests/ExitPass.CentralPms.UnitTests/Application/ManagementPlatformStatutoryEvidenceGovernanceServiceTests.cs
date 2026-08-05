using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class ManagementPlatformStatutoryEvidenceGovernanceServiceTests
{
    private static readonly Guid UserId = Guid.Parse("91400000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("91400000-0000-0000-0000-000000000101");
    private static readonly Guid SiteGroupId = Guid.Parse("91400000-0000-0000-0000-000000000201");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-04T08:00:00Z");

    [Fact]
    public void PolicyCatalog_MapsGovernancePolicyOnlyToDedicatedPermission()
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementPlatformStatutoryEvidenceGovernanceValues.PolicyName)
            .Should()
            .Equal(ManagementPlatformStatutoryEvidenceGovernanceValues.Permission);

        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementPlatformStatutoryEvidenceGovernanceValues.PolicyName)
            .Should()
            .NotContain([
                "statutory-discounts.evidence.capture",
                "statutory-discounts.evidence.view",
                "statutory-discounts.evidence.hold",
                "statutory-discounts.evidence.delete-request",
                "statutory-discount-policy.view",
                "reconciliation.manage"
            ]);
    }

    [Fact]
    public void GovernanceDtos_DoNotExposeCustomerEvidenceStorageOrPaymentFields()
    {
        var names = new[]
        {
            typeof(ManagementPlatformStatutoryEvidenceGovernanceResponse),
            typeof(ManagementPlatformStatutoryEvidenceGovernanceSiteDto),
            typeof(ManagementPlatformStatutoryEvidenceDocumentProfileDto)
        }
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        names.Should().NotContain(name => name.Contains("EvidenceSet", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("EvidenceItem", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Decision", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("ParkingSession", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("UploadUrl", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("ObjectKey", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Bucket", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("ChecksumValue", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("DeclaredChecksum", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("VerifiedChecksum", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Payment", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Reviewer", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Plate", StringComparison.OrdinalIgnoreCase));
        names.Should().NotContain(name => name.Contains("Ticket", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadGovernanceAsync_WhenUploadStorageRetentionAndCaptureConfigured_ReturnsPartialReadyBecauseScanWorkerIsDisabled()
    {
        var repository = new FakeRepository()
            .WithScope(Scope(Site()))
            .WithConfiguration(Configuration(captureEnabled: true, updatedAt: Now));
        var service = CreateService(repository, ValidUploadOptions());

        var result = await service.ReadGovernanceAsync(Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceOutcome.Success);
        var site = result.Governance!.Sites.Single();
        site.GovernanceStatus.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.ConfiguredPartiallyReady);
        site.ReadinessStatus.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.PartiallyReady);
        site.UploadAuthorizationReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Ready);
        site.UploadFinalizationReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Ready);
        site.ProtectedStorageReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Ready);
        site.ChecksumVerificationReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Ready);
        site.MalwareScanLifecycleReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Ready);
        site.MalwareScanningExecutionReadiness.Should().Be(StatutoryEvidenceScanConstants.WorkerDisabled);
        site.SecurePreviewReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented);
        site.RetentionWorkerReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented);
        site.DeletionWorkerReadiness.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotImplemented);
        site.Blockers.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadGovernanceAsync_WhenMaximumSizeMissing_ReturnsIncomplete()
    {
        var options = ValidUploadOptions();
        options.MaxContentLengthBytes = 0;
        var service = CreateService(
            new FakeRepository().WithScope(Scope(Site())).WithConfiguration(Configuration(captureEnabled: true)),
            options);

        var result = await service.ReadGovernanceAsync(Query(), CancellationToken.None);

        var site = result.Governance!.Sites.Single();
        site.GovernanceStatus.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.ConfigurationIncomplete);
        site.ReadinessStatus.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.NotConfigured);
        site.Warnings.Should().Contain(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningMaximumSizeNotConfigured);
        site.Blockers.Should().Contain(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningUploadProfileIncomplete);
    }

    [Fact]
    public async Task ReadGovernanceAsync_WhenCaptureNotEnabled_ReturnsCaptureDisabled()
    {
        var service = CreateService(
            new FakeRepository().WithScope(Scope(Site())).WithConfiguration(Configuration(captureEnabled: false)),
            ValidUploadOptions());

        var result = await service.ReadGovernanceAsync(Query(), CancellationToken.None);

        var site = result.Governance!.Sites.Single();
        site.GovernanceStatus.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.CaptureDisabled);
        site.ReadinessStatus.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.Disabled);
        site.EvidenceCaptureEnabled.Should().BeFalse();
        site.Warnings.Should().Contain(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningCaptureDisabled);
    }

    [Fact]
    public async Task ReadGovernanceAsync_WhenConfigurationIsOlderThanThreshold_MarksStale()
    {
        var service = CreateService(
            new FakeRepository().WithScope(Scope(Site())).WithConfiguration(Configuration(captureEnabled: true, updatedAt: Now.AddDays(-2))),
            ValidUploadOptions());

        var result = await service.ReadGovernanceAsync(Query(includeStale: true), CancellationToken.None);

        result.Governance!.Stale.Should().BeTrue();
        result.Governance.FreshnessStatus.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceValues.StaleFreshness);
        result.Governance.Sites.Single().Warnings.Should().Contain(ManagementPlatformStatutoryEvidenceGovernanceValues.WarningConfigurationStale);
    }

    [Theory]
    [InlineData("statutory-discounts.evidence.capture")]
    [InlineData("statutory-discounts.evidence.view")]
    [InlineData("statutory-discounts.evidence.hold")]
    [InlineData("statutory-discounts.evidence.delete-request")]
    [InlineData("statutory-discount-policy.view")]
    public void DedicatedPermission_IsNotSatisfiedByRelatedStatutoryPermissions(string unrelatedPermission)
    {
        CentralPmsRbacPolicyCatalog.ResolvePermissions(ManagementPlatformStatutoryEvidenceGovernanceValues.PolicyName)
            .Should()
            .NotContain(unrelatedPermission);
    }

    [Fact]
    public async Task ReadGovernanceAsync_WhenScopeDenied_DoesNotReadConfiguration()
    {
        var repository = new FakeRepository()
            .WithScope(new ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult(
                ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Denied,
                Sites: []));
        var service = CreateService(repository, ValidUploadOptions());

        var result = await service.ReadGovernanceAsync(Query(), CancellationToken.None);

        result.Outcome.Should().Be(ManagementPlatformStatutoryEvidenceGovernanceOutcome.ScopeDenied);
        repository.ConfigurationReadCount.Should().Be(0);
    }

    private static ManagementPlatformStatutoryEvidenceGovernanceService CreateService(
        FakeRepository repository,
        StatutoryEvidenceUploadOptions options) =>
        new(repository, options, new StatutoryEvidenceScanWorkerOptions(), new FixedTimeProvider(Now));

    private static ManagementPlatformStatutoryEvidenceGovernanceQuery Query(bool includeStale = true) =>
        new(
            ManagementPlatformStatutoryEvidenceGovernanceValues.ScopeTypeSite,
            SiteId,
            EntitlementType: null,
            GovernanceStatus: null,
            ReadinessStatus: null,
            CaptureEnabled: null,
            IncludeStale: includeStale,
            Guid.Parse("91400000-0000-0000-0000-000000000301"),
            UserId,
            ActorServiceIdentityId: null);

    private static ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult Scope(
        params ManagementPlatformStatutoryEvidenceGovernanceScopeSite[] sites) =>
        new(ManagementPlatformStatutoryEvidenceGovernanceScopeReadStatus.Resolved, sites);

    private static ManagementPlatformStatutoryEvidenceGovernanceScopeSite Site() =>
        new(SiteId, "Synthetic Site", SiteGroupId, "Synthetic Group");

    private static ManagementPlatformStatutoryEvidenceGovernanceConfiguration Configuration(
        bool captureEnabled,
        DateTimeOffset? updatedAt = null) =>
        new(
            [
                new ManagementPlatformStatutoryEvidenceDocumentProfile(
                    "STATUTORY_ID",
                    "v1",
                    "STATUTORY_ID",
                    "v1",
                    "APPROVED_ENABLED",
                    RetentionPolicyApproved: true)
            ],
            captureEnabled ? new HashSet<Guid> { SiteId } : new HashSet<Guid>(),
            HasMetadataTables: true,
            HasUploadAuthorizationTable: true,
            LastConfigurationUpdatedAt: updatedAt ?? Now);

    private static StatutoryEvidenceUploadOptions ValidUploadOptions() =>
        new()
        {
            ProviderType = StatutoryEvidenceUploadConstants.ProviderTypeS3Compatible,
            Endpoint = "https://storage.internal",
            PublicUploadEndpoint = "https://upload.internal",
            Region = "ap-southeast-1",
            BucketName = "not-returned",
            BucketReference = "protected-evidence",
            AccessKeyId = "test-access-key-id",
            SecretAccessKey = "test-secret-signing-material",
            MaxContentLengthBytes = 1_048_576,
            AuthorizationTtlSeconds = 300,
            AllowedContentTypes = ["image/jpeg", "image/png"],
            RequireSha256Checksum = true
        };

    private sealed class FakeRepository : IManagementPlatformStatutoryEvidenceGovernanceRepository
    {
        private ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult _scope = Scope(Site());
        private ManagementPlatformStatutoryEvidenceGovernanceConfiguration _configuration = Configuration(captureEnabled: true);

        public int ConfigurationReadCount { get; private set; }

        public FakeRepository WithScope(ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult scope)
        {
            _scope = scope;
            return this;
        }

        public FakeRepository WithConfiguration(ManagementPlatformStatutoryEvidenceGovernanceConfiguration configuration)
        {
            _configuration = configuration;
            return this;
        }

        public Task<ManagementPlatformStatutoryEvidenceGovernanceScopeReadResult> ResolveScopeAsync(
            ManagementPlatformStatutoryEvidenceGovernanceActor actor,
            string? scopeType,
            Guid? scopeReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(_scope);

        public Task<ManagementPlatformStatutoryEvidenceGovernanceConfiguration> ReadConfigurationAsync(
            IReadOnlyList<ManagementPlatformStatutoryEvidenceGovernanceScopeSite> sites,
            CancellationToken cancellationToken)
        {
            ConfigurationReadCount++;
            return Task.FromResult(_configuration);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
