using ExitPass.CentralPms.Application.Security;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for Central PMS RBAC policy mapping.
/// </summary>
public sealed class CentralPmsRbacPolicyCatalogTests
{
    [Theory]
    [InlineData("ReconciliationViewer", "reconciliation.view")]
    [InlineData("ReconciliationReviewer", "reconciliation.review")]
    [InlineData("ReconciliationApprover", "reconciliation.approve")]
    [InlineData("MopsTransactionImporter", "mops.import")]
    [InlineData("EventOutboxDispatcher", "event.outbox.dispatch")]
    [InlineData("EventRecoveryViewer", "event.recovery.view")]
    [InlineData("EventDeadLetterReplayer", "event.dead-letter.replay")]
    [InlineData("EventCheckpointViewer", "event.checkpoint.view")]
    [InlineData("EventCheckpointOperator", "event.checkpoint.operate")]
    [InlineData("VendorPaymentAcknowledgmentViewer", "reconciliation.view")]
    [InlineData("FiscalIssuanceStatusRead", "fiscal-issuance.status.read")]
    [InlineData("FiscalIssuanceVoidCommand", "fiscal-issuance.void.command")]
    [InlineData("FiscalVoidActionAuditReview", "fiscal-issuance.void.audit.read")]
    [InlineData("ManagementPlatformIdentityRbacInventoryRead", "management-platform.identity-rbac.inventory.read")]
    [InlineData("OperatorConsoleStatutoryDiscountSessionLookup", "statutory-discounts.session.lookup")]
    [InlineData("OperatorConsoleStatutoryDiscountDraftView", "statutory-discounts.draft.view")]
    [InlineData("OperatorConsoleStatutoryDiscountDraftCreate", "statutory-discounts.draft.create")]
    [InlineData("OperatorConsoleStatutoryDiscountEvidenceView", "statutory-discounts.evidence.view")]
    [InlineData("OperatorConsoleStatutoryDiscountEvidenceCapture", "statutory-discounts.evidence.capture")]
    [InlineData("OperatorConsoleStatutoryDiscountDecisionReview", "statutory-discounts.decision.review")]
    [InlineData("OperatorConsoleStatutoryDiscountReviewQueueRead", "statutory-discounts.review.queue.read")]
    [InlineData("OperatorConsoleStatutoryDiscountReviewDetailRead", "statutory-discounts.review.detail.read")]
    [InlineData("OperatorConsoleStatutoryDiscountDecisionMutate", "statutory-discounts.decision.approve")]
    [InlineData("OperatorConsoleStatutoryDiscountDecisionMutate", "statutory-discounts.decision.reject")]
    [InlineData("OperatorConsoleStatutoryDiscountDecisionApprove", "statutory-discounts.decision.approve")]
    [InlineData("OperatorConsoleStatutoryDiscountDecisionReject", "statutory-discounts.decision.reject")]
    [InlineData("OperatorConsoleStatutoryDiscountPayableBasisApply", "statutory-discounts.payable-basis.apply")]
    [InlineData("OperatorConsoleStatutoryDiscountPolicyResolve", "statutory-discounts.policy.resolve")]
    [InlineData("OperatorConsoleStatutoryDiscountAuditRead", "statutory-discounts.audit.read")]
    public void ResolvePermissions_ReturnsExpectedPermission(string policyName, string expectedPermission)
    {
        var permissions = CentralPmsRbacPolicyCatalog.ResolvePermissions(policyName);

        permissions.Should().Contain(expectedPermission);
    }

    [Fact]
    public void ResolvePermissions_AllowsReconciliationManageAsBreakGlassForReconciliationPolicies()
    {
        var permissions = CentralPmsRbacPolicyCatalog.ResolvePermissions("ReconciliationApprover");

        permissions.Should().Contain("reconciliation.manage");
    }

    [Fact]
    public void ResolvePermissions_KeepsStatutoryReviewReadSeparateFromApproveAndReject()
    {
        var reviewPermissions = CentralPmsRbacPolicyCatalog.ResolvePermissions("OperatorConsoleStatutoryDiscountDecisionReview");

        reviewPermissions.Should().Contain("statutory-discounts.decision.review");
        reviewPermissions.Should().NotContain("statutory-discounts.decision.approve");
        reviewPermissions.Should().NotContain("statutory-discounts.decision.reject");
    }

    [Fact]
    public void ResolvePermissions_KeepsStatutoryDiscountRuntimeSeparateFromPolicyImportReview()
    {
        var runtimePermissions = new[]
        {
            "OperatorConsoleStatutoryDiscountSessionLookup",
            "OperatorConsoleStatutoryDiscountDraftView",
            "OperatorConsoleStatutoryDiscountDraftCreate",
            "OperatorConsoleStatutoryDiscountEvidenceView",
            "OperatorConsoleStatutoryDiscountEvidenceCapture",
            "OperatorConsoleStatutoryDiscountDecisionReview",
            "OperatorConsoleStatutoryDiscountReviewQueueRead",
            "OperatorConsoleStatutoryDiscountReviewDetailRead",
            "OperatorConsoleStatutoryDiscountDecisionMutate",
            "OperatorConsoleStatutoryDiscountDecisionApprove",
            "OperatorConsoleStatutoryDiscountDecisionReject",
            "OperatorConsoleStatutoryDiscountPayableBasisApply",
            "OperatorConsoleStatutoryDiscountPolicyResolve",
            "OperatorConsoleStatutoryDiscountAuditRead"
        }
            .SelectMany(CentralPmsRbacPolicyCatalog.ResolvePermissions)
            .ToArray();

        runtimePermissions.Should().NotContain(permission => permission.StartsWith("operator-console.policy-import-review.", StringComparison.Ordinal));

        var policyImportPermissions = new[]
        {
            "OperatorConsolePolicyImportReviewSubmit",
            "OperatorConsolePolicyImportReviewViewer",
            "OperatorConsolePolicyImportReviewDecision"
        }
            .SelectMany(CentralPmsRbacPolicyCatalog.ResolvePermissions)
            .ToArray();

        policyImportPermissions.Should().NotContain(permission => permission.StartsWith("statutory-discounts.", StringComparison.Ordinal));
    }

    [Fact]
    public void ListPolicyMappings_ReturnsInventoryPolicyMapping()
    {
        var mappings = CentralPmsRbacPolicyCatalog.ListPolicyMappings();

        mappings.Should().ContainKey("ManagementPlatformIdentityRbacInventoryRead");
        mappings["ManagementPlatformIdentityRbacInventoryRead"]
            .Should()
            .Contain("management-platform.identity-rbac.inventory.read");
    }
}
