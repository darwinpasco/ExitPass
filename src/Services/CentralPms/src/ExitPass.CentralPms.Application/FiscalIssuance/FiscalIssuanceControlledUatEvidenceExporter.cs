using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalIssuanceControlledUatEvidenceExporter
{
    FiscalIssuanceControlledUatEvidenceExportResult BuildEvidence(
        FiscalIssuanceControlledUatEvidenceExportRequest request);

    string SerializeEvidence(FiscalIssuanceControlledUatEvidence evidence);
}

public sealed class FiscalIssuanceControlledUatEvidenceExporter : IFiscalIssuanceControlledUatEvidenceExporter
{
    private const string SchemaVersion = "central-pms-pos-server-controlled-uat-evidence.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly string[] SensitiveTerms =
    [
        "pan",
        "cvv",
        "token",
        "secret",
        "credential",
        "password",
        "provider callback",
        "provider_callback",
        "raw provider callback",
        "raw_provider_callback",
        "raw payload",
        "raw_payload",
        "callback_payload",
        "entitlement image",
        "entitlement_image",
        "base64 image",
        "base64_image",
        "unmanaged pii",
        "unmanaged_pii",
        "unmanaged customer pii",
        "customer_pii"
    ];

    static FiscalIssuanceControlledUatEvidenceExporter()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public FiscalIssuanceControlledUatEvidenceExportResult BuildEvidence(
        FiscalIssuanceControlledUatEvidenceExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.HarnessRequest);
        ArgumentNullException.ThrowIfNull(request.HarnessResult);
        ArgumentNullException.ThrowIfNull(request.PosServerOptions);

        var sensitiveErrors = ValidateNoSensitiveMarkers(request);
        var sensitiveDataExcluded = sensitiveErrors.Count == 0;

        if (!sensitiveDataExcluded)
        {
            return new FiscalIssuanceControlledUatEvidenceExportResult(
                Succeeded: false,
                Evidence: null,
                Json: null,
                SensitiveDataExcluded: false,
                RedactionRequired: true,
                RedactionStatus: FiscalIssuanceControlledUatEvidenceRedactionStatuses.RejectedSensitiveMetadata,
                Errors: sensitiveErrors);
        }

        var readiness = request.PosServerOptions.EvaluateReadiness();
        var gatingOptions = request.GatingOptions ?? new FiscalIssuanceExitAuthorizationGatingOptions();
        var evidence = new FiscalIssuanceControlledUatEvidence(
            SchemaVersion: SchemaVersion,
            Run: BuildRunMetadata(request),
            Approval: BuildApprovalMetadata(request),
            Environment: BuildEnvironmentMetadata(request),
            SiteContext: BuildSiteContext(request),
            ConfigurationReadiness: BuildConfigurationReadiness(request, readiness, gatingOptions),
            FiscalRequestFacts: BuildFiscalRequestFacts(request),
            DiagnosticInvocation: BuildDiagnosticInvocation(request),
            PosServerResponse: BuildPosServerResponse(request),
            CentralPmsFiscalReference: BuildCentralPmsFiscalReference(request),
            OutcomeSummary: BuildOutcomeSummary(request),
            ImpactConfirmation: BuildImpactConfirmation(request),
            EvidencePosture: new FiscalIssuanceControlledUatEvidencePosture(
                SensitiveDataExcluded: true,
                RedactionRequired: false,
                RedactionStatus: FiscalIssuanceControlledUatEvidenceRedactionStatuses.Safe),
            FinalOutcome: ResolveFinalOutcome(request.HarnessResult),
            ReviewerRef: Normalize(request.ReviewerRef),
            Notes: Normalize(request.Notes),
            SafeMetadata: NormalizeDictionary(request.SafeMetadata),
            Errors: request.HarnessResult.Errors);

