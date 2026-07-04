using ExitPass.CentralPms.Domain.FiscalIssuance;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalExceptionReadbackWorker
{
    Task<FiscalExceptionReadbackWorkerResult> RunReadbackAsync(
        Guid caseId,
        Guid? correlationId,
        Guid? serviceIdentityId,
        CancellationToken cancellationToken);
}

public interface IFiscalExceptionReadbackClient
{
    bool SupportsFiscalDocumentIdReadback { get; }

    Task<PosServerFiscalDocumentReadResult> GetFiscalDocumentAsync(
        Guid fiscalDocumentId,
        CancellationToken cancellationToken);
}

public sealed class PosServerFiscalExceptionReadbackClient : IFiscalExceptionReadbackClient
{
    private readonly IPosServerFiscalDocumentClient _client;

    public PosServerFiscalExceptionReadbackClient(IPosServerFiscalDocumentClient client)
    {
        _client = client;
    }

    public bool SupportsFiscalDocumentIdReadback => true;

    public Task<PosServerFiscalDocumentReadResult> GetFiscalDocumentAsync(
        Guid fiscalDocumentId,
        CancellationToken cancellationToken) =>
        _client.GetFiscalDocumentAsync(fiscalDocumentId, cancellationToken);
}

public sealed class FiscalExceptionReadbackWorker : IFiscalExceptionReadbackWorker
{
    private readonly IFiscalExceptionQueueService _queueService;
    private readonly IFiscalExceptionReadbackClient _readbackClient;
    private readonly IFiscalIssuanceOrchestrationService _orchestrationService;
    private readonly ILogger<FiscalExceptionReadbackWorker> _logger;

    public FiscalExceptionReadbackWorker(
        IFiscalExceptionQueueService queueService,
        IFiscalExceptionReadbackClient readbackClient,
        IFiscalIssuanceOrchestrationService orchestrationService,
        ILogger<FiscalExceptionReadbackWorker> logger)
    {
        _queueService = queueService;
        _readbackClient = readbackClient;
        _orchestrationService = orchestrationService;
        _logger = logger;
    }

