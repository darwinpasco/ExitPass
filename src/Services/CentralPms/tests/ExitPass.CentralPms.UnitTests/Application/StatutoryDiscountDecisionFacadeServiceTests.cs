using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class StatutoryDiscountDecisionFacadeServiceTests
{
    private static readonly Guid CommandId = Guid.Parse("6d000000-0000-0000-0000-000000000001");
    private static readonly Guid RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000002");
    private static readonly Guid ParkingSessionId = Guid.Parse("6d000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteId = Guid.Parse("6d000000-0000-0000-0000-000000000004");
    private static readonly Guid SiteGroupId = Guid.Parse("6d000000-0000-0000-0000-000000000005");
    private static readonly Guid ActorUserId = Guid.Parse("6d000000-0000-0000-0000-000000000006");
    private static readonly Guid ReviewerUserId = Guid.Parse("6d000000-0000-0000-0000-000000000007");
    private static readonly Guid DeviceBindingId = Guid.Parse("6d000000-0000-0000-0000-000000000008");
    private static readonly Guid ShiftId = Guid.Parse("6d000000-0000-0000-0000-000000000009");
    private static readonly Guid ValidationId = Guid.Parse("6d000000-0000-0000-0000-00000000000a");
    private static readonly Guid PolicyId = Guid.Parse("6d000000-0000-0000-0000-00000000000b");
    private static readonly Guid OriginalTariffSnapshotId = Guid.Parse("6d000000-0000-0000-0000-00000000000c");
    private static readonly Guid AppliedTariffSnapshotId = Guid.Parse("6d000000-0000-0000-0000-00000000000d");
    private static readonly Guid PayableBasisApplicationId = Guid.Parse("6d000000-0000-0000-0000-00000000000e");
    private static readonly Guid CorrelationId = Guid.Parse("6d000000-0000-0000-0000-00000000000f");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-21T08:00:00Z");

    [Fact]
    public async Task SubmitAsync_WhenRequestIsValid_ReusesExistingOperatorConsoleWorkflow()
    {
        var fixture = CreateFixture();

        var result = await fixture.Sut.SubmitAsync(Command(), CancellationToken.None);

        result.StatutoryDiscountDecisionCommandId.Should().Be(CommandId);
        result.StatutoryDiscountValidationId.Should().Be(ValidationId);
        result.SourceChannel.Should().Be("OPERATOR_CONSOLE");
        result.DecisionStatus.Should().Be("APPLIED_PAYABLE_BASIS");
        result.GrossAmountMinorUnits.Should().Be(12500);
        result.StatutoryDiscountAmountMinorUnits.Should().Be(2232);
        result.NetPayableAmountMinorUnits.Should().Be(8929);
        result.AppliedTariffSnapshotId.Should().Be(AppliedTariffSnapshotId);

        await fixture.DraftService.Received(1).DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
        await fixture.EvidenceService.Received(1).CaptureAsync(Arg.Any<OperatorConsoleStatutoryDiscountEvidenceCaptureCommand>(), Arg.Any<CancellationToken>());
        await fixture.DecisionService.Received(1).DecideAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionCommand>(), Arg.Any<CancellationToken>());
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("OPERATOR_CONSOLE")]
    [InlineData("WEBPAY")]
    [InlineData("ASSISTED_PAYMENT_TERMINAL")]
    public async Task SubmitAsync_AttributesSupportedSourceChannelsWithoutChangingCalculationAuthority(string sourceChannel)
    {
        var fixture = CreateFixture();

        var result = await fixture.Sut.SubmitAsync(Command(sourceChannel: sourceChannel, applyPayableBasis: false), CancellationToken.None);

        result.SourceChannel.Should().Be(sourceChannel);
        fixture.Repository.LastBeginCommand!.Command.SourceChannel.Should().Be(sourceChannel);
        await fixture.DraftService.Received(1).DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenSameIdempotencyKeyAndSemanticRequest_ReplaysOriginalResult()
    {
        var fixture = CreateFixture();
        var command = Command();

        var first = await fixture.Sut.SubmitAsync(command, CancellationToken.None);
        var second = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        second.StatutoryDiscountDecisionCommandId.Should().Be(first.StatutoryDiscountDecisionCommandId);
        second.ResultClassification.Should().Be("IDEMPOTENT_REPLAY");
        await fixture.DraftService.Received(1).DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenSameIdempotencyKeyChangesSourceChannel_ReplaysOriginalResult()
    {
        var fixture = CreateFixture();
        var command = Command();
        var first = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var second = await fixture.Sut.SubmitAsync(command with { SourceChannel = "WEBPAY" }, CancellationToken.None);

        second.StatutoryDiscountDecisionCommandId.Should().Be(first.StatutoryDiscountDecisionCommandId);
        second.SourceChannel.Should().Be("OPERATOR_CONSOLE");
        second.ResultClassification.Should().Be("IDEMPOTENT_REPLAY");
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenSameIdempotencyKeyChangesRequestReference_ReplaysOriginalResult()
    {
        var fixture = CreateFixture();
        var command = Command();
        var first = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var second = await fixture.Sut.SubmitAsync(
            command with { RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000202") },
            CancellationToken.None);

        second.StatutoryDiscountDecisionCommandId.Should().Be(first.StatutoryDiscountDecisionCommandId);
        second.RequestReference.Should().Be(RequestReference);
        second.ResultClassification.Should().Be("IDEMPOTENT_REPLAY");
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenSameBusinessRequestUsesDifferentKeyAndChannel_ReplaysOriginalResult()
    {
        var fixture = CreateFixture();
        var command = Command();
        var first = await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var second = await fixture.Sut.SubmitAsync(
            command with
            {
                SourceChannel = "ASSISTED_PAYMENT_TERMINAL",
                RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000203"),
                IdempotencyKey = "statutory-discount-idempotency-key-apt"
            },
            CancellationToken.None);

        second.StatutoryDiscountDecisionCommandId.Should().Be(first.StatutoryDiscountDecisionCommandId);
        second.SourceChannel.Should().Be("OPERATOR_CONSOLE");
        second.ResultClassification.Should().Be("IDEMPOTENT_REPLAY");
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenSameIdempotencyKeyHasDifferentMaterialFacts_ReturnsConflict()
    {
        var fixture = CreateFixture();
        var command = Command();
        await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var action = () => fixture.Sut.SubmitAsync(command with { EntitlementType = "PWD" }, CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "IDEMPOTENCY_SEMANTIC_CONFLICT");
    }

    [Fact]
    public async Task SubmitAsync_WhenSameBusinessRequestChangesEvidenceFact_ReturnsConflict()
    {
        var fixture = CreateFixture();
        var command = Command();
        await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        var action = () => fixture.Sut.SubmitAsync(
            command with
            {
                IdempotencyKey = "statutory-discount-idempotency-key-evidence-change",
                EvidenceReferences =
                [
                    command.EvidenceReferences[0] with { VerificationStatus = "REJECTED" }
                ]
            },
            CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "IDEMPOTENCY_SEMANTIC_CONFLICT");
    }

    [Fact]
    public async Task SubmitAsync_WhenExistingProcessingUsesDifferentKey_ReturnsInProgressConflict()
    {
        var fixture = CreateFixture();
        var command = Command();
        fixture.Repository.SeedProcessing(command);

        var action = () => fixture.Sut.SubmitAsync(
            command with { IdempotencyKey = "statutory-discount-idempotency-key-webpay" },
            CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "STATUTORY_DISCOUNT_DECISION_IN_PROGRESS");
        await fixture.DraftService.DidNotReceive().DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenConcurrentOperatorConsoleAndWebPayRequestsArrive_OnlyOneAppliesPayableBasis()
    {
        var fixture = CreateFixture();
        fixture.Repository.DelayInsideLock = TimeSpan.FromMilliseconds(25);
        var operatorCommand = Command();
        var webPayCommand = operatorCommand with
        {
            SourceChannel = "WEBPAY",
            RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000204"),
            IdempotencyKey = "statutory-discount-idempotency-key-webpay"
        };

        var results = await Task.WhenAll(
            fixture.Sut.SubmitAsync(operatorCommand, CancellationToken.None),
            fixture.Sut.SubmitAsync(webPayCommand, CancellationToken.None));

        results.Select(result => result.StatutoryDiscountDecisionCommandId).Distinct().Should().ContainSingle();
        results.Should().Contain(result => result.ResultClassification == "ACCEPTED");
        results.Should().Contain(result => result.ResultClassification == "IDEMPOTENT_REPLAY");
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenConcurrentWebPayAndAptRequestsArrive_OnlyOneAppliesPayableBasis()
    {
        var fixture = CreateFixture();
        fixture.Repository.DelayInsideLock = TimeSpan.FromMilliseconds(25);
        var webPayCommand = Command(sourceChannel: "WEBPAY");
        var aptCommand = webPayCommand with
        {
            SourceChannel = "ASSISTED_PAYMENT_TERMINAL",
            RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000205"),
            IdempotencyKey = "statutory-discount-idempotency-key-apt"
        };

        var results = await Task.WhenAll(
            fixture.Sut.SubmitAsync(webPayCommand, CancellationToken.None),
            fixture.Sut.SubmitAsync(aptCommand, CancellationToken.None));

        results.Select(result => result.StatutoryDiscountDecisionCommandId).Distinct().Should().ContainSingle();
        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubmitAsync_WhenReplayed_DoesNotApplyPayableBasisAgain()
    {
        var fixture = CreateFixture();
        var command = Command();

        await fixture.Sut.SubmitAsync(command, CancellationToken.None);
        await fixture.Sut.SubmitAsync(command, CancellationToken.None);

        await fixture.ApplyService.Received(1).ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenReferenceExists_ReturnsCanonicalReadback()
    {
        var fixture = CreateFixture();
        var submitted = await fixture.Sut.SubmitAsync(Command(), CancellationToken.None);

        var readback = await fixture.Sut.GetAsync(submitted.StatutoryDiscountDecisionCommandId, CorrelationId, CancellationToken.None);

        readback.Should().NotBeNull();
        readback!.StatutoryDiscountDecisionCommandId.Should().Be(CommandId);
        readback.StatutoryDiscountValidationId.Should().Be(ValidationId);
        readback.SourceChannel.Should().Be("OPERATOR_CONSOLE");
    }

    [Fact]
    public async Task GetAsync_WhenReferenceMissing_ReturnsNull()
    {
        var fixture = CreateFixture();

        var readback = await fixture.Sut.GetAsync(Guid.Parse("6d000000-0000-0000-0000-000000000099"), CorrelationId, CancellationToken.None);

        readback.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_WhenFullStatutoryIdIsProvided_RejectsUnsafeIdentifier()
    {
        var fixture = CreateFixture();

        var action = () => fixture.Sut.SubmitAsync(Command(maskedIdReference: "SC-123456789"), CancellationToken.None);

        await action.Should().ThrowAsync<StatutoryDiscountDecisionRejectedException>()
            .Where(ex => ex.ErrorCode == "UNSAFE_IDENTIFIER_REJECTED");
    }

    [Fact]
    public void SemanticHash_ExcludesCorrelationId()
    {
        var first = Command(correlationId: Guid.Parse("6d000000-0000-0000-0000-000000000101"));
        var second = first with { CorrelationId = Guid.Parse("6d000000-0000-0000-0000-000000000102") };

        StatutoryDiscountDecisionSemanticHash.Compute(first)
            .Should().Be(StatutoryDiscountDecisionSemanticHash.Compute(second));
    }

    [Fact]
    public void SemanticHash_ExcludesSourceChannelAndRequestReference()
    {
        var first = Command();
        var second = first with
        {
            SourceChannel = "WEBPAY",
            RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000206")
        };

        StatutoryDiscountDecisionSemanticHash.Compute(first)
            .Should().Be(StatutoryDiscountDecisionSemanticHash.Compute(second));
    }

    [Fact]
    public void IdempotencyScope_UsesParkingSessionAndEntitlementOnly()
    {
        var first = Command();
        var second = first with
        {
            SourceChannel = "ASSISTED_PAYMENT_TERMINAL",
            RequestReference = Guid.Parse("6d000000-0000-0000-0000-000000000207")
        };

        StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(first)
            .Should().Be("statutory-discount-decision:6d000000000000000000000000000003:SENIOR_CITIZEN");
        StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(second)
            .Should().Be(StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(first));
    }

    [Fact]
    public void SemanticHash_IncludesMaterialFacts()
    {
        var first = Command();
        var second = first with { EntitlementType = "PWD" };

        StatutoryDiscountDecisionSemanticHash.Compute(first)
            .Should().NotBe(StatutoryDiscountDecisionSemanticHash.Compute(second));
    }

    private static TestFixture CreateFixture()
    {
        var repository = new InMemoryRepository();
        var draftService = Substitute.For<IOperatorConsoleStatutoryDiscountDraftService>();
        draftService.DraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftCommand>(), Arg.Any<CancellationToken>())
            .Returns(DraftResult());

        var evidenceService = Substitute.For<IOperatorConsoleStatutoryDiscountEvidenceService>();
        evidenceService.CaptureAsync(Arg.Any<OperatorConsoleStatutoryDiscountEvidenceCaptureCommand>(), Arg.Any<CancellationToken>())
            .Returns(EvidenceResult());

        var decisionService = Substitute.For<IOperatorConsoleStatutoryDiscountDecisionService>();
        decisionService.DecideAsync(Arg.Any<OperatorConsoleStatutoryDiscountDecisionCommand>(), Arg.Any<CancellationToken>())
            .Returns(DecisionResult());

        var applyService = Substitute.For<IOperatorConsoleStatutoryDiscountApplyPayableBasisService>();
        applyService.ApplyAsync(Arg.Any<OperatorConsoleStatutoryDiscountApplyPayableBasisCommand>(), Arg.Any<CancellationToken>())
            .Returns(ApplyResult());

        var readService = Substitute.For<IOperatorConsoleStatutoryDiscountReadService>();
        readService.GetDraftAsync(Arg.Any<OperatorConsoleStatutoryDiscountDraftDetailQuery>(), Arg.Any<CancellationToken>())
            .Returns(DetailResult());

        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);

        var sut = new StatutoryDiscountDecisionFacadeService(
            repository,
            draftService,
            evidenceService,
            decisionService,
            applyService,
            readService,
            clock);

        return new TestFixture(repository, draftService, evidenceService, decisionService, applyService, sut);
    }

    private static StatutoryDiscountDecisionCommand Command(
        string sourceChannel = "OPERATOR_CONSOLE",
        string entitlementType = "SENIOR_CITIZEN",
        bool applyPayableBasis = true,
        string maskedIdReference = "SC-****-1234",
        Guid? correlationId = null) =>
        new(
            RequestReference,
            sourceChannel,
            ParkingSessionId,
            SiteId,
            SiteGroupId,
            "TICKET-001",
            "ABC1234",
            entitlementType,
            "SENIOR_CITIZEN_ID",
            "OSCA",
            DateOnly.Parse("2030-01-01"),
            maskedIdReference,
            true,
            [new StatutoryDiscountEvidenceReference(
                "SENIOR_CITIZEN_ID",
                "MANUAL_REFERENCE",
                null,
                null,
                null,
                "evidence-ref-001",
                "SC-****-1234",
                "VERIFIED")],
            ActorUserId,
            DeviceBindingId,
            ShiftId,
            true,
            "attested",
            "CUSTOMER_REQUEST",
            "APPROVE",
            "ELIGIBLE",
            ReviewerUserId,
            true,
            applyPayableBasis,
            OriginalTariffSnapshotId,
            "statutory-discount-idempotency-key",
            correlationId ?? CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftResult DraftResult() =>
        new(
            Guid.Parse("6d000000-0000-0000-0000-000000000020"),
            true,
            "ALLOWED",
            [],
            true,
            true,
            true,
            ValidationId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            true,
            true,
            true,
            Guid.Parse("6d000000-0000-0000-0000-000000000021"),
            false,
            null,
            null,
            null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountEvidenceCaptureResult EvidenceResult() =>
        new(
            Guid.Parse("6d000000-0000-0000-0000-000000000030"),
            ValidationId,
            "SENIOR_CITIZEN_ID",
            "MANUAL_REFERENCE",
            null,
            null,
            null,
            "evidence-ref-001",
            "SC-****-1234",
            ActorUserId,
            Now,
            "NOT_REDACTED",
            "PENDING_REVIEW",
            true,
            "REQUESTED",
            true,
            null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDecisionResult DecisionResult() =>
        new(
            Guid.Parse("6d000000-0000-0000-0000-000000000040"),
            true,
            "ALLOWED",
            [],
            true,
            true,
            true,
            ValidationId,
            ParkingSessionId,
            "SENIOR_CITIZEN",
            "REQUESTED",
            "APPROVED",
            "APPROVE",
            "ELIGIBLE",
            false,
            true,
            null,
            null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountApplyPayableBasisResult ApplyResult() =>
        new(
            Guid.Parse("6d000000-0000-0000-0000-000000000050"),
            true,
            "ALLOWED",
            [],
            true,
            true,
            true,
            PayableBasisApplicationId,
            ValidationId,
            ParkingSessionId,
            OriginalTariffSnapshotId,
            AppliedTariffSnapshotId,
            "APPLIED",
            false,
            12500,
            1339,
            11161,
            2232,
            8929,
            "PHP",
            PolicyId,
            null,
            "NATIONAL_LAW_FALLBACK",
            "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            "RA 9994",
            null,
            true,
            null,
            null,
            CorrelationId);

    private static OperatorConsoleStatutoryDiscountDraftDetailResult DetailResult() =>
        new(
            ValidationId,
            ParkingSessionId,
            "TICKET-001",
            "ABC1234",
            SiteId,
            "Site",
            SiteGroupId,
            "SENIOR_CITIZEN",
            "APPROVED",
            true,
            true,
            true,
            1,
            "PENDING_REVIEW",
            ["SENIOR_CITIZEN_ID"],
            Now,
            Now.AddMinutes(1),
            ActorUserId,
            ReviewerUserId,
            "ELIGIBLE",
            null,
            "NATIONAL_LAW_FALLBACK",
            PolicyId,
            null,
            "PH_RA9994_SENIOR_CITIZEN_NATIONAL_FALLBACK",
            "Senior Citizen National Fallback",
            "RA 9994",
            null,
            "RA 9994",
            "ACTIVE",
            "STATUTORY_DISCOUNT_VAT_EXEMPT",
            null,
            "APPLY_NATIONAL_STATUTORY_DISCOUNT",
            "VAT_EXCLUSIVE",
            "STATUTORY_FIRST",
            null,
            OriginalTariffSnapshotId,
            PayableBasisApplicationId,
            "APPLIED",
            AppliedTariffSnapshotId,
            12500,
            1339,
            11161,
            2232,
            8929,
            8929,
            "PHP",
            []);

    private sealed record TestFixture(
        InMemoryRepository Repository,
        IOperatorConsoleStatutoryDiscountDraftService DraftService,
        IOperatorConsoleStatutoryDiscountEvidenceService EvidenceService,
        IOperatorConsoleStatutoryDiscountDecisionService DecisionService,
        IOperatorConsoleStatutoryDiscountApplyPayableBasisService ApplyService,
        StatutoryDiscountDecisionFacadeService Sut);

    private sealed class InMemoryRepository : IStatutoryDiscountDecisionFacadeRepository
    {
        private StatutoryDiscountDecisionCommandRecord? _record;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public StatutoryDiscountDecisionRepositoryCommand? LastBeginCommand { get; private set; }

        public TimeSpan DelayInsideLock { get; set; }

        public async Task<T> ExecuteWithCommandLockAsync<T>(
            StatutoryDiscountDecisionRepositoryCommand command,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (DelayInsideLock > TimeSpan.Zero)
                {
                    await Task.Delay(DelayInsideLock, cancellationToken);
                }

                return await operation(cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        public void SeedProcessing(StatutoryDiscountDecisionCommand command)
        {
            var normalized = command with
            {
                SourceChannel = StatutoryDiscountSourceChannels.Normalize(command.SourceChannel),
                EntitlementType = command.EntitlementType.Trim().ToUpperInvariant()
            };
            _record = CreateRecord(new StatutoryDiscountDecisionRepositoryCommand(
                normalized,
                StatutoryDiscountDecisionSemanticHash.BuildIdempotencyScope(normalized),
                StatutoryDiscountDecisionSemanticHash.Compute(normalized),
                StatutoryDiscountDecisionSemanticHash.SourceVersion,
                Now));
        }

        public Task<StatutoryDiscountDecisionBeginResult> BeginAsync(
            StatutoryDiscountDecisionRepositoryCommand command,
            CancellationToken cancellationToken)
        {
            LastBeginCommand = command;
            if (_record is not null)
            {
                var conflict = !string.Equals(_record.SemanticRequestHash, command.SemanticRequestHash, StringComparison.Ordinal);
                return Task.FromResult(new StatutoryDiscountDecisionBeginResult(Existing: true, conflict, _record));
            }

            _record = CreateRecord(command);

            return Task.FromResult(new StatutoryDiscountDecisionBeginResult(Existing: false, SemanticConflict: false, _record));
        }

        public Task<StatutoryDiscountDecisionCommandRecord> CompleteAsync(
            StatutoryDiscountDecisionCommandRecord record,
            CancellationToken cancellationToken)
        {
            _record = record;
            return Task.FromResult(record);
        }

        public Task<StatutoryDiscountDecisionCommandRecord?> GetAsync(
            Guid statutoryDiscountDecisionCommandId,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_record?.StatutoryDiscountDecisionCommandId == statutoryDiscountDecisionCommandId
                ? _record with { CorrelationId = correlationId }
                : null);

        private static StatutoryDiscountDecisionCommandRecord CreateRecord(
            StatutoryDiscountDecisionRepositoryCommand command) =>
            new(
                CommandId,
                command.Command.RequestReference,
                command.Command.ParkingSessionId,
                command.Command.SourceChannel,
                command.Command.EntitlementType,
                command.Command.IdempotencyKey,
                "PROCESSING",
                "ACCEPTED",
                command.IdempotencyScope,
                command.SemanticHashSourceVersion,
                command.SemanticRequestHash,
                null,
                null,
                command.Command.OriginalTariffSnapshotId,
                null,
                null,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                command.Command.EvidenceCaptureRequested,
                false,
                null,
                null,
                command.Command.CorrelationId,
                command.RequestedAt,
                null,
                null);
    }
}
