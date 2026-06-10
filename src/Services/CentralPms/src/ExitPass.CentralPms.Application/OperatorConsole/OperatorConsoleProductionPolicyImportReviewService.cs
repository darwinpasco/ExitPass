using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExitPass.CentralPms.Application.OperatorConsole;

public sealed class OperatorConsoleProductionPolicyImportReviewService : IOperatorConsoleProductionPolicyImportReviewService
{
    private static readonly JsonSerializerOptions FingerprintJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly ProductionPolicyImportReviewerRole[] RequiredReviewerRoles =
    [
        ProductionPolicyImportReviewerRole.LEGAL,
        ProductionPolicyImportReviewerRole.OPS,
        ProductionPolicyImportReviewerRole.QA,
        ProductionPolicyImportReviewerRole.DB
    ];

    private static readonly ProductionPolicyImportReviewSubmissionStatus[] TerminalStatuses =
    [
        ProductionPolicyImportReviewSubmissionStatus.REJECTED,
        ProductionPolicyImportReviewSubmissionStatus.CANCELLED,
        ProductionPolicyImportReviewSubmissionStatus.SUPERSEDED
    ];

    private readonly IOperatorConsoleProductionPolicyImportReviewQueue _queue;

    public OperatorConsoleProductionPolicyImportReviewService(IOperatorConsoleProductionPolicyImportReviewQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public async Task<ProductionPolicyImportReviewSubmitResult> SubmitForReviewAsync(
        ProductionPolicyImportReviewSubmitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DryRunResult);
        ValidateGuid(request.MakerOperatorId, nameof(request.MakerOperatorId));

        if (!request.DryRunResult.IsDryRun || request.DryRunResult.PoliciesImported)
        {
            throw new ArgumentException("Only dry-run results with no imported policies can be submitted for review.", nameof(request.DryRunResult));
        }

        var now = DateTimeOffset.UtcNow;
        var correlationId = request.CorrelationId ?? request.DryRunResult.CorrelationId ?? Guid.NewGuid();
        var submissionFingerprint = ComputeSubmissionFingerprint(
            request.MakerOperatorId,
            request.FileName,
            request.DryRunResult);

        var existing = await _queue.FindActiveByFingerprintAsync(
            request.MakerOperatorId,
            submissionFingerprint,
            cancellationToken);

        if (existing is not null)
        {
            return new ProductionPolicyImportReviewSubmitResult(
                existing,
                PoliciesImported: false,
                "Active review submission already exists. No policies were imported.",
                existing.Findings);
        }

        var status = request.DryRunResult.FailCount > 0
            ? ProductionPolicyImportReviewSubmissionStatus.SUBMITTED_FOR_REVIEW
            : ProductionPolicyImportReviewSubmissionStatus.LEGAL_REVIEW_PENDING;
        var history = new[]
        {
            new ProductionPolicyImportReviewHistoryEntry(
                ProductionPolicyImportReviewDecisionAction.SUBMIT_FOR_REVIEW,
                status,
                request.MakerOperatorId,
                null,
                null,
                now,
                correlationId)
        };
        var findings = request.DryRunResult.FailCount > 0
            ? new[]
            {
                new ProductionPolicyImportReviewFinding(
                    ProductionPolicyImportFindingSeverity.WARN,
                    "Dry-run FAIL findings must be resolved before DB repo alignment approval.")
            }
            : Array.Empty<ProductionPolicyImportReviewFinding>();

        var submission = new ProductionPolicyImportReviewSubmission(
            Guid.NewGuid(),
            request.MakerOperatorId,
            request.FileName,
            submissionFingerprint,
            status,
            request.DryRunResult,
            Array.Empty<ProductionPolicyImportReviewDecision>(),
            history,
            findings,
            now,
            now,
            correlationId);

        await _queue.SaveAsync(submission, cancellationToken);

        return new ProductionPolicyImportReviewSubmitResult(
            submission,
            PoliciesImported: false,
            "Review submission created. No policies were imported.",
            findings);
    }

