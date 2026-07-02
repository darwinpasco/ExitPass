using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceReferenceModelsTests
{
    [Fact]
    public void Validate_AllowsPendingFiscalIssuanceWithoutPosServerEvidence()
    {
        var request = CreateRequest(
            FiscalIssuanceIntegrationState.PendingFiscalIssuance,
            FiscalNumberAssignmentState.NotAssigned);

        request.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed)]
    public void Validate_RequiresCompleteFiscalEvidenceForRecordedOrReplayedState(
        FiscalIssuanceIntegrationState state)
    {
        var request = CreateRequest(state, FiscalNumberAssignmentState.NotAssigned);

        request.Validate().Should().Contain(new[]
        {
            "pos_server_fiscal_document_id_required",
            "fiscal_identity_id_required",
            "fiscal_sequence_policy_id_required",
            "fiscal_sequence_value_required",
            "fiscal_document_number_required",
            "fiscal_number_assigned_at_required",
            "fiscal_issuance_evidence_status_required",
            "fiscal_number_assignment_state_assigned_required"
        });
    }

    [Fact]
    public void Validate_AllowsRecordedStateWhenNumberedFiscalEvidenceIsComplete()
    {
        var request = CreateCompleteEvidenceRequest(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);

        request.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Validate_AllowsReplayedStateWhenOriginalNumberedFiscalEvidenceIsComplete()
    {
        var request = CreateCompleteEvidenceRequest(
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed,
            FiscalIssuanceResultClassification.IdempotentReplay);

        request.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceConflict)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedService)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceManualReview)]
    public void Validate_RequiresExceptionReasonForExceptionState(FiscalIssuanceIntegrationState state)
    {
        var request = CreateRequest(state, FiscalNumberAssignmentState.NotAssigned);

        request.Validate().Should().Contain("latest_exception_reason_required");
    }

    [Fact]
    public void Validate_AllowsConflictStateWhenExceptionReasonIsPresent()
    {
        var request = CreateRequest(
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict,
            FiscalNumberAssignmentState.NotAssigned,
            latestExceptionReason: FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict);

        request.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, true)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed, true)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceReconciled, true)]
    [InlineData(FiscalIssuanceIntegrationState.PendingFiscalIssuance, false)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceFailedService, false)]
    [InlineData(FiscalIssuanceIntegrationState.FiscalIssuanceUnknown, false)]
    public void RequiresCompleteFiscalEvidence_ReturnsExpectedValue(
        FiscalIssuanceIntegrationState state,
        bool expected)
    {
        CreateFiscalIssuanceReferenceRequest.RequiresCompleteFiscalEvidence(state).Should().Be(expected);
    }

    [Fact]
    public void FiscalIssuanceReferenceRecord_DoesNotExposeRawSensitivePayloadFields()
    {
        var prohibitedFragments = new[]
        {
            "RawPayload",
            "CallbackPayload",
            "Pan",
            "Cvv",
            "Secret",
            "Token",
            "Credential"
        };

        var propertyNames = typeof(FiscalIssuanceReferenceRecord)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        propertyNames.Should().NotContain(propertyName =>
            prohibitedFragments.Any(fragment =>
                propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    private static CreateFiscalIssuanceReferenceRequest CreateCompleteEvidenceRequest(
        FiscalIssuanceIntegrationState state,
        FiscalIssuanceResultClassification resultClassification = FiscalIssuanceResultClassification.NewlyCreated) =>
        CreateRequest(
            state,
            FiscalNumberAssignmentState.Assigned,
            posServerFiscalDocumentId: Guid.NewGuid(),
            fiscalIdentityId: Guid.NewGuid(),
            fiscalSequencePolicyId: Guid.NewGuid(),
            fiscalSequenceValue: 101,
            fiscalDocumentNumber: "SI-000101",
            fiscalNumberAssignedAt: DateTimeOffset.Parse("2026-07-02T10:30:00+08:00"),
            resultClassification: resultClassification,
            evidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned);

    private static CreateFiscalIssuanceReferenceRequest CreateRequest(
        FiscalIssuanceIntegrationState state,
        FiscalNumberAssignmentState assignmentState,
        Guid? posServerFiscalDocumentId = null,
        Guid? fiscalIdentityId = null,
        Guid? fiscalSequencePolicyId = null,
        long? fiscalSequenceValue = null,
        string? fiscalDocumentNumber = null,
        DateTimeOffset? fiscalNumberAssignedAt = null,
        FiscalIssuanceResultClassification? resultClassification = null,
        FiscalIssuanceEvidenceStatus? evidenceStatus = null,
        FiscalIssuanceExceptionReason? latestExceptionReason = null) =>
        new(
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: Guid.NewGuid(),
            SitePosServerRef: "site-pos-server-main",
            FiscalDocumentTypeCodeId: Guid.NewGuid(),
            FiscalDocumentTypeCodeKey: "SALES_INVOICE",
            PayableBasisRef: "tariff-snapshot-ref",
            UpstreamFinalityReference: $"pay-final-{Guid.NewGuid():N}",
            PosServerFiscalDocumentId: posServerFiscalDocumentId,
            FiscalIdentityId: fiscalIdentityId,
            FiscalSequencePolicyId: fiscalSequencePolicyId,
            FiscalSequenceValue: fiscalSequenceValue,
            FiscalDocumentNumber: fiscalDocumentNumber,
            FiscalSeries: "SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: fiscalNumberAssignedAt,
            FiscalNumberAssignedByRef: "pos-server",
            FiscalDocumentStatusCodeId: Guid.NewGuid(),
            ResultClassification: resultClassification,
            FiscalIssuanceEvidenceStatus: evidenceStatus,
            FiscalNumberAssignmentState: assignmentState,
            FiscalIssuanceState: state,
            LatestExceptionReason: latestExceptionReason,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            RecordedByServiceIdentityId: Guid.NewGuid());
}
