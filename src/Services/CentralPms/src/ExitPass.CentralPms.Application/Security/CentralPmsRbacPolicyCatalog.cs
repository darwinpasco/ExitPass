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
}
