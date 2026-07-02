using System.Net;
using System.Text.Json;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public static class PosServerFiscalDocumentResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PosServerFiscalDocumentCreateResult ParseCreateResponse(
        int httpStatusCode,
        string responseBody)
    {
        PosServerCreateResponseEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PosServerCreateResponseEnvelope>(responseBody, JsonOptions);
        }
        catch (JsonException)
        {
            return InvalidCreateResponse(httpStatusCode, "invalid_json_response", "POS Server response body was not valid JSON.");
        }

        if (envelope is null)
        {
            return InvalidCreateResponse(httpStatusCode, "empty_response", "POS Server response body was empty.");
        }

        var resultClassification = ParseResultClassification(envelope.ResultClassification);
        var evidenceStatus = ParseEvidenceStatus(envelope.FiscalIssuanceEvidenceStatus);
        var assignmentState = ParseAssignmentState(envelope.FiscalNumberAssignmentState);
        var errorPosture = ParseErrorPosture(envelope.ErrorPosture);

        if (httpStatusCode == (int)HttpStatusCode.Accepted && envelope.Code == "accepted" && envelope.Succeeded)
        {
            if (!HasCompleteFiscalNumberingEvidence(envelope, evidenceStatus, assignmentState, resultClassification))
            {
                return new PosServerFiscalDocumentCreateResult(
                    Outcome: PosServerFiscalDocumentOutcome.InvalidResponse,
                    Succeeded: false,
                    HttpStatusCode: httpStatusCode,
                    Code: "fiscal_number_assignment_incomplete",
                    Message: "POS Server accepted response lacked complete fiscal numbering evidence.",
                    FiscalDocumentId: envelope.FiscalDocumentId,
                    ResultClassification: resultClassification,
                    FiscalIssuanceEvidenceStatus: evidenceStatus,
                    FiscalNumberAssignmentState: assignmentState,
                    FiscalIdentityId: envelope.FiscalIdentityId,
                    FiscalDocumentStatusCodeId: envelope.FiscalDocumentStatusCodeId,
                    FiscalSequencePolicyId: envelope.FiscalSequencePolicyId,
                    FiscalSequenceValue: envelope.FiscalSequenceValue,
                    FiscalDocumentNumber: envelope.FiscalDocumentNumber,
                    FiscalSeries: envelope.FiscalSeries,
                    FiscalNumberPrefixText: envelope.FiscalNumberPrefixText,
                    FiscalNumberSuffixText: envelope.FiscalNumberSuffixText,
                    FiscalNumberAssignedAt: envelope.FiscalNumberAssignedAt,
                    FiscalNumberAssignedByRef: envelope.FiscalNumberAssignedByRef,
                    ErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery);
            }

            return new PosServerFiscalDocumentCreateResult(
                Outcome: PosServerFiscalDocumentOutcome.Accepted,
                Succeeded: true,
                HttpStatusCode: httpStatusCode,
                Code: envelope.Code,
                Message: envelope.Message,
                FiscalDocumentId: envelope.FiscalDocumentId,
                ResultClassification: resultClassification,
                FiscalIssuanceEvidenceStatus: evidenceStatus,
                FiscalNumberAssignmentState: assignmentState,
                FiscalIdentityId: envelope.FiscalIdentityId,
                FiscalDocumentStatusCodeId: envelope.FiscalDocumentStatusCodeId,
                FiscalSequencePolicyId: envelope.FiscalSequencePolicyId,
                FiscalSequenceValue: envelope.FiscalSequenceValue,
                FiscalDocumentNumber: envelope.FiscalDocumentNumber,
                FiscalSeries: envelope.FiscalSeries,
                FiscalNumberPrefixText: envelope.FiscalNumberPrefixText,
                FiscalNumberSuffixText: envelope.FiscalNumberSuffixText,
                FiscalNumberAssignedAt: envelope.FiscalNumberAssignedAt,
                FiscalNumberAssignedByRef: envelope.FiscalNumberAssignedByRef,
                ErrorPosture: null);
        }

        return new PosServerFiscalDocumentCreateResult(
            Outcome: MapFailureOutcome(httpStatusCode, envelope.Code, errorPosture),
            Succeeded: false,
            HttpStatusCode: httpStatusCode,
            Code: string.IsNullOrWhiteSpace(envelope.Code) ? "pos_server_failure" : envelope.Code,
            Message: envelope.Message,
            FiscalDocumentId: envelope.FiscalDocumentId,
            ResultClassification: resultClassification,
            FiscalIssuanceEvidenceStatus: evidenceStatus,
            FiscalNumberAssignmentState: assignmentState,
            FiscalIdentityId: envelope.FiscalIdentityId,
            FiscalDocumentStatusCodeId: envelope.FiscalDocumentStatusCodeId,
            FiscalSequencePolicyId: envelope.FiscalSequencePolicyId,
            FiscalSequenceValue: envelope.FiscalSequenceValue,
            FiscalDocumentNumber: envelope.FiscalDocumentNumber,
            FiscalSeries: envelope.FiscalSeries,
            FiscalNumberPrefixText: envelope.FiscalNumberPrefixText,
            FiscalNumberSuffixText: envelope.FiscalNumberSuffixText,
            FiscalNumberAssignedAt: envelope.FiscalNumberAssignedAt,
            FiscalNumberAssignedByRef: envelope.FiscalNumberAssignedByRef,
            ErrorPosture: errorPosture);
    }

    public static PosServerFiscalDocumentReadResult ParseReadResponse(
        int httpStatusCode,
        string responseBody)
    {
        PosServerReadResponseEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PosServerReadResponseEnvelope>(responseBody, JsonOptions);
        }
        catch (JsonException)
        {
            return new PosServerFiscalDocumentReadResult(
                Outcome: PosServerFiscalDocumentOutcome.InvalidResponse,
                Succeeded: false,
                HttpStatusCode: httpStatusCode,
                Code: "invalid_json_response",
                Message: "POS Server read response body was not valid JSON.",
                FiscalDocumentId: null,
                FiscalIssuanceEvidenceStatus: null,
                FiscalNumberAssignmentState: null,
                FiscalDocumentStatusCodeId: null);
        }

        if (envelope is null)
        {
            return new PosServerFiscalDocumentReadResult(
                Outcome: PosServerFiscalDocumentOutcome.InvalidResponse,
                Succeeded: false,
                HttpStatusCode: httpStatusCode,
                Code: "empty_response",
                Message: "POS Server read response body was empty.",
                FiscalDocumentId: null,
                FiscalIssuanceEvidenceStatus: null,
                FiscalNumberAssignmentState: null,
                FiscalDocumentStatusCodeId: null);
        }

        var succeeded = httpStatusCode == (int)HttpStatusCode.OK && envelope.Succeeded;
        return new PosServerFiscalDocumentReadResult(
            Outcome: succeeded ? PosServerFiscalDocumentOutcome.Accepted : MapFailureOutcome(httpStatusCode, envelope.Code, null),
            Succeeded: succeeded,
            HttpStatusCode: httpStatusCode,
            Code: envelope.Code,
            Message: envelope.Message,
            FiscalDocumentId: envelope.Document?.FiscalDocumentId,
            FiscalIssuanceEvidenceStatus: ParseEvidenceStatus(envelope.FiscalIssuanceEvidenceStatus),
            FiscalNumberAssignmentState: ParseAssignmentState(envelope.FiscalNumberAssignmentState),
            FiscalDocumentStatusCodeId: envelope.FiscalDocumentStatusCodeId);
    }

    private static bool HasCompleteFiscalNumberingEvidence(
        PosServerCreateResponseEnvelope envelope,
        FiscalIssuanceEvidenceStatus? evidenceStatus,
        FiscalNumberAssignmentState? assignmentState,
        FiscalIssuanceResultClassification? resultClassification) =>
        resultClassification is FiscalIssuanceResultClassification.NewlyCreated
            or FiscalIssuanceResultClassification.IdempotentReplay
        && evidenceStatus == FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned
        && assignmentState == FiscalNumberAssignmentState.Assigned
        && envelope.FiscalDocumentId is not null
        && envelope.FiscalIdentityId is not null
        && envelope.FiscalSequencePolicyId is not null
        && envelope.FiscalSequenceValue is > 0
        && !string.IsNullOrWhiteSpace(envelope.FiscalDocumentNumber)
        && envelope.FiscalNumberAssignedAt is not null;

    private static PosServerFiscalDocumentOutcome MapFailureOutcome(
        int httpStatusCode,
        string code,
        FiscalIssuanceErrorPosture? errorPosture)
    {
        if (httpStatusCode == (int)HttpStatusCode.Conflict || code == "fiscal_document_idempotency_conflict")
        {
            return PosServerFiscalDocumentOutcome.Conflict;
        }

        if (httpStatusCode == (int)HttpStatusCode.BadRequest)
        {
            return errorPosture == FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection
                ? PosServerFiscalDocumentOutcome.FailedConfiguration
                : PosServerFiscalDocumentOutcome.FailedRequest;
        }

        if (httpStatusCode == (int)HttpStatusCode.ServiceUnavailable)
        {
            return PosServerFiscalDocumentOutcome.FailedService;
        }

        return PosServerFiscalDocumentOutcome.InvalidResponse;
    }

    private static PosServerFiscalDocumentCreateResult InvalidCreateResponse(
        int httpStatusCode,
        string code,
        string message) =>
        new(
            Outcome: PosServerFiscalDocumentOutcome.InvalidResponse,
            Succeeded: false,
            HttpStatusCode: httpStatusCode,
            Code: code,
            Message: message,
            FiscalDocumentId: null,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: null,
            FiscalIdentityId: null,
            FiscalDocumentStatusCodeId: null,
            FiscalSequencePolicyId: null,
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            ErrorPosture: null);

    private static FiscalIssuanceResultClassification? ParseResultClassification(string? value) =>
        value switch
        {
            "newly_created" => FiscalIssuanceResultClassification.NewlyCreated,
            "idempotent_replay" => FiscalIssuanceResultClassification.IdempotentReplay,
            _ => null
        };

    private static FiscalIssuanceEvidenceStatus? ParseEvidenceStatus(string? value) =>
        value == "fiscal_document_number_assigned"
            ? FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned
            : null;

    private static FiscalNumberAssignmentState? ParseAssignmentState(string? value) =>
        value switch
        {
            "assigned" => FiscalNumberAssignmentState.Assigned,
            "not_assigned" => FiscalNumberAssignmentState.NotAssigned,
            _ => null
        };

    private static FiscalIssuanceErrorPosture? ParseErrorPosture(string? value) =>
        value switch
        {
            "do_not_retry_without_request_change" => FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange,
            "retry_after_configuration_correction" => FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection,
            "retry_after_service_recovery" => FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            _ => null
        };

    private sealed record PosServerCreateResponseEnvelope(
        bool Succeeded,
        string Code,
        string Message,
        Guid? FiscalDocumentId,
        string? ResultClassification,
        string? FiscalIssuanceEvidenceStatus,
        string? FiscalNumberAssignmentState,
        Guid? FiscalIdentityId,
        Guid? FiscalDocumentStatusCodeId,
        Guid? FiscalSequencePolicyId,
        long? FiscalSequenceValue,
        string? FiscalDocumentNumber,
        string? FiscalSeries,
        string? FiscalNumberPrefixText,
        string? FiscalNumberSuffixText,
        DateTimeOffset? FiscalNumberAssignedAt,
        string? FiscalNumberAssignedByRef,
        string? ErrorPosture);

    private sealed record PosServerReadResponseEnvelope(
        bool Succeeded,
        string Code,
        string Message,
        PosServerReadDocumentEnvelope? Document,
        string? FiscalIssuanceEvidenceStatus,
        string? FiscalNumberAssignmentState,
        Guid? FiscalDocumentStatusCodeId);

    private sealed record PosServerReadDocumentEnvelope(Guid? FiscalDocumentId);
}
