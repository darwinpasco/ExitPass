using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

public sealed class OperatorConsoleProductionPolicyImportApiIntegrationTests
{
    private static readonly Guid OperatorUserId = Guid.Parse("7c000000-0000-0000-0000-000000000001");
    private static readonly Guid CorrelationId = Guid.Parse("7c000000-0000-0000-0000-000000000002");
    private const string Endpoint = "/v1/ops/operator-console/statutory-discounts/policies/import/dry-run";
    private const string ReviewEndpoint = "/v1/ops/operator-console/statutory-discounts/policies/import/reviews";
    private static readonly SemaphoreSlim SchemaSemaphore = new(1, 1);
    private static bool s_schemaEnsured;
    private const string SubmitPermission = "operator-console.policy-import-review.submit";
    private const string ViewOwnPermission = "operator-console.policy-import-review.view-own";
    private const string ReviewPermission = "operator-console.policy-import-review.review";
    private const string ApproveLegalPermission = "operator-console.policy-import-review.approve.legal";
    private const string ApproveOpsPermission = "operator-console.policy-import-review.approve.ops";
    private const string ApproveQaPermission = "operator-console.policy-import-review.approve.qa";
    private const string ApproveDbPermission = "operator-console.policy-import-review.approve.db";

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

    [Fact]
    public async Task SubmitReview_WhenDryRunSubmitted_PersistsSubmissionHistoryAndFindings()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var dryRun = await DryRunAsync(client, makerId, correlationId, Csv(Row(policyCode: $"PH_REVIEW_FAIL_{Guid.NewGuid():N}", sourceReference: string.Empty)));

            using var response = await SubmitReviewAsync(client, makerId, correlationId, dryRun);

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewResponse>();
            body.Should().NotBeNull();
            body!.Imported.Should().BeFalse();
            body.ProductionPolicyActivationBlocked.Should().BeTrue();
            body.Submission.Status.Should().Be("SUBMITTED_FOR_REVIEW");
            body.Submission.History.Should().ContainSingle(history => history.Action == "SUBMIT_FOR_REVIEW");
            body.Findings.Should().Contain(finding => finding.Message.Contains("FAIL findings", StringComparison.Ordinal));

