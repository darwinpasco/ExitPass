using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalExceptionQueueService
{
    Task<IReadOnlyList<FiscalExceptionQueueCaseSummary>> ListAsync(
        FiscalExceptionQueueQuery query,
        CancellationToken cancellationToken);

    Task<FiscalExceptionQueueCaseDetail?> GetAsync(
        Guid caseId,
        CancellationToken cancellationToken);

    Task<FiscalExceptionQueueCaseDetail> CreateOrUpdateFromFiscalReferenceAsync(
        FiscalIssuanceReferenceRecord source,
        CancellationToken cancellationToken);

    Task<FiscalExceptionReadbackPreparation?> PrepareReadbackAsync(
        Guid caseId,
        CancellationToken cancellationToken);
}

public sealed class FiscalExceptionQueueService : IFiscalExceptionQueueService
{
    private const string DuplicateCollapseStrategy = "source_fiscal_issuance_reference_identity";

    private readonly IFiscalExceptionQueueReferenceReader _referenceReader;

    public FiscalExceptionQueueService(IFiscalExceptionQueueReferenceReader referenceReader)
    {
        _referenceReader = referenceReader;
    }

    public async Task<IReadOnlyList<FiscalExceptionQueueCaseSummary>> ListAsync(
        FiscalExceptionQueueQuery query,
        CancellationToken cancellationToken)
    {
        var records = await _referenceReader.ListFiscalExceptionReferencesAsync(
            NormalizeQuery(query),
            cancellationToken);

        return records
            .Where(IsFiscalExceptionCandidate)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<FiscalExceptionQueueCaseDetail?> GetAsync(
        Guid caseId,
        CancellationToken cancellationToken)
    {
        if (caseId == Guid.Empty)
        {
            throw new ArgumentException("FEQ case id is required.", nameof(caseId));
        }

        var record = await _referenceReader.FindFiscalExceptionReferenceAsync(caseId, cancellationToken);
        return record is null || !IsFiscalExceptionCandidate(record)
            ? null
            : ToDetail(record);
    }

    public Task<FiscalExceptionQueueCaseDetail> CreateOrUpdateFromFiscalReferenceAsync(
        FiscalIssuanceReferenceRecord source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!IsFiscalExceptionCandidate(source))
        {
            throw new ArgumentException(
                "Fiscal issuance reference is not in a FEQ-visible exception state.",
                nameof(source));
        }

        // This first FEQ slice uses the fiscal reference row as the stable case identity.
        // Duplicate detections for the same fiscal reference collapse to the same case id.
        return Task.FromResult(ToDetail(source));
    }

