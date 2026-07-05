using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionRetrySchedulingPreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_WhenDefaultOptions_ReturnsDisabledWithoutAuditIntent()
    {
        var (detail, commandPreparation, _) = await SafeCommandPreparationAsync();
        var audit = new FakeRetrySchedulingPreparationAuditRepository();
        var sut = new FiscalExceptionRetrySchedulingPreparationService(
            new FiscalExceptionRetrySchedulingPreparationOptions(),
            audit);

        var result = await sut.PrepareAsync(
            new FiscalExceptionRetrySchedulingPreparationRequest(detail, commandPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.Disabled);
        result.BlockReasonCode.Should().Be("retry_scheduling_preparation_disabled");
        result.Schedule.Should().BeNull();
        audit.Records.Should().BeEmpty();
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task PrepareAsync_WhenRetryCommandPreparationIsNotSafe_BlocksScheduling()
    {
        var (detail, _, _) = await SafeCommandPreparationAsync(semanticHashConfirmed: false);
        var unavailableCommand = await new FiscalExceptionRetryCommandPreparationService()
            .PrepareAsync(new FiscalExceptionRetryCommandPreparationRequest(detail), CancellationToken.None);
        var sut = EnabledSut(new FakeRetrySchedulingPreparationAuditRepository());

        var result = await sut.PrepareAsync(
            new FiscalExceptionRetrySchedulingPreparationRequest(detail, unavailableCommand),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_request_hash_required_but_missing");
        result.ExecutableJobEnqueued.Should().BeFalse();
    }

    [Fact]
    public async Task PrepareAsync_WhenSemanticHashIsMissing_BlocksScheduling()
    {
        var (detail, _, _) = await SafeCommandPreparationAsync(semanticHashConfirmed: false);
        var commandPreparation = PreparedCommand(detail, retryCommandPreparationAttemptId: Guid.NewGuid());
        var sut = EnabledSut(new FakeRetrySchedulingPreparationAuditRepository());

        var result = await sut.PrepareAsync(
            new FiscalExceptionRetrySchedulingPreparationRequest(detail, commandPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("semantic_request_hash_required_but_missing");
        result.Schedule.Should().BeNull();
    }

    [Fact]
    public async Task PrepareAsync_WhenReadbackBasisIsNotNotFound_BlocksScheduling()
    {
        var (detail, _, _) = await SafeCommandPreparationAsync(
            readbackClassification: FiscalExceptionReadbackClassification.Matched);
        var commandPreparation = PreparedCommand(detail, retryCommandPreparationAttemptId: Guid.NewGuid());
        var sut = EnabledSut(new FakeRetrySchedulingPreparationAuditRepository());

        var result = await sut.PrepareAsync(
            new FiscalExceptionRetrySchedulingPreparationRequest(detail, commandPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("readback_matched");
        result.Schedule.Should().BeNull();
    }

    [Fact]
    public async Task PrepareAsync_WhenCommandPreparationAuditIsMissing_BlocksScheduling()
    {
        var (detail, _, _) = await SafeCommandPreparationAsync();
        var commandPreparation = PreparedCommand(detail, retryCommandPreparationAttemptId: null);
        var sut = EnabledSut(new FakeRetrySchedulingPreparationAuditRepository());

        var result = await sut.PrepareAsync(
            new FiscalExceptionRetrySchedulingPreparationRequest(detail, commandPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("retry_command_preparation_audit_missing");
        result.Schedule.Should().BeNull();
    }

    [Fact]
    public async Task PrepareAsync_WhenRetrySchedulePolicyIsMissing_ReturnsUnavailable()
    {
        var (detail, commandPreparation, _) = await SafeCommandPreparationAsync();
        var sut = new FiscalExceptionRetrySchedulingPreparationService(
            new FiscalExceptionRetrySchedulingPreparationOptions(
                EnableSchedulePreparation: true,
                RetrySchedulePolicyConfigured: false,
                RetryBackoffPolicyConfigured: true),
            new FakeRetrySchedulingPreparationAuditRepository());

        var result = await sut.PrepareAsync(
            new FiscalExceptionRetrySchedulingPreparationRequest(detail, commandPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.Unavailable);
        result.BlockReasonCode.Should().Be("retry_schedule_policy_not_configured");
        result.Schedule.Should().BeNull();
    }

    [Fact]
    public async Task PrepareAsync_WhenAuditPersistenceIsUnavailable_ReturnsUnavailable()
    {
        var (detail, commandPreparation, _) = await SafeCommandPreparationAsync();
        var sut = new FiscalExceptionRetrySchedulingPreparationService(
            EnabledOptions(),
            auditRepository: null);

        var result = await sut.PrepareAsync(
            new FiscalExceptionRetrySchedulingPreparationRequest(detail, commandPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.Unavailable);
        result.BlockReasonCode.Should().Be("retry_scheduling_audit_persistence_unavailable");
        result.Schedule.Should().BeNull();
    }

    [Fact]
    public async Task PrepareAsync_WhenAllPrerequisitesPass_RecordsSafeAuditIntentOnly()
    {
        var (detail, commandPreparation, _) = await SafeCommandPreparationAsync();
        var audit = new FakeRetrySchedulingPreparationAuditRepository();
        var sut = EnabledSut(audit);

        var result = await sut.PrepareAsync(
            new FiscalExceptionRetrySchedulingPreparationRequest(detail, commandPreparation),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.ScheduledPrepared);
        result.BlockReasonCode.Should().BeNull();
        result.Schedule.Should().NotBeNull();
        result.Schedule!.Executable.Should().BeFalse();
        result.RetrySchedulePreparationAttemptId.Should().NotBeNull();
        audit.Records.Should().ContainSingle();
        audit.Records[0].SchedulingPreparationStatus
            .Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.ScheduledPrepared);
        audit.Records[0].RetryCommandPreparationAttemptId
            .Should().Be(commandPreparation.RetryCommandPreparationAttemptId);
        AssertNoExecutionSideEffects(result);
    }

    [Fact]
    public async Task PrepareAsync_WhenNewUpstreamFinalityReferenceIsRequested_BlocksScheduling()
    {
        var (detail, commandPreparation, _) = await SafeCommandPreparationAsync();
        var sut = EnabledSut(new FakeRetrySchedulingPreparationAuditRepository());

        var result = await sut.PrepareAsync(
            new FiscalExceptionRetrySchedulingPreparationRequest(
                detail,
                commandPreparation,
                RequestedUpstreamFinalityReference: $"new-upstream-{Guid.NewGuid():N}"),
            CancellationToken.None);

        result.Status.Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.Blocked);
        result.BlockReasonCode.Should().Be("new_upstream_finality_reference_rejected");
        result.ExecutableJobEnqueued.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WhenSchedulerPrepAuditIsAvailable_ReturnsSafeSchedulerPosture()
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown) with
        {
            SemanticRequestHashStatus = FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue = ValidSemanticRequestHashValue(),
            SemanticRequestHashAlgorithm = FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            SemanticRequestHashSourceVersion = FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            SemanticRequestHashSourceFactCount = 42,
            SemanticRequestHashSafeSummary = "semantic_request_hash_source_available:facts=42"
        };
        var readback = new FakeReadbackAttemptRepository();
        readback.Seed(new FiscalExceptionReadbackAttemptSummary(
            Classification: FiscalExceptionReadbackClassification.NotFound,
            AttemptedAt: DateTimeOffset.Parse("2026-07-05T10:00:00+08:00"),
            AttemptCount: 1,
            SafeErrorSummary: "not_found"));
        var commandAudit = new FakeRetryCommandPreparationAuditRepository();
        var scheduleAudit = new FakeRetrySchedulingPreparationAuditRepository();
        var service = new FiscalExceptionQueueService(
            new FakeReferenceReader([reference]),
            readback,
            new FiscalExceptionRetryEligibilityEvaluator(),
            new FiscalExceptionRetryCommandPreparationService(commandAudit),
            commandAudit,
            EnabledSut(scheduleAudit),
            scheduleAudit);

        var detail = await service.GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.RetryCommandPreparationStatus
            .Should().Be(FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable);
        detail.Summary.RetrySchedulingPreparationStatus
            .Should().Be(FiscalExceptionRetrySchedulingPreparationStatus.ScheduledPrepared);
        detail.Summary.RetrySchedulingPreparationAttemptCount.Should().Be(1);
        detail.Summary.RetrySchedulingBlockReasonCode.Should().BeNull();
        detail.Summary.SafeRetrySchedulingPreparationSummary.Should().Be("retry_scheduling_prepared_non_executable");
        detail.Summary.RetryExecutionAvailable.Should().BeFalse();
        detail.PaymentFinalityChanged.Should().BeFalse();
        detail.ExitAuthorizationIssued.Should().BeFalse();
        detail.GateBehaviorTriggered.Should().BeFalse();
        detail.FiscalNumberEditingAllowed.Should().BeFalse();
        detail.ManualFiscalDocumentCreationAllowed.Should().BeFalse();
    }

    [Fact]
    public void RetrySchedulingPreparation_DoesNotIntroduceRetryExecutionEndpointOrPosServerDependency()
    {
        var constructorParameters = typeof(FiscalExceptionRetrySchedulingPreparationService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        constructorParameters.Should().NotContain(parameter =>
            parameter.Contains("PosServer", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("Payment", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase) ||
            parameter.Contains("Gate", StringComparison.OrdinalIgnoreCase));

        var fiscalIssuanceTypes = typeof(FiscalExceptionRetrySchedulingPreparationService).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(FiscalExceptionRetrySchedulingPreparationService).Namespace)
            .Select(type => type.Name)
            .ToArray();

        fiscalIssuanceTypes.Should().NotContain(name =>
            name.Contains("RetryEndpoint", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RetryWorker", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalExceptionRetrySchedulingPreparationService EnabledSut(
        IFiscalExceptionRetrySchedulingPreparationAuditRepository auditRepository) =>
        new(EnabledOptions(), auditRepository);

    private static FiscalExceptionRetrySchedulingPreparationOptions EnabledOptions() =>
        new(
            EnableSchedulePreparation: true,
            RetrySchedulePolicyConfigured: true,
            RetryBackoffPolicyConfigured: true);

    private static async Task<(
        FiscalExceptionQueueCaseDetail Detail,
        FiscalExceptionRetryCommandPreparationResult CommandPreparation,
        FakeRetryCommandPreparationAuditRepository CommandAudit)> SafeCommandPreparationAsync(
        bool semanticHashConfirmed = true,
        FiscalExceptionReadbackClassification readbackClassification = FiscalExceptionReadbackClassification.NotFound)
    {
        var reference = Reference(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown);
        if (semanticHashConfirmed)
        {
            reference = reference with
            {
                SemanticRequestHashStatus = FiscalSemanticRequestHashSourceStatus.Available,
                SemanticRequestHashValue = ValidSemanticRequestHashValue(),
                SemanticRequestHashAlgorithm = FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
                SemanticRequestHashSourceVersion = FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
                SemanticRequestHashSourceFactCount = 42,
                SemanticRequestHashSafeSummary = "semantic_request_hash_source_available:facts=42"
            };
        }

        var readback = new FakeReadbackAttemptRepository();
        readback.Seed(new FiscalExceptionReadbackAttemptSummary(
                Classification: readbackClassification,
                AttemptedAt: DateTimeOffset.Parse("2026-07-05T10:00:00+08:00"),
                AttemptCount: 1,
                SafeErrorSummary: readbackClassification.ToString()));
        var detail = await new FiscalExceptionQueueService(
                new FakeReferenceReader([reference]),
                readback)
            .GetAsync(reference.FiscalIssuanceReferenceId, CancellationToken.None);

        var commandAudit = new FakeRetryCommandPreparationAuditRepository();
        var commandPreparation = await new FiscalExceptionRetryCommandPreparationService(commandAudit)
            .PrepareAsync(new FiscalExceptionRetryCommandPreparationRequest(detail!), CancellationToken.None);

        return (detail!, commandPreparation, commandAudit);
    }

    private static FiscalExceptionRetryCommandPreparationResult PreparedCommand(
        FiscalExceptionQueueCaseDetail detail,
        Guid? retryCommandPreparationAttemptId) =>
        new(
            Status: FiscalExceptionRetryCommandPreparationStatus.PreparedNonExecutable,
            BlockReasonCode: null,
            SafeSummary: "retry_command_prepared_non_executable",
            Command: new FiscalExceptionRetryCommandEnvelope(
                FiscalIssuanceReferenceId: detail.Summary.FiscalIssuanceReferenceId,
                PaymentConfirmationId: detail.Summary.PaymentConfirmationId,
                PaymentAttemptId: detail.Summary.PaymentAttemptId,
                ParkingSessionId: detail.Summary.ParkingSessionId,
                SiteId: detail.Summary.SiteId,
                SitePosServerId: detail.Summary.SitePosServerId,
                SitePosServerRef: detail.Summary.SitePosServerRef,
                FiscalDocumentTypeContextStatus: "not_available_in_current_fiscal_reference_model",
                UpstreamFinalityReference: detail.Summary.UpstreamFinalityReference,
                SemanticRequestHashAvailabilityStatus: detail.Summary.SemanticRequestHashAvailabilityStatus,
                SemanticRequestHashValue: detail.Summary.SemanticRequestHashValue,
                SemanticRequestHashAlgorithm: detail.Summary.SemanticRequestHashAlgorithm,
                SemanticRequestHashSourceVersion: detail.Summary.SemanticRequestHashSourceVersion,
                LatestReadbackClassificationBasis: detail.Summary.ReadbackClassification ?? FiscalExceptionReadbackClassification.NotFound,
                RetryEligibilityDecisionBasis: detail.Summary.RetryEligibilityDecision,
                SafeBlockReasonCode: null,
                CorrelationId: detail.CorrelationId,
                Executable: false),
            SemanticRequestHashAvailabilityStatus: detail.Summary.SemanticRequestHashAvailabilityStatus,
            IdempotencyContextAvailabilityStatus: detail.Summary.IdempotencyContextAvailabilityStatus,
            PosServerPostCalled: false,
            RetryScheduled: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEdited: false,
            ManualFiscalDocumentCreated: false,
            RetryCommandPreparationAttemptId: retryCommandPreparationAttemptId,
            RetryCommandPreparationAttemptedAt: DateTimeOffset.UtcNow);

    private static void AssertNoExecutionSideEffects(FiscalExceptionRetrySchedulingPreparationResult result)
    {
        result.PosServerPostCalled.Should().BeFalse();
        result.ExecutableJobEnqueued.Should().BeFalse();
        result.RetryEndpointExposed.Should().BeFalse();
        result.PaymentFinalityChanged.Should().BeFalse();
        result.FiscalReferenceSuccessRecorded.Should().BeFalse();
        result.ExitAuthorizationIssued.Should().BeFalse();
        result.GateBehaviorTriggered.Should().BeFalse();
        result.FiscalNumberEdited.Should().BeFalse();
        result.ManualFiscalDocumentCreated.Should().BeFalse();
    }

    private static FiscalIssuanceReferenceRecord Reference(
        FiscalIssuanceIntegrationState state,
        FiscalIssuanceExceptionReason? reason = null)
    {
        var now = DateTimeOffset.Parse("2026-07-05T09:00:00+08:00");
        return new FiscalIssuanceReferenceRecord(
            FiscalIssuanceReferenceId: Guid.NewGuid(),
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            PayableBasisRef: "DEV-PAYABLE-BASIS-ATC-001",
            UpstreamFinalityReference: $"CPS-POS-UAT:{Guid.NewGuid():N}",
            PosServerFiscalDocumentId: Guid.NewGuid(),
            FiscalIdentityId: Guid.NewGuid(),
            FiscalSequencePolicyId: Guid.NewGuid(),
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            FiscalDocumentStatusCodeId: Guid.NewGuid(),
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: reason ?? FiscalIssuanceExceptionReason.PostTimeout,
            LatestErrorCode: reason?.ToString() ?? "post_timeout",
            LatestErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            RecordedByServiceIdentityId: Guid.NewGuid());
    }

    private static string ValidSemanticRequestHashValue() =>
        new('a', 64);

    private sealed class FakeReferenceReader : IFiscalExceptionQueueReferenceReader
    {
        private readonly IReadOnlyList<FiscalIssuanceReferenceRecord> _records;

        public FakeReferenceReader(IReadOnlyList<FiscalIssuanceReferenceRecord> records)
        {
            _records = records;
        }

        public Task<IReadOnlyList<FiscalIssuanceReferenceRecord>> ListFiscalExceptionReferencesAsync(
            FiscalExceptionQueueQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records);

        public Task<FiscalIssuanceReferenceRecord?> FindFiscalExceptionReferenceAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.SingleOrDefault(record =>
                record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId));
    }

    private sealed class FakeReadbackAttemptRepository : IFiscalExceptionReadbackAttemptRepository
    {
        private FiscalExceptionReadbackAttemptSummary? _summary;

        public Task<FiscalExceptionReadbackAttemptRecord> RecordAsync(
            FiscalExceptionReadbackAttemptWrite attempt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("retry scheduling preparation tests are read-only");

        public Task<FiscalExceptionReadbackAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_summary);

        public void Seed(FiscalExceptionReadbackAttemptSummary summary)
        {
            _summary = summary;
        }
    }

    private sealed class FakeRetryCommandPreparationAuditRepository :
        IFiscalExceptionRetryCommandPreparationAuditRepository
    {
        public List<FiscalExceptionRetryCommandPreparationAttemptRecord> Records { get; } = [];

        public Task<FiscalExceptionRetryCommandPreparationAttemptRecord> RecordAsync(
            FiscalExceptionRetryCommandPreparationAttemptWrite attempt,
            CancellationToken cancellationToken)
        {
            var record = new FiscalExceptionRetryCommandPreparationAttemptRecord(
                RetryCommandPreparationAttemptId: Guid.NewGuid(),
                FiscalIssuanceReferenceId: attempt.FiscalIssuanceReferenceId,
                PaymentConfirmationId: attempt.PaymentConfirmationId,
                PaymentAttemptId: attempt.PaymentAttemptId,
                ParkingSessionId: attempt.ParkingSessionId,
                SiteId: attempt.SiteId,
                SitePosServerId: attempt.SitePosServerId,
                SitePosServerRef: attempt.SitePosServerRef,
                LatestReadbackClassificationBasis: attempt.LatestReadbackClassificationBasis,
                RetryEligibilityDecisionBasis: attempt.RetryEligibilityDecisionBasis,
                CommandPreparationStatus: attempt.CommandPreparationStatus,
                CommandBlockReasonCode: attempt.CommandBlockReasonCode,
                SemanticRequestHashAvailabilityStatus: attempt.SemanticRequestHashAvailabilityStatus,
                IdempotencyContextAvailabilityStatus: attempt.IdempotencyContextAvailabilityStatus,
                AttemptedAt: attempt.AttemptedAt,
                SafeSummary: attempt.SafeSummary,
                CorrelationId: attempt.CorrelationId,
                ServiceIdentityId: attempt.ServiceIdentityId,
                CreatedAt: attempt.AttemptedAt);

            Records.Add(record);
            return Task.FromResult(record);
        }

        public Task<FiscalExceptionRetryCommandPreparationAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken)
        {
            var latest = Records
                .Where(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId)
                .OrderByDescending(record => record.AttemptedAt)
                .FirstOrDefault();

            if (latest is null)
            {
                return Task.FromResult<FiscalExceptionRetryCommandPreparationAttemptSummary?>(null);
            }

            return Task.FromResult<FiscalExceptionRetryCommandPreparationAttemptSummary?>(
                new FiscalExceptionRetryCommandPreparationAttemptSummary(
                    LastRetryCommandPreparationAttemptId: latest.RetryCommandPreparationAttemptId,
                    LastCommandPreparationStatus: latest.CommandPreparationStatus,
                    LastAttemptedAt: latest.AttemptedAt,
                    AttemptCount: Records.Count(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId),
                    LastCommandBlockReasonCode: latest.CommandBlockReasonCode,
                    SemanticRequestHashAvailabilityStatus: latest.SemanticRequestHashAvailabilityStatus,
                    IdempotencyContextAvailabilityStatus: latest.IdempotencyContextAvailabilityStatus,
                    SafeSummary: latest.SafeSummary));
        }
    }

    private sealed class FakeRetrySchedulingPreparationAuditRepository :
        IFiscalExceptionRetrySchedulingPreparationAuditRepository
    {
        public List<FiscalExceptionRetrySchedulingPreparationAttemptRecord> Records { get; } = [];

        public Task<FiscalExceptionRetrySchedulingPreparationAttemptRecord> RecordAsync(
            FiscalExceptionRetrySchedulingPreparationAttemptWrite attempt,
            CancellationToken cancellationToken)
        {
            var record = new FiscalExceptionRetrySchedulingPreparationAttemptRecord(
                RetrySchedulePreparationAttemptId: Guid.NewGuid(),
                FiscalIssuanceReferenceId: attempt.FiscalIssuanceReferenceId,
                RetryCommandPreparationAttemptId: attempt.RetryCommandPreparationAttemptId,
                PaymentConfirmationId: attempt.PaymentConfirmationId,
                PaymentAttemptId: attempt.PaymentAttemptId,
                ParkingSessionId: attempt.ParkingSessionId,
                SiteId: attempt.SiteId,
                SitePosServerId: attempt.SitePosServerId,
                SitePosServerRef: attempt.SitePosServerRef,
                LatestReadbackClassificationBasis: attempt.LatestReadbackClassificationBasis,
                RetryEligibilityDecisionBasis: attempt.RetryEligibilityDecisionBasis,
                SemanticRequestHashAvailabilityStatus: attempt.SemanticRequestHashAvailabilityStatus,
                IdempotencyContextAvailabilityStatus: attempt.IdempotencyContextAvailabilityStatus,
                SchedulingPreparationStatus: attempt.SchedulingPreparationStatus,
                SchedulingBlockReasonCode: attempt.SchedulingBlockReasonCode,
                RequestedAt: attempt.RequestedAt,
                EarliestEligibleAt: attempt.EarliestEligibleAt,
                SafeSummary: attempt.SafeSummary,
                CorrelationId: attempt.CorrelationId,
                ServiceIdentityId: attempt.ServiceIdentityId,
                CreatedAt: attempt.RequestedAt);

            Records.Add(record);
            return Task.FromResult(record);
        }

        public Task<FiscalExceptionRetrySchedulingPreparationAttemptSummary?> GetSummaryAsync(
            Guid fiscalIssuanceReferenceId,
            CancellationToken cancellationToken)
        {
            var latest = Records
                .Where(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId)
                .OrderByDescending(record => record.RequestedAt)
                .FirstOrDefault();

            if (latest is null)
            {
                return Task.FromResult<FiscalExceptionRetrySchedulingPreparationAttemptSummary?>(null);
            }

            return Task.FromResult<FiscalExceptionRetrySchedulingPreparationAttemptSummary?>(
                new FiscalExceptionRetrySchedulingPreparationAttemptSummary(
                    LastSchedulingPreparationStatus: latest.SchedulingPreparationStatus,
                    LastRequestedAt: latest.RequestedAt,
                    AttemptCount: Records.Count(record => record.FiscalIssuanceReferenceId == fiscalIssuanceReferenceId),
                    LastSchedulingBlockReasonCode: latest.SchedulingBlockReasonCode,
                    SafeSummary: latest.SafeSummary));
        }
    }
}