        return new FiscalIssuanceControlledUatEvidenceExportResult(
            Succeeded: true,
            Evidence: evidence,
            Json: SerializeEvidence(evidence),
            SensitiveDataExcluded: true,
            RedactionRequired: false,
            RedactionStatus: FiscalIssuanceControlledUatEvidenceRedactionStatuses.Safe,
            Errors: Array.Empty<string>());
    }

    public string SerializeEvidence(FiscalIssuanceControlledUatEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return JsonSerializer.Serialize(evidence, JsonOptions);
    }

    private static FiscalIssuanceControlledUatRunMetadata BuildRunMetadata(
        FiscalIssuanceControlledUatEvidenceExportRequest request) =>
        new(
            RunId: request.HarnessRequest.RunId,
            RunTimestamp: request.RunTimestamp,
            ExpectedRunType: request.HarnessRequest.ExpectedRunType.ToString(),
            EnvironmentName: request.HarnessRequest.EnvironmentName,
            CorrelationId: request.HarnessRequest.CorrelationId,
            EvidenceReference: request.HarnessRequest.EvidenceReference,
            EvidenceLocation: request.HarnessRequest.EvidenceLocation,
            EvidenceOwner: request.HarnessRequest.EvidenceOwner);

    private static FiscalIssuanceControlledUatApprovalMetadata BuildApprovalMetadata(
        FiscalIssuanceControlledUatEvidenceExportRequest request) =>
        new(
            ApprovedByRef: request.HarnessRequest.ApprovedByRef,
            ReviewerRef: Normalize(request.ReviewerRef));

    private static FiscalIssuanceControlledUatEnvironmentMetadata BuildEnvironmentMetadata(
        FiscalIssuanceControlledUatEvidenceExportRequest request) =>
        new(
            EnvironmentName: request.HarnessRequest.EnvironmentName,
            EvidenceReference: request.HarnessRequest.EvidenceReference,
            EvidenceLocation: request.HarnessRequest.EvidenceLocation);

    private static FiscalIssuanceControlledUatSiteContext BuildSiteContext(
        FiscalIssuanceControlledUatEvidenceExportRequest request) =>
        new(
            SitePosServerId: request.HarnessRequest.FiscalContext.SitePosServerId,
            SitePosServerRef: request.HarnessRequest.FiscalContext.SitePosServerRef,
            FiscalDocumentTypeCodeId: request.HarnessRequest.FiscalContext.FiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: request.HarnessRequest.FiscalContext.FiscalDocumentTypeCodeKey,
            FiscalDocumentStatusCodeId: request.HarnessRequest.FiscalContext.FiscalDocumentStatusCodeId);

    private static FiscalIssuanceControlledUatConfigurationReadiness BuildConfigurationReadiness(
        FiscalIssuanceControlledUatEvidenceExportRequest request,
        FiscalIssuancePosServerIntegrationReadiness readiness,
        FiscalIssuanceExitAuthorizationGatingOptions gatingOptions) =>
        new(
            ReadinessStatus: readiness.Status,
            ReadinessReason: readiness.Reason,
            LiveCallEnabled: request.PosServerOptions.EnablePosServerFiscalIssuanceLiveCall,
            DiagnosticPathEnabled: request.PosServerOptions.EnableControlledUatDiagnosticPath,
            BaseUrlConfigured: readiness.BaseUrlConfigured,
            TimeoutConfigured: readiness.TimeoutConfigured,
            PaymentFlowGuardEnabled: request.PosServerOptions.EnableLiveFiscalIssuanceFromPaymentFlow,
            ExitFlowGuardEnabled: request.PosServerOptions.EnableLiveFiscalIssuanceFromExitFlow,
            FiscalGatingEnforcementEnabled: gatingOptions.EnableFiscalBeforeExitAuthorizationEnforcement,
            EnforcementWiredForBlocking: false);

    private static FiscalIssuanceControlledUatFiscalRequestFacts BuildFiscalRequestFacts(
        FiscalIssuanceControlledUatEvidenceExportRequest request) =>
        new(
            ParkingSessionRef: request.HarnessRequest.FiscalContext.CentralPmsParkingSessionRef,
            PaymentAttemptRef: request.HarnessRequest.FiscalContext.CentralPmsPaymentAttemptRef,
            PaymentConfirmationRef: request.HarnessRequest.FiscalContext.CentralPmsPaymentConfirmationRef,
            PayableBasisRef: request.HarnessRequest.FiscalContext.PayableBasis.PayableBasisRef,
            UpstreamFinalityRef: request.HarnessRequest.FiscalContext.PayableBasis.UpstreamFinalityRef,
            BusinessDayDate: request.HarnessRequest.FiscalContext.BusinessDayDate,
            CurrencyCode: request.HarnessRequest.FiscalContext.PayableBasis.CurrencyCode,
            AmountMinorUnits: request.HarnessRequest.FiscalContext.PayableBasis.PayableAmountMinorUnits,
            LineCount: request.HarnessRequest.FiscalContext.DocumentLines.Count,
            TenderCount: request.HarnessRequest.FiscalContext.Tenders.Count,
            TaxDetailCount: request.HarnessRequest.FiscalContext.TaxDetails.Count,
            TotalCount: request.HarnessRequest.FiscalContext.Totals.Count,
            DiscountReferenceCount: request.HarnessRequest.FiscalContext.PayableBasis.DiscountReferences.Count);

    private static FiscalIssuanceControlledUatDiagnosticInvocation BuildDiagnosticInvocation(
        FiscalIssuanceControlledUatEvidenceExportRequest request) =>
        new(
            HarnessStatus: request.HarnessResult.Status,
            ValidationPassed: request.HarnessResult.ValidationPassed,
            DiagnosticInvoked: request.HarnessResult.DiagnosticInvoked,
            PosServerCallAttempted: request.HarnessResult.PosServerCallAttempted,
            DiagnosticStatus: request.HarnessResult.DiagnosticStatus);

    private static FiscalIssuanceControlledUatPosServerResponseFacts BuildPosServerResponse(
        FiscalIssuanceControlledUatEvidenceExportRequest request) =>
        new(
            ResultClassification: request.HarnessResult.ResultClassification,
            FiscalDocumentId: request.HarnessResult.FiscalDocumentId,
            FiscalDocumentNumber: request.HarnessResult.FiscalDocumentNumber,
            FiscalIssuanceEvidenceStatus: request.HarnessResult.FiscalIssuanceEvidenceStatus,
            FiscalNumberAssignmentState: request.HarnessResult.FiscalNumberAssignmentState,
            ErrorCode: request.HarnessResult.ErrorCode,
            ErrorPosture: request.HarnessResult.ErrorPosture);

    private static FiscalIssuanceControlledUatCentralPmsFiscalReferenceFacts BuildCentralPmsFiscalReference(
        FiscalIssuanceControlledUatEvidenceExportRequest request) =>
        new(
            FiscalIssuanceReferenceId: request.HarnessRequest.FiscalIssuanceReferenceId,
            CentralPmsFiscalState: request.HarnessResult.CentralPmsFiscalState,
            FiscalDocumentIdRecorded: request.HarnessResult.FiscalDocumentId,
            FiscalDocumentNumberRecorded: request.HarnessResult.FiscalDocumentNumber,
            FiscalIssuanceEvidenceStatusRecorded: request.HarnessResult.FiscalIssuanceEvidenceStatus,
            FiscalNumberAssignmentStateRecorded: request.HarnessResult.FiscalNumberAssignmentState);

    private static FiscalIssuanceControlledUatOutcomeSummary BuildOutcomeSummary(
        FiscalIssuanceControlledUatEvidenceExportRequest request)
    {
        var status = request.HarnessResult.Status;
        var isReplay = status == FiscalIssuanceControlledUatHarnessStatuses.ReplayRecorded;
        var isConflict = status == FiscalIssuanceControlledUatHarnessStatuses.ConflictFailureMapped;
        var isFailure = status is FiscalIssuanceControlledUatHarnessStatuses.RequestFailureMapped
            or FiscalIssuanceControlledUatHarnessStatuses.ConfigurationFailureMapped
            or FiscalIssuanceControlledUatHarnessStatuses.ServiceFailureMapped;
        var isUnknown = status == FiscalIssuanceControlledUatHarnessStatuses.UnknownFailClosed;

        return new FiscalIssuanceControlledUatOutcomeSummary(
            FinalStatus: status,
            NewlyCreatedRecorded: status == FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded,
            ReplayRecorded: isReplay,
            ConflictMapped: isConflict,
            FailureMapped: isFailure,
            UnknownFailClosed: isUnknown,
            DuplicateReferenceExpected: false,
            AutomaticRetryPerformed: false);
    }

    private static FiscalIssuanceControlledUatImpactConfirmation BuildImpactConfirmation(
        FiscalIssuanceControlledUatEvidenceExportRequest request) =>
        new(
            PaymentFinalityChanged: request.HarnessResult.PaymentFinalityChanged,
            ExitAuthorizationIssued: request.HarnessResult.ExitAuthorizationIssued,
            GateBehaviorTriggered: request.HarnessResult.GateBehaviorTriggered,
            PaymentFinalityUnaffected: !request.HarnessResult.PaymentFinalityChanged,
            ExitAuthorizationUnaffected: !request.HarnessResult.ExitAuthorizationIssued,
            GateBehaviorUnaffected: !request.HarnessResult.GateBehaviorTriggered);

    private static string ResolveFinalOutcome(FiscalIssuanceControlledUatHarnessResult result)
    {
        if (!result.ValidationPassed)
        {
            return FiscalIssuanceControlledUatFinalOutcomes.Aborted;
        }

        return result.Status switch
        {
            FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded
                or FiscalIssuanceControlledUatHarnessStatuses.ReplayRecorded => FiscalIssuanceControlledUatFinalOutcomes.Passed,
            FiscalIssuanceControlledUatHarnessStatuses.ConflictFailureMapped
                or FiscalIssuanceControlledUatHarnessStatuses.RequestFailureMapped
                or FiscalIssuanceControlledUatHarnessStatuses.ConfigurationFailureMapped
                or FiscalIssuanceControlledUatHarnessStatuses.ServiceFailureMapped => FiscalIssuanceControlledUatFinalOutcomes.PassedWithNotes,
            FiscalIssuanceControlledUatHarnessStatuses.UnknownFailClosed => FiscalIssuanceControlledUatFinalOutcomes.Inconclusive,
            _ => FiscalIssuanceControlledUatFinalOutcomes.Inconclusive
        };
    }

    private static IReadOnlyList<string> ValidateNoSensitiveMarkers(
        FiscalIssuanceControlledUatEvidenceExportRequest request)
    {
        var values = EnumerateSafeText(request).Where(value => !string.IsNullOrWhiteSpace(value));
        return values.Any(ContainsSensitiveTerm)
            ? ["sensitive_evidence_metadata_rejected"]
            : [];
    }

    private static IEnumerable<string?> EnumerateSafeText(FiscalIssuanceControlledUatEvidenceExportRequest request)
    {
        yield return request.ReviewerRef;
        yield return request.Notes;

        foreach (var value in EnumerateDictionaryStrings(request.SafeMetadata))
        {
            yield return value;
        }

        yield return request.HarnessRequest.RunId;
        yield return request.HarnessRequest.EnvironmentName;
        yield return request.HarnessRequest.EvidenceReference;
        yield return request.HarnessRequest.EvidenceLocation;
        yield return request.HarnessRequest.EvidenceOwner;
        yield return request.HarnessRequest.ApprovedByRef;
        yield return request.HarnessRequest.CorrelationId;
        yield return request.HarnessRequest.FiscalContext.SitePosServerRef;
        yield return request.HarnessRequest.FiscalContext.FiscalDocumentTypeCodeKey;
        yield return request.HarnessRequest.FiscalContext.CentralPmsParkingSessionRef;
        yield return request.HarnessRequest.FiscalContext.CentralPmsPaymentAttemptRef;
        yield return request.HarnessRequest.FiscalContext.CentralPmsPaymentConfirmationRef;
        yield return request.HarnessRequest.FiscalContext.PayableBasis.PayableBasisRef;
        yield return request.HarnessRequest.FiscalContext.PayableBasis.UpstreamFinalityRef;
        yield return request.HarnessRequest.FiscalContext.PayableBasis.CurrencyCode;
        yield return request.HarnessResult.ErrorCode;
    }

    private static IEnumerable<string?> EnumerateDictionaryStrings(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null)
        {
            yield break;
        }

        foreach (var pair in values)
        {
            yield return pair.Key;
            yield return pair.Value;
        }
    }

    private static IReadOnlyDictionary<string, string> NormalizeDictionary(IReadOnlyDictionary<string, string>? values) =>
        values is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : values
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.Ordinal);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool ContainsSensitiveTerm(string? value) =>
        value is not null &&
        SensitiveTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}

