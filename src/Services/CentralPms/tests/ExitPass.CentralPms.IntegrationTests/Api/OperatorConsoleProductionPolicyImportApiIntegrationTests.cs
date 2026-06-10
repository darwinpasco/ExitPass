using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class OperatorConsoleProductionPolicyImportApiIntegrationTests
{
    private static readonly Guid OperatorUserId = Guid.Parse("7c000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("7c000000-0000-0000-0000-000000000002");
    private const string Endpoint = "/v1/ops/operator-console/statutory-discounts/policies/import/dry-run";

    [Fact]
    public async Task DryRun_WhenHeaderOnlyWorksheet_ReturnsSafeNoImportResponse()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HeaderOnlyCsv());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportDryRunResponse>();
        body.Should().NotBeNull();
        body!.Imported.Should().BeFalse();
        body.ImportedRowCount.Should().Be(0);
        body.DryRunOnly.Should().BeTrue();
        body.Message.Should().Be("Dry run completed. No policies were imported.");
        body.Summary.TotalRows.Should().Be(0);
        body.Summary.FailCount.Should().Be(0);
        body.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRun_WhenBadSampleSubmitted_ReturnsFindingsButDoesNotImport()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Csv(
            Row(policyCode: "DRYRUNONLY_SC_VALID_LOOKING", approvalNotes: "EXAMPLE_DO_NOT_IMPORT"),
            Row(policyCode: "DUMMY_PWD_WRONG_EVIDENCE", entitlementType: "PWD", requiredEvidenceType: "SENIOR_CITIZEN_ID", notes: "password=secret"),
            Row(policyCode: "DUMMY_DUPLICATE_POLICY"),
            Row(policyCode: "DUMMY_DUPLICATE_POLICY", ordinanceReference: "ORD-2099-002")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawResponse = await response.Content.ReadAsStringAsync();
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportDryRunResponse>();

        body.Should().NotBeNull();
        body!.Imported.Should().BeFalse();
        body.ImportedRowCount.Should().Be(0);
        body.DryRunOnly.Should().BeTrue();
        body.Summary.TotalRows.Should().Be(4);
        body.Summary.FailCount.Should().BeGreaterThan(0);
        body.Summary.DryRunOnlyCount.Should().Be(1);
        body.Summary.DuplicateCount.Should().Be(1);
        body.Rows.Select(row => row.RowNumber).Should().Contain(new[] { 2, 3, 4, 5 });
        body.Rows.Should().Contain(row => row.Findings.Any(finding => finding.Message.Contains("not importable", StringComparison.Ordinal)));
        body.Rows.Should().Contain(row => row.Findings.Any(finding => finding.Message.Contains("PWD policies requiring evidence must use PWD_ID", StringComparison.Ordinal)));
        body.Rows.Should().Contain(row => row.Findings.Any(finding => finding.Message.Contains("Duplicate policy_code", StringComparison.Ordinal)));
        var normalizedRawResponse = rawResponse.ToUpperInvariant();
        normalizedRawResponse.Should().NotContain("PASSWORD=SECRET");
        normalizedRawResponse.Should().NotContain("STACKTRACE");
        normalizedRawResponse.Should().NotContain("INSERT ");
        normalizedRawResponse.Should().NotContain("SELECT ");
    }

    [Fact]
    public async Task DryRun_WhenCsvContentEmpty_ReturnsBadRequest()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_POLICY_IMPORT_DRY_RUN_REQUEST");
    }

    [Fact]
    public async Task DryRun_WhenOperatorIdentityMissing_ReturnsBadRequest()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new OperatorConsoleProductionPolicyImportDryRunRequest(HeaderOnlyCsv(), "header-only.csv", CorrelationId: CorrelationId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_OPERATOR_CONSOLE_POLICY_IMPORT_DRY_RUN_REQUEST");
    }

    [Fact]
    public async Task DryRun_WhenCsvContentTooLarge_ReturnsBadRequest()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var oversized = new string('A', 1_000_001);

        using var response = await SendAsync(client, oversized);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("OPERATOR_CONSOLE_POLICY_IMPORT_DRY_RUN_TOO_LARGE");
    }

    [Fact]
    public async Task DryRun_ResponseConfirmsEndpointDidNotImportOrWritePolicyRows()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Csv(Row()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportDryRunResponse>();
        body.Should().NotBeNull();
        body!.Imported.Should().BeFalse();
        body.ImportedRowCount.Should().Be(0);
        body.DryRunOnly.Should().BeTrue();
        body.Message.Should().Contain("No policies were imported");
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string csvContent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new OperatorConsoleProductionPolicyImportDryRunRequest(
                csvContent,
                "candidate.csv",
                OperatorUserId,
                CorrelationId))
        };
        request.Headers.Add("X-Operator-User-Id", OperatorUserId.ToString());
        request.Headers.Add("X-Correlation-Id", CorrelationId.ToString());
        return await client.SendAsync(request);
    }

    private static string HeaderOnlyCsv() => string.Join(
        ",",
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
        ]);

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
