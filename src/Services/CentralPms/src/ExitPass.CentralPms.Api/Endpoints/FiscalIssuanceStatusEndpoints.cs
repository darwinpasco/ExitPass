using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Contracts.Common;

namespace ExitPass.CentralPms.Api.Endpoints;

#pragma warning disable CS1591

/// <summary>
/// Read-only fiscal issuance status endpoints.
/// </summary>
public static class FiscalIssuanceStatusEndpoints
{
    private const string StatusReadPolicy = "FiscalIssuanceStatusRead";

    /// <summary>
    /// Maps read-only fiscal issuance status endpoints.
    /// </summary>
    /// <param name="app">Endpoint route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapFiscalIssuanceStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/fiscal-issuance")
            .WithTags("FiscalIssuance");

        group.MapGet("/references/{fiscalIssuanceReferenceId:guid}", GetByReferenceIdAsync)
            .WithName("GetFiscalIssuanceReferenceStatus")
            .WithMetadata(new ReconciliationPolicyMetadata(StatusReadPolicy))
            .Produces<FiscalIssuanceStatusResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetByReferenceIdAsync(
        Guid fiscalIssuanceReferenceId,
        IFiscalIssuanceStatusReadService service,
        CancellationToken cancellationToken)
    {
        var status = await service.GetByReferenceIdAsync(fiscalIssuanceReferenceId, cancellationToken)
            .ConfigureAwait(false);

        if (status is null)
        {
            return Results.NotFound(new ErrorResponse
            {
                ErrorCode = "FISCAL_ISSUANCE_REFERENCE_NOT_FOUND",
                Message = "Fiscal issuance reference was not found.",
                Retryable = false
            });
        }

        return Results.Ok(FiscalIssuanceStatusResponse.FromReadModel(status));
    }
}

public sealed record FiscalIssuanceStatusResponse(
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
    Guid? CorrelationId,
    string? PosServerFiscalDocumentReadStatus,
    string? PosServerFiscalDocumentStatusCodeKey,
    string? PosServerVoidStatus,
    string? PosServerVoidReasonCode,
    DateTimeOffset? PosServerVoidedAt)
{
    public static FiscalIssuanceStatusResponse FromReadModel(FiscalIssuanceStatusReadModel model) =>
        new(
            FiscalIssuanceReferenceId: model.FiscalIssuanceReferenceId,
            FiscalIssuanceState: model.FiscalIssuanceState,
            ResultClassification: model.ResultClassification,
            FiscalIssuanceEvidenceStatus: model.FiscalIssuanceEvidenceStatus,
            FiscalNumberAssignmentState: model.FiscalNumberAssignmentState,
            UpstreamFinalityReference: model.UpstreamFinalityReference,
            PaymentConfirmationId: model.PaymentConfirmationId,
            PaymentAttemptId: model.PaymentAttemptId,
            ParkingSessionId: model.ParkingSessionId,
            SiteId: model.SiteId,
            SitePosServerId: model.SitePosServerId,
            SitePosServerRef: model.SitePosServerRef,
            FiscalDocumentTypeCodeId: model.FiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: model.FiscalDocumentTypeCodeKey,
            PosServerFiscalDocumentId: model.PosServerFiscalDocumentId,
            FiscalDocumentNumber: model.FiscalDocumentNumber,
            FiscalIdentityId: model.FiscalIdentityId,
            FiscalSequencePolicyId: model.FiscalSequencePolicyId,
            FiscalSequenceValue: model.FiscalSequenceValue,
            FiscalSeries: model.FiscalSeries,
            FiscalNumberPrefixText: model.FiscalNumberPrefixText,
            FiscalNumberSuffixText: model.FiscalNumberSuffixText,
            FiscalNumberAssignedAt: model.FiscalNumberAssignedAt,
            FiscalNumberAssignedByRef: model.FiscalNumberAssignedByRef,
            SemanticRequestHashValue: model.SemanticRequestHashValue,
            SemanticRequestHashVersion: model.SemanticRequestHashVersion,
            SemanticRequestHashStatus: model.SemanticRequestHashStatus,
            SemanticRequestHashAlgorithm: model.SemanticRequestHashAlgorithm,
            SemanticRequestHashSourceFactCount: model.SemanticRequestHashSourceFactCount,
            LatestErrorCode: model.LatestErrorCode,
            LatestErrorPosture: model.LatestErrorPosture,
            LatestExceptionReason: model.LatestExceptionReason,
            FirstRecordedAt: model.FirstRecordedAt,
            LastUpdatedAt: model.LastUpdatedAt,
            CorrelationId: model.CorrelationId,
            PosServerFiscalDocumentReadStatus: model.PosServerFiscalDocumentReadStatus,
            PosServerFiscalDocumentStatusCodeKey: model.PosServerFiscalDocumentStatusCodeKey,
            PosServerVoidStatus: model.PosServerVoidStatus,
            PosServerVoidReasonCode: model.PosServerVoidReasonCode,
            PosServerVoidedAt: model.PosServerVoidedAt);
}

#pragma warning restore CS1591
