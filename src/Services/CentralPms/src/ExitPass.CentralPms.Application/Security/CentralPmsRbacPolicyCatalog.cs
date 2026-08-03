namespace ExitPass.CentralPms.Application.Security;

/// <summary>
/// Central PMS policy-to-permission mapping for operational RBAC.
/// </summary>
public static class CentralPmsRbacPolicyCatalog
{
    /// <summary>
    /// Claim/header type used for permission grants.
    /// </summary>
    public const string PermissionClaimType = "exitpass_permission";

    /// <summary>
    /// Header carrying comma-separated test/operator permissions.
    /// </summary>
    public const string PermissionsHeaderName = "X-ExitPass-Permissions";

    /// <summary>
    /// Header carrying an operator user id.
    /// </summary>
    public const string UserIdHeaderName = "X-ExitPass-User-Id";

    /// <summary>
    /// Header carrying an internal service identity id.
    /// </summary>
    public const string ServiceIdentityIdHeaderName = "X-ExitPass-Service-Identity-Id";

    private static readonly IReadOnlyDictionary<string, string[]> PolicyPermissions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ReconciliationViewer"] = ["reconciliation.view", "reconciliation.manage"],
            ["ReconciliationReviewer"] = ["reconciliation.review", "reconciliation.manage"],
            ["ReconciliationApprover"] = ["reconciliation.approve", "reconciliation.manage"],
            ["ReconciliationOperator"] = ["reconciliation.operate", "reconciliation.manage"],
            ["MopsTransactionImporter"] = ["mops.import", "reconciliation.manage"],
            ["MopsTransactionViewer"] = ["mops.view", "reconciliation.view", "reconciliation.manage"],
            ["EventOutboxDispatcher"] = ["event.outbox.dispatch", "event.manage"],
            ["EventRecoveryViewer"] = ["event.recovery.view", "event.manage"],
            ["EventDeadLetterReplayer"] = ["event.dead-letter.replay", "event.manage"],
            ["EventCheckpointViewer"] = ["event.checkpoint.view", "event.manage"],
            ["EventCheckpointOperator"] = ["event.checkpoint.operate", "event.manage"],

            ["ReconciliationExceptionViewer"] = ["reconciliation.view", "reconciliation.manage"],
            ["ReconciliationExceptionAssignment"] = ["reconciliation.review", "reconciliation.operate", "reconciliation.manage"],
            ["ReconciliationExceptionStatusUpdate"] = ["reconciliation.review", "reconciliation.operate", "reconciliation.manage"],
            ["ReconciliationExceptionResolution"] = ["reconciliation.review", "reconciliation.operate", "reconciliation.manage"],
            ["ReconciliationExceptionRejection"] = ["reconciliation.approve", "reconciliation.manage"],
            ["ReconciliationExceptionEscalation"] = ["reconciliation.operate", "reconciliation.manage"],
            ["ReconciliationExceptionClosure"] = ["reconciliation.approve", "reconciliation.manage"],
            ["ReconciliationRunCreator"] = ["reconciliation.operate", "reconciliation.manage"],
            ["ReconciliationRunViewer"] = ["reconciliation.view", "reconciliation.manage"],
            ["ReconciliationItemViewer"] = ["reconciliation.view", "reconciliation.manage"],
            ["ReconciliationEvaluator"] = ["reconciliation.evaluate", "reconciliation.manage"],
            ["ReconciliationRunEvaluator"] = ["reconciliation.evaluate", "reconciliation.manage"],
            ["VendorPaymentAcknowledgmentViewer"] = ["reconciliation.view", "reconciliation.manage"],
            ["FiscalIssuanceStatusRead"] =
            [
                "fiscal-issuance.status.read",
                "reconciliation.view",
                "reconciliation.manage"
            ],
            ["FiscalIssuanceVoidCommand"] =
            [
                "fiscal-issuance.void.command",
                "reconciliation.manage"
            ],
            ["FiscalVoidActionAuditReview"] =
            [
                "fiscal-issuance.void.audit.read",
                "reconciliation.manage"
            ],
            ["VendorSessionProjectionHealthViewer"] =
            [
                "ops.vendor-session-projection-health.view",
                "operator-console.vendor-projection-health.view",
                "reconciliation.view",
                "reconciliation.manage"
            ],
            ["ManagementPlatformIdentityRbacInventoryRead"] =
            [
                "management-platform.identity-rbac.inventory.read"
            ],
            ["ManagementPlatformStatutoryDiscountPolicyCoverageRead"] =
            [
                "statutory-discount-policy.view"
            ],
            ["SalesInvoiceProfileRead"] =
            [
                "sales-invoice-profile.read"
            ],
            ["SalesInvoiceProfileManage"] =
            [
                "sales-invoice-profile.manage"
            ],
            ["SalesInvoiceProfileApprove"] =
            [
                "sales-invoice-profile.approve"
            ],
            ["TerminalCashPayableBasisRead"] =
            [
                "terminal-cash.payable-basis.read"
            ],
            ["AptStatutoryOrdinanceAvailabilityRead"] =
            [
                "statutory-discounts.ordinance-availability.read.apt"
            ],

