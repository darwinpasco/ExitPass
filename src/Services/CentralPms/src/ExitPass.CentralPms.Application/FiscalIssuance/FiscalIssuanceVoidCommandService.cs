using ExitPass.CentralPms.Domain.FiscalIssuance;
using Microsoft.Extensions.Logging;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

#pragma warning disable CS1591

public interface IFiscalIssuanceVoidCommandService
{
    Task<FiscalIssuanceVoidCommandResponse> VoidAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceVoidCommandRequest request,
        CancellationToken cancellationToken);
}

public sealed class FiscalIssuanceVoidCommandService : IFiscalIssuanceVoidCommandService
{
    public const int MaxReasonTextLength = 512;
    public const string SourceSystemRef = "central-pms";

    private readonly IFiscalIssuanceReferenceRepository _referenceRepository;
    private readonly IPosServerFiscalDocumentClient _posServerClient;
    private readonly ILogger<FiscalIssuanceVoidCommandService> _logger;

    public FiscalIssuanceVoidCommandService(
        IFiscalIssuanceReferenceRepository referenceRepository,
        IPosServerFiscalDocumentClient posServerClient,
        ILogger<FiscalIssuanceVoidCommandService> logger)
    {
        _referenceRepository = referenceRepository;
        _posServerClient = posServerClient;
        _logger = logger;
    }

    public async Task<FiscalIssuanceVoidCommandResponse> VoidAsync(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceVoidCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationErrors = ValidateRequest(fiscalIssuanceReferenceId, request);
        if (validationErrors.Count > 0)
        {
            return Rejected(
                fiscalIssuanceReferenceId,
                null,
                request,
                "fiscal_void_request_rejected",
                400,
                validationErrors);
        }

        var reference = await _referenceRepository.FindByFiscalIssuanceReferenceIdAsync(
                fiscalIssuanceReferenceId,
                cancellationToken)
            .ConfigureAwait(false);
        if (reference is null)
        {
            return Rejected(
                fiscalIssuanceReferenceId,
                null,
                request,
                "fiscal_issuance_reference_not_found",
                404,
                ["fiscal_issuance_reference_not_found"]);
        }

        var referenceErrors = ValidateReference(reference);
        if (referenceErrors.Count > 0)
        {
            return Rejected(
                fiscalIssuanceReferenceId,
                reference,
                request,
                "fiscal_void_reference_rejected",
                409,
                referenceErrors);
        }

        PosServerFiscalDocumentVoidResult posResult;
        try
        {
            posResult = await _posServerClient.VoidFiscalDocumentAsync(
                    reference.PosServerFiscalDocumentId!.Value,
                    new PosServerFiscalDocumentVoidRequest(
                        IdempotencyKey: request.IdempotencyKey!.Trim(),
                        ReasonCode: request.ReasonCode!.Trim(),
                        ReasonText: NormalizeOptional(request.ReasonText),
                        RequestedByRef: request.RequestedByRef!.Trim(),
                        RequestedAt: request.RequestedAt,
                        CorrelationId: ResolveCorrelationId(request, reference, fiscalIssuanceReferenceId),
                        SourceSystemRef: SourceSystemRef,
                        BusinessDayDate: null),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Fiscal issuance void command failed safely for reference {FiscalIssuanceReferenceId}.",
                fiscalIssuanceReferenceId);

            return Rejected(
                fiscalIssuanceReferenceId,
                reference,
                request,
                "pos_server_void_failed",
                503,
                ["pos_server_void_failed"]);
        }

        return posResult.Succeeded
            ? Accepted(fiscalIssuanceReferenceId, reference, request, posResult)
            : Rejected(
                fiscalIssuanceReferenceId,
                reference,
                request,
                MapFailedStatus(posResult.Outcome),
                MapFailedHttpStatus(posResult),
                [SafeErrorCode(posResult)],
                posResult);
    }

