using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsoleCanonicalSessionServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T04:00:00Z");
    private static readonly Guid SiteId = Guid.Parse("31000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteGroupId = Guid.Parse("31000000-0000-0000-0000-000000000002");
    private static readonly Guid VendorSystemId = Guid.Parse("31000000-0000-0000-0000-000000000003");
    private static readonly Guid ProjectionId = Guid.Parse("31000000-0000-0000-0000-000000000004");
    private static readonly Guid AdapterId = Guid.Parse("31000000-0000-0000-0000-000000000005");
    private static readonly Guid SessionId = Guid.Parse("31000000-0000-0000-0000-000000000006");
    private static readonly Guid CorrelationId = Guid.Parse("31000000-0000-0000-0000-000000000007");

    [Fact]
    public async Task EnsureAsync_WithDirectSiteScope_ResolvesCanonicalSiteGroupAndPersistsSession()
    {
        var lookup = Substitute.For<IVendorSessionProjectionLookupService>();
        var repository = Substitute.For<IOperatorConsoleCanonicalSessionRepository>();
        lookup.LookupAsync(Arg.Any<VendorSessionProjectionLookupQuery>(), Arg.Any<CancellationToken>())
            .Returns(VendorSessionProjectionLookupResult.FoundProjection(Projection(), Now, Now, CorrelationId));
        repository.EnsureAsync(Arg.Any<OperatorConsoleCanonicalSessionPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleCanonicalSessionPersistenceResult(SessionId, ReusedExistingSession: false));

        var result = await CreateSut(lookup, repository).EnsureAsync(Command(), CancellationToken.None);

        result.ParkingSessionId.Should().Be(SessionId);
        result.ReusedExistingSession.Should().BeFalse();
        await lookup.Received(1).LookupAsync(
            Arg.Is<VendorSessionProjectionLookupQuery>(query =>
                query.CardNum == "TICKET-3101" &&
                query.PlateLicense == null &&
                query.SiteId == SiteId &&
                query.SiteGroupId == SiteGroupId &&
                query.VendorSystemId == VendorSystemId),
            Arg.Any<CancellationToken>());
        await repository.Received(1).EnsureAsync(
            Arg.Is<OperatorConsoleCanonicalSessionPersistenceCommand>(request =>
                request.Projection.VendorSessionProjectionId == ProjectionId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureAsync_WithoutSiteScope_FailsBeforeCanonicalResolution()
    {
        var lookup = Substitute.For<IVendorSessionProjectionLookupService>();
        var repository = Substitute.For<IOperatorConsoleCanonicalSessionRepository>();
        var configuration = ActiveSiteConfiguration();

        var action = () => CreateSut(lookup, repository, configuration).EnsureAsync(
            Command() with { SiteId = Guid.Empty },
            CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("SiteId is required.*");
        await configuration.DidNotReceiveWithAnyArgs().ListSitesAsync(default);
        await lookup.DidNotReceiveWithAnyArgs().LookupAsync(default!, default);
    }

    [Fact]
    public async Task EnsureAsync_WhenCanonicalActiveSiteGroupCannotBeResolved_FailsClosed()
    {
        var lookup = Substitute.For<IVendorSessionProjectionLookupService>();
        var repository = Substitute.For<IOperatorConsoleCanonicalSessionRepository>();
        var configuration = ActiveSiteConfiguration(includeSiteGroup: false);

        var action = () => CreateSut(lookup, repository, configuration)
            .EnsureAsync(Command(), CancellationToken.None);

        await action.Should().ThrowAsync<OperatorConsoleCanonicalSessionException>()
            .Where(exception => exception.ErrorCode == "OPERATOR_CANONICAL_SITE_SCOPE_UNRESOLVED");
        await lookup.DidNotReceiveWithAnyArgs().LookupAsync(default!, default);
        await repository.DidNotReceiveWithAnyArgs().EnsureAsync(default!, default);
    }

    [Fact]
    public async Task EnsureAsync_WithStaleProjection_FailsWithoutPersistence()
    {
        var lookup = Substitute.For<IVendorSessionProjectionLookupService>();
        var repository = Substitute.For<IOperatorConsoleCanonicalSessionRepository>();
        lookup.LookupAsync(Arg.Any<VendorSessionProjectionLookupQuery>(), Arg.Any<CancellationToken>())
            .Returns(VendorSessionProjectionLookupResult.FoundProjection(
                Projection(),
                Now.AddMinutes(-2),
                Now,
                CorrelationId));

        var action = () => CreateSut(lookup, repository).EnsureAsync(Command(), CancellationToken.None);

        await action.Should().ThrowAsync<OperatorConsoleCanonicalSessionException>()
            .Where(exception => exception.ErrorCode == "OPERATOR_SESSION_PROJECTION_STALE");
        await repository.DidNotReceiveWithAnyArgs().EnsureAsync(default!, default);
    }

    [Fact]
    public async Task EnsureAsync_WithMismatchedProjectionRoute_FailsWithoutPersistence()
    {
        var lookup = Substitute.For<IVendorSessionProjectionLookupService>();
        var repository = Substitute.For<IOperatorConsoleCanonicalSessionRepository>();
        lookup.LookupAsync(Arg.Any<VendorSessionProjectionLookupQuery>(), Arg.Any<CancellationToken>())
            .Returns(VendorSessionProjectionLookupResult.FoundProjection(
                Projection() with { SiteId = Guid.NewGuid() },
                Now,
                Now,
                CorrelationId));

        var action = () => CreateSut(lookup, repository).EnsureAsync(Command(), CancellationToken.None);

        await action.Should().ThrowAsync<OperatorConsoleCanonicalSessionException>()
            .Where(exception => exception.ErrorCode == "OPERATOR_SESSION_PROJECTION_INVALID");
        await repository.DidNotReceiveWithAnyArgs().EnsureAsync(default!, default);
    }

    private static OperatorConsoleCanonicalSessionService CreateSut(
        IVendorSessionProjectionLookupService lookup,
        IOperatorConsoleCanonicalSessionRepository repository,
        IManagementConfigurationRepository? configuration = null)
    {
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);
        return new OperatorConsoleCanonicalSessionService(
            lookup,
            repository,
            configuration ?? ActiveSiteConfiguration(),
            Options.Create(new VendorSessionProjectionOptions { MaxProjectionAgeMinutes = 1 }),
            clock);
    }

    private static OperatorConsoleCanonicalSessionCommand Command() =>
        new(SiteId, VendorSystemId, "TICKET_REFERENCE", "TICKET-3101", null, CorrelationId);

    private static IManagementConfigurationRepository ActiveSiteConfiguration(bool includeSiteGroup = true)
    {
        var configuration = Substitute.For<IManagementConfigurationRepository>();
        configuration.ListSitesAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ManagementSite(
                SiteId,
                SiteGroupId,
                "PITX-L3",
                "PITX Level 3",
                "PARKING",
                "Asia/Manila",
                null,
                null,
                "Paranaque",
                "Metro Manila",
                "PH",
                null,
                "ACTIVE",
                PublicLookupEnabled: true,
                PaymentEnabled: true,
                Now.AddYears(-1),
                EffectiveTo: null,
                RowVersion: 1)
        ]);
        configuration.ListSiteGroupsAsync(Arg.Any<CancellationToken>()).Returns(
            includeSiteGroup
                ? [new ManagementSiteGroup(SiteGroupId, "PITX", "PITX", "Asia/Manila", "PHP", "ACTIVE")]
                : []);
        return configuration;
    }

    private static VendorSessionProjection Projection() =>
        new(
            ProjectionId,
            VendorSystemId: VendorSystemId,
            SiteId: SiteId,
            SiteGroupId: SiteGroupId,
            ParkingLotIndexCode: "LOT-1",
            ParkingLotName: "PITX Level 3",
            PassagewayIndexCode: null,
            PassagewayName: null,
            LaneIndexCode: null,
            LaneName: null,
            LaneDirection: "IN",
            VendorRecordGuid: "VENDOR-RECORD-3101",
            CardNum: "TICKET-3101",
            PlateLicense: "ABC3101",
            EnterTime: Now.AddHours(-1),
            ExitTime: null,
            AllowType: null,
            AllowResult: null,
            ImageUrl: null,
            SourceApi: "HIKCENTRAL_PASSAGEWAY",
            SourcePayloadHash: new string('a', 64),
            SourcePayloadReference: null,
            SourceEventAt: Now.AddHours(-1),
            StableIdentityType: "VENDOR_RECORD_GUID",
            StableIdentityKey: "VENDOR-RECORD-3101",
            FirstSeenAt: Now.AddHours(-1),
            LastSeenAt: Now,
            LastRefreshedAt: Now,
            ProjectionStatus: VendorSessionProjectionStatus.Active,
            CorrelationId: CorrelationId,
            CreatedAt: Now.AddHours(-1),
            UpdatedAt: Now)
        {
            SourceAdapterIdentityId = AdapterId,
            VendorVehicleTypeCode = "5",
            CanonicalVehicleTypeCode = "CAR"
        };
}