public sealed record FiscalIssuanceControlledUatEvidenceExportRequest(
    FiscalIssuanceControlledUatHarnessRequest HarnessRequest,
    FiscalIssuanceControlledUatHarnessResult HarnessResult,
    FiscalIssuancePosServerIntegrationOptions PosServerOptions,
    FiscalIssuanceExitAuthorizationGatingOptions? GatingOptions,
    DateTimeOffset? RunTimestamp,
    string? ReviewerRef,
    string? Notes,
    IReadOnlyDictionary<string, string>? SafeMetadata);

public sealed record FiscalIssuanceControlledUatEvidenceExportResult(
    bool Succeeded,
    FiscalIssuanceControlledUatEvidence? Evidence,
    string? Json,
    bool SensitiveDataExcluded,
    bool RedactionRequired,
    string RedactionStatus,
    IReadOnlyList<string> Errors);

public sealed record FiscalIssuanceControlledUatEvidence(
    string SchemaVersion,
    FiscalIssuanceControlledUatRunMetadata Run,
    FiscalIssuanceControlledUatApprovalMetadata Approval,
    FiscalIssuanceControlledUatEnvironmentMetadata Environment,
    FiscalIssuanceControlledUatSiteContext SiteContext,
    FiscalIssuanceControlledUatConfigurationReadiness ConfigurationReadiness,
    FiscalIssuanceControlledUatFiscalRequestFacts FiscalRequestFacts,
    FiscalIssuanceControlledUatDiagnosticInvocation DiagnosticInvocation,
    FiscalIssuanceControlledUatPosServerResponseFacts PosServerResponse,
    FiscalIssuanceControlledUatCentralPmsFiscalReferenceFacts CentralPmsFiscalReference,
    FiscalIssuanceControlledUatOutcomeSummary OutcomeSummary,
    FiscalIssuanceControlledUatImpactConfirmation ImpactConfirmation,
    FiscalIssuanceControlledUatEvidencePosture EvidencePosture,
    string FinalOutcome,
    string? ReviewerRef,
    string? Notes,
    IReadOnlyDictionary<string, string> SafeMetadata,
    IReadOnlyList<string> Errors);

