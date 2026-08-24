using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsoleOperatingContextServiceTests
{
    private const string Proof = "operator-console-proof-with-more-than-32-characters";
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid SessionId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid ShiftId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteId = Guid.Parse("10000000-0000-0000-0000-000000000005");
    private static readonly Guid SiteGroupId = Guid.Parse("10000000-0000-0000-0000-000000000006");
    private static readonly Guid CorrelationId = Guid.Parse("10000000-0000-0000-0000-000000000007");

    [Fact]
    public async Task BindSession_WithValidDeviceActiveShiftAndSiteGroup_PersistsServerOwnedContext()
    {
        var repository = ValidRepository();
        var service = Create(repository);
        repository.Device = repository.Device! with { AssignmentSiteId = SiteId, AssignmentSiteGroupId = SiteGroupId };

        var result = await service.BindSessionAsync(SessionId, UserId, [], [SiteGroupId], false, Proof, CorrelationId, default);

        result.Succeeded.Should().BeTrue();
        result.Context.Should().NotBeNull();
        result.Context!.OperatorDeviceBindingId.Should().Be(DeviceId);
        result.Context.OperatorShiftId.Should().Be(ShiftId);
        repository.BoundContext.Should().Be(result.Context);
    }

    [Fact]
    public async Task EstablishDeviceBinding_ReplacesProvisioningProofWithServerGeneratedCookieCredential()
    {
        var repository = ValidRepository();
        var service = Create(repository);

        var result = await service.EstablishDeviceBindingAsync(Proof, CorrelationId, default);

        result.Succeeded.Should().BeTrue();
        result.CookieCredential.Should().NotBeNullOrWhiteSpace().And.NotBe(Proof);
        result.CookieCredential!.Length.Should().BeGreaterThanOrEqualTo(32);
        repository.ReplacementThumbprint.Should().Be(service.HashDeviceProof(result.CookieCredential));
        repository.ExpectedThumbprint.Should().Be(service.HashDeviceProof(Proof));
    }

    [Theory]
    [InlineData(null, OperatorConsoleOperatingContextFailureCodes.DeviceBindingRequired)]
    [InlineData("too-short", OperatorConsoleOperatingContextFailureCodes.DeviceBindingRequired)]
    public async Task BindSession_WithMissingOrMalformedProof_FailsClosed(string? proof, string expected)
    {
        var result = await Create(ValidRepository()).BindSessionAsync(SessionId, UserId, [SiteId], [], false, proof, CorrelationId, default);
        result.ErrorCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("REVOKED", OperatorConsoleOperatingContextFailureCodes.DeviceBindingRevoked)]
    [InlineData("SUSPENDED", OperatorConsoleOperatingContextFailureCodes.DeviceBindingRevoked)]
    [InlineData("EXPIRED", OperatorConsoleOperatingContextFailureCodes.DeviceBindingExpired)]
    [InlineData("PENDING", OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid)]
    public async Task BindSession_WithInactiveDevice_ReturnsControlledClassification(string status, string expected)
    {
        var repository = ValidRepository();
        repository.Device = repository.Device! with { DeviceStatus = status };
        var result = await Create(repository).BindSessionAsync(SessionId, UserId, [SiteId], [], false, Proof, CorrelationId, default);
        result.ErrorCode.Should().Be(expected);
    }

    [Fact]
    public async Task BindSession_WithExpiredDeviceCredential_FailsClosed()
    {
        var repository = ValidRepository();
        repository.Device = repository.Device! with { CredentialExpiresAt = Now };

        var result = await Create(repository).BindSessionAsync(SessionId, UserId, [SiteId], [], false, Proof, CorrelationId, default);

        result.ErrorCode.Should().Be(OperatorConsoleOperatingContextFailureCodes.DeviceBindingExpired);
    }

    [Fact]
    public async Task BindSession_WithUnknownOrDuplicatedProof_FailsAsInvalid()
    {
        var repository = ValidRepository();
        repository.Device = repository.Device! with { MatchingProofCount = 2 };
        var duplicated = await Create(repository).BindSessionAsync(SessionId, UserId, [SiteId], [], false, Proof, CorrelationId, default);
        repository.Device = null;
        var unknown = await Create(repository).BindSessionAsync(SessionId, UserId, [SiteId], [], false, Proof, CorrelationId, default);
        duplicated.ErrorCode.Should().Be(OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid);
        unknown.ErrorCode.Should().Be(OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid);
    }

    [Fact]
    public async Task BindSession_WithWrongSiteDevice_FailsBeforeShiftResolution()
    {
        var result = await Create(ValidRepository()).BindSessionAsync(SessionId, UserId, [Guid.NewGuid()], [], false, Proof, CorrelationId, default);
        result.ErrorCode.Should().Be(OperatorConsoleOperatingContextFailureCodes.DeviceOutsideAuthorizedSite);
    }

    [Fact]
    public async Task BindSession_WithInvalidCanonicalSiteGroupRelationship_FailsClosed()
    {
        var repository = ValidRepository();
        repository.Device = repository.Device! with { HasCanonicalSiteGroupRelationship = false };

        var result = await Create(repository).BindSessionAsync(SessionId, UserId, [SiteId], [], false, Proof, CorrelationId, default);

        result.ErrorCode.Should().Be(OperatorConsoleOperatingContextFailureCodes.DeviceOutsideAuthorizedSite);
    }

    [Theory]
    [InlineData(0, false, false, false, OperatorConsoleOperatingContextFailureCodes.ActiveShiftRequired)]
    [InlineData(2, false, false, false, OperatorConsoleOperatingContextFailureCodes.ActiveShiftConflict)]
    [InlineData(0, true, false, false, OperatorConsoleOperatingContextFailureCodes.ShiftClosedOrExpired)]
    [InlineData(0, false, true, false, OperatorConsoleOperatingContextFailureCodes.ShiftIncompatibleWithDevice)]
    [InlineData(0, false, false, true, OperatorConsoleOperatingContextFailureCodes.ShiftOutsideUserScope)]
    public async Task BindSession_ClassifiesShiftResolutionFailures(
        int count,
        bool closed,
        bool outsideDevice,
        bool outsideScope,
        string expected)
    {
        var repository = ValidRepository();
        repository.Shift = new OperatorConsoleShiftResolution(count, count == 1 ? ShiftId : null, closed, outsideDevice, outsideScope);
        var result = await Create(repository).BindSessionAsync(SessionId, UserId, [SiteId], [], false, Proof, CorrelationId, default);
        result.ErrorCode.Should().Be(expected);
    }

    [Fact]
    public async Task ValidateSession_WithLiveContext_SucceedsAndTouchesContext()
    {
        var repository = ValidRepository();
        var service = Create(repository);
        repository.Validation = ValidValidation(service.HashDeviceProof(Proof));
        var result = await service.ValidateSessionAsync(SessionId, Proof, CorrelationId, default);
        result.Succeeded.Should().BeTrue();
        repository.Touched.Should().BeTrue();
        repository.InvalidatedWith.Should().BeNull();
    }

    [Theory]
    [InlineData("REVOKED", "ACTIVE", 4L, OperatorConsoleOperatingContextFailureCodes.DeviceBindingRevoked)]
    [InlineData("ACTIVE", "ENDED", 4L, OperatorConsoleOperatingContextFailureCodes.ShiftClosedOrExpired)]
    [InlineData("ACTIVE", "ACTIVE", 5L, OperatorConsoleOperatingContextFailureCodes.StaleAuthorizationEpoch)]
    public async Task ValidateSession_WhenLiveAuthorityChanges_InvalidatesExistingContext(
        string deviceStatus,
        string shiftStatus,
        long currentEpoch,
        string expected)
    {
        var repository = ValidRepository();
        var service = Create(repository);
        repository.Validation = ValidValidation(service.HashDeviceProof(Proof)) with
        {
            DeviceStatus = deviceStatus,
            ShiftStatus = shiftStatus,
            CurrentAuthorizationEpoch = currentEpoch
        };
        var result = await service.ValidateSessionAsync(SessionId, Proof, CorrelationId, default);
        result.ErrorCode.Should().Be(expected);
        repository.InvalidatedWith.Should().Be(expected);
    }

    [Theory]
    [InlineData("SESSION_REVOKED", OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked)]
    [InlineData("CREDENTIAL_CHANGED", OperatorConsoleOperatingContextFailureCodes.SessionExpiredOrRevoked)]
    [InlineData("SCOPE_REMOVED", OperatorConsoleOperatingContextFailureCodes.DeviceOutsideAuthorizedSite)]
    [InlineData("WRONG_USER_SHIFT", OperatorConsoleOperatingContextFailureCodes.ShiftOutsideUserScope)]
    [InlineData("WRONG_DEVICE_SHIFT", OperatorConsoleOperatingContextFailureCodes.ShiftIncompatibleWithDevice)]
    [InlineData("DUPLICATE_ASSIGNMENT", OperatorConsoleOperatingContextFailureCodes.DeviceOutsideAuthorizedSite)]
    [InlineData("SITE_GROUP_RELATIONSHIP_CHANGED", OperatorConsoleOperatingContextFailureCodes.DeviceOutsideAuthorizedSite)]
    public async Task ValidateSession_WhenCanonicalRelationshipChanges_InvalidatesExistingContext(
        string scenario,
        string expected)
    {
        var repository = ValidRepository();
        var service = Create(repository);
        var facts = ValidValidation(service.HashDeviceProof(Proof));
        repository.Validation = scenario switch
        {
            "SESSION_REVOKED" => facts with { SessionStatus = "REVOKED" },
            "CREDENTIAL_CHANGED" => facts with { CurrentCredentialVersion = facts.CurrentCredentialVersion + 1 },
            "SCOPE_REMOVED" => facts with { HasEffectiveSiteScope = false },
            "WRONG_USER_SHIFT" => facts with { ShiftUserId = Guid.NewGuid() },
            "WRONG_DEVICE_SHIFT" => facts with { ShiftSiteId = Guid.NewGuid() },
            "DUPLICATE_ASSIGNMENT" => facts with { ActiveAssignmentCount = 2 },
            "SITE_GROUP_RELATIONSHIP_CHANGED" => facts with { HasCanonicalSiteGroupRelationship = false },
            _ => throw new InvalidOperationException("Unsupported scenario.")
        };

        var result = await service.ValidateSessionAsync(SessionId, Proof, CorrelationId, default);

        result.ErrorCode.Should().Be(expected);
        repository.InvalidatedWith.Should().Be(expected);
    }

    [Fact]
    public async Task ValidateSession_WithBrowserAuthoredDifferentProof_HasNoDeviceAuthority()
    {
        var repository = ValidRepository();
        var service = Create(repository);
        repository.Validation = ValidValidation(service.HashDeviceProof(Proof));
        var result = await service.ValidateSessionAsync(SessionId, "different-browser-proof-with-more-than-32-characters", CorrelationId, default);
        result.ErrorCode.Should().Be(OperatorConsoleOperatingContextFailureCodes.DeviceBindingInvalid);
    }

    private static OperatorConsoleOperatingContextService Create(FakeRepository repository) =>
        new(repository, new FixedTimeProvider(Now), NullLogger<OperatorConsoleOperatingContextService>.Instance);

    private static FakeRepository ValidRepository() => new()
    {
        Device = new OperatorConsoleDeviceBindingCandidate(DeviceId, "ACTIVE", "BROWSER_KEY_ONLY", SiteId, SiteGroupId, true, null, 1, 1, SiteId, SiteGroupId),
        Shift = new OperatorConsoleShiftResolution(1, ShiftId, false, false, false),
        Snapshot = new OperatorConsoleSessionBindingSnapshot(4, 7, "ACTIVE", Now.AddMinutes(30), Now.AddHours(8))
    };

    private static OperatorConsoleOperatingContextValidationFacts ValidValidation(string proofThumbprint)
    {
        var context = new OperatorConsoleOperatingContext(SessionId, UserId, DeviceId, ShiftId, SiteId, SiteGroupId, 4, 7, Now.AddMinutes(-1), CorrelationId);
        return new OperatorConsoleOperatingContextValidationFacts(
            context, "ACTIVE", proofThumbprint, "ACTIVE", "BROWSER_KEY_ONLY", null,
            1, SiteId, SiteGroupId, "ACTIVE", UserId, SiteId, SiteGroupId,
            Now.AddHours(-1), Now.AddHours(1), null, "ACTIVE", Now.AddMinutes(30),
            Now.AddHours(8), 4, 7, true, true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeRepository : IOperatorConsoleOperatingContextRepository
    {
        public OperatorConsoleDeviceBindingCandidate? Device { get; set; }
        public OperatorConsoleShiftResolution Shift { get; set; } = new(0, null, false, false, false);
        public OperatorConsoleSessionBindingSnapshot? Snapshot { get; set; }
        public OperatorConsoleOperatingContextValidationFacts? Validation { get; set; }
        public OperatorConsoleOperatingContext? BoundContext { get; private set; }
        public string? InvalidatedWith { get; private set; }
        public bool Touched { get; private set; }
        public string? ExpectedThumbprint { get; private set; }
        public string? ReplacementThumbprint { get; private set; }

        public Task<OperatorConsoleDeviceBindingCandidate?> FindDeviceByProofAsync(string proofThumbprint, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(Device);
        public Task<bool> RotateDeviceProofAsync(Guid operatorDeviceBindingId, string expectedThumbprint, string replacementThumbprint, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken)
        {
            ExpectedThumbprint = expectedThumbprint;
            ReplacementThumbprint = replacementThumbprint;
            return Task.FromResult(true);
        }
        public Task<OperatorConsoleShiftResolution> ResolveShiftAsync(Guid userId, Guid siteId, Guid siteGroupId, IReadOnlyList<Guid> authorizedSiteIds, IReadOnlyList<Guid> authorizedSiteGroupIds, bool hasGlobalScope, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(Shift);
        public Task<OperatorConsoleSessionBindingSnapshot?> ReadSessionBindingSnapshotAsync(Guid humanSessionId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(Snapshot);

        public Task<OperatorConsoleOperatingContext> BindSessionAsync(Guid humanSessionId, Guid userId, Guid operatorDeviceBindingId, Guid operatorShiftId, Guid siteId, Guid siteGroupId, long authorizationEpoch, long credentialVersion, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken)
        {
            BoundContext = new OperatorConsoleOperatingContext(humanSessionId, userId, operatorDeviceBindingId, operatorShiftId, siteId, siteGroupId, authorizationEpoch, credentialVersion, now, correlationId);
            return Task.FromResult(BoundContext);
        }

        public Task<OperatorConsoleOperatingContextValidationFacts> ReadValidationFactsAsync(Guid humanSessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Validation ?? throw new InvalidOperationException("Validation facts were not configured."));

        public Task InvalidateAsync(Guid humanSessionId, string reasonCode, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken)
        {
            InvalidatedWith = reasonCode;
            return Task.CompletedTask;
        }

        public Task TouchAsync(Guid humanSessionId, DateTimeOffset now, Guid correlationId, CancellationToken cancellationToken)
        {
            Touched = true;
            return Task.CompletedTask;
        }
    }
}
