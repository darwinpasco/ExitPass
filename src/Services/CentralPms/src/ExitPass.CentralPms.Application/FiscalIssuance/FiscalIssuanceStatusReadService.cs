using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

#pragma warning disable CS1591

public interface IFiscalIssuanceStatusReadService
{
    Task<FiscalIssuanceStatusReadModel?> GetByReferenceIdAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken);
}

public sealed class FiscalIssuanceStatusReadService : IFiscalIssuanceStatusReadService
{
    private readonly IFiscalIssuanceReferenceRepository _repository;

    public FiscalIssuanceStatusReadService(IFiscalIssuanceReferenceRepository repository)
    {
        _repository = repository;
    }

    public async Task<FiscalIssuanceStatusReadModel?> GetByReferenceIdAsync(
        Guid fiscalIssuanceReferenceId,
        CancellationToken cancellationToken)
    {
        if (fiscalIssuanceReferenceId == Guid.Empty)
        {
            return null;
        }

        var reference = await _repository.FindByFiscalIssuanceReferenceIdAsync(
            fiscalIssuanceReferenceId,
            cancellationToken);

        return reference is null ? null : FromReference(reference);
    }

    private static FiscalIssuanceStatusReadModel FromReference(FiscalIssuanceReferenceRecord reference) =>
        new(
            FiscalIssuanceReferenceId: reference.FiscalIssuanceReferenceId,
            FiscalIssuanceState: ToWireValue(reference.FiscalIssuanceState),
            ResultClassification: ToWireValue(reference.ResultClassification),
            FiscalIssuanceEvidenceStatus: ToWireValue(reference.FiscalIssuanceEvidenceStatus),
            FiscalNumberAssignmentState: ToWireValue(reference.FiscalNumberAssignmentState),
            UpstreamFinalityReference: reference.UpstreamFinalityReference,
            PaymentConfirmationId: reference.PaymentConfirmationId,
            PaymentAttemptId: reference.PaymentAttemptId,
            ParkingSessionId: reference.ParkingSessionId,
            SiteId: reference.SiteId,
            SitePosServerId: reference.SitePosServerId,
            SitePosServerRef: reference.SitePosServerRef,
            FiscalDocumentTypeCodeId: reference.FiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: reference.FiscalDocumentTypeCodeKey,
            PosServerFiscalDocumentId: reference.PosServerFiscalDocumentId,
            FiscalDocumentNumber: reference.FiscalDocumentNumber,
            FiscalIdentityId: reference.FiscalIdentityId,
            FiscalSequencePolicyId: reference.FiscalSequencePolicyId,
            FiscalSequenceValue: reference.FiscalSequenceValue,
            FiscalSeries: reference.FiscalSeries,
            FiscalNumberPrefixText: reference.FiscalNumberPrefixText,
            FiscalNumberSuffixText: reference.FiscalNumberSuffixText,
            FiscalNumberAssignedAt: reference.FiscalNumberAssignedAt,
            FiscalNumberAssignedByRef: reference.FiscalNumberAssignedByRef,
            SemanticRequestHashValue: reference.SemanticRequestHashValue,
            SemanticRequestHashVersion: reference.SemanticRequestHashSourceVersion,
            SemanticRequestHashStatus: ToWireValue(reference.SemanticRequestHashStatus),
            SemanticRequestHashAlgorithm: reference.SemanticRequestHashAlgorithm,
            SemanticRequestHashSourceFactCount: reference.SemanticRequestHashSourceFactCount,
            LatestErrorCode: reference.LatestErrorCode,
            LatestErrorPosture: ToWireValue(reference.LatestErrorPosture),
            LatestExceptionReason: ToWireValue(reference.LatestExceptionReason),
            FirstRecordedAt: reference.FirstRecordedAt,
            LastUpdatedAt: reference.LastUpdatedAt,
            CorrelationId: reference.CorrelationId);

