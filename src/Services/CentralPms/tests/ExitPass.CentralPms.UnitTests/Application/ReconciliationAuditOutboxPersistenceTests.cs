using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Static contract checks for reconciliation audit/domain/outbox persistence.
/// </summary>
public sealed class ReconciliationAuditOutboxPersistenceTests
{
    /// <summary>
    /// Verifies reconciliation event evidence uses the live v1.2 audit/events tables.
    /// </summary>
    [Fact]
    public void ReconciliationEventPersistence_UsesAuditDomainAndOutboxTables()
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "Reconciliation",
            "ReconciliationEventPersistence.cs");

        source.Should().Contain("INSERT INTO events.domain_events");
        source.Should().Contain("INSERT INTO events.outbox_events");
        source.Should().Contain("INSERT INTO audit.audit_events");
        source.Should().Contain("'RECONCILIATION'");
        source.Should().Contain("correlation_id");
        source.Should().Contain("central-pms.reconciliation");
        Assert.DoesNotContain("AUB", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies reconciliation write repositories call the shared event persistence helper.
    /// </summary>
    [Theory]
    [InlineData("MopsTransactionRepository.cs", "MopsTransactionImported")]
    [InlineData("ReconciliationRunItemRepository.cs", "ReconciliationRunCreated")]
    [InlineData("ReconciliationEvaluationRepository.cs", "ReconciliationItemEvaluated")]
    [InlineData("ReconciliationWorkflowRepository.cs", "ReconciliationNoteAdded")]
    [InlineData("ReconciliationWorkflowRepository.cs", "ReconciliationResolutionRequestSubmitted")]
    [InlineData("ReconciliationWorkflowRepository.cs", "ReconciliationResolutionDecisionRecorded")]
    [InlineData("ReconciliationExceptionLifecycleRepository.cs", "ReconciliationExceptionLifecycleChanged")]
    public void ReconciliationRepositories_PersistExpectedAuditOutboxEvents(string repositoryFile, string eventName)
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "Reconciliation",
            repositoryFile);

        source.Should().Contain("ReconciliationEventPersistence.PersistAsync");
        source.Should().Contain(eventName);
        Assert.DoesNotContain("INSERT INTO core.payment_attempts", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.payment_confirmations", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.exit_authorizations", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE payments.provider_outcomes", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AUB", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the reconciliation outbox dispatcher uses durable publication tables and row-locking discipline.
    /// </summary>
    [Fact]
    public void ReconciliationOutboxDispatcherRepository_UsesDurableOutboxPublicationState()
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "Eventing",
            "ReconciliationOutboxDispatcherRepository.cs");

        source.Should().Contain("events.outbox_events");
        source.Should().Contain("events.event_publications");
        source.Should().Contain("events.dead_letter_records");
        source.Should().Contain("FOR UPDATE SKIP LOCKED");
        source.Should().Contain("'LOCKED'");
        source.Should().Contain("'PUBLISHED'");
        source.Should().Contain("'RETRY_PENDING'");
        source.Should().Contain("DEAD_LETTERED");
        source.Should().Contain("@broker_type::events.event_broker_type_enum");
        source.Should().Contain("source_schema = 'reconciliation'");
        Assert.DoesNotContain("INSERT INTO core.payment_attempts", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.payment_confirmations", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.exit_authorizations", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE payments.provider_outcomes", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AUB", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidateParts = new[] { current.FullName }.Concat(pathParts).ToArray();
            var candidate = Path.Combine(candidateParts);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }
}