    public async Task<FiscalExceptionReadbackWorkerResult> RunReadbackAsync(
        Guid caseId,
        Guid? correlationId,
        Guid? serviceIdentityId,
        CancellationToken cancellationToken)
    {
        if (caseId == Guid.Empty)
        {
            throw new ArgumentException("FEQ case id is required.", nameof(caseId));
        }

        var attemptedAt = DateTimeOffset.UtcNow;
        var detail = await _queueService.GetAsync(caseId, cancellationToken);
        if (detail is null)
        {
            return Result(
                caseId,
                caseId,
                FiscalExceptionReadbackClassification.IdentifierMissing,
                attemptedAt,
                "feq_case_not_found",
                posServerReadbackCallAttempted: false,
                updatedCase: null);
        }

        var fiscalDocumentId = detail.PosServerFiscalDocumentId;
        if (fiscalDocumentId is null || fiscalDocumentId == Guid.Empty)
        {
            _logger.LogInformation(
                "FEQ readback skipped for {FiscalIssuanceReferenceId}: identifier missing.",
                detail.Summary.FiscalIssuanceReferenceId);

            return Result(
                detail.Summary.CaseId,
                detail.Summary.FiscalIssuanceReferenceId,
                FiscalExceptionReadbackClassification.IdentifierMissing,
                attemptedAt,
                "pos_server_fiscal_document_id_missing",
                posServerReadbackCallAttempted: false,
                updatedCase: detail);
        }

        if (!_readbackClient.SupportsFiscalDocumentIdReadback)
        {
            _logger.LogInformation(
                "FEQ readback not supported for {FiscalIssuanceReferenceId}.",
                detail.Summary.FiscalIssuanceReferenceId);

            return Result(
                detail.Summary.CaseId,
                detail.Summary.FiscalIssuanceReferenceId,
                FiscalExceptionReadbackClassification.NotSupportedYet,
                attemptedAt,
                "pos_server_fiscal_document_id_readback_not_supported_yet",
                posServerReadbackCallAttempted: false,
                updatedCase: detail);
        }

        PosServerFiscalDocumentReadResult readResult;
        try
        {
            readResult = await _readbackClient.GetFiscalDocumentAsync(fiscalDocumentId.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            readResult = UnavailableReadResult("pos_server_readback_timeout", "POS Server readback timed out.");
        }
        catch (HttpRequestException ex)
        {
            readResult = UnavailableReadResult("pos_server_readback_unavailable", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            readResult = FailedReadResult("pos_server_readback_failed", ex.Message);
        }

        var classification = Classify(detail, readResult);
        var updatedCase = await ApplyClassificationAsync(
            detail,
            readResult,
            classification,
            correlationId,
            serviceIdentityId,
            cancellationToken);

        _logger.LogInformation(
            "FEQ readback classified {FiscalIssuanceReferenceId} as {Classification}.",
            detail.Summary.FiscalIssuanceReferenceId,
            classification);

        return Result(
            detail.Summary.CaseId,
            detail.Summary.FiscalIssuanceReferenceId,
            classification,
            attemptedAt,
            ToSafeSummary(classification, readResult),
            posServerReadbackCallAttempted: true,
            updatedCase);
    }

    internal static FiscalExceptionReadbackClassification Classify(
        FiscalExceptionQueueCaseDetail detail,
        PosServerFiscalDocumentReadResult readResult)
    {
        if (readResult.HttpStatusCode == 404 ||
            string.Equals(readResult.Code, "not_found", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(readResult.Code, "fiscal_document_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return FiscalExceptionReadbackClassification.NotFound;
        }

        if (readResult.HttpStatusCode == 503)
        {
            return FiscalExceptionReadbackClassification.Unavailable;
        }

        if (!readResult.Succeeded)
        {
            return readResult.Outcome == PosServerFiscalDocumentOutcome.InvalidResponse
                ? FiscalExceptionReadbackClassification.Unknown
                : FiscalExceptionReadbackClassification.Failed;
        }

        if (readResult.FiscalDocumentId != detail.PosServerFiscalDocumentId)
        {
            return FiscalExceptionReadbackClassification.Mismatch;
        }

        if (detail.FiscalIssuanceEvidenceStatus is not null &&
            readResult.FiscalIssuanceEvidenceStatus is not null &&
            detail.FiscalIssuanceEvidenceStatus != readResult.FiscalIssuanceEvidenceStatus)
        {
            return FiscalExceptionReadbackClassification.Mismatch;
        }

        if (readResult.FiscalNumberAssignmentState == FiscalNumberAssignmentState.NotAssigned)
        {
            return FiscalExceptionReadbackClassification.Unknown;
        }

        if (detail.FiscalNumberAssignmentState == FiscalNumberAssignmentState.Assigned &&
            readResult.FiscalNumberAssignmentState is not null &&
            detail.FiscalNumberAssignmentState != readResult.FiscalNumberAssignmentState)
        {
            return FiscalExceptionReadbackClassification.Mismatch;
        }

        if (detail.FiscalDocumentStatusCodeId is not null &&
            readResult.FiscalDocumentStatusCodeId is not null &&
            detail.FiscalDocumentStatusCodeId != readResult.FiscalDocumentStatusCodeId)
        {
            return FiscalExceptionReadbackClassification.Mismatch;
        }

        return FiscalExceptionReadbackClassification.Matched;
    }

    private async Task<FiscalExceptionQueueCaseDetail?> ApplyClassificationAsync(
        FiscalExceptionQueueCaseDetail detail,
        PosServerFiscalDocumentReadResult readResult,
        FiscalExceptionReadbackClassification classification,
        Guid? correlationId,
        Guid? serviceIdentityId,
        CancellationToken cancellationToken)
    {
        var outcome = classification switch
        {
            FiscalExceptionReadbackClassification.NotFound => FiscalIssuanceReadbackPlanningOutcome.NotFound,
            FiscalExceptionReadbackClassification.Mismatch => FiscalIssuanceReadbackPlanningOutcome.Mismatch,
            FiscalExceptionReadbackClassification.Failed => FiscalIssuanceReadbackPlanningOutcome.ServiceFailed,
            FiscalExceptionReadbackClassification.Unavailable => FiscalIssuanceReadbackPlanningOutcome.ServiceFailed,
            FiscalExceptionReadbackClassification.Unknown => FiscalIssuanceReadbackPlanningOutcome.Inconclusive,
            _ => (FiscalIssuanceReadbackPlanningOutcome?)null
        };

        if (outcome is null)
        {
            return detail;
        }

        var updatedReference = await _orchestrationService.ApplyReadbackPlanningResultAsync(
            detail.Summary.FiscalIssuanceReferenceId,
            new FiscalIssuanceReadbackPlanningResult(
                Outcome: outcome.Value,
                KnownPosServerFiscalDocumentId: detail.PosServerFiscalDocumentId ?? readResult.FiscalDocumentId,
                ExceptionReason: ToExceptionReason(classification),
                ErrorCode: ToErrorCode(classification, readResult),
                CorrelationId: correlationId ?? detail.CorrelationId,
                ServiceIdentityId: serviceIdentityId),
            cancellationToken);

        return FiscalExceptionQueueService.ToDetail(updatedReference);
    }

    private static FiscalIssuanceExceptionReason? ToExceptionReason(FiscalExceptionReadbackClassification classification) =>
        classification switch
        {
            FiscalExceptionReadbackClassification.NotFound => FiscalIssuanceExceptionReason.GetReadbackNotFound,
            FiscalExceptionReadbackClassification.Mismatch => FiscalIssuanceExceptionReason.FiscalReferenceMismatch,
            FiscalExceptionReadbackClassification.Failed => FiscalIssuanceExceptionReason.GetReadbackServiceFailed,
            FiscalExceptionReadbackClassification.Unavailable => FiscalIssuanceExceptionReason.GetReadbackServiceFailed,
            FiscalExceptionReadbackClassification.Unknown => FiscalIssuanceExceptionReason.GetReadbackInconclusive,
            _ => null
        };

    private static string? ToErrorCode(
        FiscalExceptionReadbackClassification classification,
        PosServerFiscalDocumentReadResult readResult) =>
        classification switch
        {
            FiscalExceptionReadbackClassification.NotFound => "get_readback_not_found",
            FiscalExceptionReadbackClassification.Mismatch => "fiscal_reference_mismatch",
            FiscalExceptionReadbackClassification.Failed => string.IsNullOrWhiteSpace(readResult.Code)
                ? "get_readback_service_failed"
                : readResult.Code,
            FiscalExceptionReadbackClassification.Unavailable => "get_readback_service_failed",
            FiscalExceptionReadbackClassification.Unknown => "get_readback_inconclusive",
            _ => null
        };

    private static string ToSafeSummary(
        FiscalExceptionReadbackClassification classification,
        PosServerFiscalDocumentReadResult readResult) =>
        classification switch
        {
            FiscalExceptionReadbackClassification.Matched => "readback_matched",
            FiscalExceptionReadbackClassification.NotFound => "readback_not_found",
            FiscalExceptionReadbackClassification.Mismatch => "readback_mismatch",
            FiscalExceptionReadbackClassification.Failed => string.IsNullOrWhiteSpace(readResult.Code)
                ? "readback_failed"
                : $"readback_failed:{readResult.Code}",
            FiscalExceptionReadbackClassification.Unavailable => "readback_unavailable",
            FiscalExceptionReadbackClassification.Unknown => "readback_unknown",
            _ => "readback_not_classified"
        };

    private static FiscalExceptionReadbackWorkerResult Result(
        Guid caseId,
        Guid fiscalIssuanceReferenceId,
        FiscalExceptionReadbackClassification classification,
        DateTimeOffset attemptedAt,
        string safeSummary,
        bool posServerReadbackCallAttempted,
        FiscalExceptionQueueCaseDetail? updatedCase) =>
        new(
            CaseId: caseId,
            FiscalIssuanceReferenceId: fiscalIssuanceReferenceId,
            Classification: classification,
            AttemptedAt: attemptedAt,
            SafeSummary: safeSummary,
            PosServerReadbackCallAttempted: posServerReadbackCallAttempted,
            RetryScheduled: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            UpdatedCase: updatedCase);

    private static PosServerFiscalDocumentReadResult FailedReadResult(string code, string message) =>
        new(
            Outcome: PosServerFiscalDocumentOutcome.InvalidResponse,
            Succeeded: false,
            HttpStatusCode: 500,
            Code: code,
            Message: message,
            FiscalDocumentId: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: null,
            FiscalDocumentStatusCodeId: null);

    private static PosServerFiscalDocumentReadResult UnavailableReadResult(string code, string message) =>
        new(
            Outcome: PosServerFiscalDocumentOutcome.FailedService,
            Succeeded: false,
            HttpStatusCode: 503,
            Code: code,
            Message: message,
            FiscalDocumentId: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: null,
            FiscalDocumentStatusCodeId: null);
}