    private static string ToWireValue(FiscalIssuanceIntegrationState value) =>
        value switch
        {
            FiscalIssuanceIntegrationState.NotRequired => "NOT_REQUIRED",
            FiscalIssuanceIntegrationState.PendingFiscalIssuance => "PENDING_FISCAL_ISSUANCE",
            FiscalIssuanceIntegrationState.FiscalIssuanceRequested => "FISCAL_ISSUANCE_REQUESTED",
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded => "FISCAL_ISSUANCE_RECORDED",
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed => "FISCAL_ISSUANCE_REPLAYED",
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict => "FISCAL_ISSUANCE_CONFLICT",
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest => "FISCAL_ISSUANCE_FAILED_REQUEST",
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration => "FISCAL_ISSUANCE_FAILED_CONFIGURATION",
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedService => "FISCAL_ISSUANCE_FAILED_SERVICE",
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown => "FISCAL_ISSUANCE_UNKNOWN",
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview => "FISCAL_ISSUANCE_MANUAL_REVIEW",
            FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased => "FISCAL_ISSUANCE_EXCEPTION_RELEASED",
            FiscalIssuanceIntegrationState.FiscalIssuanceReconciled => "FISCAL_ISSUANCE_RECONCILED",
            _ => value.ToString()
        };

    private static string ToWireValue(FiscalNumberAssignmentState value) =>
        value switch
        {
            FiscalNumberAssignmentState.NotAssigned => "NOT_ASSIGNED",
            FiscalNumberAssignmentState.Assigned => "ASSIGNED",
            _ => value.ToString()
        };

    private static string? ToWireValue(FiscalIssuanceResultClassification? value) =>
        value switch
        {
            null => null,
            FiscalIssuanceResultClassification.NewlyCreated => "NEWLY_CREATED",
            FiscalIssuanceResultClassification.IdempotentReplay => "IDEMPOTENT_REPLAY",
            _ => value.ToString()
        };

    private static string? ToWireValue(FiscalIssuanceEvidenceStatus? value) =>
        value switch
        {
            null => null,
            FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned => "FISCAL_DOCUMENT_NUMBER_ASSIGNED",
            _ => value.ToString()
        };

    private static string? ToWireValue(FiscalIssuanceErrorPosture? value) =>
        value switch
        {
            null => null,
            FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange => "DO_NOT_RETRY_WITHOUT_REQUEST_CHANGE",
            FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection => "RETRY_AFTER_CONFIGURATION_CORRECTION",
            FiscalIssuanceErrorPosture.RetryAfterServiceRecovery => "RETRY_AFTER_SERVICE_RECOVERY",
            _ => value.ToString()
        };

    private static string? ToWireValue(FiscalIssuanceExceptionReason? value) =>
        value is null ? null : ToSnakeUpper(value.Value.ToString());

    private static string? ToWireValue(FiscalSemanticRequestHashSourceStatus? value) =>
        value switch
        {
            null => null,
            FiscalSemanticRequestHashSourceStatus.Unavailable => "UNAVAILABLE",
            FiscalSemanticRequestHashSourceStatus.Incomplete => "INCOMPLETE",
            FiscalSemanticRequestHashSourceStatus.Available => "AVAILABLE",
            _ => value.ToString()
        };

    private static string ToSnakeUpper(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var characters = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) && !char.IsUpper(value[index - 1]))
            {
                characters.Add('_');
            }

            characters.Add(char.ToUpperInvariant(current));
        }

        return new string(characters.ToArray());
    }
}

public sealed record FiscalIssuanceStatusReadModel(
    Guid FiscalIssuanceReferenceId,
    string FiscalIssuanceState,
    string? ResultClassification,
    string? FiscalIssuanceEvidenceStatus,
    string FiscalNumberAssignmentState,
    string UpstreamFinalityReference,
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    Guid? FiscalDocumentTypeCodeId,
    string? FiscalDocumentTypeCodeKey,
    Guid? PosServerFiscalDocumentId,
    string? FiscalDocumentNumber,
    Guid? FiscalIdentityId,
    Guid? FiscalSequencePolicyId,
    long? FiscalSequenceValue,
    string? FiscalSeries,
    string? FiscalNumberPrefixText,
    string? FiscalNumberSuffixText,
    DateTimeOffset? FiscalNumberAssignedAt,
    string? FiscalNumberAssignedByRef,
    string? SemanticRequestHashValue,
    string? SemanticRequestHashVersion,
    string? SemanticRequestHashStatus,
    string? SemanticRequestHashAlgorithm,
    int? SemanticRequestHashSourceFactCount,
    string? LatestErrorCode,
    string? LatestErrorPosture,
    string? LatestExceptionReason,
    DateTimeOffset FirstRecordedAt,
    DateTimeOffset LastUpdatedAt,
    Guid? CorrelationId);

#pragma warning restore CS1591