    public async Task<ProductionPolicyImportReviewDecisionResult> DecideAsync(
        ProductionPolicyImportReviewDecisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateGuid(request.ReviewId, nameof(request.ReviewId));
        ValidateGuid(request.ReviewerOperatorId, nameof(request.ReviewerOperatorId));

        var submission = await _queue.GetAsync(request.ReviewId, cancellationToken)
            ?? throw new ArgumentException("Review submission was not found.", nameof(request.ReviewId));

        if (TerminalStatuses.Contains(submission.Status))
        {
            throw new InvalidOperationException($"Review submission is terminal with status {submission.Status}.");
        }

        if ((request.Action is ProductionPolicyImportReviewDecisionAction.REJECT or ProductionPolicyImportReviewDecisionAction.REQUEST_CHANGES or ProductionPolicyImportReviewDecisionAction.ESCALATE) &&
            string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("A reason is required for rejection, requested changes, or escalation.", nameof(request.Reason));
        }

        var now = DateTimeOffset.UtcNow;
        var correlationId = request.CorrelationId ?? submission.CorrelationId;
        var reviewerDecisions = submission.ReviewerDecisions.ToList();
        var history = submission.History.ToList();
        var nextStatus = submission.Status;
        ProductionPolicyImportReviewerRole? reviewerRole = null;

        if (TryResolveReviewerRole(request.Action, out var role))
        {
            reviewerRole = role;

            if (request.ReviewerOperatorId == submission.MakerOperatorId)
            {
                throw new InvalidOperationException("Maker cannot approve their own production policy import review submission.");
            }

            if (submission.DryRunResult.FailCount > 0)
            {
                throw new InvalidOperationException("Dry-run FAIL findings block DB repo alignment approval.");
            }

            if (reviewerDecisions.Any(decision => decision.ReviewerRole == role))
            {
                throw new InvalidOperationException($"{role} review approval has already been recorded.");
            }

            reviewerDecisions.Add(new ProductionPolicyImportReviewDecision(
                role,
                request.Action,
                request.ReviewerOperatorId,
                request.Reason,
                now,
                correlationId));

            nextStatus = EvaluateStatus(reviewerDecisions);
        }
        else
        {
            nextStatus = request.Action switch
            {
                ProductionPolicyImportReviewDecisionAction.REQUEST_CHANGES => ProductionPolicyImportReviewSubmissionStatus.DRAFT_DRY_RUN,
                ProductionPolicyImportReviewDecisionAction.REJECT => ProductionPolicyImportReviewSubmissionStatus.REJECTED,
                ProductionPolicyImportReviewDecisionAction.ESCALATE => ProductionPolicyImportReviewSubmissionStatus.SUBMITTED_FOR_REVIEW,
                ProductionPolicyImportReviewDecisionAction.CANCEL => ProductionPolicyImportReviewSubmissionStatus.CANCELLED,
                ProductionPolicyImportReviewDecisionAction.MARK_SUPERSEDED => ProductionPolicyImportReviewSubmissionStatus.SUPERSEDED,
                ProductionPolicyImportReviewDecisionAction.SUBMIT_FOR_REVIEW => submission.DryRunResult.FailCount > 0
                    ? ProductionPolicyImportReviewSubmissionStatus.SUBMITTED_FOR_REVIEW
                    : EvaluateStatus(reviewerDecisions),
                _ => throw new ArgumentException("Unsupported production policy import review decision.", nameof(request.Action))
            };
        }

        history.Add(new ProductionPolicyImportReviewHistoryEntry(
            request.Action,
            nextStatus,
            request.ReviewerOperatorId,
            reviewerRole,
            request.Reason,
            now,
            correlationId));

        var updated = submission with
        {
            Status = nextStatus,
            ReviewerDecisions = reviewerDecisions,
            History = history,
            UpdatedAt = now
        };

        await _queue.SaveAsync(updated, cancellationToken);

        return new ProductionPolicyImportReviewDecisionResult(
            updated,
            PoliciesImported: false,
            "Review decision recorded. No policies were imported or activated.",
            Array.Empty<ProductionPolicyImportReviewFinding>());
    }

    private static string ComputeSubmissionFingerprint(
        Guid makerOperatorId,
        string? fileName,
        ProductionPolicyImportDryRunResult dryRunResult)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                MakerOperatorId = makerOperatorId,
                FileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim(),
                DryRunResult = dryRunResult
            },
            FingerprintJsonOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ProductionPolicyImportReviewSubmissionStatus EvaluateStatus(
        IReadOnlyCollection<ProductionPolicyImportReviewDecision> reviewerDecisions)
    {
        foreach (var role in RequiredReviewerRoles)
        {
            if (reviewerDecisions.All(decision => decision.ReviewerRole != role))
            {
                return role switch
                {
                    ProductionPolicyImportReviewerRole.LEGAL => ProductionPolicyImportReviewSubmissionStatus.LEGAL_REVIEW_PENDING,
                    ProductionPolicyImportReviewerRole.OPS => ProductionPolicyImportReviewSubmissionStatus.OPS_REVIEW_PENDING,
                    ProductionPolicyImportReviewerRole.QA => ProductionPolicyImportReviewSubmissionStatus.QA_REVIEW_PENDING,
                    ProductionPolicyImportReviewerRole.DB => ProductionPolicyImportReviewSubmissionStatus.DB_REVIEW_PENDING,
                    _ => ProductionPolicyImportReviewSubmissionStatus.SUBMITTED_FOR_REVIEW
                };
            }
        }

        return ProductionPolicyImportReviewSubmissionStatus.APPROVED_FOR_DB_REPO_ALIGNMENT;
    }

    private static bool TryResolveReviewerRole(
        ProductionPolicyImportReviewDecisionAction action,
        out ProductionPolicyImportReviewerRole role)
    {
        switch (action)
        {
            case ProductionPolicyImportReviewDecisionAction.APPROVE_LEGAL:
                role = ProductionPolicyImportReviewerRole.LEGAL;
                return true;
            case ProductionPolicyImportReviewDecisionAction.APPROVE_OPS:
                role = ProductionPolicyImportReviewerRole.OPS;
                return true;
            case ProductionPolicyImportReviewDecisionAction.APPROVE_QA:
                role = ProductionPolicyImportReviewerRole.QA;
                return true;
            case ProductionPolicyImportReviewDecisionAction.APPROVE_DB:
                role = ProductionPolicyImportReviewerRole.DB;
                return true;
            default:
                role = default;
                return false;
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
