using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsoleProductionPolicyImportServiceTests
{
    private static readonly string[] CandidateHeaders =
    [
        "policy_code",
        "policy_name",
        "entitlement_type",
        "lgu_code",
        "jurisdiction_name",
        "site_group_code",
        "site_code",
        "policy_level",
        "policy_type",
        "policy_resolution_basis",
        "benefit_type",
        "discount_base_scope",
        "free_duration_minutes",
        "initial_rate_exempt",
        "full_fee_exempt",
        "overnight_excluded",
        "valet_excluded",
        "standalone_parking_excluded",
        "driver_or_passenger_required",
        "beneficiary_residency_scope",
        "requires_evidence",
        "required_evidence_type",
        "requires_operator_validation",
        "legal_basis_reference",
        "ordinance_reference",
        "national_law_reference",
        "source_reference",
        "verification_status",
        "effective_from",
        "effective_to",
        "reviewed_by",
        "reviewed_at",
        "approved_by",
        "approved_at",
        "notes",
        "review_status",
        "review_owner",
        "legal_review_decision",
        "product_review_decision",
        "ops_review_decision",
        "engineering_review_decision",
        "qa_review_decision",
        "approval_notes"
    ];

    [Fact]
    public async Task DryRunAsync_WhenHeaderOnlyWorksheet_ReturnsNoRowsAndNoFailures()
    {
        var result = await Sut().DryRunAsync(new ProductionPolicyImportDryRunRequest(HeaderOnlyCsv()), CancellationToken.None);

        result.IsDryRun.Should().BeTrue();
        result.PoliciesImported.Should().BeFalse();
        result.TotalRows.Should().Be(0);
        result.FailCount.Should().Be(0);
        result.PassCount.Should().Be(2);
        result.Findings.Should().Contain(finding => finding.Message.Contains("header-only", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunAsync_WhenCandidateIsApprovedAndClean_ReturnsImportableAfterApproval()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row())),
            CancellationToken.None);

        result.TotalRows.Should().Be(1);
        result.ImportableRows.Should().Be(1);
        result.FailCount.Should().Be(0);
        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.IMPORTABLE_AFTER_APPROVAL);
        result.Rows[0].RowNumber.Should().Be(2);
    }

    [Theory]
    [InlineData("DRY_RUN_ONLY")]
    [InlineData("EXAMPLE_DO_NOT_IMPORT")]
    public async Task DryRunAsync_WhenDryRunOrExampleMarkerExists_ReturnsDryRunOnly(string marker)
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(approvalNotes: marker))),
            CancellationToken.None);

        result.DryRunOnlyRows.Should().Be(1);
        result.FailCount.Should().BeGreaterThan(0);
        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.DRY_RUN_ONLY);
        result.Rows[0].Findings.Should().Contain(finding =>
            finding.Severity == ProductionPolicyImportFindingSeverity.FAIL &&
            finding.Message.Contains("not importable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunAsync_WhenSourceReferenceMissing_FailsRowValidation()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(sourceReference: string.Empty))),
            CancellationToken.None);

        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.NOT_IMPORTABLE);
        result.Rows[0].Findings.Should().Contain(finding =>
            finding.RowNumber == 2 &&
            finding.Field == "source_reference" &&
            finding.Message.Contains("Required field 'source_reference'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunAsync_WhenPwdUsesSeniorCitizenEvidence_FailsEvidenceRule()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(
                policyCode: "PH_VALID_PWD_IMPORT_001",
                entitlementType: "PWD",
                requiredEvidenceType: "SENIOR_CITIZEN_ID",
                nationalLawReference: string.Empty))),
            CancellationToken.None);

        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.NOT_IMPORTABLE);
        result.Rows[0].Findings.Should().Contain(finding => finding.Message.Contains("PWD policies requiring evidence must use PWD_ID", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunAsync_WhenSeniorCitizenUsesPwdEvidence_FailsEvidenceRule()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(requiredEvidenceType: "PWD_ID"))),
            CancellationToken.None);

        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.NOT_IMPORTABLE);
        result.Rows[0].Findings.Should().Contain(finding => finding.Message.Contains("SENIOR_CITIZEN policies requiring evidence must use SENIOR_CITIZEN_ID", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunAsync_WhenPolicyCodeDuplicated_FlagsDuplicateRow()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(
                Row(policyCode: "PH_VALID_SC_IMPORT_001"),
                Row(policyCode: "PH_VALID_SC_IMPORT_001", ordinanceReference: "ORD-2099-002"))),
            CancellationToken.None);

        result.DuplicateRows.Should().Be(1);
        result.Rows[1].Decision.Should().Be(ProductionPolicyImportRowDecision.DUPLICATE_IN_FILE);
        result.Rows[1].Findings.Should().Contain(finding => finding.Message.Contains("Duplicate policy_code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunAsync_WhenProposedOnlyIsApprovedForImport_FailsReviewConsistency()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(
                verificationStatus: "PROPOSED_ONLY",
                reviewStatus: "APPROVE_FOR_IMPORT"))),
            CancellationToken.None);

        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.NOT_IMPORTABLE);
        result.Rows[0].Findings.Should().Contain(finding => finding.Message.Contains("APPROVE_FOR_IMPORT requires verification_status=ACTIVE_APPROVED", StringComparison.Ordinal));
        result.Rows[0].Findings.Should().Contain(finding => finding.Severity == ProductionPolicyImportFindingSeverity.WARN);
    }

    [Theory]
    [InlineData("SANDBOX_OC_POLICY")]
    [InlineData("TEST_OC_POLICY")]
    [InlineData("DEV_OC_POLICY")]
    [InlineData("DUMMY_OC_POLICY")]
    public async Task DryRunAsync_WhenPolicyCodeUsesNonProductionMarker_Fails(string policyCode)
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(policyCode: policyCode))),
            CancellationToken.None);

        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.NOT_IMPORTABLE);
        result.Rows[0].Findings.Should().Contain(finding => finding.Message.Contains("sandbox/test/dev/dummy/example marker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunAsync_WhenEffectiveDateRangeInvalid_Fails()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(effectiveFrom: "2099-01-02", effectiveTo: "2099-01-01"))),
            CancellationToken.None);

        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.NOT_IMPORTABLE);
        result.Rows[0].Findings.Should().Contain(finding => finding.Message.Contains("effective_to must be later than effective_from", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunAsync_WhenReviewColumnsPresent_DoNotOverrideSafety()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(
                reviewStatus: "APPROVE_FOR_IMPORT",
                approvalNotes: "EXAMPLE_DO_NOT_IMPORT"))),
            CancellationToken.None);

        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.DRY_RUN_ONLY);
        result.Rows[0].Findings.Should().Contain(finding => finding.Message.Contains("not importable production policy data", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunAsync_WhenManualReviewSignalExists_ReturnsManualReviewRequired()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(
                verificationStatus: "APPROVED_FOR_PILOT",
                reviewStatus: "APPROVE_FOR_PILOT_ONLY"))),
            CancellationToken.None);

        result.ManualReviewRows.Should().Be(1);
        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.MANUAL_REVIEW_REQUIRED);
    }

    [Fact]
    public async Task DryRunAsync_WhenQuotedCsvValueContainsComma_ParsesRow()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(Row(notes: "\"Reviewed by product, legal, and ops\""))),
            CancellationToken.None);

        result.TotalRows.Should().Be(1);
        result.Rows[0].Decision.Should().Be(ProductionPolicyImportRowDecision.IMPORTABLE_AFTER_APPROVAL);
    }

    [Fact]
    public async Task DryRunAsync_AggregatesPassWarnFailCounts()
    {
        var result = await Sut().DryRunAsync(
            new ProductionPolicyImportDryRunRequest(Csv(
                Row(policyCode: "PH_VALID_SC_IMPORT_001"),
                Row(policyCode: "PH_VALID_SC_IMPORT_002", verificationStatus: "PROPOSED_ONLY", reviewStatus: "APPROVE_FOR_IMPORT"))),
            CancellationToken.None);

        result.TotalRows.Should().Be(2);
        result.ImportableRows.Should().Be(1);
        result.NotImportableRows.Should().Be(1);
        result.WarnCount.Should().BeGreaterThan(0);
        result.FailCount.Should().BeGreaterThan(0);
    }

    private static OperatorConsoleProductionPolicyImportService Sut() => new();

    private static string HeaderOnlyCsv() => string.Join(",", CandidateHeaders);

    private static string Csv(params string[] rows) =>
        string.Join(Environment.NewLine, new[] { HeaderOnlyCsv() }.Concat(rows));

    private static string Row(
        string policyCode = "PH_VALID_SC_IMPORT_001",
        string policyName = "Controlled Senior Citizen Candidate",
        string entitlementType = "SENIOR_CITIZEN",
        string lguCode = "QAX",
        string jurisdictionName = "Controlled Review City",
        string siteGroupCode = "CONTROLLED_GROUP",
        string siteCode = "CONTROLLED_SITE",
        string policyLevel = "LOCAL_ORDINANCE",
        string policyType = "LOCAL_ORDINANCE",
        string policyResolutionBasis = "LOCAL_ORDINANCE_APPLIED",
        string benefitType = "STATUTORY_DISCOUNT_VAT_EXEMPT",
        string discountBaseScope = "VAT_EXCLUSIVE",
        string freeDurationMinutes = "",
        string initialRateExempt = "false",
        string fullFeeExempt = "false",
        string overnightExcluded = "true",
        string valetExcluded = "true",
        string standaloneParkingExcluded = "false",
        string driverOrPassengerRequired = "true",
        string beneficiaryResidencyScope = "RESIDENT_ONLY",
        string requiresEvidence = "true",
        string requiredEvidenceType = "SENIOR_CITIZEN_ID",
        string requiresOperatorValidation = "true",
        string legalBasisReference = "CONTROLLED LEGAL REFERENCE",
        string ordinanceReference = "ORD-2099-001",
        string nationalLawReference = "",
        string sourceReference = "CONTROLLED SOURCE REFERENCE",
        string verificationStatus = "ACTIVE_APPROVED",
        string effectiveFrom = "2099-01-01",
        string effectiveTo = "",
        string reviewedBy = "reviewer",
        string reviewedAt = "2099-01-02T00:00:00Z",
        string approvedBy = "approver",
        string approvedAt = "2099-01-03T00:00:00Z",
        string notes = "Controlled review note",
        string reviewStatus = "APPROVE_FOR_IMPORT",
        string reviewOwner = "review-owner",
        string legalReviewDecision = "APPROVE",
        string productReviewDecision = "APPROVE",
        string opsReviewDecision = "APPROVE",
        string engineeringReviewDecision = "APPROVE",
        string qaReviewDecision = "APPROVE",
        string approvalNotes = "Controlled approval note") =>
        string.Join(
            ",",
            [
                policyCode,
                policyName,
                entitlementType,
                lguCode,
                jurisdictionName,
                siteGroupCode,
                siteCode,
                policyLevel,
                policyType,
                policyResolutionBasis,
                benefitType,
                discountBaseScope,
                freeDurationMinutes,
                initialRateExempt,
                fullFeeExempt,
                overnightExcluded,
                valetExcluded,
                standaloneParkingExcluded,
                driverOrPassengerRequired,
                beneficiaryResidencyScope,
                requiresEvidence,
                requiredEvidenceType,
                requiresOperatorValidation,
                legalBasisReference,
                ordinanceReference,
                nationalLawReference,
                sourceReference,
                verificationStatus,
                effectiveFrom,
                effectiveTo,
                reviewedBy,
                reviewedAt,
                approvedBy,
                approvedAt,
                notes,
                reviewStatus,
                reviewOwner,
                legalReviewDecision,
                productReviewDecision,
                opsReviewDecision,
                engineeringReviewDecision,
                qaReviewDecision,
                approvalNotes
            ]);
}
