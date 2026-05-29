using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated Operator Console statutory discount validation draft behavior.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDraftServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("47000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("47000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("47000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("47000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("47000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("47000000-0000-0000-0000-000000000006");
    private static readonly Guid ParkingSessionId = Guid.Parse("47000000-0000-0000-0000-000000000007");
    private static readonly Guid DraftId = Guid.Parse("47000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("47000000-0000-0000-0000-000000000009");

    /// <summary>
    /// Verifies access denial is persisted and prevents draft creation.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenAccessDenied_DoesNotCreateDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), repository, writer);

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessDecision.Should().Be("DENIED");
        result.AccessPersisted.Should().BeTrue();
        result.AccessEvaluationId.Should().Be(EvaluationId);
        result.AccessDenialReasons.Should().ContainSingle().Which.Should().Be("NO_ACTIVE_SHIFT");
        result.DraftAccepted.Should().BeFalse();
        result.DraftPersisted.Should().BeFalse();
        result.DraftId.Should().BeNull();
        result.IneligibilityReason.Should().Be("ACCESS_DENIED");

        await repository.DidNotReceiveWithAnyArgs().FindAsync(default!, default);
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies a valid access-allowed draft creates a requested validation row without applying a discount.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenAccessAllowedAndSessionActive_PersistsRequestedDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns(Session("ACTIVE"));

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new OperatorConsoleStatutoryDiscountDraftPersistenceResult(DraftId, "REQUESTED", Persisted: true));

        var sut = CreateSut(AccessResult(allowed: true, []), repository, writer);

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.AccessPersisted.Should().BeTrue();
        result.DraftAccepted.Should().BeTrue();
        result.DraftPersisted.Should().BeTrue();
        result.DraftId.Should().Be(DraftId);
        result.ValidationStatus.Should().Be("REQUESTED");
        result.EntitlementType.Should().Be("SENIOR_CITIZEN");

        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountDraftPersistenceCommand>(request =>
                request.ParkingSessionId == ParkingSessionId &&
                request.EntitlementType == "SENIOR_CITIZEN" &&
                request.EvidenceRequired &&
                request.RequestedByUserId == UserId &&
                request.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies the draft requires a parking session ID.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenParkingSessionIdMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(parkingSessionId: Guid.Empty), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ParkingSessionId is required*");
    }

    /// <summary>
    /// Verifies entitlement type is required.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenEntitlementTypeMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(entitlementType: ""), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*EntitlementType is required*");
    }

    /// <summary>
    /// Verifies unsupported entitlement types are rejected before draft creation.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenEntitlementTypeUnsupported_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(entitlementType: "OTHER_STATUTORY"), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*EntitlementType must be SENIOR_CITIZEN or PWD*");
    }

    /// <summary>
    /// Verifies masked ID reference is required.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenMaskedIdReferenceMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(maskedIdReference: ""), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*MaskedIdReference is required*");
    }

    /// <summary>
    /// Verifies full ID-looking references are rejected.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenMaskedIdReferenceLooksRaw_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(maskedIdReference: "123456789012"), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*masked or last-four style*");
    }

    /// <summary>
    /// Verifies operator attestation is required.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenOperatorAttestationFalse_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DraftAsync(Command(operatorAttestation: false), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*OperatorAttestation must be true*");
    }

    /// <summary>
    /// Verifies missing sessions are not drafted.
    /// </summary>
    [Fact]
    public async Task DraftAsync_WhenSessionMissing_ReturnsNotFoundWithoutDraft()
    {
        var repository = Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        repository.FindAsync(Arg.Any<OperatorConsoleSessionLookupReadRequest>(), Arg.Any<CancellationToken>())
            .Returns((OperatorConsoleSessionReadModel?)null);
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();
        var sut = CreateSut(AccessResult(allowed: true, []), repository, writer);

        var result = await sut.DraftAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.DraftAccepted.Should().BeFalse();
        result.DraftPersisted.Should().BeFalse();
        result.ErrorCode.Should().Be("SESSION_NOT_FOUND");
        result.IneligibilityReason.Should().Be("SESSION_NOT_FOUND");
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    private static OperatorConsoleStatutoryDiscountDraftService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IOperatorConsoleSessionLookupReadRepository? sessionRepository = null,
        IOperatorConsoleStatutoryDiscountDraftWriter? draftWriter = null)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = Guid.Empty, Persisted = false });

        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = EvaluationId, Persisted = true });

        sessionRepository ??= Substitute.For<IOperatorConsoleSessionLookupReadRepository>();
        draftWriter ??= Substitute.For<IOperatorConsoleStatutoryDiscountDraftWriter>();

        return new OperatorConsoleStatutoryDiscountDraftService(
            accessService,
            accessWriter,
            sessionRepository,
            draftWriter);
    }

    private static OperatorConsoleStatutoryDiscountDraftCommand Command(
        Guid? parkingSessionId = null,
        string entitlementType = "SENIOR_CITIZEN",
        string maskedIdReference = "****1234",
        bool operatorAttestation = true) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            parkingSessionId ?? ParkingSessionId,
            "TICKET-001",
            PlateNumber: null,
            entitlementType,
            "OSCA_ID",
            "OSCA",
            ExpiryDate: null,
            maskedIdReference,
            EntitlementFingerprint: null,
            EvidenceCaptureRequested: true,
            EvidenceAccessIntent: "SUPERVISOR_REVIEW",
            operatorAttestation,
            AttestationNotes: "Manual API test attestation.",
            ReasonCode: "OPERATOR_DRAFT_REQUESTED",
            "operator-console-statutory-discount-draft-test",
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
                Guid.Parse("47000000-0000-0000-0000-000000000010"),
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                "SUBMIT_DECISION",
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
            DateTimeOffset.Parse("2026-05-29T04:00:00Z"),
            CurrentPayableAmountMinorUnits: 12500,
            CurrencyCode: "PHP",
            PaymentStatus: null,
            DiscountStatus: "NOT_APPLIED",
            ExitAuthorizationStatus: null);
}
