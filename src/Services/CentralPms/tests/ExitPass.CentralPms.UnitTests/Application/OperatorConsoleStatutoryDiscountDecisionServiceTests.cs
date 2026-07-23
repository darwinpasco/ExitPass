using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests access-gated Operator Console statutory discount validation decision behavior.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountDecisionServiceTests
{
    private static readonly Guid EvaluationId = Guid.Parse("49000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("49000000-0000-0000-0000-000000000002");
    private static readonly Guid DeviceBindingId = Guid.Parse("49000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("49000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("49000000-0000-0000-0000-000000000005");
    private static readonly Guid ShiftId = Guid.Parse("49000000-0000-0000-0000-000000000006");
    private static readonly Guid DraftId = Guid.Parse("49000000-0000-0000-0000-000000000007");
    private static readonly Guid ParkingSessionId = Guid.Parse("49000000-0000-0000-0000-000000000008");
    private static readonly Guid CorrelationId = Guid.Parse("49000000-0000-0000-0000-000000000009");
    private static readonly Guid DecisionCommandId = Guid.Parse("49000000-0000-0000-0000-000000000011");
    private static readonly Guid RequestedByUserId = Guid.Parse("49000000-0000-0000-0000-000000000012");
    private static readonly Guid PolicyId = Guid.Parse("49000000-0000-0000-0000-000000000013");
    private static readonly Guid TariffSnapshotId = Guid.Parse("49000000-0000-0000-0000-000000000014");

    /// <summary>
    /// Verifies access denial is persisted and prevents decision persistence.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenAccessDenied_DoesNotPersistDecision()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        var sut = CreateSut(AccessResult(allowed: false, ["NO_ACTIVE_SHIFT"]), writer);

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeFalse();
        result.AccessPersisted.Should().BeTrue();
        result.DecisionAccepted.Should().BeFalse();
        result.DecisionPersisted.Should().BeFalse();
        result.IneligibilityReason.Should().Be("ACCESS_DENIED");
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies an allowed approval persists an approved review status.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenApprovalAllowed_PersistsApprovedStatus()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Persisted("APPROVE", "REQUESTED", "APPROVED", decisionChanged: true));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.DecisionAccepted.Should().BeTrue();
        result.DecisionPersisted.Should().BeTrue();
        result.PreviousValidationStatus.Should().Be("REQUESTED");
        result.CurrentValidationStatus.Should().Be("APPROVED");
        result.DecisionChanged.Should().BeTrue();
        result.StatutoryDiscountDecisionCommandId.Should().Be(DecisionCommandId);

        await writer.Received(1).PersistAsync(
            Arg.Is<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(request =>
                request.DraftId == DraftId &&
                request.Decision == "APPROVE" &&
                request.TargetValidationStatus == "APPROVED" &&
                request.DecidedByUserId == UserId &&
                request.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies reject requires and preserves a reason code.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenRejectAllowed_PersistsRejectedStatusWithReason()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Persisted("REJECT", "REQUESTED", "REJECTED", decisionChanged: true, reason: "ID_NOT_VALID"));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.DecideAsync(Command(decision: "REJECT", reason: "ID_NOT_VALID"), CancellationToken.None);

        result.DecisionAccepted.Should().BeTrue();
        result.CurrentValidationStatus.Should().Be("REJECTED");
        result.DecisionReasonCode.Should().Be("ID_NOT_VALID");
        result.StatutoryDiscountDecisionCommandId.Should().Be(DecisionCommandId);
    }

    /// <summary>
    /// Verifies the legacy route resolves an existing completed canonical decision without rewriting it.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenCanonicalDecisionAlreadyCompleted_ReturnsCanonicalReplay()
    {
        var staged = Substitute.For<IStatutoryDiscountStagedCommandService>();
        staged.CreateOrResolveDecisionAsync(Arg.Any<StatutoryDiscountDecisionV2Command>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                var command = info.Arg<StatutoryDiscountDecisionV2Command>();
                return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>(
                    StatutoryDiscountDecisionClientResultStatuses.IdempotentReplay,
                    Existing: true,
                    SemanticConflict: false,
                    Retryable: false,
                    StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                    CompletedDecision(command),
                    SafeErrorCode: null);
            });

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        var sut = CreateSut(AccessResult(allowed: true, []), writer, staged: staged);

        var result = await sut.DecideAsync(Command(correlationId: Guid.NewGuid()), CancellationToken.None);

        result.DecisionAccepted.Should().BeTrue();
        result.AlreadyDecided.Should().BeTrue();
        result.StatutoryDiscountDecisionCommandId.Should().Be(DecisionCommandId);
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies material canonical decision conflicts are surfaced before legacy decision persistence.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenCanonicalDecisionSemanticConflict_ThrowsConflict()
    {
        var staged = Substitute.For<IStatutoryDiscountStagedCommandService>();
        staged.CreateOrResolveDecisionAsync(Arg.Any<StatutoryDiscountDecisionV2Command>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                var command = info.Arg<StatutoryDiscountDecisionV2Command>();
                return new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>(
                    StatutoryDiscountDecisionClientResultStatuses.SemanticConflict,
                    Existing: true,
                    SemanticConflict: true,
                    Retryable: false,
                    StatutoryDiscountDecisionRecoveryClassifications.CorrectRequestRequired,
                    NewDecision(command),
                    "IDEMPOTENCY_SEMANTIC_CONFLICT");
            });

        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        var sut = CreateSut(AccessResult(allowed: true, []), writer, staged: staged);

        var action = () => sut.DecideAsync(Command(reason: "UPDATED_FACT"), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<OperatorConsoleStatutoryDiscountDecisionConflictException>();
        exception.Which.CurrentStatus.Should().Be("CANONICAL_SEMANTIC_CONFLICT");
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies the legacy decision route does not invoke the payable-basis application stage.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenApprovalAllowed_DoesNotCreateApplicationCommand()
    {
        var staged = new FakeStagedCommandService();
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Persisted("APPROVE", "REQUESTED", "APPROVED", decisionChanged: true));

        var sut = CreateSut(AccessResult(allowed: true, []), writer, staged: staged);

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.DecisionAccepted.Should().BeTrue();
        staged.ApplicationCreateAttempts.Should().Be(0);
    }

    /// <summary>
    /// Verifies same terminal decision replay is deterministic.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenSameDecisionAlreadyTerminal_ReturnsAlreadyDecided()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();
        writer.PersistAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionPersistenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Persisted("APPROVE", "APPROVED", "APPROVED", alreadyDecided: true));

        var sut = CreateSut(AccessResult(allowed: true, []), writer);

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.DecisionAccepted.Should().BeTrue();
        result.DecisionPersisted.Should().BeTrue();
        result.AlreadyDecided.Should().BeTrue();
        result.DecisionChanged.Should().BeFalse();
    }

    /// <summary>
    /// Verifies evidence-required approvals can be blocked without mutating decision status.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenEvidenceRequiredNotCaptured_ReturnsIneligible()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();

        var sut = CreateSut(
            AccessResult(allowed: true, []),
            writer,
            detail: Detail(evidenceRequired: true, evidenceCaptured: false));

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.DecisionAccepted.Should().BeFalse();
        result.DecisionPersisted.Should().BeFalse();
        result.ErrorCode.Should().Be("EVIDENCE_REQUIRED_NOT_CAPTURED");
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies requester-versus-approver segregation denial is surfaced without decision persistence.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenRequesterApprovesOwnDraft_ReturnsSegregationDenial()
    {
        var writer = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();

        var sut = CreateSut(
            AccessResult(allowed: true, []),
            writer,
            detail: Detail(requestedByUserId: UserId));

        var result = await sut.DecideAsync(Command(), CancellationToken.None);

        result.AccessAllowed.Should().BeTrue();
        result.DecisionAccepted.Should().BeFalse();
        result.DecisionPersisted.Should().BeFalse();
        result.CurrentValidationStatus.Should().Be("REQUESTED");
        result.IneligibilityReason.Should().Be("REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT");
        result.ErrorCode.Should().Be("REQUESTER_CANNOT_APPROVE_OWN_DISCOUNT");
        await writer.DidNotReceiveWithAnyArgs().PersistAsync(default!, default);
    }

    /// <summary>
    /// Verifies unsupported decisions are rejected before persistence.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenDecisionUnsupported_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DecideAsync(Command(decision: "ESCALATE"), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Decision must be APPROVE or REJECT*");
    }

    /// <summary>
    /// Verifies reject decisions require a reason.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenRejectReasonMissing_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DecideAsync(Command(decision: "REJECT", reason: null), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*DecisionReasonCode is required for REJECT*");
    }

    /// <summary>
    /// Verifies reviewer attestation is required.
    /// </summary>
    [Fact]
    public async Task DecideAsync_WhenReviewerAttestationFalse_ThrowsValidationError()
    {
        var sut = CreateSut(AccessResult(allowed: true, []));

        var action = () => sut.DecideAsync(Command(reviewerAttestation: false), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ReviewerAttestation must be true*");
    }

    private static OperatorConsoleStatutoryDiscountDecisionService CreateSut(
        OperatorConsoleAccessEvaluationResult accessResult,
        IOperatorConsoleStatutoryDiscountDecisionWriter? decisionWriter = null,
        OperatorConsoleStatutoryDiscountDraftDetailResult? detail = null,
        IStatutoryDiscountStagedCommandService? staged = null)
    {
        var accessService = Substitute.For<IOperatorConsoleAccessEvaluationService>();
        accessService.EvaluateAsync(Arg.Any<OperatorConsoleAccessEvaluationCommand>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = Guid.Empty, Persisted = false });

        var accessWriter = Substitute.For<IOperatorConsoleAccessEvaluationWriter>();
        accessWriter.PersistAsync(Arg.Any<OperatorConsoleAccessEvaluationResult>(), Arg.Any<CancellationToken>())
            .Returns(accessResult with { EvaluationId = EvaluationId, Persisted = true });

        decisionWriter ??= Substitute.For<IOperatorConsoleStatutoryDiscountDecisionWriter>();

        var readService = Substitute.For<IOperatorConsoleStatutoryDiscountReadService>();
        readService.GetDraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftDetailQuery>(), Arg.Any<CancellationToken>())
            .Returns(detail ?? Detail());

        var evidenceRepository = Substitute.For<IOperatorConsoleStatutoryDiscountEvidenceRepository>();
        evidenceRepository.ListAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EvidenceList());

        return new OperatorConsoleStatutoryDiscountDecisionService(
            accessService,
            accessWriter,
            decisionWriter,
            readService,
            evidenceRepository,
            staged ?? new FakeStagedCommandService());
    }

    private static OperatorConsoleStatutoryDiscountDecisionCommand Command(
        string decision = "APPROVE",
        string? reason = null,
        bool reviewerAttestation = true,
        Guid? correlationId = null) =>
        new(
            DraftId,
            UserId,
            DeviceBindingId,
            SiteId,
            SiteGroupId,
            ShiftId,
            decision,
            reason,
            DecisionNotes: "Manual review decision.",
            reviewerAttestation,
            "operator-console-statutory-discount-decision-test",
            correlationId ?? CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftDetailResult Detail(
        bool evidenceRequired = false,
        bool evidenceCaptured = false,
        Guid? requestedByUserId = null,
        string validationStatus = "REQUESTED",
        Guid? statutoryDiscountDecisionCommandId = null) =>
        new(
            DraftId,
            ParkingSessionId,
            "MANUAL-SESSION-LOOKUP-001",
            "ABC123",
            SiteId,
            "Unit Test Site",
            SiteGroupId,
            "SENIOR_CITIZEN",
            validationStatus,
            statutoryDiscountDecisionCommandId,
            "SENIOR_CITIZEN_ID",
            "OSCA",
            ExpiryDate: null,
            "****1234",
            RequesterAttestation: true,
            "Unit-test attestation.",
            evidenceRequired,
            evidenceCaptured,
            evidenceCaptured,
            evidenceCaptured ? 1 : 0,
            evidenceCaptured ? "CAPTURED" : null,
            evidenceRequired ? ["SENIOR_CITIZEN_ID"] : [],
            DateTimeOffset.Parse("2026-07-23T08:00:00Z"),
            ValidatedAt: null,
            requestedByUserId ?? RequestedByUserId,
            ValidatedByUserId: null,
            DecisionReasonCode: null,
            FailureReasonCode: null,
            "NATIONAL_LAW_FALLBACK",
            PolicyId,
            ResolvedJurisdictionId: null,
            "RA9994",
            "Senior Citizen National Statutory Discount",
            "RA9994",
            OrdinanceReference: null,
            "RA9994",
            "ACTIVE",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            FreeDurationMinutes: null,
            "APPLY_NATIONAL_STATUTORY_DISCOUNT",
            "VAT_EXCLUSIVE",
            "STATUTORY_FIRST",
            PolicySnapshot: null,
            TariffSnapshotId,
            PayableBasisApplicationId: null,
            PayableBasisApplicationStatus: null,
            AppliedTariffSnapshotId: null,
            OriginalAmountMinorUnits: 10000,
            VatAmountMinorUnits: 1071,
            VatExclusiveAmountMinorUnits: 8929,
            StatutoryDiscountAmountMinorUnits: 1786,
            PayableAmountMinorUnits: 8214,
            FinalPayableAmountMinorUnits: 8214,
            "PHP",
            []);

    private static OperatorConsoleStatutoryDiscountEvidenceListResult EvidenceList() =>
        new(
            DraftId,
            EvidenceRequired: false,
            EvidenceRequiredSatisfied: false,
            RequiredEvidenceTypes: [],
            EvidenceCount: 0,
            LatestEvidenceStatus: null,
            Items: [],
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDecisionPersistenceResult Persisted(
        string decision,
        string previousStatus,
        string currentStatus,
        bool decisionChanged = false,
        bool alreadyDecided = false,
        string? reason = null) =>
        new(
            Found: true,
            DecisionAccepted: true,
            DecisionPersisted: true,
            DraftId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            previousStatus,
            currentStatus,
            decision,
            reason,
            alreadyDecided,
            decisionChanged,
            IneligibilityReason: null,
            ErrorCode: null);

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
                Guid.Parse("49000000-0000-0000-0000-000000000010"),
                DeviceBindingId,
                ShiftId,
                ShiftTakeoverId: null,
                SiteGroupId,
                SiteId,
                OperatorConsoleActionCodes.DecideStatutoryDiscount,
                "STATUTORY_DISCOUNT_VALIDATION",
                TargetEntityType: null,
                TargetEntityId: null));

    private static StatutoryDiscountDecisionV2Record NewDecision(StatutoryDiscountDecisionV2Command command)
    {
        var now = DateTimeOffset.Parse("2026-07-23T08:00:00Z");
        return new StatutoryDiscountDecisionV2Record(
            DecisionCommandId,
            command.RequestReference,
            command.ParkingSessionId,
            command.SourceChannel,
            command.EntitlementType,
            StatutoryDiscountDecisionV2SemanticHash.BuildBusinessIdentity(command),
            StatutoryDiscountDecisionV2SemanticHash.BuildIdempotencyScope(command),
            command.IdempotencyKey,
            StatutoryDiscountDecisionV2SemanticHash.SourceVersion,
            StatutoryDiscountDecisionV2SemanticHash.Compute(command),
            StatutoryDiscountDecisionV2CommandStates.Received,
            StatutoryDiscountDecisionV2ResultStates.NotDecided,
            StatutoryDiscountDecisionClientResultStatuses.InProgress,
            Retryable: true,
            StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey,
            SafeErrorCode: null,
            StatutoryDiscountValidationId: null,
            command.OriginalTariffSnapshotId,
            command.AppliedPolicyReferenceId,
            command.FallbackPolicyReferenceId,
            command.PolicyResolutionBasis,
            command.LocalOrdinanceApplied,
            GrossAmountMinorUnits: null,
            VatExclusiveAmountMinorUnits: null,
            VatAmountMinorUnits: null,
            StatutoryDiscountAmountMinorUnits: null,
            NetPayableAmountMinorUnits: null,
            Currency: null,
            EvidenceRequired: false,
            EvidenceRecorded: false,
            ReasonCode: null,
            command.CorrelationId,
            now,
            ProcessingStartedAt: null,
            DecidedAt: null,
            CompletedAt: null,
            FailedAt: null,
            UpdatedAt: now);
    }

    private static StatutoryDiscountDecisionV2Record CompletedDecision(StatutoryDiscountDecisionV2Command command) =>
        NewDecision(command) with
        {
            CommandStatus = StatutoryDiscountDecisionV2CommandStates.Completed,
            DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.Approved,
            ResultClassification = StatutoryDiscountDecisionClientResultStatuses.Approved,
            Retryable = false,
            RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
            StatutoryDiscountValidationId = DraftId,
            GrossAmountMinorUnits = 10000,
            VatExclusiveAmountMinorUnits = 8929,
            VatAmountMinorUnits = 1071,
            StatutoryDiscountAmountMinorUnits = 1786,
            NetPayableAmountMinorUnits = 8214,
            Currency = "PHP",
            DecidedAt = DateTimeOffset.Parse("2026-07-23T08:01:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-07-23T08:01:00Z")
        };

    private sealed class FakeStagedCommandService : IStatutoryDiscountStagedCommandService
    {
        private StatutoryDiscountDecisionV2Record? _decision;

        public int ApplicationCreateAttempts { get; private set; }

        public Task<StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>> CreateOrResolveDecisionAsync(
            StatutoryDiscountDecisionV2Command command,
            CancellationToken cancellationToken)
        {
            _decision ??= OperatorConsoleStatutoryDiscountDecisionServiceTests.NewDecision(command);
            return Task.FromResult(new StagedStatutoryDiscountCommandStartResult<StatutoryDiscountDecisionV2Record>(
                StatutoryDiscountDecisionClientResultStatuses.InProgress,
                Existing: false,
                SemanticConflict: false,
                Retryable: true,
                StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey,
                _decision,
                SafeErrorCode: null));
        }

        public Task<StatutoryDiscountDecisionV2Record?> GetDecisionAsync(
            Guid statutoryDiscountDecisionCommandId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_decision?.StatutoryDiscountDecisionCommandId == statutoryDiscountDecisionCommandId ? _decision : null);

        public Task<StatutoryDiscountDecisionV2Record> MarkDecisionProcessingAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _decision = RequireDecision(statutoryDiscountDecisionCommandId) with
            {
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.Processing,
                CorrelationId = correlationId
            };
            return Task.FromResult(_decision);
        }

        public Task<StatutoryDiscountDecisionV2Record> CompleteDecisionApprovedAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid? statutoryDiscountValidationId,
            Guid? originalTariffSnapshotId,
            Guid? appliedPolicyReferenceId,
            Guid? fallbackPolicyReferenceId,
            string? policyResolutionBasis,
            bool localOrdinanceApplied,
            StatutoryDiscountDecisionV2TariffFacts? tariffFacts,
            string? reasonCode,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _decision = RequireDecision(statutoryDiscountDecisionCommandId) with
            {
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.Completed,
                DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.Approved,
                ResultClassification = StatutoryDiscountDecisionClientResultStatuses.Approved,
                Retryable = false,
                RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                StatutoryDiscountValidationId = statutoryDiscountValidationId,
                OriginalTariffSnapshotId = originalTariffSnapshotId,
                AppliedPolicyReferenceId = appliedPolicyReferenceId,
                FallbackPolicyReferenceId = fallbackPolicyReferenceId,
                PolicyResolutionBasis = policyResolutionBasis,
                LocalOrdinanceApplied = localOrdinanceApplied,
                GrossAmountMinorUnits = tariffFacts?.GrossAmountMinorUnits,
                VatExclusiveAmountMinorUnits = tariffFacts?.VatExclusiveAmountMinorUnits,
                VatAmountMinorUnits = tariffFacts?.VatAmountMinorUnits,
                StatutoryDiscountAmountMinorUnits = tariffFacts?.StatutoryDiscountAmountMinorUnits,
                NetPayableAmountMinorUnits = tariffFacts?.NetPayableAmountMinorUnits,
                Currency = tariffFacts?.Currency,
                ReasonCode = reasonCode,
                CorrelationId = correlationId,
                DecidedAt = DateTimeOffset.Parse("2026-07-23T08:01:00Z"),
                CompletedAt = DateTimeOffset.Parse("2026-07-23T08:01:00Z")
            };
            return Task.FromResult(_decision);
        }

        public Task<StatutoryDiscountDecisionV2Record> CompleteDecisionRejectedAsync(
            Guid statutoryDiscountDecisionCommandId,
            string? reasonCode,
            string? safeErrorCode,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _decision = RequireDecision(statutoryDiscountDecisionCommandId) with
            {
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.Completed,
                DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.Rejected,
                ResultClassification = StatutoryDiscountDecisionClientResultStatuses.RejectedOrNonApproved,
                Retryable = false,
                RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
                SafeErrorCode = safeErrorCode,
                ReasonCode = reasonCode,
                CorrelationId = correlationId,
                DecidedAt = DateTimeOffset.Parse("2026-07-23T08:01:00Z"),
                CompletedAt = DateTimeOffset.Parse("2026-07-23T08:01:00Z")
            };
            return Task.FromResult(_decision);
        }

        public Task<StatutoryDiscountDecisionV2Record> RecordDecisionFailureAsync(
            Guid statutoryDiscountDecisionCommandId,
            bool retryable,
            string safeErrorCode,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            _decision = RequireDecision(statutoryDiscountDecisionCommandId) with
            {
                CommandStatus = retryable
                    ? StatutoryDiscountDecisionV2CommandStates.FailedRetryable
                    : StatutoryDiscountDecisionV2CommandStates.FailedNonRetryable,
                Retryable = retryable,
                SafeErrorCode = safeErrorCode,
                CorrelationId = correlationId
            };
            return Task.FromResult(_decision);
        }

        public Task<StagedStatutoryDiscountCommandStartResult<StatutoryDiscountPayableBasisApplicationV1Record>> CreateOrResolveApplicationAsync(
            StatutoryDiscountPayableBasisApplicationV1Command command,
            CancellationToken cancellationToken)
        {
            ApplicationCreateAttempts++;
            throw new NotSupportedException("Legacy decision convergence must not create application commands.");
        }

        public Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationAsync(
            Guid statutoryDiscountPayableBasisApplicationCommandId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StatutoryDiscountPayableBasisApplicationV1Record?>(null);

        public Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationByDecisionAsync(
            Guid statutoryDiscountDecisionCommandId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StatutoryDiscountPayableBasisApplicationV1Record?>(null);

        public Task<StatutoryDiscountPayableBasisApplicationV1Record> MarkApplicationProcessingAsync(
            Guid statutoryDiscountPayableBasisApplicationCommandId,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Legacy decision convergence must not process application commands.");

        public Task<StatutoryDiscountPayableBasisApplicationV1Record> CompleteApplicationAppliedAsync(
            Guid statutoryDiscountPayableBasisApplicationCommandId,
            Guid? statutoryDiscountPayableBasisApplicationId,
            Guid? appliedTariffSnapshotId,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Legacy decision convergence must not complete application commands.");

        public Task<StatutoryDiscountPayableBasisApplicationV1Record> RecordApplicationFailureAsync(
            Guid statutoryDiscountPayableBasisApplicationCommandId,
            bool retryable,
            string safeErrorCode,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Legacy decision convergence must not record application commands.");

        private StatutoryDiscountDecisionV2Record RequireDecision(Guid statutoryDiscountDecisionCommandId) =>
            _decision?.StatutoryDiscountDecisionCommandId == statutoryDiscountDecisionCommandId
                ? _decision
                : throw new InvalidOperationException("The staged decision command was not created.");
    }
}