public sealed record FiscalIssuanceControlledUatRunMetadata(
    string RunId,
    DateTimeOffset? RunTimestamp,
    string ExpectedRunType,
    string EnvironmentName,
    string? CorrelationId,
    string? EvidenceReference,
    string? EvidenceLocation,
    string EvidenceOwner);

public sealed record FiscalIssuanceControlledUatApprovalMetadata(
    string ApprovedByRef,
    string? ReviewerRef);

public sealed record FiscalIssuanceControlledUatEnvironmentMetadata(
    string EnvironmentName,
    string? EvidenceReference,
    string? EvidenceLocation);

public sealed record FiscalIssuanceControlledUatSiteContext(
    Guid? SitePosServerId,
    string? SitePosServerRef,
    Guid? FiscalDocumentTypeCodeId,
    string? FiscalDocumentTypeCodeKey,
    Guid? FiscalDocumentStatusCodeId);

public sealed record FiscalIssuanceControlledUatConfigurationReadiness(
    string ReadinessStatus,
    string ReadinessReason,
    bool LiveCallEnabled,
    bool DiagnosticPathEnabled,
    bool BaseUrlConfigured,
    bool TimeoutConfigured,
    bool PaymentFlowGuardEnabled,
    bool ExitFlowGuardEnabled,
    bool FiscalGatingEnforcementEnabled,
    bool EnforcementWiredForBlocking);

