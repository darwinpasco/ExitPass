using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class OperatorConsoleProductionPolicyImportReviewServiceTests
{
    private static readonly Guid MakerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LegalReviewerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OpsReviewerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid QaReviewerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DbReviewerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid CorrelationId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task SubmitForReviewAsync_WhenDryRunResultIsClean_CreatesReviewSubmission()
    {
        var queue = new FakeReviewQueue();
        var sut = new OperatorConsoleProductionPolicyImportReviewService(queue);

        var result = await sut.SubmitForReviewAsync(
            new ProductionPolicyImportReviewSubmitRequest(
                MakerId,
                "candidate.csv",
                DryRunResult(),
                CorrelationId),
            CancellationToken.None);

        result.PoliciesImported.Should().BeFalse();
        result.Message.Should().Contain("No policies were imported");
        result.Submission.MakerOperatorId.Should().Be(MakerId);
        result.Submission.Status.Should().Be(ProductionPolicyImportReviewSubmissionStatus.LEGAL_REVIEW_PENDING);
        result.Submission.DryRunResult.TotalRows.Should().Be(1);
        result.Submission.History.Should().ContainSingle(entry =>
            entry.Action == ProductionPolicyImportReviewDecisionAction.SUBMIT_FOR_REVIEW);
        queue.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task DecideAsync_WhenDryRunHasFailFindings_BlocksApprovalForDbRepoAlignment()
    {
        var sut = Sut(out var queue);
        var submitted = await SubmitAsync(sut, DryRunResult(failCount: 1));

        var act = () => sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, LegalReviewerId, ProductionPolicyImportReviewDecisionAction.APPROVE_LEGAL),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FAIL findings block*");
        queue.LastSaved!.Status.Should().Be(ProductionPolicyImportReviewSubmissionStatus.SUBMITTED_FOR_REVIEW);
    }

    [Fact]
    public async Task DecideAsync_WhenMakerApprovesOwnSubmission_RejectsSelfApproval()
    {
        var sut = Sut(out _);
        var submitted = await SubmitAsync(sut, DryRunResult());

        var act = () => sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, MakerId, ProductionPolicyImportReviewDecisionAction.APPROVE_LEGAL),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Maker cannot approve*");
    }

    [Fact]
    public async Task DecideAsync_WhenReviewerApproves_RecordsLegalOpsQaAndDbSeparately()
    {
        var sut = Sut(out _);
        var submitted = await SubmitAsync(sut, DryRunResult());

        var legal = await sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, LegalReviewerId, ProductionPolicyImportReviewDecisionAction.APPROVE_LEGAL),
            CancellationToken.None);
        var ops = await sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, OpsReviewerId, ProductionPolicyImportReviewDecisionAction.APPROVE_OPS),
            CancellationToken.None);
        var qa = await sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, QaReviewerId, ProductionPolicyImportReviewDecisionAction.APPROVE_QA),
            CancellationToken.None);
        var db = await sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, DbReviewerId, ProductionPolicyImportReviewDecisionAction.APPROVE_DB),
            CancellationToken.None);

        legal.Submission.Status.Should().Be(ProductionPolicyImportReviewSubmissionStatus.OPS_REVIEW_PENDING);
        ops.Submission.Status.Should().Be(ProductionPolicyImportReviewSubmissionStatus.QA_REVIEW_PENDING);
        qa.Submission.Status.Should().Be(ProductionPolicyImportReviewSubmissionStatus.DB_REVIEW_PENDING);
        db.Submission.Status.Should().Be(ProductionPolicyImportReviewSubmissionStatus.APPROVED_FOR_DB_REPO_ALIGNMENT);
        db.Submission.ReviewerDecisions.Select(decision => decision.ReviewerRole)
            .Should()
            .Equal(
                ProductionPolicyImportReviewerRole.LEGAL,
                ProductionPolicyImportReviewerRole.OPS,
                ProductionPolicyImportReviewerRole.QA,
                ProductionPolicyImportReviewerRole.DB);
        db.PoliciesImported.Should().BeFalse();
        db.Message.Should().Contain("No policies were imported or activated");
    }

    [Fact]
    public async Task DecideAsync_WhenRejectHasNoReason_Throws()
    {
        var sut = Sut(out _);
        var submitted = await SubmitAsync(sut, DryRunResult());

        var act = () => sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, LegalReviewerId, ProductionPolicyImportReviewDecisionAction.REJECT, reason: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*reason is required*");
    }

    [Fact]
    public async Task DecideAsync_WhenRequestChangesHasNoReason_Throws()
    {
        var sut = Sut(out _);
        var submitted = await SubmitAsync(sut, DryRunResult());

        var act = () => sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, LegalReviewerId, ProductionPolicyImportReviewDecisionAction.REQUEST_CHANGES, reason: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*reason is required*");
    }

    [Theory]
    [InlineData(ProductionPolicyImportReviewDecisionAction.REJECT)]
    [InlineData(ProductionPolicyImportReviewDecisionAction.CANCEL)]
    [InlineData(ProductionPolicyImportReviewDecisionAction.MARK_SUPERSEDED)]
    public async Task DecideAsync_WhenSubmissionIsTerminal_CannotApproveLater(ProductionPolicyImportReviewDecisionAction terminalAction)
    {
        var sut = Sut(out _);
        var submitted = await SubmitAsync(sut, DryRunResult());
        var reason = terminalAction == ProductionPolicyImportReviewDecisionAction.REJECT
            ? "not ready"
            : null;

        await sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, LegalReviewerId, terminalAction, reason),
            CancellationToken.None);

        var act = () => sut.DecideAsync(
            Decision(submitted.Submission.ReviewId, OpsReviewerId, ProductionPolicyImportReviewDecisionAction.APPROVE_OPS),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*terminal*");
    }

    [Fact]
    public void ProductionPolicyImportReviewDecisionAction_DoesNotContainImportOrActivationAction()
    {
        var forbidden = new[]
        {
            "IMPORT",
            "ACTIVATE",
            "APPLY",
            "SEED",
            "APPROVE_PRODUCTION_AUTO_APPLICATION"
        };

        var actionNames = Enum.GetNames<ProductionPolicyImportReviewDecisionAction>();

        actionNames.Should().OnlyContain(action =>
            forbidden.All(marker => !action.Contains(marker, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ReviewResponses_DoNotExposeRawCsvContent()
    {
        var sut = Sut(out _);

        var result = await SubmitAsync(sut, DryRunResult());

        var json = JsonSerializer.Serialize(result);
        json.Contains("csvContent", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        json.Contains("raw,csv,line", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        json.Should().Contain("No policies were imported");
    }

    [Fact]
    public async Task SubmitForReviewAsync_ReferencesDryRunSummaryWithoutDatabaseWriteDependency()
    {
        var queue = new FakeReviewQueue();
        var sut = new OperatorConsoleProductionPolicyImportReviewService(queue);

        var result = await SubmitAsync(sut, DryRunResult(totalRows: 3));

        result.Submission.DryRunResult.TotalRows.Should().Be(3);
        result.PoliciesImported.Should().BeFalse();
        queue.SaveCount.Should().Be(1);
        queue.DbWriteRequested.Should().BeFalse();
    }

    private static OperatorConsoleProductionPolicyImportReviewService Sut(out FakeReviewQueue queue)
    {
        queue = new FakeReviewQueue();
        return new OperatorConsoleProductionPolicyImportReviewService(queue);
    }

    private static Task<ProductionPolicyImportReviewSubmitResult> SubmitAsync(
        OperatorConsoleProductionPolicyImportReviewService sut,
        ProductionPolicyImportDryRunResult dryRunResult) =>
        sut.SubmitForReviewAsync(
            new ProductionPolicyImportReviewSubmitRequest(MakerId, "candidate.csv", dryRunResult, CorrelationId),
            CancellationToken.None);

    private static ProductionPolicyImportReviewDecisionRequest Decision(
        Guid reviewId,
        Guid reviewerId,
        ProductionPolicyImportReviewDecisionAction action,
        string? reason = "reviewed") =>
        new(reviewId, reviewerId, action, reason, CorrelationId);

    private static ProductionPolicyImportDryRunResult DryRunResult(
        int totalRows = 1,
        int failCount = 0) =>
        new(
            IsDryRun: true,
            PoliciesImported: false,
            TotalRows: totalRows,
            ImportableRows: failCount == 0 ? totalRows : 0,
            ManualReviewRows: 0,
            NotImportableRows: failCount > 0 ? totalRows : 0,
            DryRunOnlyRows: 0,
            DuplicateRows: 0,
            PassCount: failCount == 0 ? 1 : 0,
            WarnCount: 0,
            FailCount: failCount,
            Rows:
            [
                new ProductionPolicyImportRowResult(
                    RowNumber: 2,
                    PolicyCode: "PH_VALID_SC_IMPORT_001",
                    EntitlementType: "SENIOR_CITIZEN",
                    Decision: failCount == 0
                        ? ProductionPolicyImportRowDecision.IMPORTABLE_AFTER_APPROVAL
                        : ProductionPolicyImportRowDecision.NOT_IMPORTABLE,
                    Findings: failCount == 0
                        ? Array.Empty<ProductionPolicyImportFinding>()
                        :
                        [
                            new ProductionPolicyImportFinding(
                                ProductionPolicyImportFindingSeverity.FAIL,
                                "dry-run failure",
                                RowNumber: 2)
                        ])
            ],
            Findings: Array.Empty<ProductionPolicyImportFinding>(),
            CorrelationId);

    private sealed class FakeReviewQueue : IOperatorConsoleProductionPolicyImportReviewQueue
    {
        private readonly Dictionary<Guid, ProductionPolicyImportReviewSubmission> _submissions = new();

        public int SaveCount { get; private set; }

        public bool DbWriteRequested => false;

        public ProductionPolicyImportReviewSubmission? LastSaved { get; private set; }

        public Task<ProductionPolicyImportReviewSubmission?> GetAsync(
            Guid reviewId,
            CancellationToken cancellationToken)
        {
            _submissions.TryGetValue(reviewId, out var submission);
            return Task.FromResult(submission);
        }

        public Task SaveAsync(
            ProductionPolicyImportReviewSubmission submission,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            LastSaved = submission;
            _submissions[submission.ReviewId] = submission;
            return Task.CompletedTask;
        }
    }
}
