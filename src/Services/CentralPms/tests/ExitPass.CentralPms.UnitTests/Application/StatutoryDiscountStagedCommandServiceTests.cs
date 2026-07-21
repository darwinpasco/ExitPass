using ExitPass.CentralPms.Application.StatutoryDiscounts;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class StatutoryDiscountStagedCommandServiceTests
{
    private static readonly Guid ParkingSessionId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid SiteGroupId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid TariffSnapshotId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid ActorUserId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid ReviewerUserId = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid CorrelationId = Guid.Parse("77777777-7777-4777-8777-777777777777");

    [Fact]
    public async Task CreateOrResolveDecisionAsync_WhenNewCommand_CreatesReceivedDecisionV2()
    {
        var fixture = new Fixture();

        var result = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(), CancellationToken.None);

        result.Existing.Should().BeFalse();
        result.SemanticConflict.Should().BeFalse();
        result.Record.Should().NotBeNull();
        result.Record!.CommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.Received);
        result.Record.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.NotDecided);
        result.Record.BusinessIdentity.Should().Be($"statutory-discount-decision:{ParkingSessionId:N}:SENIOR_CITIZEN");
        result.Record.SemanticHashSourceVersion.Should().Be(StatutoryDiscountDecisionV2SemanticHash.SourceVersion);
    }

    [Fact]
    public async Task CreateOrResolveDecisionAsync_WhenSameSemanticCommandReplays_ReturnsExisting()
    {
        var fixture = new Fixture();
        var command = DecisionCommand();

        var first = await fixture.Sut.CreateOrResolveDecisionAsync(command, CancellationToken.None);
        var second = await fixture.Sut.CreateOrResolveDecisionAsync(command with
        {
            RequestReference = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid()
        }, CancellationToken.None);

        second.Existing.Should().BeTrue();
        second.SemanticConflict.Should().BeFalse();
        second.Record!.StatutoryDiscountDecisionCommandId.Should().Be(first.Record!.StatutoryDiscountDecisionCommandId);
        fixture.Repository.DecisionCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrResolveDecisionAsync_WhenMaterialEvidenceChanges_ReturnsSemanticConflict()
    {
        var fixture = new Fixture();
        await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(), CancellationToken.None);

        var conflict = await fixture.Sut.CreateOrResolveDecisionAsync(
            DecisionCommand(idempotencyKey: "changed-key") with
            {
                EvidenceReferences =
                [
                    Evidence("REJECTED")
                ]
            },
            CancellationToken.None);

        conflict.SemanticConflict.Should().BeTrue();
        conflict.ResultClassification.Should().Be(StatutoryDiscountDecisionClientResultStatuses.SemanticConflict);
        fixture.Repository.DecisionCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrResolveDecisionAsync_WhenExistingProcessingUsesOriginalKey_ReturnsRecoverable()
    {
        var fixture = new Fixture();
        var first = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(), CancellationToken.None);
        await fixture.Sut.MarkDecisionProcessingAsync(first.Record!.StatutoryDiscountDecisionCommandId, Guid.NewGuid(), CancellationToken.None);

        var replay = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(), CancellationToken.None);

        replay.Existing.Should().BeTrue();
        replay.Retryable.Should().BeTrue();
        replay.RecoveryClassification.Should().Be(StatutoryDiscountDecisionRecoveryClassifications.RetryOriginalIdempotencyKey);
    }

    [Fact]
    public async Task CreateOrResolveDecisionAsync_WhenExistingV1SameBusinessExists_ReturnsSourceVersionAwareConflict()
    {
        var fixture = new Fixture();
        fixture.Repository.SeedDecision(DecisionRecord(
            businessIdentity: $"statutory-discount-decision:{ParkingSessionId:N}:SENIOR_CITIZEN",
            sourceVersion: StatutoryDiscountDecisionSemanticHash.SourceVersion,
            semanticHash: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));

        var result = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(), CancellationToken.None);

        result.SemanticConflict.Should().BeTrue();
        result.Record!.SemanticHashSourceVersion.Should().Be(StatutoryDiscountDecisionSemanticHash.SourceVersion);
    }

    [Fact]
    public async Task CompleteDecisionApprovedAsync_PersistsApprovedResult()
    {
        var fixture = new Fixture();
        var created = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(), CancellationToken.None);

        var approved = await fixture.Sut.CompleteDecisionApprovedAsync(
            created.Record!.StatutoryDiscountDecisionCommandId,
            Guid.NewGuid(),
            TariffSnapshotId,
            Guid.NewGuid(),
            fallbackPolicyReferenceId: null,
            "NATIONAL_DEFAULT",
            localOrdinanceApplied: false,
            new StatutoryDiscountDecisionV2TariffFacts(10000, 8929, 1071, 1786, 8214, "php"),
            "ELIGIBLE",
            CorrelationId,
            CancellationToken.None);

        approved.CommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.Completed);
        approved.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.Approved);
        approved.StatutoryDiscountAmountMinorUnits.Should().Be(1786);
        approved.Currency.Should().Be("PHP");
    }

    [Fact]
    public async Task CompleteDecisionRejectedAsync_PersistsRejectedResult()
    {
        var fixture = new Fixture();
        var created = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(decision: "REJECT"), CancellationToken.None);

        var rejected = await fixture.Sut.CompleteDecisionRejectedAsync(
            created.Record!.StatutoryDiscountDecisionCommandId,
            "INELIGIBLE",
            "STATUTORY_DISCOUNT_INELIGIBLE",
            CorrelationId,
            CancellationToken.None);

        rejected.CommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.Completed);
        rejected.DecisionResultStatus.Should().Be(StatutoryDiscountDecisionV2ResultStates.Rejected);
        rejected.SafeErrorCode.Should().Be("STATUTORY_DISCOUNT_INELIGIBLE");
    }

    [Fact]
    public async Task RecordDecisionFailureAsync_PersistsRetryableAndNonRetryableFailure()
    {
        var fixture = new Fixture();
        var retryable = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(), CancellationToken.None);
        var nonRetryable = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(ParkingSessionId: Guid.NewGuid()), CancellationToken.None);

        var retryableRecord = await fixture.Sut.RecordDecisionFailureAsync(
            retryable.Record!.StatutoryDiscountDecisionCommandId,
            retryable: true,
            "TEMPORARY_STORE_UNAVAILABLE",
            CorrelationId,
            CancellationToken.None);
        var nonRetryableRecord = await fixture.Sut.RecordDecisionFailureAsync(
            nonRetryable.Record!.StatutoryDiscountDecisionCommandId,
            retryable: false,
            "VALIDATION_FAILED",
            CorrelationId,
            CancellationToken.None);

        retryableRecord.CommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.FailedRetryable);
        retryableRecord.Retryable.Should().BeTrue();
        nonRetryableRecord.CommandStatus.Should().Be(StatutoryDiscountDecisionV2CommandStates.FailedNonRetryable);
        nonRetryableRecord.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task CreateOrResolveApplicationAsync_WhenDecisionApproved_CreatesApplicationCommand()
    {
        var fixture = new Fixture();
        var approved = await CreateApprovedDecisionAsync(fixture);

        var result = await fixture.Sut.CreateOrResolveApplicationAsync(ApplicationCommand(approved.StatutoryDiscountDecisionCommandId), CancellationToken.None);

        result.Existing.Should().BeFalse();
        result.Record!.BusinessIdentity.Should()
            .Be($"statutory-discount-payable-basis-application:{approved.StatutoryDiscountDecisionCommandId:N}");
        result.Record.SemanticHashSourceVersion.Should().Be(StatutoryDiscountPayableBasisApplicationV1SemanticHash.SourceVersion);
    }

    [Fact]
    public async Task CreateOrResolveApplicationAsync_WhenSameApplicationReplays_ReturnsExisting()
    {
        var fixture = new Fixture();
        var approved = await CreateApprovedDecisionAsync(fixture);
        var command = ApplicationCommand(approved.StatutoryDiscountDecisionCommandId);

        var first = await fixture.Sut.CreateOrResolveApplicationAsync(command, CancellationToken.None);
        var replay = await fixture.Sut.CreateOrResolveApplicationAsync(command with
        {
            RequestReference = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid()
        }, CancellationToken.None);

        replay.Existing.Should().BeTrue();
        replay.SemanticConflict.Should().BeFalse();
        replay.Record!.StatutoryDiscountPayableBasisApplicationCommandId.Should()
            .Be(first.Record!.StatutoryDiscountPayableBasisApplicationCommandId);
        fixture.Repository.ApplicationCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrResolveApplicationAsync_WhenMaterialAmountChanges_ReturnsSemanticConflict()
    {
        var fixture = new Fixture();
        var approved = await CreateApprovedDecisionAsync(fixture);
        await fixture.Sut.CreateOrResolveApplicationAsync(ApplicationCommand(approved.StatutoryDiscountDecisionCommandId), CancellationToken.None);

        var conflict = await fixture.Sut.CreateOrResolveApplicationAsync(
            ApplicationCommand(
                approved.StatutoryDiscountDecisionCommandId,
                idempotencyKey: "different-key") with
            {
                ApprovedDiscountAmountMinorUnits = 2000
            },
            CancellationToken.None);

        conflict.SemanticConflict.Should().BeTrue();
        fixture.Repository.ApplicationCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateOrResolveApplicationAsync_WhenDecisionRejectedOrMissing_ReturnsSafeFailure()
    {
        var fixture = new Fixture();
        var rejectedCommand = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(decision: "REJECT"), CancellationToken.None);
        await fixture.Sut.CompleteDecisionRejectedAsync(
            rejectedCommand.Record!.StatutoryDiscountDecisionCommandId,
            "INELIGIBLE",
            "STATUTORY_DISCOUNT_INELIGIBLE",
            CorrelationId,
            CancellationToken.None);

        var rejected = await fixture.Sut.CreateOrResolveApplicationAsync(
            ApplicationCommand(rejectedCommand.Record.StatutoryDiscountDecisionCommandId),
            CancellationToken.None);
        var missing = await fixture.Sut.CreateOrResolveApplicationAsync(
            ApplicationCommand(Guid.NewGuid()),
            CancellationToken.None);

        rejected.ResultClassification.Should().Be(StatutoryDiscountPayableBasisApplicationV1ResultClassifications.DecisionNotApproved);
        missing.ResultClassification.Should().Be(StatutoryDiscountPayableBasisApplicationV1ResultClassifications.DecisionNotFound);
    }

    [Fact]
    public async Task CompleteApplicationAppliedAsync_PersistsAppliedResult()
    {
        var fixture = new Fixture();
        var approved = await CreateApprovedDecisionAsync(fixture);
        var created = await fixture.Sut.CreateOrResolveApplicationAsync(ApplicationCommand(approved.StatutoryDiscountDecisionCommandId), CancellationToken.None);

        var applied = await fixture.Sut.CompleteApplicationAppliedAsync(
            created.Record!.StatutoryDiscountPayableBasisApplicationCommandId,
            statutoryDiscountPayableBasisApplicationId: Guid.NewGuid(),
            appliedTariffSnapshotId: Guid.NewGuid(),
            CorrelationId,
            CancellationToken.None);

        applied.CommandStatus.Should().Be(StatutoryDiscountPayableBasisApplicationV1CommandStates.Applied);
        applied.ResultClassification.Should().Be(StatutoryDiscountPayableBasisApplicationV1ResultClassifications.Applied);
        applied.AppliedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateOrResolveApplicationAsync_WhenConcurrentRequestsUseSameDecision_CreatesOneCommand()
    {
        var fixture = new Fixture();
        var approved = await CreateApprovedDecisionAsync(fixture);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            fixture.Sut.CreateOrResolveApplicationAsync(
                ApplicationCommand(
                    approved.StatutoryDiscountDecisionCommandId,
                    requestReference: Guid.NewGuid(),
                    idempotencyKey: $"app-key-{index}"),
                CancellationToken.None)));

        results.Select(result => result.Record!.StatutoryDiscountPayableBasisApplicationCommandId)
            .Distinct()
            .Should()
            .ContainSingle();
        fixture.Repository.ApplicationCount.Should().Be(1);
    }

    [Fact]
    public void DecisionV2SemanticHash_IsDeterministicAndExcludesTransportFacts()
    {
        var first = DecisionCommand();
        var second = first with
        {
            RequestReference = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            IdempotencyKey = "different-key",
            EvidenceReferences = first.EvidenceReferences.Reverse().ToArray()
        };

        StatutoryDiscountDecisionV2SemanticHash.Compute(second)
            .Should()
            .Be(StatutoryDiscountDecisionV2SemanticHash.Compute(first));
        typeof(StatutoryDiscountDecisionV2Command).GetProperty("ApplyPayableBasis").Should().BeNull();
    }

    [Fact]
    public void DecisionV2SemanticHash_ChangesWhenMaterialFactsChange()
    {
        var original = DecisionCommand();

        StatutoryDiscountDecisionV2SemanticHash.Compute(original with { ParkingSessionId = Guid.NewGuid() })
            .Should().NotBe(StatutoryDiscountDecisionV2SemanticHash.Compute(original));
        StatutoryDiscountDecisionV2SemanticHash.Compute(original with { EntitlementType = "PWD" })
            .Should().NotBe(StatutoryDiscountDecisionV2SemanticHash.Compute(original));
        StatutoryDiscountDecisionV2SemanticHash.Compute(original with
            {
                Beneficiary = original.Beneficiary! with { BeneficiaryReference = "beneficiary-changed" }
            })
            .Should().NotBe(StatutoryDiscountDecisionV2SemanticHash.Compute(original));
        StatutoryDiscountDecisionV2SemanticHash.Compute(original with
            {
                EvidenceReferences = [Evidence("REJECTED")]
            })
            .Should().NotBe(StatutoryDiscountDecisionV2SemanticHash.Compute(original));
    }

    [Fact]
    public void DecisionV2SemanticHash_RejectsRawEvidenceAndFullIdentityValues()
    {
        var fullId = DecisionCommand() with
        {
            IdentityMetadata = new StatutoryDiscountDecisionV2IdentityMetadata("SENIOR_CITIZEN_ID", "OSCA", null, "123456789", null)
        };
        var rawEvidence = DecisionCommand() with
        {
            EvidenceReferences = [Evidence("VERIFIED") with { StorageReference = "data:image/png;base64,iVBORw0KGgoAAAANS" }]
        };

        Action fullIdHash = () => StatutoryDiscountDecisionV2SemanticHash.Compute(fullId);
        Action rawEvidenceHash = () => StatutoryDiscountDecisionV2SemanticHash.Compute(rawEvidence);

        fullIdHash.Should().Throw<ArgumentException>();
        rawEvidenceHash.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ApplicationV1SemanticHash_IsDeterministicAndExcludesTransportAndSourceChannel()
    {
        var decisionId = Guid.NewGuid();
        var first = ApplicationCommand(decisionId);
        var second = first with
        {
            RequestReference = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            IdempotencyKey = "different-key",
            SourceChannel = StatutoryDiscountSourceChannels.WebPay
        };

        StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(second)
            .Should()
            .Be(StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(first));
    }

    [Fact]
    public void ApplicationV1SemanticHash_ChangesWhenMaterialFactsChange()
    {
        var decisionId = Guid.NewGuid();
        var original = ApplicationCommand(decisionId);

        StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original with { StatutoryDiscountDecisionCommandId = Guid.NewGuid() })
            .Should().NotBe(StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original));
        StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original with { OriginalTariffSnapshotId = Guid.NewGuid() })
            .Should().NotBe(StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original));
        StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original with { ApprovedDiscountAmountMinorUnits = 2000 })
            .Should().NotBe(StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original));
        StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original with { ApprovedFinalPayableAmountMinorUnits = 8000 })
            .Should().NotBe(StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original));
        StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original with { Currency = "usd" })
            .Should().NotBe(StatutoryDiscountPayableBasisApplicationV1SemanticHash.Compute(original));
    }

    private static async Task<StatutoryDiscountDecisionV2Record> CreateApprovedDecisionAsync(Fixture fixture)
    {
        var created = await fixture.Sut.CreateOrResolveDecisionAsync(DecisionCommand(), CancellationToken.None);
        return await fixture.Sut.CompleteDecisionApprovedAsync(
            created.Record!.StatutoryDiscountDecisionCommandId,
            Guid.NewGuid(),
            TariffSnapshotId,
            Guid.NewGuid(),
            fallbackPolicyReferenceId: null,
            "NATIONAL_DEFAULT",
            localOrdinanceApplied: false,
            new StatutoryDiscountDecisionV2TariffFacts(10000, 8929, 1071, 1786, 8214, "PHP"),
            "ELIGIBLE",
            CorrelationId,
            CancellationToken.None);
    }

    private static StatutoryDiscountDecisionV2Command DecisionCommand(
        Guid? ParkingSessionId = null,
        Guid? requestReference = null,
        string idempotencyKey = "decision-key",
        string decision = "APPROVE") =>
        new(
            requestReference ?? Guid.NewGuid(),
            StatutoryDiscountSourceChannels.OperatorConsole,
            ParkingSessionId ?? StatutoryDiscountStagedCommandServiceTests.ParkingSessionId,
            SiteId,
            SiteGroupId,
            "ticket-001",
            "abc1234",
            "senior_citizen",
            new StatutoryDiscountDecisionV2BeneficiaryMetadata("beneficiary-ref", "senior_citizen", "DRIVER", 1),
            new StatutoryDiscountDecisionV2IdentityMetadata("SENIOR_CITIZEN_ID", "OSCA", DateOnly.Parse("2030-01-01"), "SC-****-1234", null),
            [Evidence("VERIFIED")],
            new StatutoryDiscountDecisionV2AttestationFacts(true, "attestation-ref", "CUSTOMER_REQUEST", true),
            ActorUserId,
            ReviewerUserId,
            OperatorDeviceBindingId: null,
            OperatorShiftId: null,
            new StatutoryDiscountDecisionV2DecisionFacts(decision, decision == "APPROVE" ? "ELIGIBLE" : "INELIGIBLE", null),
            PolicyResolutionReferenceId: null,
            AppliedPolicyReferenceId: null,
            FallbackPolicyReferenceId: null,
            PolicyResolutionBasis: "NATIONAL_DEFAULT",
            LocalOrdinanceApplied: false,
            TariffSnapshotId,
            new StatutoryDiscountDecisionV2TariffFacts(10000, 8929, 1071, 1786, 8214, "PHP"),
            idempotencyKey,
            CorrelationId);

    private static StatutoryDiscountDecisionV2EvidenceReference Evidence(string status) =>
        new(
            "SENIOR_CITIZEN_ID",
            "MANUAL_REFERENCE",
            "evidence-ref-001",
            "SC-****-1234",
            status,
            "verification-ref-001",
            DateTimeOffset.Parse("2026-07-21T01:00:00Z"));

    private static StatutoryDiscountPayableBasisApplicationV1Command ApplicationCommand(
        Guid decisionId,
        Guid? requestReference = null,
        string idempotencyKey = "application-key") =>
        new(
            requestReference ?? Guid.NewGuid(),
            decisionId,
            ParkingSessionId,
            SiteId,
            "SENIOR_CITIZEN",
            StatutoryDiscountValidationId: null,
            TariffSnapshotId,
            TargetTariffSnapshotId: null,
            AppliedTariffSnapshotId: null,
            AppliedPolicyReferenceId: null,
            "NATIONAL_DEFAULT",
            ApprovedDiscountAmountMinorUnits: 1786,
            ApprovedVatExclusiveAmountMinorUnits: 8929,
            ApprovedVatAmountMinorUnits: 1071,
            ApprovedFinalPayableAmountMinorUnits: 8214,
            "PHP",
            StatutoryDiscountSourceChannels.OperatorConsole,
            idempotencyKey,
            CorrelationId);

    private static StatutoryDiscountDecisionV2Record DecisionRecord(
        string businessIdentity,
        string sourceVersion,
        string semanticHash) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ParkingSessionId,
            StatutoryDiscountSourceChannels.OperatorConsole,
            "SENIOR_CITIZEN",
            businessIdentity,
            businessIdentity,
            "existing-key",
            sourceVersion,
            semanticHash,
            StatutoryDiscountDecisionV2CommandStates.Completed,
            StatutoryDiscountDecisionV2ResultStates.Approved,
            "ACCEPTED",
            Retryable: false,
            StatutoryDiscountDecisionRecoveryClassifications.ReadCanonicalResult,
            SafeErrorCode: null,
            StatutoryDiscountValidationId: null,
            TariffSnapshotId,
            AppliedPolicyReferenceId: null,
            FallbackPolicyReferenceId: null,
            "NATIONAL_DEFAULT",
            LocalOrdinanceApplied: false,
            GrossAmountMinorUnits: 10000,
            VatExclusiveAmountMinorUnits: 8929,
            VatAmountMinorUnits: 1071,
            StatutoryDiscountAmountMinorUnits: 1786,
            NetPayableAmountMinorUnits: 8214,
            "PHP",
            EvidenceRequired: true,
            EvidenceRecorded: true,
            "ELIGIBLE",
            CorrelationId,
            DateTimeOffset.UtcNow,
            ProcessingStartedAt: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            FailedAt: null,
            DateTimeOffset.UtcNow);

    private sealed class Fixture
    {
        public Fixture()
        {
            Repository = new FakeRepository();
            Sut = new StatutoryDiscountStagedCommandService(Repository);
        }

        public FakeRepository Repository { get; }

        public StatutoryDiscountStagedCommandService Sut { get; }
    }

    private sealed class FakeRepository : IStatutoryDiscountStagedCommandRepository
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly Dictionary<Guid, StatutoryDiscountDecisionV2Record> _decisions = [];
        private readonly Dictionary<Guid, StatutoryDiscountPayableBasisApplicationV1Record> _applications = [];

        public int DecisionCount => _decisions.Count;

        public int ApplicationCount => _applications.Count;

        public void SeedDecision(StatutoryDiscountDecisionV2Record record) =>
            _decisions[record.StatutoryDiscountDecisionCommandId] = record;

        public async Task<T> ExecuteWithDecisionLockAsync<T>(
            StatutoryDiscountDecisionV2RepositoryCommand command,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        public Task<StatutoryDiscountDecisionV2BeginResult> BeginDecisionAsync(
            StatutoryDiscountDecisionV2RepositoryCommand command,
            CancellationToken cancellationToken)
        {
            var existing = _decisions.Values.FirstOrDefault(record =>
                record.IdempotencyScope == command.IdempotencyScope && record.IdempotencyKey == command.Command.IdempotencyKey)
                ?? _decisions.Values.FirstOrDefault(record => record.BusinessIdentity == command.BusinessIdentity)
                ?? _decisions.Values.FirstOrDefault(record => record.RequestReference == command.Command.RequestReference);

            if (existing is not null)
            {
                return Task.FromResult(new StatutoryDiscountDecisionV2BeginResult(
                    Existing: true,
                    SemanticConflict: existing.SemanticHashSourceVersion != command.SemanticHashSourceVersion
                        || existing.SemanticRequestHash != command.SemanticRequestHash,
                    RecoverableWithOriginalKey: existing.IdempotencyKey == command.Command.IdempotencyKey
                        && existing.CommandStatus is StatutoryDiscountDecisionV2CommandStates.Received
                            or StatutoryDiscountDecisionV2CommandStates.Processing,
                    existing));
            }

            var record = DecisionRecord(command.BusinessIdentity, command.SemanticHashSourceVersion, command.SemanticRequestHash) with
            {
                StatutoryDiscountDecisionCommandId = Guid.NewGuid(),
                RequestReference = command.Command.RequestReference,
                ParkingSessionId = command.Command.ParkingSessionId,
                SourceChannel = command.Command.SourceChannel,
                EntitlementType = "SENIOR_CITIZEN",
                IdempotencyKey = command.Command.IdempotencyKey,
                CommandStatus = StatutoryDiscountDecisionV2CommandStates.Received,
                DecisionResultStatus = StatutoryDiscountDecisionV2ResultStates.NotDecided,
                ResultClassification = "ACCEPTED",
                Retryable = false,
                RecoveryClassification = StatutoryDiscountDecisionRecoveryClassifications.None,
                CreatedAt = command.RequestedAt,
                DecidedAt = null,
                CompletedAt = null
            };
            _decisions[record.StatutoryDiscountDecisionCommandId] = record;

            return Task.FromResult(new StatutoryDiscountDecisionV2BeginResult(false, false, false, record));
        }

        public Task<StatutoryDiscountDecisionV2Record?> GetDecisionAsync(Guid statutoryDiscountDecisionCommandId, CancellationToken cancellationToken) =>
            Task.FromResult(_decisions.GetValueOrDefault(statutoryDiscountDecisionCommandId));

        public Task<StatutoryDiscountDecisionV2Record> UpdateDecisionAsync(StatutoryDiscountDecisionV2Record record, CancellationToken cancellationToken)
        {
            _decisions[record.StatutoryDiscountDecisionCommandId] = record;
            return Task.FromResult(record);
        }

        public async Task<T> ExecuteWithApplicationLockAsync<T>(
            StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        public Task<StatutoryDiscountPayableBasisApplicationV1BeginResult> BeginApplicationAsync(
            StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command,
            CancellationToken cancellationToken)
        {
            var existing = _applications.Values.FirstOrDefault(record =>
                record.IdempotencyScope == command.IdempotencyScope && record.IdempotencyKey == command.Command.IdempotencyKey)
                ?? _applications.Values.FirstOrDefault(record => record.BusinessIdentity == command.BusinessIdentity)
                ?? _applications.Values.FirstOrDefault(record => record.RequestReference == command.Command.RequestReference);

            if (existing is not null)
            {
                return Task.FromResult(new StatutoryDiscountPayableBasisApplicationV1BeginResult(
                    Existing: true,
                    SemanticConflict: existing.SemanticHashSourceVersion != command.SemanticHashSourceVersion
                        || existing.SemanticRequestHash != command.SemanticRequestHash,
                    RecoverableWithOriginalKey: existing.IdempotencyKey == command.Command.IdempotencyKey
                        && existing.CommandStatus is StatutoryDiscountPayableBasisApplicationV1CommandStates.Received
                            or StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing,
                    existing));
            }

            var record = new StatutoryDiscountPayableBasisApplicationV1Record(
                Guid.NewGuid(),
                command.Command.RequestReference,
                command.Command.StatutoryDiscountDecisionCommandId,
                command.Command.ParkingSessionId,
                "SENIOR_CITIZEN",
                command.BusinessIdentity,
                command.IdempotencyScope,
                command.Command.IdempotencyKey,
                command.SemanticHashSourceVersion,
                command.SemanticRequestHash,
                StatutoryDiscountPayableBasisApplicationV1CommandStates.Received,
                StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress,
                Retryable: false,
                StatutoryDiscountDecisionRecoveryClassifications.None,
                SafeErrorCode: null,
                command.Command.StatutoryDiscountValidationId,
                StatutoryDiscountPayableBasisApplicationId: null,
                command.Command.OriginalTariffSnapshotId,
                command.Command.TargetTariffSnapshotId,
                command.Command.AppliedTariffSnapshotId,
                command.Command.AppliedPolicyReferenceId,
                command.Command.PolicyResolutionBasis,
                command.Command.ApprovedDiscountAmountMinorUnits,
                command.Command.ApprovedVatExclusiveAmountMinorUnits,
                command.Command.ApprovedVatAmountMinorUnits,
                command.Command.ApprovedFinalPayableAmountMinorUnits,
                command.Command.Currency,
                command.Command.SourceChannel,
                command.Command.CorrelationId,
                command.RequestedAt,
                ProcessingStartedAt: null,
                AppliedAt: null,
                CompletedAt: null,
                FailedAt: null,
                command.RequestedAt);
            _applications[record.StatutoryDiscountPayableBasisApplicationCommandId] = record;

            return Task.FromResult(new StatutoryDiscountPayableBasisApplicationV1BeginResult(false, false, false, record));
        }

        public Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationAsync(Guid statutoryDiscountPayableBasisApplicationCommandId, CancellationToken cancellationToken) =>
            Task.FromResult(_applications.GetValueOrDefault(statutoryDiscountPayableBasisApplicationCommandId));

        public Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationByDecisionAsync(Guid statutoryDiscountDecisionCommandId, CancellationToken cancellationToken) =>
            Task.FromResult(_applications.Values.FirstOrDefault(record => record.StatutoryDiscountDecisionCommandId == statutoryDiscountDecisionCommandId));

        public Task<StatutoryDiscountPayableBasisApplicationV1Record> UpdateApplicationAsync(
            StatutoryDiscountPayableBasisApplicationV1Record record,
            CancellationToken cancellationToken)
        {
            _applications[record.StatutoryDiscountPayableBasisApplicationCommandId] = record;
            return Task.FromResult(record);
        }
    }
}
