using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests Operator Console fiscal void facade behavior.
/// </summary>
public sealed class OperatorConsoleFiscalIssuanceVoidServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("8a000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("8a000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("8a000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("8a000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("8a000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("8a000000-0000-0000-0000-000000000006");
    private static readonly Guid FiscalIssuanceReferenceId = Guid.Parse("8a000000-0000-0000-0000-000000000007");
    private static readonly Guid OperatorActionRequestId = Guid.Parse("8a000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("8a000000-0000-0000-0000-000000000009");
    private static readonly Guid PosDocumentId = Guid.Parse("8a000000-0000-0000-0000-000000000010");

    [Fact]
    public async Task VoidAsync_WhenReasonCodeMissing_RejectsBeforeAccessEvaluation()
    {
        var access = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        var voidCommand = Substitute.For<IFiscalIssuanceVoidCommandService>();
        var sut = CreateSut(accessService: access, voidCommandService: voidCommand);

        var result = await sut.VoidAsync(Command() with { ReasonCode = null }, CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.VoidResult.Should().NotBeNull();
        result.VoidResult!.HttpStatusCode.Should().Be(400);
        result.VoidResult.Errors.Should().Contain("reason_code_required");
        await access.DidNotReceiveWithAnyArgs().EvaluateAsync(default!, default);
        await voidCommand.DidNotReceiveWithAnyArgs().VoidAsync(default, default!, default);
    }

    [Fact]
    public async Task VoidAsync_WhenConfirmationMissing_RejectsBeforeAccessEvaluation()
    {
        var result = await CreateSut().VoidAsync(Command() with { ConfirmationText = "VOID" }, CancellationToken.None);

        result.VoidResult.Should().NotBeNull();
        result.VoidResult!.HttpStatusCode.Should().Be(400);
        result.VoidResult.Errors.Should().Contain("confirmation_text_invalid");
    }

    [Fact]
    public async Task VoidAsync_WhenAccessDenied_PersistsDeniedAuditAndDoesNotCallVoidCommand()
    {
        var voidCommand = Substitute.For<IFiscalIssuanceVoidCommandService>();
        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });
        var sut = CreateSut(
            accessResult: AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]),
            writer: writer,
            voidCommandService: voidCommand);

        var result = await sut.VoidAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.VoidResult.Should().BeNull();
        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(persisted =>
                persisted.PersistenceContext.RequestedAction == OperatorConsoleActionCodes.VoidFiscalDocument &&
                persisted.PersistenceContext.TargetEntityId == FiscalIssuanceReferenceId &&
                persisted.PersistenceContext.ResultClass == "DENIED"),
            Arg.Any<CancellationToken>());
        await voidCommand.DidNotReceiveWithAnyArgs().VoidAsync(default, default!, default);
    }

    [Fact]
    public async Task VoidAsync_WhenReferenceMissing_ReturnsSafeNotFoundAndPersistsAudit()
    {
        var statusRead = Substitute.For<IFiscalIssuanceStatusReadService>();
        statusRead.GetByReferenceIdAsync(FiscalIssuanceReferenceId, Arg.Any<CancellationToken>())
            .Returns((FiscalIssuanceStatusReadModel?)null);
        var voidCommand = Substitute.For<IFiscalIssuanceVoidCommandService>();
        var sut = CreateSut(statusReadService: statusRead, voidCommandService: voidCommand);

        var result = await sut.VoidAsync(Command(), CancellationToken.None);

        result.VoidResult.Should().NotBeNull();
        result.VoidResult!.HttpStatusCode.Should().Be(404);
        result.VoidResult.Status.Should().Be("fiscal_issuance_reference_not_found");
        await voidCommand.DidNotReceiveWithAnyArgs().VoidAsync(default, default!, default);
    }

    [Fact]
    public async Task VoidAsync_WhenAlreadyVoided_ReturnsAlreadyVoidedWithoutCallingVoidCommand()
    {
        var voidCommand = Substitute.For<IFiscalIssuanceVoidCommandService>();
        var sut = CreateSut(status: Status() with
        {
            PosServerFiscalDocumentStatusCodeKey = "voided",
            PosServerVoidStatus = "recorded",
            PosServerVoidReasonCode = "operator_error"
        }, voidCommandService: voidCommand);

        var result = await sut.VoidAsync(Command(), CancellationToken.None);

        result.VoidResult.Should().NotBeNull();
        result.VoidResult!.Accepted.Should().BeTrue();
        result.VoidResult.Status.Should().Be("pos_server_already_voided");
        result.VoidResult.PosServerResultClassification.Should().Be("already_voided");
        await voidCommand.DidNotReceiveWithAnyArgs().VoidAsync(default, default!, default);
    }

    [Fact]
    public async Task VoidAsync_WhenAccessAllowed_CallsCommandWithDerivedIdempotencyAndPersistsSuccess()
    {
        var voidCommand = Substitute.For<IFiscalIssuanceVoidCommandService>();
        voidCommand.VoidAsync(FiscalIssuanceReferenceId, Arg.Any<FiscalIssuanceVoidCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(VoidResponse("pos_server_void_recorded", accepted: true, httpStatusCode: 200, classification: "newly_voided"));

        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });
        var sut = CreateSut(writer: writer, voidCommandService: voidCommand);

        var result = await sut.VoidAsync(Command(), CancellationToken.None);

        result.VoidResult.Should().NotBeNull();
        result.VoidResult!.Status.Should().Be("pos_server_void_recorded");
        await voidCommand.Received(1).VoidAsync(
            FiscalIssuanceReferenceId,
            Arg.Is<FiscalIssuanceVoidCommandRequest>(request =>
                request.IdempotencyKey == $"operator-console-fiscal-void:{FiscalIssuanceReferenceId:D}:{OperatorActionRequestId:D}" &&
                request.ReasonCode == "operator_error" &&
                request.ReasonText == "Operator selected wrong fiscal document." &&
                request.RequestedByRef == $"operator-console:{UserId:D}" &&
                request.CorrelationId == CorrelationId.ToString("D")),
            Arg.Any<CancellationToken>());
        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(persisted =>
                persisted.PersistenceContext.RequestedAction == OperatorConsoleActionCodes.VoidFiscalDocument &&
                persisted.PersistenceContext.ResultClass == "SUCCEEDED"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoidAsync_WhenPosServerConflict_PersistsConflictPosture()
    {
        var voidCommand = Substitute.For<IFiscalIssuanceVoidCommandService>();
        voidCommand.VoidAsync(FiscalIssuanceReferenceId, Arg.Any<FiscalIssuanceVoidCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(VoidResponse(
                "pos_server_void_conflict",
                accepted: false,
                httpStatusCode: 409,
                classification: "conflict",
                errors: ["fiscal_document_void_idempotency_conflict"]));

        var writer = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });
        var sut = CreateSut(writer: writer, voidCommandService: voidCommand);

        var result = await sut.VoidAsync(Command(), CancellationToken.None);

        result.VoidResult!.HttpStatusCode.Should().Be(409);
        result.VoidResult.Status.Should().Be("pos_server_void_conflict");
        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleAccessEvaluationResult>(persisted =>
                persisted.PersistenceContext.ResultClass == "CONFLICT" &&
                persisted.PersistenceContext.SafeErrorCode == "fiscal_document_void_idempotency_conflict"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_DoesNotWireForbiddenOperationalDependencies()
    {
        var constructorTypes = typeof(OperatorConsoleFiscalIssuanceVoidService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        constructorTypes.Should().Contain(nameof(IFiscalIssuanceStatusReadService));
        constructorTypes.Should().Contain(nameof(IFiscalIssuanceVoidCommandService));
        constructorTypes.Should().NotContain(typeName =>
            typeName.Contains("PaymentProvider", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Gate", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("HikCentral", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Refund", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Render", StringComparison.OrdinalIgnoreCase));
    }

    private static OperatorConsoleFiscalIssuanceVoidService CreateSut(
        OperatorConsoleAccessEvaluationResult? accessResult = null,
        IOperatorConsoleAccessEvaluationService? accessService = null,
        IOperatorConsoleAccessEvaluationWriter? writer = null,
        IFiscalIssuanceStatusReadService? statusReadService = null,
        IFiscalIssuanceVoidCommandService? voidCommandService = null,
        FiscalIssuanceStatusReadModel? status = null)
    {
        accessService ??= Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult ?? AccessResult(allowed: true, []));

        writer ??= Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(call => ((OperatorConsoleAccessEvaluationResult)call[0]!) with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });

        if (statusReadService is null)
        {
            statusReadService = Substitute.For<IFiscalIssuanceStatusReadService>();
            statusReadService.GetByReferenceIdAsync(FiscalIssuanceReferenceId, Arg.Any<CancellationToken>())
                .Returns(status ?? Status());
        }

        if (voidCommandService is null)
        {
            voidCommandService = Substitute.For<IFiscalIssuanceVoidCommandService>();
            voidCommandService.VoidAsync(FiscalIssuanceReferenceId, Arg.Any<FiscalIssuanceVoidCommandRequest>(), Arg.Any<CancellationToken>())
                .Returns(VoidResponse("pos_server_void_recorded", accepted: true, httpStatusCode: 200, classification: "newly_voided"));
        }

        return new OperatorConsoleFiscalIssuanceVoidService(
            accessService,
            writer,
            statusReadService,
            voidCommandService);
    }

    private static OperatorConsoleFiscalIssuanceVoidCommand Command() =>
        new(
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            FiscalIssuanceReferenceId,
            OperatorActionRequestId,
            ReasonCode: "operator_error",
            ReasonText: "Operator selected wrong fiscal document.",
            ConfirmationText: OperatorConsoleFiscalIssuanceVoidService.ConfirmationPhrase,
            CorrelationId);

    private static OperatorConsoleAccessEvaluationResult AccessResult(bool allowed, IReadOnlyList<string> reasons) =>
        new(
            Guid.Empty,
            allowed,
            allowed ? "ALLOWED" : "DENIED",
            reasons,
            allowed ? "OPERATOR" : null,
            new OperatorConsoleDeviceTrustResult(DeviceBindingId, "ACTIVE", "BROWSER_KEY_AND_MTLS", Trusted: true),
            new OperatorConsoleShiftContextResult(ShiftId, "ACTIVE", Active: true),
            new OperatorConsoleSiteContextResult(SiteId, SiteGroupId, Assigned: true),
            DateTimeOffset.Parse("2026-07-10T00:00:00Z"),
            Persisted: false,
            CorrelationId,
            new OperatorConsoleAccessEvaluationPersistenceContext(
                UserId,
                Guid.Parse("8a000000-0000-0000-0000-000000000011"),
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                OperatorConsoleActionCodes.VoidFiscalDocument,
                OperatorConsoleActionCodes.FiscalIssuanceStatusVisibilityWorkflow,
                TargetEntityType: null,
                TargetEntityId: null));

    private static FiscalIssuanceStatusReadModel Status()
    {
        var now = DateTimeOffset.Parse("2026-07-10T00:00:00Z");
        return new FiscalIssuanceStatusReadModel(
            FiscalIssuanceReferenceId,
            "FISCAL_ISSUANCE_RECORDED",
            ResultClassification: "NEWLY_CREATED",
            FiscalIssuanceEvidenceStatus: "FISCAL_DOCUMENT_NUMBER_ASSIGNED",
            FiscalNumberAssignmentState: "ASSIGNED",
            UpstreamFinalityReference: "CPS-POS-VOIDCMD-RUNTIME:20260710:001",
            PaymentConfirmationId: Guid.Parse("8a000000-0000-0000-0000-000000000012"),
            PaymentAttemptId: Guid.Parse("8a000000-0000-0000-0000-000000000013"),
            ParkingSessionId: Guid.Parse("8a000000-0000-0000-0000-000000000014"),
            SiteId,
            SitePosServerId: Guid.Parse("8a000000-0000-0000-0000-000000000015"),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            FiscalDocumentTypeCodeId: Guid.Parse("8a000000-0000-0000-0000-000000000016"),
            FiscalDocumentTypeCodeKey: "sales_invoice",
            PosServerFiscalDocumentId: PosDocumentId,
            FiscalDocumentNumber: "SI-VOIDCMD-0001-UAT",
            FiscalIdentityId: Guid.Parse("8a000000-0000-0000-0000-000000000017"),
            FiscalSequencePolicyId: Guid.Parse("8a000000-0000-0000-0000-000000000018"),
            FiscalSequenceValue: 9001,
            FiscalSeries: "UAT-SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: "-UAT",
            FiscalNumberAssignedAt: now,
            FiscalNumberAssignedByRef: "pos-server",
            SemanticRequestHashValue: "hash",
            SemanticRequestHashVersion: "sha256:v1",
            SemanticRequestHashStatus: "AVAILABLE",
            SemanticRequestHashAlgorithm: "SHA-256",
            SemanticRequestHashSourceFactCount: 20,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            LatestExceptionReason: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            CorrelationId,
            PosServerFiscalDocumentReadStatus: "AVAILABLE",
            PosServerFiscalDocumentStatusCodeKey: "issued");
    }

    private static FiscalIssuanceVoidCommandResponse VoidResponse(
        string status,
        bool accepted,
        int httpStatusCode,
        string classification,
        IReadOnlyList<string>? errors = null) =>
        new(
            accepted,
            status,
            httpStatusCode,
            errors ?? Array.Empty<string>(),
            FiscalIssuanceReferenceId,
            PosDocumentId,
            "SI-VOIDCMD-0001-UAT",
            9001,
            accepted ? "voided" : null,
            accepted ? "recorded" : null,
            accepted ? "operator_error" : null,
            accepted ? DateTimeOffset.Parse("2026-07-10T00:01:00Z") : null,
            classification,
            $"operator-console-fiscal-void:{FiscalIssuanceReferenceId:D}:{OperatorActionRequestId:D}",
            CorrelationId.ToString("D"),
            accepted ? null : "do_not_retry_without_request_change",
            NewFiscalNumberAllocated: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            RefundOrReversalCreated: false,
            HikCentralCalled: false,
            PaymentProviderCalled: false,
            RenderingGenerated: false,
            ReplacementFiscalDocumentCreated: false,
            FiscalSequenceChangedByCentralPms: false,
            IdempotentReplay: status == "pos_server_void_idempotent_replay");
}