            ["OperatorConsoleStatutoryDiscountSessionLookup"] =
            [
                "statutory-discounts.session.lookup",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountDraftView"] =
            [
                "statutory-discounts.draft.view",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountDraftCreate"] =
            [
                "statutory-discounts.draft.create",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountEvidenceView"] =
            [
                "statutory-discounts.evidence.view",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountEvidenceCapture"] =
            [
                "statutory-discounts.evidence.capture",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountDecisionReview"] =
            [
                "statutory-discounts.decision.review",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountReviewQueueRead"] =
            [
                "statutory-discounts.review.queue.read",
                "statutory-discounts.decision.review",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountReviewDetailRead"] =
            [
                "statutory-discounts.review.detail.read",
                "statutory-discounts.decision.review",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountDecisionMutate"] =
            [
                "statutory-discounts.decision.approve",
                "statutory-discounts.decision.reject",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountDecisionApprove"] =
            [
                "statutory-discounts.decision.approve",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountDecisionReject"] =
            [
                "statutory-discounts.decision.reject",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountPayableBasisApply"] =
            [
                "statutory-discounts.payable-basis.apply",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountPolicyResolve"] =
            [
                "statutory-discounts.policy.resolve",
                "reconciliation.manage"
            ],
            ["OperatorConsoleStatutoryDiscountAuditRead"] =
            [
                "statutory-discounts.audit.read",
                "reconciliation.manage"
            ],
            ["CentralPmsStatutoryDiscountDecisionSubmit"] =
            [
                "statutory-discounts.decision.submit.operator-console",
                "statutory-discounts.decision.submit.webpay",
                "statutory-discounts.decision.submit.assisted-payment-terminal",
                "reconciliation.manage"
            ],
            ["CentralPmsStatutoryDiscountDecisionRead"] =
            [
                "statutory-discounts.decision.read",
                "statutory-discounts.draft.view",
                "reconciliation.manage"
            ],
            ["WebPayStatutoryDiscountPendingLifecycleRediscover"] =
            [
                "statutory-discounts.pending-lifecycle.rediscover.webpay"
            ],

            ["OperatorConsolePolicyImportReviewSubmit"] = ["operator-console.policy-import-review.submit", "operator-console.policy-import-review.manage"],
            ["OperatorConsolePolicyImportReviewViewer"] =
            [
                "operator-console.policy-import-review.view-own",
                "operator-console.policy-import-review.review",
                "operator-console.policy-import-review.manage",
                "operator-console.policy-import-review.approve.legal",
                "operator-console.policy-import-review.approve.ops",
                "operator-console.policy-import-review.approve.qa",
                "operator-console.policy-import-review.approve.db"
            ],
            ["OperatorConsolePolicyImportReviewDecision"] =
            [
                "operator-console.policy-import-review.review",
                "operator-console.policy-import-review.manage",
                "operator-console.policy-import-review.approve.legal",
                "operator-console.policy-import-review.approve.ops",
                "operator-console.policy-import-review.approve.qa",
                "operator-console.policy-import-review.approve.db"
            ]
        };

    /// <summary>
    /// Resolves the permissions that can satisfy a policy.
    /// </summary>
    public static IReadOnlyList<string> ResolvePermissions(string policyName) =>
        PolicyPermissions.TryGetValue(policyName, out var permissions)
            ? permissions
            : [policyName];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ListPolicyMappings() =>
        PolicyPermissions.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
}
