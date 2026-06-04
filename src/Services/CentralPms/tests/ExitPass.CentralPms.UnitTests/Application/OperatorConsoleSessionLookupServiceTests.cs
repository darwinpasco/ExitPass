using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated read-only Operator Console session lookup behavior.
/// </summary>
public sealed class OperatorConsoleSessionLookupServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("45000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("45000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("45000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("45000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("45000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("45000000-0000-0000-0000-000000000006");
    private static readonly Guid ParkingSessionId = Guid.Parse("45000000-0000-0000-0000-000000000007");
    private static readonly Guid CorrelationId = Guid.Parse("45000000-0000-0000-0000-000000000008");
    private static readonly DateTimeOffset EntryTime = DateTimeOffset.Parse("2026-05-29T04:00:00Z");

    /// <summary>
    /// Verifies denied access is persisted and prevents parking session lookup.
    /// </summary>
    [Fact]
    public async Task LookupAsync_WhenAccessDenied_DoesNotLookupSession()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), repository);

        var result = await sut.LookupAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessDecision.Should().Be("DENIED");
        result.AccessEvaluationId.Should().Be(EvaluationId);
        result.AccessPersisted.Should().BeTrue();
        result.AccessDenialReasons.Should().ContainSingle().Which.Should().Be("NO_ACTIVE_SHIFT");
        result.Session.Should().BeNull();
        result.IneligibilityReason.Should().Be("ACCESS_DENIED");

        await repository.DidNotReceiveWithAnyArgs().FindAsync(default!, default);
    }

    /// <summary>
    /// Verifies an allowed request returns read-only session context.
    /// </summary>
    [Fact]
    public async Task LookupAsync_WhenAccessAllowedAndSessionFound_ReturnsSessionContext()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var result = await sut.LookupAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.AccessDecision.Should().Be("ALLOWED");
        result.AccessEvaluationId.Should().Be(EvaluationId);
        result.AccessPersisted.Should().BeTrue();
        result.Session.Should().NotBeNull();
        result.Session!.ParkingSessionId.Should().Be(ParkingSessionId);
        result.SessionEligible.Should().BeTrue();
        result.IneligibilityReason.Should().BeNull();

        await repository.Received(1).FindAsync(
            Arg.Is<OperatorConsoleSessionLookupReadRequest>(request =>
                request.ParkingSessionId == ParkingSessionId &&
                request.LookupMode == "PARKING_SESSION_ID"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies not-found lookup returns a deterministic non-eligible result.
    /// </summary>
    [Fact]
    public async Task LookupAsync_WhenAccessAllowedAndSessionMissing_ReturnsNotFoundResult()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns((OperatorConsoleSessionReadModel?)null);

        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var result = await sut.LookupAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.Session.Should().BeNull();
        result.SessionEligible.Should().BeFalse();
        result.IneligibilityReason.Should().Be("SESSION_NOT_FOUND");
    }

    /// <summary>
    /// Verifies inactive sessions are returned but marked ineligible without mutation.
    /// </summary>
    [Fact]
    public async Task LookupAsync_WhenSessionClosed_ReturnsIneligibleSession()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("CLOSED"));

        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var result = await sut.LookupAsync(Command(), CancellationToken.None);

        result.Session.Should().NotBeNull();
        result.SessionEligible.Should().BeFalse();
        result.IneligibilityReason.Should().Be("SESSION_NOT_ACTIVE");
        result.Alerts.Should().Contain("SESSION_NOT_ELIGIBLE_FOR_OPERATOR_WORKFLOW");
    }

    /// <summary>
    /// Verifies lookup identifiers are required.
    /// </summary>
    [Fact]
    public async Task LookupAsync_WhenLookupIdentifierMissing_ThrowsValidationError()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var action = () => sut.LookupAsync(MissingLookupCommand(), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ParkingSessionId or TicketReference*");
    }

    /// <summary>
    /// Verifies unsupported lookup modes are rejected deterministically.
    /// </summary>
    [Fact]
    public async Task LookupAsync_WhenLookupModeUnsupported_ThrowsValidationError()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        var sut = CreateSut(AccessResult(allowed: true, []), repository);

        var action = () => sut.LookupAsync(Command(lookupMode: "PLATE"), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*LookupMode must be PARKING_SESSION_ID or TICKET_REFERENCE*");
    }

    private static OperatorConsoleSessionLookupService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IOperatorConsoleSessionLookupReadRepository sessionRepository)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = Guid.Empty, Persisted = false });

        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = EvaluationId, Persisted = true });

        return new OperatorConsoleSessionLookupService(accessService, writer, sessionRepository);
    }

    private static OperatorConsoleSessionLookupCommand Command(
        Guid? parkingSessionId = null,
        string? ticketReference = "TICKET-001",
        string? lookupMode = null) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            parkingSessionId ?? ParkingSessionId,
            ticketReference,
            PlateNumber: null,
            lookupMode,
            "operator-console-session-lookup-test",
            CorrelationId);

    private static OperatorConsoleSessionLookupCommand MissingLookupCommand() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            ParkingSessionId: null,
            TicketReference: null,
            PlateNumber: null,
            LookupMode: null,
            "operator-console-session-lookup-test",
            CorrelationId);

    private static OperatorConsoleAccessEvaluationResult AccessResult(
        bool allowed,
        IReadOnlyList<string> reasons) =>
        new(
            Guid.Empty,
            allowed,
            allowed ? "ALLOWED" : "DENIED",
            reasons,
            allowed ? "OPERATOR" : null,
            new OperatorConsoleDeviceTrustResult(DeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: true),
            new OperatorConsoleShiftContextResult(ShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(SiteId, SiteGroupId, Assigned: true),
            DateTimeOffset.Parse("2026-05-29T08:00:00Z"),
            Persisted: false,
            CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                UserId,
                Guid.Parse("45000000-0000-0000-0000-000000000009"),
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                OperatorConsoleActionCodes.SessionLookup,
                "STATUTORY_DISCOUNT_VALIDATION",
                "PARKING_SESSION",
                ParkingSessionId));

    private static OperatorConsoleSessionReadModel Session(string status) =>
        new(
            ParkingSessionId,
            "TICKET-001",
            "ABC-1234",
            SiteId,
            SiteGroupId,
            status,
            EntryTime,
            CurrentPayableAmountMinorUnits: 12500,
            CurrencyCode: "PHP",
            PaymentStatus: null,
            DiscountStatus: "NOT_APPLIED",
            ExitAuthorizationStatus: null);
}