    private static IReadOnlyList<string> ValidateRequest(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceVoidCommandRequest request)
    {
        var errors = new List<string>();
        if (fiscalIssuanceReferenceId == Guid.Empty)
        {
            errors.Add("fiscal_issuance_reference_id_required");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            errors.Add("idempotency_key_required");
        }

        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            errors.Add("reason_code_required");
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByRef))
        {
            errors.Add("requested_by_ref_required");
        }

        if (request.ReasonText?.Length > MaxReasonTextLength)
        {
            errors.Add("reason_text_too_long");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateReference(FiscalIssuanceReferenceRecord reference)
    {
        var errors = new List<string>();
        if (reference.FiscalIssuanceState != FiscalIssuanceIntegrationState.FiscalIssuanceRecorded)
        {
            errors.Add("fiscal_reference_not_recorded");
        }

        if (reference.PosServerFiscalDocumentId is null || reference.PosServerFiscalDocumentId == Guid.Empty)
        {
            errors.Add("pos_server_fiscal_document_id_required");
        }

        return errors;
    }

    private static FiscalIssuanceVoidCommandResponse Accepted(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceReferenceRecord reference,
        FiscalIssuanceVoidCommandRequest request,
        PosServerFiscalDocumentVoidResult posResult) =>
        new(
            Accepted: true,
            Status: MapSuccessfulStatus(posResult.Outcome),
            HttpStatusCode: 200,
            Errors: Array.Empty<string>(),
            FiscalIssuanceReferenceId: fiscalIssuanceReferenceId,
            PosServerFiscalDocumentId: posResult.FiscalDocumentId ?? reference.PosServerFiscalDocumentId,
            FiscalDocumentNumber: posResult.FiscalDocumentNumber ?? reference.FiscalDocumentNumber,
            FiscalSequenceValue: posResult.FiscalSequenceValue ?? reference.FiscalSequenceValue,
            FiscalDocumentStatusPosture: posResult.FiscalDocumentStatus,
            VoidStatus: posResult.VoidStatus,
            VoidReasonCode: posResult.VoidReasonCode,
            VoidedAt: posResult.VoidedAt,
            PosServerResultClassification: posResult.ResultClassification,
            IdempotencyKey: posResult.IdempotencyKey ?? request.IdempotencyKey,
            CorrelationId: posResult.CorrelationId ?? ResolveCorrelationId(request, reference, fiscalIssuanceReferenceId),
            ErrorPosture: posResult.ErrorPosture,
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
            IdempotentReplay: posResult.Outcome == PosServerFiscalDocumentVoidOutcome.IdempotentReplay);

    private static FiscalIssuanceVoidCommandResponse Rejected(
        Guid fiscalIssuanceReferenceId,
        FiscalIssuanceReferenceRecord? reference,
        FiscalIssuanceVoidCommandRequest request,
        string status,
        int httpStatusCode,
        IReadOnlyList<string> errors,
        PosServerFiscalDocumentVoidResult? posResult = null) =>
        new(
            Accepted: false,
            Status: status,
            HttpStatusCode: httpStatusCode,
            Errors: errors,
            FiscalIssuanceReferenceId: fiscalIssuanceReferenceId,
            PosServerFiscalDocumentId: reference?.PosServerFiscalDocumentId ?? posResult?.FiscalDocumentId,
            FiscalDocumentNumber: reference?.FiscalDocumentNumber ?? posResult?.FiscalDocumentNumber,
            FiscalSequenceValue: reference?.FiscalSequenceValue ?? posResult?.FiscalSequenceValue,
            FiscalDocumentStatusPosture: posResult?.FiscalDocumentStatus,
            VoidStatus: posResult?.VoidStatus,
            VoidReasonCode: posResult?.VoidReasonCode,
            VoidedAt: posResult?.VoidedAt,
            PosServerResultClassification: posResult?.ResultClassification,
            IdempotencyKey: posResult?.IdempotencyKey ?? request.IdempotencyKey,
            CorrelationId: posResult?.CorrelationId ?? reference?.CorrelationId?.ToString("D") ?? request.CorrelationId,
            ErrorPosture: posResult?.ErrorPosture,
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
            IdempotentReplay: false);

    private static string MapSuccessfulStatus(PosServerFiscalDocumentVoidOutcome outcome) =>
        outcome switch
        {
            PosServerFiscalDocumentVoidOutcome.NewlyVoided => "pos_server_void_recorded",
            PosServerFiscalDocumentVoidOutcome.IdempotentReplay => "pos_server_void_idempotent_replay",
            PosServerFiscalDocumentVoidOutcome.AlreadyVoided => "pos_server_already_voided",
            _ => "pos_server_void_failed"
        };

    private static string MapFailedStatus(PosServerFiscalDocumentVoidOutcome outcome) =>
        outcome switch
        {
            PosServerFiscalDocumentVoidOutcome.Conflict => "pos_server_void_conflict",
            PosServerFiscalDocumentVoidOutcome.Rejected or
            PosServerFiscalDocumentVoidOutcome.NotFound => "pos_server_void_rejected",
            _ => "pos_server_void_failed"
        };

    private static int MapFailedHttpStatus(PosServerFiscalDocumentVoidResult posResult) =>
        posResult.Outcome switch
        {
            PosServerFiscalDocumentVoidOutcome.Conflict => 409,
            PosServerFiscalDocumentVoidOutcome.Rejected => 400,
            PosServerFiscalDocumentVoidOutcome.NotFound => 404,
            PosServerFiscalDocumentVoidOutcome.FailedService => 503,
            _ => posResult.HttpStatusCode is >= 400 and < 600 ? posResult.HttpStatusCode : 409
        };

    private static string SafeErrorCode(PosServerFiscalDocumentVoidResult posResult) =>
        string.IsNullOrWhiteSpace(posResult.Code) ? MapFailedStatus(posResult.Outcome) : posResult.Code;

    private static string ResolveCorrelationId(
        FiscalIssuanceVoidCommandRequest request,
        FiscalIssuanceReferenceRecord reference,
        Guid fiscalIssuanceReferenceId) =>
        NormalizeOptional(request.CorrelationId)
        ?? reference.CorrelationId?.ToString("D")
        ?? fiscalIssuanceReferenceId.ToString("D");

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record FiscalIssuanceVoidCommandRequest(
    string? IdempotencyKey,
    string? ReasonCode,
    string? ReasonText,
    string? RequestedByRef,
    DateTimeOffset? RequestedAt,
    string? CorrelationId);

public sealed record FiscalIssuanceVoidCommandResponse(
    bool Accepted,
    string Status,
    int HttpStatusCode,
    IReadOnlyList<string> Errors,
    Guid FiscalIssuanceReferenceId,
    Guid? PosServerFiscalDocumentId,
    string? FiscalDocumentNumber,
    long? FiscalSequenceValue,
    string? FiscalDocumentStatusPosture,
    string? VoidStatus,
    string? VoidReasonCode,
    DateTimeOffset? VoidedAt,
    string? PosServerResultClassification,
    string? IdempotencyKey,
    string? CorrelationId,
    string? ErrorPosture,
    bool NewFiscalNumberAllocated,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool RefundOrReversalCreated,
    bool HikCentralCalled,
    bool PaymentProviderCalled,
    bool RenderingGenerated,
    bool ReplacementFiscalDocumentCreated,
    bool FiscalSequenceChangedByCentralPms,
    bool IdempotentReplay);

#pragma warning restore CS1591
