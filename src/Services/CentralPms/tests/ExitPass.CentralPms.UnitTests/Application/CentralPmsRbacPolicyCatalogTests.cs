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
}
