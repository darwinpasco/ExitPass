using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated Operator Console statutory discount payable-basis application behavior.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountApplyPayableBasisServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("4b000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("4b000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("4b000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("4b000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("4b000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("4b000000-0000-0000-0000-000000000006");
    private static readonly Guid ValidationId = Guid.Parse("4b000000-0000-0000-0000-000000000007");
    private static readonly Guid ParkingSessionId = Guid.Parse("4b000000-0000-0000-0000-000000000008");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("4b000000-0000-0000-0000-000000000009");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("4b000000-0000-0000-0000-00000000000a");
    private static readonly Guid ApplicationId = Guid.Parse("4b000000-0000-0000-0000-00000000000b");
    private static readonly Guid CorrelationId = Guid.Parse("4b000000-0000-0000-0000-00000000000c");
    private static readonly Guid DecisionCommandId = Guid.Parse("4b000000-0000-0000-0000-000000000010");
    private static readonly Guid ApplicationCommandId = Guid.Parse("4b000000-0000-0000-0000-000000000011");

    /// <summary>
    /// Verifies access denial is persisted and prevents payable-basis application.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenAccessDenied_DoesNotCallWriter()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), writer);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessPersisted.Should().BeTrue();
        result.ApplicationAccepted.Should().BeFalse();
        result.ApplicationPersisted.Should().BeFalse();
        result.IneligibilityReason.Should().Be("ACCESS_DENIED");
        await writer.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default);
    }

    /// <summary>
    /// Verifies allowed access delegates payable-basis application to the writer.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenAccessAllowed_ReturnsPersistedApplication()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        writer.ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(PersistedApplication(alreadyApplied: false));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.ApplicationAccepted.Should().BeTrue();
        result.ApplicationPersisted.Should().BeTrue();
        result.PayableBasisApplicationId.Should().Be(ApplicationId);
        result.ApplicationStatus.Should().Be("APPLIED");
        result.StatutoryDiscountDecisionCommandId.Should().Be(DecisionCommandId);
        result.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(ApplicationCommandId);
        result.GrossAmountMinorUnits.Should().Be(12500);
        result.VatAmountMinorUnits.Should().Be(1339);
        result.VatExclusiveAmountMinorUnits.Should().Be(11161);
        result.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
        result.FinalPayableAmountMinorUnits.Should().Be(8929);
        result.PolicySnapshotUsed.Should().BeTrue();
        result.PolicyCode.Should().Be("PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK");

        await writer.Received(1).ApplyAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand>(request =>
                request.ValidationId == ValidationId &&
                request.OriginalTariffSnapshotId == OriginalTariffSnapshotId &&
                request.AppliedByUserId == UserId &&
                request.IdempotencyKey.StartsWith("operator-console-payable-basis-application-v1:sha256:", StringComparison.Ordinal) &&
                request.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies replayed applications surface deterministic already-applied state.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenWriterReturnsExistingApplication_SurfacesAlreadyApplied()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        writer.ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(PersistedApplication(alreadyApplied: true));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.ApplicationAccepted.Should().BeTrue();
        result.ApplicationPersisted.Should().BeTrue();
        result.AlreadyApplied.Should().BeTrue();
        result.AppliedTariffSnapshotId.Should().Be(AppliedTariffSnapshotId);
        result.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(ApplicationCommandId);
        await writer.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyAsync_WhenCanonicalApplicationAlreadyApplied_DoesNotCallWriter()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        var staged = Substitute.For<IStatutoryDiscountStagedCommandService>();
        staged.GetDecisionAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(ApprovedDecision());
        staged.GetApplicationByDecisionAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(AppliedApplicationRecord());

        var sut = CreateSut(AccessResult(allowed: true, []), writer, staged: staged);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.ApplicationAccepted.Should().BeTrue();
        result.ApplicationPersisted.Should().BeTrue();
        result.AlreadyApplied.Should().BeTrue();
        result.PayableBasisApplicationId.Should().Be(ApplicationId);
        result.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(ApplicationCommandId);
        await writer.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default);
    }

    [Fact]
    public async Task ApplyAsync_WhenCanonicalDecisionMissing_ReturnsSafeFailureWithoutWriter()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        var staged = Substitute.For<IStatutoryDiscountStagedCommandService>();
        staged.GetDecisionAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns((StatutoryDiscountDecisionV2Record?)null);

        var sut = CreateSut(AccessResult(allowed: true, []), writer, staged: staged);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.ApplicationAccepted.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DECISION_NOT_FOUND");
        result.StatutoryDiscountDecisionCommandId.Should().Be(DecisionCommandId);
        await writer.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default);
    }

    [Fact]
    public async Task ApplyAsync_WhenCanonicalDecisionRejected_ReturnsSafeFailureWithoutWriter()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        var staged = Substitute.For<IStatutoryDiscountStagedCommandService>();
        staged.GetDecisionAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(ApprovedDecision() with
            {
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.Completed,
                DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.Rejected
            });

        var sut = CreateSut(AccessResult(allowed: true, []), writer, staged: staged);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.ApplicationAccepted.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_DECISION_NOT_APPROVED");
        await writer.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default);
    }

    [Fact]
    public async Task ApplyAsync_WhenApplicationSemanticConflict_ReturnsDeterministicFailureWithoutWriter()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        writer.ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(PersistedApplication(alreadyApplied: true));
        var staged = Substitute.For<IStatutoryDiscountStagedCommandService>();
        staged.GetDecisionAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(ApprovedDecision());
        staged.GetApplicationByDecisionAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns((StatutoryDiscountPayableBasisApplicationV1Record?)null);
        staged.CreateOrResolveApplicationAsync(Arg.Any<StatutoryDiscountPayableBasisApplicationV1Command>(), Arg.Any<CancellationToken>())
            .Returns(new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
                StatutoryDiscountPayableBasisApplicationV1ResultClassifications.SemanticConflict,
                Existing: true,
                SemanticConflict: true,
                Retryable: false,
                StatutoryDiscountDecisionRecoveryClassifications.CorrectRequestRequired,
                ApplicationRecord(),
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT"));

        var sut = CreateSut(AccessResult(allowed: true, []), writer, staged: staged);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.ApplicationAccepted.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_SEMANTIC_CONFLICT");
        result.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(ApplicationCommandId);
        await writer.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default);
    }

    [Fact]
    public async Task ApplyAsync_WhenCanonicalApplicationInProgress_ReturnsSafeInProgressWithoutWriter()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        var staged = Substitute.For<IStatutoryDiscountStagedCommandService>();
        staged.GetDecisionAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(ApprovedDecision());
        staged.GetApplicationByDecisionAsync(DecisionCommandId, Arg.Any<CancellationToken>())
            .Returns(ApplicationRecord() with
            {
                CommandStatus = StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing,
                SafeErrorCode = "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS"
            });
        staged.GetApplicationAsync(ApplicationCommandId, Arg.Any<CancellationToken>())
            .Returns(ApplicationRecord() with
            {
                CommandStatus = StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing,
                SafeErrorCode = "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS"
            });

        var sut = CreateSut(AccessResult(allowed: true, []), writer, staged: staged);

        var result = await sut.ApplyAsync(Command(), CancellationToken.None);

        result.ApplicationAccepted.Should().BeFalse();
        result.ErrorCode.Should().Be("STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_IN_PROGRESS");
        result.StatutoryDiscountPayableBasisApplicationCommandId.Should().Be(ApplicationCommandId);
        await writer.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default);
    }

    /// <summary>
    /// Verifies request validation runs before access evaluation.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WhenValidationIdMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.ApplyAsync(Command(validationId: Guid.Empty), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ValidationId is required*");
    }

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter? writer = null,
        IOperatorConsoleStatutoryDiscountReadService? readService = null,
        IStatutoryDiscountStagedCommandService? staged = null)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = Guid.Empty, Persisted = false });

        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = EvaluationId, Persisted = true });

        writer ??= Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>();
        if (readService is null)
        {
            readService = Substitute.For<IOperatorConsoleStatutoryDiscountReadService>();
            readService.GetDraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftDetailQuery>(), Arg.Any<CancellationToken>())
                .Returns(DraftDetail());
        }

        if (staged is null)
        {
            staged = Substitute.For<IStatutoryDiscountStagedCommandService>();
            staged.GetDecisionAsync(DecisionCommandId, Arg.Any<CancellationToken>())
                .Returns(ApprovedDecision());
            staged.GetApplicationAsync(ApplicationCommandId, Arg.Any<CancellationToken>())
                .Returns(ApplicationRecord());
            staged.CreateOrResolveApplicationAsync(Arg.Any<StatutoryDiscountPayableBasisApplicationV1Command>(), Arg.Any<CancellationToken>())
                .Returns(new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>(
                    StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress,
                    Existing: false,
                    SemanticConflict: false,
                    Retryable: false,
                    StatutoryDiscountDecisionRecoveryClassifications.None,
                    ApplicationRecord(),
                    SafeErrorCode: null));
            staged.MarkApplicationProcessingAsync(ApplicationCommandId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ApplicationRecord() with { CommandStatus = StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing });
            staged.CompleteApplicationAppliedAsync(ApplicationCommandId, ApplicationId, AppliedTariffSnapshotId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(AppliedApplicationRecord());
        }

        ConfigureApplicationLock(staged);

        return new OperatorConsoleStatutoryDiscountApplyPayableBasisService(
            accessService,
            accessWriter,
            writer,
            readService,
            staged);
    }

    private static void ConfigureApplicationLock(IStatutoryDiscountStagedCommandService staged) =>
        staged.ExecuteWithApplicationLockAsync(
                Arg.Any<StatutoryDiscountPayableBasisApplicationV1Record>(),
                Arg.Any<Func<CancellationToken, Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.ArgAt<Func<CancellationToken, Task<OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult>>>(1)(
                    call.ArgAt<CancellationToken>(2)));

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisCommand Command(Guid? validationId = null) =>
        new(
            validationId ?? ValidationId,
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            OriginalTariffSnapshotId,
            "operator-console-statutory-discount-apply-test",
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisPersistenceResult PersistedApplication(bool alreadyApplied) =>
        new(
            ApplicationAccepted: true,
            ApplicationPersisted: true,
            ApplicationId,
            ValidationId,
            ParkingSessionId,
            OriginalTariffSnapshotId,
            AppliedTariffSnapshotId,
            "APPLIED",
            alreadyApplied,
            GrossAmountMinorUnits: 12500,
            VatAmountMinorUnits: 1339,
            VatExclusiveAmountMinorUnits: 11161,
            StatutoryDiscountAmountMinorUnits: 2232,
            FinalPayableAmountMinorUnits: 8929,
            CurrencyCode: "PHP",
            StatutoryDiscountPolicyId: Guid.Parse("4b000000-0000-0000-0000-00000000000e"),
            ResolvedJurisdictionId: Guid.Parse("4b000000-0000-0000-0000-00000000000f"),
            PolicyResolutionBasis: "NATIONAL_LAW_FALLBACK",
            PolicyCode: "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            BenefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
            NationalLawReference: "RA 9994",
            OrdinanceReference: null,
            PolicySnapshotUsed: true,
            IneligibilityReason: null,
            ErrorCode: null);

    private static OperatorConsoleStatutoryDiscountDraftDetailResult DraftDetail() =>
        new(
            ValidationId,
            ParkingSessionId,
            "TICKET-APPLY-001",
            "ABC1234",
            SiteId,
            "Test Site",
            SiteGroupId,
            "SENIOR_CITIZEN",
            "APPROVED",
            DecisionCommandId,
            IdDocumentType: "SENIOR_CITIZEN_ID",
            IssuingAuthority: "OSCA",
            ExpiryDate: null,
            MaskedIdReference: "SC-****-1234",
            RequesterAttestation: true,
            AttestationNotes: "attested",
            EvidenceRequired: false,
            EvidenceCaptured: false,
            EvidenceRequiredSatisfied: false,
            EvidenceCount: 0,
            LatestEvidenceStatus: null,
            RequiredEvidenceTypes: [],
            DateTimeOffset.Parse("2026-05-29T07:50:00Z"),
            DateTimeOffset.Parse("2026-05-29T08:00:00Z"),
            RequestedByUserId: UserId,
            ValidatedByUserId: UserId,
            DecisionReasonCode: "ELIGIBLE",
            FailureReasonCode: null,
            PolicyResolutionBasis: "NATIONAL_LAW_FALLBACK",
            StatutoryDiscountPolicyId: Guid.Parse("4b000000-0000-0000-0000-00000000000e"),
            ResolvedJurisdictionId: Guid.Parse("4b000000-0000-0000-0000-00000000000f"),
            PolicyCode: "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            PolicyName: "Senior Citizen National Fallback",
            LegalBasisReference: "RA 9994",
            OrdinanceReference: null,
            NationalLawReference: "RA 9994",
            VerificationStatus: "ACTIVE",
            BenefitType: "STATUTORY_DISCOUNT_VAT_EXEMPT",
            FreeDurationMinutes: null,
            SucceedingHoursDiscountRule: "STANDARD_20_PERCENT",
            DiscountBaseScope: "VAT_EXCLUSIVE",
            StackingPolicy: "STATUTORY_FIRST",
            PolicySnapshot: null,
            OriginalTariffSnapshotId,
            PayableBasisApplicationId: ApplicationId,
            StatutoryDiscountPayableBasisApplicationCommandId: ApplicationCommandId,
            PayableBasisApplicationStatus: "APPLIED",
            AppliedTariffSnapshotId,
            OriginalAmountMinorUnits: 12500,
            VatAmountMinorUnits: 1339,
            VatExclusiveAmountMinorUnits: 11161,
            StatutoryDiscountAmountMinorUnits: 2232,
            PayableAmountMinorUnits: 8929,
            FinalPayableAmountMinorUnits: 8929,
            CurrencyCode: "PHP",
            Activity: []);

    private static StatutoryDiscountDecisionV2Record ApprovedDecision() =>
        new(
            DecisionCommandId,
            RequestReference: ValidationId,
            ParkingSessionId,
            StatutoryDiscountSourceChannels.OperatorConsole,
            "SENIOR_CITIZEN",
            $"statutory-discount-decision:{ParkingSessionId:N}:SENIOR_CITIZEN",
            $"statutory-discount-decision:{ParkingSessionId:N}:SENIOR_CITIZEN",
            "operator-console-decision-v2:sha256:test",
            StatutoryDiscountDecisionV2SemanticHash.SourceVersion,
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            StatutoryDiscountDecisionV2CommandStates.Completed,
            StatutoryDiscountDecisionV2ResultStates.Approved,
            StatutoryDiscountDecisionClientResultStatuses.Approved,
            Retryable: false,
            StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
            SafeErrorCode: null,
            ValidationId,
            OriginalTariffSnapshotId,
            Guid.Parse("4b000000-0000-0000-0000-00000000000e"),
            FallbackPolicyReferenceId: null,
            "NATIONAL_LAW_FALLBACK",
            LocalOrdinanceApplied: false,
            GrossAmountMinorUnits: 12500,
            VatExclusiveAmountMinorUnits: 11161,
            VatAmountMinorUnits: 1339,
            StatutoryDiscountAmountMinorUnits: 2232,
            NetPayableAmountMinorUnits: 8929,
            "PHP",
            EvidenceRequired: false,
            EvidenceRecorded: false,
            ReasonCode: "ELIGIBLE",
            CorrelationId,
            DateTimeOffset.Parse("2026-05-29T07:50:00Z"),
            DateTimeOffset.Parse("2026-05-29T07:51:00Z"),
            DateTimeOffset.Parse("2026-05-29T08:00:00Z"),
            DateTimeOffset.Parse("2026-05-29T08:00:00Z"),
            FailedAt: null,
            DateTimeOffset.Parse("2026-05-29T08:00:00Z"));

    private static StatutoryDiscountPayableBasisApplicationV1Record ApplicationRecord() =>
        new(
            ApplicationCommandId,
            RequestReference: ValidationId,
            DecisionCommandId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            $"statutory-discount-payable-basis-application:{DecisionCommandId:N}",
            $"statutory-discount-payable-basis-application:{DecisionCommandId:N}",
            "operator-console-payable-basis-application-v1:sha256:test",
            StatutoryDiscountPayableBasisApplicationV1SemanticHash.SourceVersion,
            "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            StatutoryDiscountPayableBasisApplicationV1CommandStates.Received,
            StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress,
            Retryable: false,
            StatutoryDiscountDecisionRecoveryClassifications.None,
            SafeErrorCode: null,
            ValidationId,
            StatutoryDiscountPayableBasisApplicationId: null,
            OriginalTariffSnapshotId,
            TargetTariffSnapshotId: null,
            AppliedTariffSnapshotId: null,
            Guid.Parse("4b000000-0000-0000-0000-00000000000e"),
            "NATIONAL_LAW_FALLBACK",
            ApprovedDiscountAmountMinorUnits: 2232,
            ApprovedVatExclusiveAmountMinorUnits: 11161,
            ApprovedVatAmountMinorUnits: 1339,
            ApprovedFinalPayableAmountMinorUnits: 8929,
            "PHP",
            StatutoryDiscountSourceChannels.OperatorConsole,
            CorrelationId,
            DateTimeOffset.Parse("2026-05-29T08:01:00Z"),
            ProcessingStartedAt: null,
            AppliedAt: null,
            CompletedAt: null,
            FailedAt: null,
            DateTimeOffset.Parse("2026-05-29T08:01:00Z"));

    private static StatutoryDiscountPayableBasisApplicationV1Record AppliedApplicationRecord() =>
        ApplicationRecord() with
        {
            CommandStatus = StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied,
            ResultClassification = StatutoryDiscountPayableBasisApplicationV1ResultClassifications.Applied,
            Retryable = false,
            RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
            StatutoryDiscountPayableBasisApplicationId = ApplicationId,
            AppliedTariffSnapshotId = AppliedTariffSnapshotId,
            AppliedAt = DateTimeOffset.Parse("2026-05-29T08:02:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-05-29T08:02:00Z")
        };

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
                Guid.Parse("4b000000-0000-0000-0000-00000000000d"),
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                OperatorConsoleActionCodes.ApplyStatutoryDiscountPayableBasis,
                "STATUTORY_DISCOUNT_VALIDATION",
                TargetEntityType: null,
                TargetEntityId: null));
}