public sealed record FiscalIssuanceControlledUatFiscalRequestFacts(
    string ParkingSessionRef,
    string PaymentAttemptRef,
    string PaymentConfirmationRef,
    string PayableBasisRef,
    string UpstreamFinalityRef,
    DateOnly? BusinessDayDate,
    string CurrencyCode,
    long AmountMinorUnits,
    int LineCount,
    int TenderCount,
    int TaxDetailCount,
    int TotalCount,
    int DiscountReferenceCount);

public sealed record FiscalIssuanceControlledUatDiagnosticInvocation(
    string HarnessStatus,
    bool ValidationPassed,
    bool DiagnosticInvoked,
    bool PosServerCallAttempted,
    string? DiagnosticStatus);

public sealed record FiscalIssuanceControlledUatPosServerResponseFacts(
    FiscalIssuanceResultClassification? ResultClassification,
    Guid? FiscalDocumentId,
    string? FiscalDocumentNumber,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState? FiscalNumberAssignmentState,
    string? ErrorCode,
    FiscalIssuanceErrorPosture? ErrorPosture);

public sealed record FiscalIssuanceControlledUatCentralPmsFiscalReferenceFacts(
    Guid FiscalIssuanceReferenceId,
    FiscalIssuanceIntegrationState? CentralPmsFiscalState,
    Guid? FiscalDocumentIdRecorded,
    string? FiscalDocumentNumberRecorded,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatusRecorded,
    FiscalNumberAssignmentState? FiscalNumberAssignmentStateRecorded);

public sealed record FiscalIssuanceControlledUatOutcomeSummary(
    string FinalStatus,
    bool NewlyCreatedRecorded,
    bool ReplayRecorded,
    bool ConflictMapped,
    bool FailureMapped,
    bool UnknownFailClosed,
    bool DuplicateReferenceExpected,
    bool AutomaticRetryPerformed);

public sealed record FiscalIssuanceControlledUatImpactConfirmation(
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool PaymentFinalityUnaffected,
    bool ExitAuthorizationUnaffected,
    bool GateBehaviorUnaffected);

public sealed record FiscalIssuanceControlledUatEvidencePosture(
    bool SensitiveDataExcluded,
    bool RedactionRequired,
    string RedactionStatus);

public static class FiscalIssuanceControlledUatEvidenceRedactionStatuses
{
    public const string Safe = "safe";
    public const string RejectedSensitiveMetadata = "rejected_sensitive_metadata";
}

public static class FiscalIssuanceControlledUatFinalOutcomes
{
    public const string Passed = "passed";
    public const string PassedWithNotes = "passed_with_notes";
    public const string Failed = "failed";
    public const string Aborted = "aborted";
    public const string Inconclusive = "inconclusive";
}
