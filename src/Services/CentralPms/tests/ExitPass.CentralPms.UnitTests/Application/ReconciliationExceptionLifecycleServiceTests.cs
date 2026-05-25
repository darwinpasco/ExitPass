using ExitPass.CentralPms.Application.Reconciliation;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests reconciliation exception lifecycle application rules.
/// </summary>
public sealed class ReconciliationExceptionLifecycleServiceTests
{
    private static readonly Guid ExceptionId = Guid.Parse("88888888-8888-8888-8888-888888888881");
    private static readonly Guid RunId = Guid.Parse("88888888-8888-8888-8888-888888888882");
    private static readonly Guid UserId = Guid.Parse("88888888-8888-8888-8888-888888888883");
    private static readonly Guid CorrelationId = Guid.Parse("88888888-8888-8888-8888-888888888884");

    /// <summary>
    /// Verifies exception detail reads delegate through the lifecycle repository.
    /// </summary>
    [Fact]
    public async Task Read_WhenValid_DelegatesToRepository()
    {
        var repository = Substitute.For<IReconciliationExceptionLifecycleRepository>();
        repository.ReadAsync(Arg.Any<ReadReconciliationExceptionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ExceptionRecord("OPEN"));
        var sut = new ReconciliationExceptionLifecycleService(repository);

        var result = await sut.ReadAsync(new ReadReconciliationExceptionQuery(ExceptionId), CancellationToken.None);

        result.ReconciliationExceptionId.Should().Be(ExceptionId);
        await repository.Received(1).ReadAsync(
            Arg.Is<ReadReconciliationExceptionQuery>(query => query.ReconciliationExceptionId == ExceptionId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies assignment records reviewer intent and transitions OPEN to ASSIGNED.
    /// </summary>
    [Fact]
    public async Task Assign_WhenOpen_TransitionsToAssigned()
    {
        var repository = RepositoryWithCurrentStatus("OPEN");
        var sut = new ReconciliationExceptionLifecycleService(repository);

        await sut.AssignAsync(
            new AssignReconciliationExceptionCommand(ExceptionId, UserId, null, "ASSIGN", null, UserId, null, CorrelationId),
            CancellationToken.None);

        await repository.Received(1).AssignAsync(
            Arg.Is<AssignReconciliationExceptionCommand>(command => command.ReasonCode == "ASSIGN"),
            "ASSIGNED",
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies lifecycle status update validates actual enum-backed values.
    /// </summary>
    [Fact]
    public async Task UpdateStatus_WhenStatusUnsupported_Throws()
    {
        var sut = new ReconciliationExceptionLifecycleService(RepositoryWithCurrentStatus("OPEN"));

        var act = () => sut.UpdateStatusAsync(
            Command("NOT_A_STATUS", "STATUS_UPDATE"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*NewStatus must be one of*");
    }

    /// <summary>
    /// Verifies resolve/reject/escalate/close transitions are delegated only when conservative transition rules allow them.
    /// </summary>
    [Theory]
    [InlineData("UNDER_REVIEW", "RESOLVED", "RESOLVE")]
    [InlineData("UNDER_REVIEW", "REJECTED", "REJECT")]
    [InlineData("ASSIGNED", "ESCALATED", "ESCALATE")]
    [InlineData("RESOLVED", "CLOSED", "CLOSE")]
    public async Task UpdateStatus_WhenTransitionAllowed_Delegates(string currentStatus, string newStatus, string action)
    {
        var repository = RepositoryWithCurrentStatus(currentStatus);
        var sut = new ReconciliationExceptionLifecycleService(repository);

        await sut.UpdateStatusAsync(Command(newStatus, action), CancellationToken.None);

        await repository.Received(1).UpdateStatusAsync(
            Arg.Is<UpdateReconciliationExceptionStatusCommand>(command =>
                command.NewStatus == newStatus &&
                command.Action == action &&
                command.ReasonCode == "REASON"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies invalid lifecycle transitions are rejected deterministically.
    /// </summary>
    [Fact]
    public async Task UpdateStatus_WhenTransitionInvalid_ThrowsConflict()
    {
        var sut = new ReconciliationExceptionLifecycleService(RepositoryWithCurrentStatus("OPEN"));

        var act = () => sut.UpdateStatusAsync(Command("CLOSED", "CLOSE"), CancellationToken.None);

        await act.Should().ThrowAsync<ReconciliationWorkflowConflictException>()
            .Where(ex => ex.ErrorCode == "RECONCILIATION_EXCEPTION_INVALID_TRANSITION");
    }

    /// <summary>
    /// Verifies terminal exceptions cannot be casually mutated.
    /// </summary>
    [Theory]
    [InlineData("CLOSED")]
    [InlineData("CANCELLED")]
    public async Task UpdateStatus_WhenTerminal_ThrowsConflict(string currentStatus)
    {
        var sut = new ReconciliationExceptionLifecycleService(RepositoryWithCurrentStatus(currentStatus));

        var act = () => sut.UpdateStatusAsync(Command("UNDER_REVIEW", "STATUS_UPDATE"), CancellationToken.None);

        await act.Should().ThrowAsync<ReconciliationWorkflowConflictException>()
            .Where(ex => ex.ErrorCode == "RECONCILIATION_EXCEPTION_TERMINAL");
    }

    /// <summary>
    /// Verifies lifecycle SQL avoids payment, provider, exit, gate, settlement, and payout mutation paths.
    /// </summary>
    [Fact]
    public void RepositorySql_DoesNotMutatePaymentOrFinancialTruth()
    {
        var repository = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "Reconciliation",
            "ReconciliationExceptionLifecycleRepository.cs");

        Assert.Contains("UPDATE reconciliation.reconciliation_exceptions", repository, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INSERT INTO reconciliation.reconciliation_exception_status_history", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE core.payment_attempts", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE core.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.exit_authorizations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE payments.provider_outcomes", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE gates.gate_authorization_consumptions", repository, StringComparison.OrdinalIgnoreCase);
    }

    private static IReconciliationExceptionLifecycleRepository RepositoryWithCurrentStatus(string status)
    {
        var repository = Substitute.For<IReconciliationExceptionLifecycleRepository>();
        repository.ReadAsync(Arg.Any<ReadReconciliationExceptionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ExceptionRecord(status));
        repository.AssignAsync(Arg.Any<AssignReconciliationExceptionCommand>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => LifecycleResult(status, call.ArgAt<string>(1), "ASSIGN"));
        repository.UpdateStatusAsync(Arg.Any<UpdateReconciliationExceptionStatusCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.ArgAt<UpdateReconciliationExceptionStatusCommand>(0);
                return LifecycleResult(status, command.NewStatus, command.Action);
            });
        return repository;
    }

    private static UpdateReconciliationExceptionStatusCommand Command(string status, string action) =>
        new(ExceptionId, status, action, "reason", "detail", UserId, null, CorrelationId);

    private static ReconciliationExceptionLifecycleResult LifecycleResult(string previous, string current, string action) =>
        new(ExceptionId, previous, current, action, DateTimeOffset.UtcNow, CorrelationId);

    private static ReconciliationExceptionDetailRecord ExceptionRecord(string status) =>
        new(
            ExceptionId,
            RunId,
            null,
            null,
            "POLICY_EXCEPTION",
            "LOW",
            status,
            "DEV_TEST",
            "Exception summary",
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            CorrelationId);

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
