using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalExceptionSemanticHashReadinessPolicyTests
{
    [Fact]
    public void Evaluate_WhenPersistedHashIsCurrentSha256V1_ReturnsReadyCurrent()
    {
        var result = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(CurrentRecord());

        result.Status.Should().Be(FiscalExceptionSemanticHashReadinessStatus.ReadyCurrent);
        result.BlockReasonCode.Should().BeNull();
        result.StoredSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        result.RequiredSourceVersion.Should().Be(FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion);
        result.RecalculationPosture.Should().Be(FiscalExceptionSemanticHashRecalculationPosture.Unknown);
    }

    [Fact]
    public void Evaluate_WhenPersistedHashUsesLegacySourceVersion_ReturnsRecalculationRequired()
    {
        var result = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(CurrentRecord() with
        {
            SemanticRequestHashSourceVersion =
                FiscalExceptionSemanticHashReadinessPolicy.LegacyCentralPmsHashSourceVersion
        });

        result.Status.Should().Be(FiscalExceptionSemanticHashReadinessStatus.LegacyRecalculationRequired);
        result.BlockReasonCode.Should().Be("semantic_hash_legacy_version_requires_recalculation");
        result.RecalculationPosture.Should().Be(FiscalExceptionSemanticHashRecalculationPosture.Unknown);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("version")]
    [InlineData("algorithm")]
    public void Evaluate_WhenRequiredHashMetadataIsMissing_ReturnsMissing(string missingField)
    {
        var record = missingField switch
        {
            "hash" => CurrentRecord() with { SemanticRequestHashValue = null },
            "version" => CurrentRecord() with { SemanticRequestHashSourceVersion = null },
            "algorithm" => CurrentRecord() with { SemanticRequestHashAlgorithm = null },
            _ => throw new ArgumentOutOfRangeException(nameof(missingField), missingField, "Unknown field.")
        };

        var result = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(record);

        result.Status.Should().Be(FiscalExceptionSemanticHashReadinessStatus.Missing);
        result.BlockReasonCode.Should().StartWith("semantic_hash_");
    }

    [Fact]
    public void Evaluate_WhenPersistedHashStatusIsIncomplete_ReturnsIncomplete()
    {
        var result = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(CurrentRecord() with
        {
            SemanticRequestHashStatus = FiscalSemanticRequestHashSourceStatus.Incomplete,
            SemanticRequestHashValue = null,
            SemanticRequestHashSourceFactCount = 0,
            SemanticRequestHashSafeSummary = "semantic_request_hash_source_incomplete:document_line_required"
        });

        result.Status.Should().Be(FiscalExceptionSemanticHashReadinessStatus.Incomplete);
        result.BlockReasonCode.Should().Be("semantic_hash_incomplete");
    }

    [Fact]
    public void Evaluate_WhenSourceVersionIsUnknown_ReturnsIncompatible()
    {
        var result = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(CurrentRecord() with
        {
            SemanticRequestHashSourceVersion = "sha256:v2"
        });

        result.Status.Should().Be(FiscalExceptionSemanticHashReadinessStatus.Incompatible);
        result.BlockReasonCode.Should().Be("semantic_hash_source_version_incompatible");
    }

    [Fact]
    public void Evaluate_WhenSourceStatusIsUnavailable_ReturnsUnavailable()
    {
        var result = FiscalExceptionSemanticHashReadinessPolicy.Evaluate(CurrentRecord() with
        {
            SemanticRequestHashStatus = FiscalSemanticRequestHashSourceStatus.Unavailable,
            SemanticRequestHashValue = null,
            SemanticRequestHashAlgorithm = null,
            SemanticRequestHashSourceVersion = null
        });

        result.Status.Should().Be(FiscalExceptionSemanticHashReadinessStatus.Unavailable);
        result.BlockReasonCode.Should().Be("semantic_hash_unavailable");
    }

    private static FiscalIssuanceReferenceRecord CurrentRecord()
    {
        var now = DateTimeOffset.Parse("2026-07-06T09:00:00+08:00");
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
            FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceUnknown,
            LatestExceptionReason: FiscalIssuanceExceptionReason.PostTimeout,
            LatestErrorCode: "post_timeout",
            LatestErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: now,
            LastUpdatedAt: now,
            RecordedByServiceIdentityId: Guid.NewGuid(),
            SemanticRequestHashStatus: FiscalSemanticRequestHashSourceStatus.Available,
            SemanticRequestHashValue: new string('a', 64),
            SemanticRequestHashAlgorithm: FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
            SemanticRequestHashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
            SemanticRequestHashSourceFactCount: 42,
            SemanticRequestHashSafeSummary: "semantic_request_hash_source_available:facts=42");
    }
}