    public async Task<FiscalExceptionReadbackPreparation?> PrepareReadbackAsync(
        Guid caseId,
        CancellationToken cancellationToken)
    {
        var detail = await GetAsync(caseId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var summary = detail.Summary;
        var readbackRequired = summary.ReadbackStatus != FiscalExceptionReadbackStatus.NotRequired;

        return new FiscalExceptionReadbackPreparation(
            CaseId: summary.CaseId,
            FiscalIssuanceReferenceId: summary.FiscalIssuanceReferenceId,
            ReadbackRequired: readbackRequired,
            ReadbackStatus: readbackRequired
                ? FiscalExceptionReadbackStatus.PendingFutureSlice
                : FiscalExceptionReadbackStatus.NotRequired,
            KnownPosServerFiscalDocumentId: detail.PosServerFiscalDocumentId,
            SitePosServerId: summary.SitePosServerId,
            SitePosServerRef: summary.SitePosServerRef,
            UpstreamFinalityReference: summary.UpstreamFinalityReference,
            PayableBasisRef: summary.PayableBasisRef,
            RetryEligibilityStatus: summary.RetryEligibilityStatus,
            RetryExecutionAvailable: false,
            PosServerReadbackCallPerformed: false,
            PreparationStatus: readbackRequired
                ? "readback_contract_prepared_no_pos_server_call"
                : "readback_not_required_for_current_case_state");
    }

    internal static bool IsFiscalExceptionCandidate(FiscalIssuanceReferenceRecord record) =>
        record.FiscalIssuanceState is FiscalIssuanceIntegrationState.FiscalIssuanceConflict
            or FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest
            or FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration
            or FiscalIssuanceIntegrationState.FiscalIssuanceFailedService
            or FiscalIssuanceIntegrationState.FiscalIssuanceUnknown
            or FiscalIssuanceIntegrationState.FiscalIssuanceManualReview
            or FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased
            or FiscalIssuanceIntegrationState.FiscalIssuanceReconciled;

    internal static FiscalExceptionQueueCaseSummary ToSummary(FiscalIssuanceReferenceRecord record)
    {
        var readbackStatus = ResolveReadbackStatus(record);

        return new FiscalExceptionQueueCaseSummary(
            CaseId: record.FiscalIssuanceReferenceId,
            FiscalIssuanceReferenceId: record.FiscalIssuanceReferenceId,
            PaymentConfirmationId: record.PaymentConfirmationId,
            PaymentAttemptId: record.PaymentAttemptId,
            ParkingSessionId: record.ParkingSessionId,
            SiteId: record.SiteId,
            SitePosServerId: record.SitePosServerId,
            SitePosServerRef: record.SitePosServerRef,
            UpstreamFinalityReference: record.UpstreamFinalityReference,
            PayableBasisRef: record.PayableBasisRef,
            Category: ResolveCategory(record),
            QueueState: ResolveQueueState(record),
            FiscalIssuanceState: record.FiscalIssuanceState,
            LatestExceptionReason: record.LatestExceptionReason,
            SafeErrorSummary: SafeErrorSummary(record),
            ReadbackStatus: readbackStatus,
            RetryEligibilityStatus: ResolveRetryEligibility(record, readbackStatus),
            RetryExecutionAvailable: false,
            DuplicateCollapseKey: $"fiscal-reference:{record.FiscalIssuanceReferenceId:N}",
            DuplicateCollapseStrategy: DuplicateCollapseStrategy,
            FirstDetectedAt: record.FirstRecordedAt,
            LastUpdatedAt: record.LastUpdatedAt);
    }

    internal static FiscalExceptionQueueCaseDetail ToDetail(FiscalIssuanceReferenceRecord record) =>
        new(
            Summary: ToSummary(record),
            PosServerFiscalDocumentId: record.PosServerFiscalDocumentId,
            FiscalDocumentNumber: record.FiscalDocumentNumber,
            FiscalIdentityId: record.FiscalIdentityId,
            FiscalSequencePolicyId: record.FiscalSequencePolicyId,
            FiscalSequenceValue: record.FiscalSequenceValue,
            ResultClassification: record.ResultClassification,
            FiscalIssuanceEvidenceStatus: record.FiscalIssuanceEvidenceStatus,
            FiscalNumberAssignmentState: record.FiscalNumberAssignmentState,
            LatestErrorPosture: record.LatestErrorPosture,
            CorrelationId: record.CorrelationId,
            PosServerResponseTimestamp: record.PosServerResponseTimestamp,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalNumberEditingAllowed: false,
            ManualFiscalDocumentCreationAllowed: false);

    private static FiscalExceptionQueueQuery NormalizeQuery(FiscalExceptionQueueQuery query) =>
        query with
        {
            Limit = query.Limit switch
            {
                < 1 => 100,
                > 500 => 500,
                _ => query.Limit
            }
        };

    private static FiscalExceptionQueueCategory ResolveCategory(FiscalIssuanceReferenceRecord record) =>
        record.LatestExceptionReason switch
        {
            FiscalIssuanceExceptionReason.PostTimeout => FiscalExceptionQueueCategory.PosServerTimeout,
            FiscalIssuanceExceptionReason.NetworkDisconnectAfterPossibleCommit => FiscalExceptionQueueCategory.UnknownOutcome,
            FiscalIssuanceExceptionReason.GetReadbackInconclusive => FiscalExceptionQueueCategory.UnknownOutcome,
            FiscalIssuanceExceptionReason.GetReadbackNotFound => FiscalExceptionQueueCategory.UnknownOutcome,
            FiscalIssuanceExceptionReason.GetReadbackServiceFailed => FiscalExceptionQueueCategory.PosServerUnavailable,
            FiscalIssuanceExceptionReason.CentralPmsReferencePersistenceFailed => FiscalExceptionQueueCategory.PosServerAcceptedCentralPmsRecordingFailed,
            FiscalIssuanceExceptionReason.PersistenceWriteFailed => FiscalExceptionQueueCategory.PosServerAcceptedCentralPmsRecordingFailed,
            FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict => FiscalExceptionQueueCategory.IdempotencyConflict,
            FiscalIssuanceExceptionReason.ReplayMismatch => FiscalExceptionQueueCategory.SemanticRequestHashMismatch,
            FiscalIssuanceExceptionReason.FiscalReferenceMismatch => FiscalExceptionQueueCategory.FiscalMismatch,
            FiscalIssuanceExceptionReason.ManualReviewRequired => FiscalExceptionQueueCategory.ManualReviewRequired,
            FiscalIssuanceExceptionReason.FiscalIdentityNotFound
                or FiscalIssuanceExceptionReason.FiscalIdentityAmbiguous
                or FiscalIssuanceExceptionReason.FiscalIdentityNotEffective
                or FiscalIssuanceExceptionReason.FiscalSequencePolicyNotFound
                or FiscalIssuanceExceptionReason.FiscalSequencePolicyAmbiguous
                or FiscalIssuanceExceptionReason.FiscalSequencePolicyNotEffective
                or FiscalIssuanceExceptionReason.FiscalSequenceStateNotFound
                or FiscalIssuanceExceptionReason.FiscalSequenceStateNotEffective
                or FiscalIssuanceExceptionReason.FiscalNumberAllocationFailed
                or FiscalIssuanceExceptionReason.FiscalDocumentNumberFormatFailed =>
                FiscalExceptionQueueCategory.FiscalConfigurationMissing,
            FiscalIssuanceExceptionReason.RequestConstructionError
                or FiscalIssuanceExceptionReason.MissingPayableBasis
                or FiscalIssuanceExceptionReason.MissingUpstreamFinalityReference
                or FiscalIssuanceExceptionReason.UnsupportedFiscalDocumentRequest
                or FiscalIssuanceExceptionReason.InvalidFiscalTender
                or FiscalIssuanceExceptionReason.MissingFiscalTender
                or FiscalIssuanceExceptionReason.InvalidFiscalTaxDetail
                or FiscalIssuanceExceptionReason.InvalidFiscalDiscountPrivilegeDetail
                or FiscalIssuanceExceptionReason.InvalidFiscalTotal
                or FiscalIssuanceExceptionReason.SensitivePayloadRejected =>
                FiscalExceptionQueueCategory.CentralPmsMappingFailure,
            _ => record.FiscalIssuanceState switch
            {
                FiscalIssuanceIntegrationState.FiscalIssuanceUnknown => FiscalExceptionQueueCategory.UnknownOutcome,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration => FiscalExceptionQueueCategory.FiscalConfigurationMissing,
                FiscalIssuanceIntegrationState.FiscalIssuanceConflict => FiscalExceptionQueueCategory.IdempotencyConflict,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedService => FiscalExceptionQueueCategory.PosServerUnavailable,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest => FiscalExceptionQueueCategory.CentralPmsMappingFailure,
                FiscalIssuanceIntegrationState.FiscalIssuanceManualReview => FiscalExceptionQueueCategory.ManualReviewRequired,
                _ => FiscalExceptionQueueCategory.OtherFiscalFailure
            }
        };

    private static FiscalExceptionQueueState ResolveQueueState(FiscalIssuanceReferenceRecord record) =>
        record.FiscalIssuanceState switch
        {
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown => FiscalExceptionQueueState.ReadbackRequired,
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration => FiscalExceptionQueueState.BlockedRequiresConfigFix,
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict => FiscalExceptionQueueState.MismatchReview,
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview => FiscalExceptionQueueState.ManualReviewRequired,
            FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased => FiscalExceptionQueueState.ManualReviewRequired,
            FiscalIssuanceIntegrationState.FiscalIssuanceReconciled => FiscalExceptionQueueState.Reconciled,
            _ => FiscalExceptionQueueState.Queued
        };

    private static FiscalExceptionReadbackStatus ResolveReadbackStatus(FiscalIssuanceReferenceRecord record) =>
        record.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceUnknown ||
        record.LatestExceptionReason is FiscalIssuanceExceptionReason.PostTimeout
            or FiscalIssuanceExceptionReason.NetworkDisconnectAfterPossibleCommit
            or FiscalIssuanceExceptionReason.GetReadbackInconclusive
            or FiscalIssuanceExceptionReason.GetReadbackNotFound
            or FiscalIssuanceExceptionReason.GetReadbackServiceFailed
            or FiscalIssuanceExceptionReason.FiscalNumberAssignmentIncomplete
            or FiscalIssuanceExceptionReason.CentralPmsReferencePersistenceFailed
            ? FiscalExceptionReadbackStatus.RequiredNotStarted
            : FiscalExceptionReadbackStatus.NotRequired;

    private static FiscalExceptionRetryEligibilityStatus ResolveRetryEligibility(
        FiscalIssuanceReferenceRecord record,
        FiscalExceptionReadbackStatus readbackStatus)
    {
        if (record.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceReconciled)
        {
            return FiscalExceptionRetryEligibilityStatus.NotRequiredRecorded;
        }

        if (readbackStatus != FiscalExceptionReadbackStatus.NotRequired)
        {
            return FiscalExceptionRetryEligibilityStatus.BlockedPendingReadback;
        }

        if (ResolveQueueState(record) is FiscalExceptionQueueState.MismatchReview
            or FiscalExceptionQueueState.ManualReviewRequired)
        {
            return FiscalExceptionRetryEligibilityStatus.BlockedManualReview;
        }

        if (ResolveQueueState(record) == FiscalExceptionQueueState.BlockedRequiresConfigFix)
        {
            return FiscalExceptionRetryEligibilityStatus.BlockedConfiguration;
        }

        return FiscalExceptionRetryEligibilityStatus.UnavailableInThisSlice;
    }

    private static string? SafeErrorSummary(FiscalIssuanceReferenceRecord record)
    {
        if (record.LatestExceptionReason is null && string.IsNullOrWhiteSpace(record.LatestErrorCode))
        {
            return null;
        }

        var reason = record.LatestExceptionReason?.ToString() ?? "FiscalIssuanceException";
        return string.IsNullOrWhiteSpace(record.LatestErrorCode)
            ? reason
            : $"{reason}:{record.LatestErrorCode}";
    }
}