            var persisted = await ReadReviewPersistenceCountsAsync(body.Submission.ReviewId);
            persisted.SubmissionCount.Should().Be(1);
            persisted.HistoryCount.Should().Be(1);
            persisted.FindingCount.Should().Be(1);
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewSubmitted")).Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
    }

    [Fact]
    public async Task DecideReview_WhenCheckerApproves_PersistsDecisionAndHistory()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var checkerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var submitted = await SubmitCleanReviewAsync(client, makerId, correlationId);

            using var response = await DecideReviewAsync(
                client,
                submitted.Submission.ReviewId,
                checkerId,
                "APPROVE_LEGAL",
                "legal approved",
                correlationId);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewResponse>();
            body.Should().NotBeNull();
            body!.Submission.Status.Should().Be("OPS_REVIEW_PENDING");
            body.Submission.ReviewerDecisions.Should().ContainSingle(decision =>
                decision.ReviewerRole == "LEGAL" &&
                decision.ReviewerOperatorId == checkerId);
            body.Submission.History.Should().Contain(history =>
                history.Action == "APPROVE_LEGAL" &&
                history.ReviewerRole == "LEGAL");

            var persisted = await ReadReviewPersistenceCountsAsync(submitted.Submission.ReviewId);
            persisted.DecisionCount.Should().Be(1);
            persisted.HistoryCount.Should().Be(2);
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewDecisionRecorded")).Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
    }

    [Fact]
    public async Task SubmitReview_WhenPermissionMissing_ReturnsForbiddenAndAuditsDenied()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var dryRun = await DryRunAsync(client, makerId, correlationId, Csv(Row()));

            using var response = await SubmitReviewAsync(
                client,
                makerId,
                correlationId,
                dryRun,
                permissions: []);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_FORBIDDEN");
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewAccessDenied")).Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
    }

    [Fact]
    public async Task ListReviews_WhenMakerPermissionOnly_ReturnsOnlyOwnReviews()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var otherMakerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var own = await SubmitCleanReviewAsync(client, makerId, correlationId);
            var other = await SubmitCleanReviewAsync(client, otherMakerId, Guid.NewGuid());

            using var response = await GetReviewListAsync(
                client,
                makerId: null,
                status: null,
                correlationId,
                operatorId: makerId,
                permissions: [ViewOwnPermission]);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewListResponse>();
            body.Should().NotBeNull();
            body!.Items.Should().ContainSingle(item => item.Submission.ReviewId == own.Submission.ReviewId);
            body.Items.Should().NotContain(item => item.Submission.ReviewId == other.Submission.ReviewId);
            body.Imported.Should().BeFalse();
            body.ProductionPolicyActivationBlocked.Should().BeTrue();
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewListed")).Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
            await CleanupReviewRowsAsync(otherMakerId);
        }
    }

    [Fact]
    public async Task GetReview_WhenUnauthorizedOperatorReadsUnrelatedReview_ReturnsForbiddenAndAuditsDenied()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var unauthorizedId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var submitted = await SubmitCleanReviewAsync(client, makerId, correlationId);

            using var response = await GetReviewAsync(
                client,
                submitted.Submission.ReviewId,
                correlationId,
                unauthorizedId,
                ViewOwnPermission);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_FORBIDDEN");
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewAccessDenied")).Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
    }

    [Fact]
    public async Task DecideReview_WhenReviewerPermissionMissing_ReturnsForbiddenAndAuditsDenied()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var submitted = await SubmitCleanReviewAsync(client, makerId, correlationId);

            using var response = await DecideReviewAsync(
                client,
                submitted.Submission.ReviewId,
                reviewerId,
                "APPROVE_LEGAL",
                "legal approved",
                correlationId,
                permissions: []);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            error.Should().NotBeNull();
            error!.ErrorCode.Should().Be("OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_DECISION_FORBIDDEN");
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewAccessDenied")).Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
    }

    [Fact]
    public async Task DecideReview_WhenMakerApprovesOwnSubmission_ReturnsConflictAndDoesNotPersistDecision()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var submitted = await SubmitCleanReviewAsync(client, makerId, correlationId);

            using var response = await DecideReviewAsync(
                client,
                submitted.Submission.ReviewId,
                makerId,
                "APPROVE_LEGAL",
                "self approved",
                correlationId);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            error.Should().NotBeNull();
            error!.Message.Should().Contain("Maker cannot decide");

            var persisted = await ReadReviewPersistenceCountsAsync(submitted.Submission.ReviewId);
            persisted.DecisionCount.Should().Be(0);
            persisted.HistoryCount.Should().Be(1);
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewSelfDecisionBlocked")).Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
    }

    [Fact]
    public async Task SubmitReview_WhenSubmissionIsReplayed_DoesNotCreateDuplicateActiveReview()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var dryRun = await DryRunAsync(client, makerId, correlationId, Csv(Row()));

            using var firstResponse = await SubmitReviewAsync(client, makerId, correlationId, dryRun);
            using var secondResponse = await SubmitReviewAsync(client, makerId, correlationId, dryRun);

            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var first = await firstResponse.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewResponse>();
            var second = await secondResponse.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewResponse>();
            first.Should().NotBeNull();
            second.Should().NotBeNull();
            second!.Submission.ReviewId.Should().Be(first!.Submission.ReviewId);
            second.Message.Should().Contain("already exists");

            var activeCount = await CountActiveReviewsForMakerAsync(makerId);
            activeCount.Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
    }

    [Fact]
    public async Task ListReviews_WhenSubmissionPersisted_ReturnsReviewQueueSummaryAndSafetyFlags()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var submitted = await SubmitCleanReviewAsync(client, makerId, correlationId);

            using var response = await GetReviewListAsync(
                client,
                makerId,
                status: submitted.Submission.Status,
                correlationId);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewListResponse>();
            body.Should().NotBeNull();
            body!.Imported.Should().BeFalse();
            body.ProductionPolicyActivationBlocked.Should().BeTrue();
            body.Items.Should().ContainSingle(item => item.Submission.ReviewId == submitted.Submission.ReviewId);
            var item = body.Items.Single(item => item.Submission.ReviewId == submitted.Submission.ReviewId);
            item.Imported.Should().BeFalse();
            item.ProductionPolicyActivationBlocked.Should().BeTrue();
            item.Submission.DryRunSummary.ImportableCount.Should().Be(submitted.Submission.DryRunSummary.ImportableCount);
            item.Submission.History.Should().Contain(history => history.Action == "SUBMIT_FOR_REVIEW");
            item.Submission.CreatedAt.Should().NotBe(default);
            item.Submission.UpdatedAt.Should().NotBe(default);
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewListed")).Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
    }

    [Fact]
    public async Task GetReview_WhenSubmissionPersisted_ReturnsDetailByReviewIdAndSafetyFlags()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var checkerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var submitted = await SubmitCleanReviewAsync(client, makerId, correlationId);
            using var decisionResponse = await DecideReviewAsync(
                client,
                submitted.Submission.ReviewId,
                checkerId,
                "APPROVE_LEGAL",
                "legal approved",
                correlationId);
            decisionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var response = await GetReviewAsync(client, submitted.Submission.ReviewId, correlationId);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewResponse>();
            body.Should().NotBeNull();
            body!.Imported.Should().BeFalse();
            body.ProductionPolicyActivationBlocked.Should().BeTrue();
            body.Submission.ReviewId.Should().Be(submitted.Submission.ReviewId);
            body.Submission.Status.Should().Be("OPS_REVIEW_PENDING");
            body.Submission.ReviewerDecisions.Should().ContainSingle(decision =>
                decision.ReviewerRole == "LEGAL" &&
                decision.ReviewerOperatorId == checkerId);
            body.Submission.History.Should().Contain(history => history.Action == "APPROVE_LEGAL");
            body.Message.Should().Contain("No policies were imported or activated");

            using var filteredListResponse = await GetReviewListAsync(
                client,
                makerId: null,
                status: null,
                correlationId,
                reviewerId: checkerId,
                reviewerRole: "LEGAL");
            filteredListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var filteredList = await filteredListResponse.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewListResponse>();
            filteredList.Should().NotBeNull();
            filteredList!.Items.Should().ContainSingle(item => item.Submission.ReviewId == submitted.Submission.ReviewId);
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewDetailViewed")).Should().Be(1);
            (await CountAuditEventsAsync(correlationId, "OperatorConsoleProductionPolicyImportReviewListed")).Should().Be(1);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
    }

    [Fact]
    public async Task GetReview_WhenReviewIdUnknown_ReturnsNotFound()
    {
        await EnsureReviewQueueSchemaAsync();

        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await GetReviewAsync(client, Guid.NewGuid(), Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be("OPERATOR_CONSOLE_POLICY_IMPORT_REVIEW_NOT_FOUND");
    }

    [Fact]
    public async Task DecideReview_WhenFullyApproved_DoesNotActivateProductionPolicyRowsOrCreateImportJob()
    {
        await EnsureReviewQueueSchemaAsync();
        var makerId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var activePoliciesBefore = await CountActiveProductionPolicyRowsAsync();
        var importJobTablesBefore = await CountProductionPolicyImportJobTablesAsync();

        try
        {
            using var factory = new CustomWebApplicationFactory();
            using var client = factory.CreateClient();
            var submitted = await SubmitCleanReviewAsync(client, makerId, correlationId);
            var reviewers = new[]
            {
                ("APPROVE_LEGAL", Guid.NewGuid()),
                ("APPROVE_OPS", Guid.NewGuid()),
                ("APPROVE_QA", Guid.NewGuid()),
                ("APPROVE_DB", Guid.NewGuid())
            };

            OperatorConsoleProductionPolicyImportReviewResponse? current = submitted;
            foreach (var (action, reviewerId) in reviewers)
            {
                using var response = await DecideReviewAsync(
                    client,
                    submitted.Submission.ReviewId,
                    reviewerId,
                    action,
                    "approved",
                    correlationId);

                response.StatusCode.Should().Be(HttpStatusCode.OK);
                current = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewResponse>();
            }

            current.Should().NotBeNull();
            current!.Imported.Should().BeFalse();
            current.ProductionPolicyActivationBlocked.Should().BeTrue();
            current.Submission.Status.Should().Be("APPROVED_FOR_DB_REPO_ALIGNMENT");
            current.Message.Should().Contain("No policies were imported or activated");

            using var detailResponse = await GetReviewAsync(client, submitted.Submission.ReviewId, correlationId);
            detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = await detailResponse.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewResponse>();
            detail.Should().NotBeNull();
            detail!.Imported.Should().BeFalse();
            detail.ProductionPolicyActivationBlocked.Should().BeTrue();
            detail.Submission.Status.Should().Be("APPROVED_FOR_DB_REPO_ALIGNMENT");

            var activePoliciesAfter = await CountActiveProductionPolicyRowsAsync();
            var importJobTablesAfter = await CountProductionPolicyImportJobTablesAsync();
            activePoliciesAfter.Should().Be(activePoliciesBefore);
            importJobTablesAfter.Should().Be(importJobTablesBefore);
        }
        finally
        {
            await CleanupReviewRowsAsync(makerId);
        }
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

    private static async Task<OperatorConsoleProductionPolicyImportDryRunResponse> DryRunAsync(
        HttpClient client,
        Guid operatorUserId,
        Guid correlationId,
        string csvContent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new OperatorConsoleProductionPolicyImportDryRunRequest(
                csvContent,
                "candidate.csv",
                operatorUserId,
                correlationId))
        };
        request.Headers.Add("X-Operator-User-Id", operatorUserId.ToString());
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportDryRunResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private static async Task<OperatorConsoleProductionPolicyImportReviewResponse> SubmitCleanReviewAsync(
        HttpClient client,
        Guid makerId,
        Guid correlationId)
    {
        var dryRun = await DryRunAsync(client, makerId, correlationId, Csv(Row()));
        using var response = await SubmitReviewAsync(client, makerId, correlationId, dryRun);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleProductionPolicyImportReviewResponse>();
        body.Should().NotBeNull();
        return body!;
    }

    private static async Task<HttpResponseMessage> SubmitReviewAsync(
        HttpClient client,
        Guid makerId,
        Guid correlationId,
        OperatorConsoleProductionPolicyImportDryRunResponse dryRun,
        string[]? permissions = null)
    {
        await SeedIdentityUserAsync(makerId, correlationId);
        var request = new HttpRequestMessage(HttpMethod.Post, ReviewEndpoint)
        {
            Content = JsonContent.Create(new OperatorConsoleProductionPolicyImportReviewSubmitRequest(
                dryRun,
                "candidate.csv",
                makerId,
                correlationId))
        };
        request.Headers.Add("X-Operator-User-Id", makerId.ToString());
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        AddPermissions(request, permissions ?? [SubmitPermission, ViewOwnPermission]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DecideReviewAsync(
        HttpClient client,
        Guid reviewId,
        Guid reviewerId,
        string action,
        string? reason,
        Guid correlationId,
        string[]? permissions = null)
    {
        await SeedIdentityUserAsync(reviewerId, correlationId);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ReviewEndpoint}/{reviewId}/decision")
        {
            Content = JsonContent.Create(new OperatorConsoleProductionPolicyImportReviewDecisionRequest(
                action,
                reason,
                reviewerId,
                correlationId))
        };
        request.Headers.Add("X-Operator-User-Id", reviewerId.ToString());
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        AddPermissions(request, permissions ?? [PermissionForDecision(action)]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetReviewListAsync(
        HttpClient client,
        Guid? makerId,
        string? status,
        Guid correlationId,
        Guid? reviewerId = null,
        string? reviewerRole = null,
        Guid? operatorId = null,
        string[]? permissions = null)
    {
        var effectiveOperatorId = operatorId ?? makerId ?? OperatorUserId;
        await SeedIdentityUserAsync(effectiveOperatorId, correlationId);
        var parameters = new List<string>
        {
            $"correlationId={Uri.EscapeDataString(correlationId.ToString())}",
            "limit=25",
            "offset=0"
        };

        if (makerId.HasValue)
        {
            parameters.Add($"makerOperatorId={Uri.EscapeDataString(makerId.Value.ToString())}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            parameters.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (reviewerId.HasValue)
        {
            parameters.Add($"reviewerOperatorId={Uri.EscapeDataString(reviewerId.Value.ToString())}");
        }

        if (!string.IsNullOrWhiteSpace(reviewerRole))
        {
            parameters.Add($"reviewerRole={Uri.EscapeDataString(reviewerRole)}");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{ReviewEndpoint}?{string.Join("&", parameters)}");
        request.Headers.Add("X-Operator-User-Id", effectiveOperatorId.ToString());
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        AddPermissions(
            request,
            permissions ?? [ReviewPermission, ViewOwnPermission, ApproveLegalPermission, ApproveOpsPermission, ApproveQaPermission, ApproveDbPermission]);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetReviewAsync(
        HttpClient client,
        Guid reviewId,
        Guid correlationId,
        Guid? operatorId = null,
        params string[] permissions)
    {
        var effectiveOperatorId = operatorId ?? OperatorUserId;
        await SeedIdentityUserAsync(effectiveOperatorId, correlationId);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ReviewEndpoint}/{reviewId}?correlationId={correlationId}");
        request.Headers.Add("X-Operator-User-Id", effectiveOperatorId.ToString());
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        AddPermissions(
            request,
            permissions.Length == 0
                ? [ReviewPermission, ViewOwnPermission, ApproveLegalPermission, ApproveOpsPermission, ApproveQaPermission, ApproveDbPermission]
                : permissions);
        return await client.SendAsync(request);
    }

    private static void AddPermissions(HttpRequestMessage request, params string[] permissions)
    {
        request.Headers.Add(CentralPmsRbacPolicyCatalog.UserIdHeaderName, request.Headers.GetValues("X-Operator-User-Id").Single());
        request.Headers.Add(CentralPmsRbacPolicyCatalog.PermissionsHeaderName, string.Join(",", permissions.Distinct()));
    }

    private static string PermissionForDecision(string action) =>
        action switch
        {
            "APPROVE_LEGAL" => ApproveLegalPermission,
            "APPROVE_OPS" => ApproveOpsPermission,
            "APPROVE_QA" => ApproveQaPermission,
            "APPROVE_DB" => ApproveDbPermission,
            _ => ReviewPermission
        };

    private static async Task SeedIdentityUserAsync(Guid userId, Guid correlationId)
    {
        const string sql = """
            INSERT INTO identity.users (
                user_id,
                username,
                email,
                email_normalized,
                display_name,
                user_type,
                user_status,
                effective_from,
                effective_to,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @user_id,
                @username,
                @email,
                @email_normalized,
                @display_name,
                'SITE_OPERATOR',
                'ACTIVE',
                @effective_from,
                @effective_to,
                @user_id,
                @user_id
            )
            ON CONFLICT (user_id) DO NOTHING;
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = userId;
        command.Parameters.Add("username", NpgsqlDbType.Varchar).Value = $"op-review-{userId:N}";
        command.Parameters.Add("email", NpgsqlDbType.Varchar).Value = $"op-review-{userId:N}@example.test";
        command.Parameters.Add("email_normalized", NpgsqlDbType.Varchar).Value = $"OP-REVIEW-{userId:N}@EXAMPLE.TEST";
        command.Parameters.Add("display_name", NpgsqlDbType.Varchar).Value = $"Operator Review Test {correlationId:N}";
        command.Parameters.Add("effective_from", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        command.Parameters.Add("effective_to", NpgsqlDbType.TimestampTz).Value = DateTimeOffset.Parse("2035-01-01T00:00:00Z");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureReviewQueueSchemaAsync()
    {
        if (s_schemaEnsured)
        {
            return;
        }

        await SchemaSemaphore.WaitAsync();
        try
        {
            if (s_schemaEnsured)
            {
                return;
            }

            if (!await ReviewQueueSchemaExistsAsync())
            {
                var patchPath = ResolveRepoPath("infra", "db", "patches", "ExitPass_ProductionPolicyImportReviewQueue_v1.2.sql");
                var sql = await File.ReadAllTextAsync(patchPath);

                await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
            }

            s_schemaEnsured = true;
        }
        finally
        {
            SchemaSemaphore.Release();
        }
    }

    private static async Task<bool> ReviewQueueSchemaExistsAsync()
    {
        const string sql = """
            SELECT COUNT(*) = 4
            FROM (
                VALUES
                    ('operator_console.production_policy_import_review_submissions'),
                    ('operator_console.production_policy_import_review_decisions'),
                    ('operator_console.production_policy_import_review_history'),
                    ('operator_console.production_policy_import_review_findings')
            ) AS required_objects(object_name)
            WHERE to_regclass(required_objects.object_name) IS NOT NULL;
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is true;
    }

    private static string ResolveRepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }

    private static async Task CleanupReviewRowsAsync(Guid makerId)
    {
        const string sql = """
            DELETE FROM operator_console.production_policy_import_review_submissions
            WHERE maker_operator_id = @maker_id;
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("maker_id", NpgsqlDbType.Uuid).Value = makerId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<ReviewPersistenceCounts> ReadReviewPersistenceCountsAsync(Guid reviewId)
    {
        const string sql = """
            SELECT
                (SELECT count(*) FROM operator_console.production_policy_import_review_submissions WHERE review_id = @review_id),
                (SELECT count(*) FROM operator_console.production_policy_import_review_decisions WHERE review_id = @review_id),
                (SELECT count(*) FROM operator_console.production_policy_import_review_history WHERE review_id = @review_id),
                (SELECT count(*) FROM operator_console.production_policy_import_review_findings WHERE review_id = @review_id);
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("review_id", NpgsqlDbType.Uuid).Value = reviewId;

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new ReviewPersistenceCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    private static async Task<long> CountActiveReviewsForMakerAsync(Guid makerId)
    {
        const string sql = """
            SELECT count(*)
            FROM operator_console.production_policy_import_review_submissions
            WHERE maker_operator_id = @maker_id
              AND review_status NOT IN ('REJECTED', 'CANCELLED', 'SUPERSEDED');
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("maker_id", NpgsqlDbType.Uuid).Value = makerId;
        var value = await command.ExecuteScalarAsync();
        return (long)value!;
    }

    private static async Task<long> CountActiveProductionPolicyRowsAsync()
    {
        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();

        await using (var existsCommand = new NpgsqlCommand(
            "SELECT to_regclass('discounts.statutory_discount_policy_registry')::text;",
            connection))
        {
            var table = await existsCommand.ExecuteScalarAsync();
            if (table is null || table is DBNull)
            {
                return 0;
            }
        }

        await using var countCommand = new NpgsqlCommand(
            "SELECT count(*) FROM discounts.statutory_discount_policy_registry WHERE policy_status::text = 'ACTIVE';",
            connection);
        var value = await countCommand.ExecuteScalarAsync();
        return (long)value!;
    }

    private static async Task<long> CountProductionPolicyImportJobTablesAsync()
    {
        const string sql = """
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema = 'operator_console'
              AND table_name LIKE '%production_policy_import%job%';
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return (long)value!;
    }

    private static async Task<long> CountAuditEventsAsync(Guid correlationId, string eventType)
    {
        const string sql = """
            SELECT count(*)
            FROM audit.audit_events
            WHERE correlation_id = @correlation_id
              AND event_type = @event_type;
            """;

        await using var connection = new NpgsqlConnection(CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = eventType;
        var value = await command.ExecuteScalarAsync();
        return (long)value!;
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

    private sealed record ReviewPersistenceCounts(
        long SubmissionCount,
        long DecisionCount,
        long HistoryCount,
        long FindingCount);
}
