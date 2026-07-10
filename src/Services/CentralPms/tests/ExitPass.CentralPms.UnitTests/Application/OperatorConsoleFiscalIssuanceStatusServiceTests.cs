using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated read-only Operator Console fiscal issuance status behavior.
/// </summary>
public sealed class OperatorConsoleFiscalIssuanceStatusServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("4f000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("4f000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("4f000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("4f000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("4f000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("4f000000-0000-0000-0000-000000000006");
    private static readonly Guid FiscalIssuanceReferenceId = Guid.Parse("4f000000-0000-0000-0000-000000000007");
    private static readonly Guid CorrelationId = Guid.Parse("4f000000-0000-0000-0000-000000000008");

    [Fact]
    public async Task GetAsync_WhenAccessAllowed_PersistsViewAuditAndReturnsFiscalStatus()
    {
        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        statusReadService.GetByReferenceIdAsync(FiscalIssuanceReferenceId, Arg.Any<CancellationToken>())
            .Returns(Status());

        var sut = CreateSut(AccessResult(allowed: true, []), statusReadService);

        var result = await sut.GetAsync(Query(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.AccessEvaluationId.Should().Be(EvaluationId);
        result.AccessPersisted.Should().BeTrue();
        result.Status.Should().NotBeNull();
        result.Status!.FiscalIssuanceState.Should().Be("FISCAL_ISSUANCE_RECORDED");
        result.Status.FiscalDocumentNumber.Should().Be("SI-00000001-UAT");

        await statusReadService.Received(1)
            .GetByReferenceIdAsync(FiscalIssuanceReferenceId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenAccessDenied_PersistsDeniedViewAuditAndDoesNotReadFiscalStatus()
    {
        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), statusReadService);

        var result = await sut.GetAsync(Query(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessDecision.Should().Be("DENIED");
        result.AccessDenialReasons.Should().ContainSingle().Which.Should().Be("NO_ACTIVE_SHIFT");
        result.AccessEvaluationId.Should().Be(EvaluationId);
        result.AccessPersisted.Should().BeTrue();
        result.Status.Should().BeNull();

        await statusReadService.DidNotReceiveWithAnyArgs()
            .GetByReferenceIdAsync(default, default);
    }

    [Fact]
    public async Task LookupAsync_WhenFiscalDocumentNumberResolves_PersistsResolvedReferenceAuditAndReturnsStatus()
    {
        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        statusReadService.LookupAsync("SI-OCVOID-0001-UAT", Arg.Any<CancellationToken>())
            .Returns(FiscalIssuanceStatusLookupResult.Found(Status()));
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(AccessResult(allowed: true, []) with { EvaluationId = Guid.Empty, Persisted = false });
        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });
        var sut = new OperatorConsoleFiscalIssuanceStatusService(accessService, writer, statusReadService);

        var result = await sut.LookupAsync(LookupQuery("SI-OCVOID-0001-UAT"), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.Status.Should().NotBeNull();
        result.Status!.FiscalIssuanceReferenceId.Should().Be(FiscalIssuanceReferenceId);
        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(persisted =>
                persisted.PersistenceContext.TargetEntityType == "FISCAL_ISSUANCE_REFERENCE" &&
                persisted.PersistenceContext.TargetEntityId == FiscalIssuanceReferenceId &&
                persisted.PersistenceContext.ResultClass == "SUCCEEDED"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LookupAsync_WhenFiscalDocumentNumberMissing_PersistsNotFoundWithoutStatus()
    {
        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        statusReadService.LookupAsync("SI-MISSING-UAT", Arg.Any<CancellationToken>())
            .Returns(FiscalIssuanceStatusLookupResult.NotFound("fiscal_document_number_not_found"));
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(AccessResult(allowed: true, []) with { EvaluationId = Guid.Empty, Persisted = false });
        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });
        var sut = new OperatorConsoleFiscalIssuanceStatusService(accessService, writer, statusReadService);

        var result = await sut.LookupAsync(LookupQuery("SI-MISSING-UAT"), CancellationToken.None);

        result.Status.Should().BeNull();
        result.SafeErrorCode.Should().Be("FISCAL_ISSUANCE_LOOKUP_NOT_FOUND");
        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(persisted =>
                persisted.PersistenceContext.TargetEntityType == "FISCAL_ISSUANCE_REFERENCE" &&
                persisted.PersistenceContext.TargetEntityId == null &&
                persisted.PersistenceContext.ResultClass == "NOT_FOUND"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LookupAsync_WhenFiscalDocumentNumberAmbiguous_PersistsFailedSafely()
    {
        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        statusReadService.LookupAsync("SI-DUP-UAT", Arg.Any<CancellationToken>())
            .Returns(FiscalIssuanceStatusLookupResult.Ambiguous("fiscal_document_number_ambiguous"));
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(AccessResult(allowed: true, []) with { EvaluationId = Guid.Empty, Persisted = false });
        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });
        var sut = new OperatorConsoleFiscalIssuanceStatusService(accessService, writer, statusReadService);

        var result = await sut.LookupAsync(LookupQuery("SI-DUP-UAT"), CancellationToken.None);

        result.Status.Should().BeNull();
        result.LookupAmbiguous.Should().BeTrue();
        result.SafeErrorCode.Should().Be("FISCAL_DOCUMENT_NUMBER_LOOKUP_AMBIGUOUS");
        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(persisted =>
                persisted.PersistenceContext.ResultClass == "FAILED_SAFELY" &&
                persisted.PersistenceContext.SafeErrorCode == "FISCAL_DOCUMENT_NUMBER_LOOKUP_AMBIGUOUS"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenReferenceMissing_PersistsViewAuditAndReturnsNullStatus()
    {
        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        statusReadService.GetByReferenceIdAsync(FiscalIssuanceReferenceId, Arg.Any<CancellationToken>())
            .Returns((FiscalIssuanceStatusReadModel?)null);

        var sut = CreateSut(AccessResult(allowed: true, []), statusReadService);

        var result = await sut.GetAsync(Query(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.AccessPersisted.Should().BeTrue();
        result.Status.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_EvaluatesFiscalStatusViewAction()
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(AccessResult(allowed: true, []) with { EvaluationId = Guid.Empty, Persisted = false });

        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(AccessResult(allowed: true, []) with { EvaluationId = EvaluationId, Persisted = true });

        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        statusReadService.GetByReferenceIdAsync(FiscalIssuanceReferenceId, Arg.Any<CancellationToken>())
            .Returns(Status());

        var sut = new OperatorConsoleFiscalIssuanceStatusService(accessService, writer, statusReadService);

        await sut.GetAsync(Query(), CancellationToken.None);

        await accessService.Received(1).EvaluateAsync(
            Arg.Is<OperatorConsoleAccessEvaluationCommand>(command =>
                command.WorkflowCode == OperatorConsoleActionCodes.FiscalIssuanceStatusVisibilityWorkflow &&
                command.ControlledActionCode == OperatorConsoleActionCodes.ViewFiscalIssuanceStatus &&
                command.ParkingSessionId == null &&
                command.UserId == UserId &&
                command.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());

        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(result =>
                result.PersistenceContext.RequestedAction == OperatorConsoleActionCodes.ViewFiscalIssuanceStatus &&
                result.PersistenceContext.TargetEntityType == "FISCAL_ISSUANCE_REFERENCE" &&
                result.PersistenceContext.TargetEntityId == FiscalIssuanceReferenceId &&
                result.PersistenceContext.ResultClass == "SUCCEEDED"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenReferenceMissing_PersistsNotFoundResultClass()
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(AccessResult(allowed: true, []) with { EvaluationId = Guid.Empty, Persisted = false });

        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });

        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        statusReadService.GetByReferenceIdAsync(FiscalIssuanceReferenceId, Arg.Any<CancellationToken>())
            .Returns((FiscalIssuanceStatusReadModel?)null);

        var sut = new OperatorConsoleFiscalIssuanceStatusService(accessService, writer, statusReadService);

        var result = await sut.GetAsync(Query(), CancellationToken.None);

        result.Status.Should().BeNull();
        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(persisted =>
                persisted.PersistenceContext.TargetEntityType == "FISCAL_ISSUANCE_REFERENCE" &&
                persisted.PersistenceContext.TargetEntityId == FiscalIssuanceReferenceId &&
                persisted.PersistenceContext.ResultClass == "NOT_FOUND" &&
                persisted.PersistenceContext.SafeErrorCode == "FISCAL_ISSUANCE_REFERENCE_NOT_FOUND"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenStatusReadFails_PersistsFailedSafelyResultClass()
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(AccessResult(allowed: true, []) with { EvaluationId = Guid.Empty, Persisted = false });

        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });

        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        statusReadService.GetByReferenceIdAsync(FiscalIssuanceReferenceId, Arg.Any<CancellationToken>())
            .Returns<Task<FiscalIssuanceStatusReadModel?>>(_ => throw new InvalidOperationException("safe test failure"));

        var sut = new OperatorConsoleFiscalIssuanceStatusService(accessService, writer, statusReadService);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAsync(Query(), CancellationToken.None));

        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(persisted =>
                persisted.PersistenceContext.TargetEntityType == "FISCAL_ISSUANCE_REFERENCE" &&
                persisted.PersistenceContext.TargetEntityId == FiscalIssuanceReferenceId &&
                persisted.PersistenceContext.ResultClass == "FAILED_SAFELY" &&
                persisted.PersistenceContext.SafeErrorCode == "OPERATOR_CONSOLE_FISCAL_STATUS_VIEW_FAILED"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenAccessDenied_PersistsDeniedResultClassWithFiscalReferenceTarget()
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]) with { EvaluationId = Guid.Empty, Persisted = false });

        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });

        var statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
        var sut = new OperatorConsoleFiscalIssuanceStatusService(accessService, writer, statusReadService);

        var result = await sut.GetAsync(Query(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(persisted =>
                persisted.PersistenceContext.TargetEntityType == "FISCAL_ISSUANCE_REFERENCE" &&
                persisted.PersistenceContext.TargetEntityId == FiscalIssuanceReferenceId &&
                persisted.PersistenceContext.ResultClass == "DENIED" &&
                persisted.PersistenceContext.SafeErrorCode == "OPERATOR_CONSOLE_FISCAL_STATUS_ACCESS_DENIED"),
            Arg.Any<CancellationToken>());
        await statusReadService.DidNotReceiveWithAnyArgs()
            .GetByReferenceIdAsync(default, default);
    }

    [Fact]
    public void Constructor_DoesNotWirePosServerOrMutationWorkflowDependencies()
    {
        var parameterTypes = typeof(OperatorConsoleFiscalIssuanceStatusService)
            .GetConstructors()
            .Should()
            .ContainSingle()
            .Which
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        parameterTypes.Should().BeEquivalentTo(
            [
                nameof(IOperatorConsoleAccessEvaluationService),
                nameof(IOperatorConsoleAccessEvaluationWriter),
                nameof(IFiscalIssuanceStatusReadService)
            ]);
        parameterTypes.Should().NotContain(typeName =>
            typeName.Contains("PosServer", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Retry", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Readback", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("PaymentConfirmation", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase));
    }

    private static OperatorConsoleFiscalIssuanceStatusService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IFiscalIssuanceStatusReadService statusReadService)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = Guid.Empty, Persisted = false });

        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = EvaluationId, Persisted = true });

        return new OperatorConsoleFiscalIssuanceStatusService(accessService, writer, statusReadService);
    }

    private static OperatorConsoleFiscalIssuanceStatusQuery Query() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            FiscalIssuanceReferenceId,
            CorrelationId);

    private static OperatorConsoleFiscalIssuanceLookupQuery LookupQuery(string query) =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            query,
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
            DateTimeOffset.Parse("2026-07-08T08:00:00Z"),
            Persisted: false,
            CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                UserId,
                Guid.Parse("4f000000-0000-0000-0000-000000000009"),
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                OperatorConsoleActionCodes.ViewFiscalIssuanceStatus,
                OperatorConsoleActionCodes.FiscalIssuanceStatusVisibilityWorkflow,
                TargetEntityType: null,
                TargetEntityId: null));

    private static FiscalIssuanceStatusReadModel Status()
    {
        var now = DateTimeOffset.Parse("2026-07-08T08:00:00Z");
        return new FiscalIssuanceStatusReadModel(
            FiscalIssuanceReferenceId,
            "FISCAL_ISSUANCE_RECORDED",
            ResultClassification: "NEWLY_CREATED",
            FiscalIssuanceEvidenceStatus: "FISCAL_DOCUMENT_NUMBER_ASSIGNED",
            FiscalNumberAssignmentState: "ASSIGNED",
            UpstreamFinalityReference: "CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001",
            PaymentConfirmationId: Guid.Parse("4f000000-0000-0000-0000-000000000010"),
            PaymentAttemptId: Guid.Parse("4f000000-0000-0000-0000-000000000011"),
            ParkingSessionId: Guid.Parse("4f000000-0000-0000-0000-000000000012"),
            SiteId,
            SitePosServerId: Guid.Parse("4f000000-0000-0000-0000-000000000013"),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            FiscalDocumentTypeCodeId: Guid.Parse("4f000000-0000-0000-0000-000000000014"),
            FiscalDocumentTypeCodeKey: "sales_invoice",
            PosServerFiscalDocumentId: Guid.Parse("4f000000-0000-0000-0000-000000000015"),
            FiscalDocumentNumber: "SI-00000001-UAT",
            FiscalIdentityId: Guid.Parse("4f000000-0000-0000-0000-000000000016"),
            FiscalSequencePolicyId: Guid.Parse("4f000000-0000-0000-0000-000000000017"),
            FiscalSequenceValue: 1,
            FiscalSeries: "UAT-SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: "-UAT",
            FiscalNumberAssignedAt: now,
            FiscalNumberAssignedByRef: "pos-server",
            SemanticRequestHashValue: "hash-value",
            SemanticRequestHashVersion: "sha256:v1",
            SemanticRequestHashStatus: "AVAILABLE",
            SemanticRequestHashAlgorithm: "SHA-256",
            SemanticRequestHashSourceFactCount: 24,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            LatestExceptionReason: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            CorrelationId);
    }
}
