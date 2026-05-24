using ExitPass.CentralPms.Application.Reconciliation;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests reconciliation workflow application rules.
/// </summary>
public sealed class ReconciliationWorkflowServiceTests
{
    private static readonly Guid ItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Verifies BRD v1.2 9.21 and SDD v1.2 10 API Architecture by adding notes through the workflow boundary only.
    /// </summary>
    [Fact]
    public async Task AddNote_WhenValid_DelegatesToWorkflowRepository()
    {
        var repository = Substitute.For<IReconciliationWorkflowRepository>();
        var expected = new ReconciliationNoteResult(
            ItemId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "REVIEW_NOTE",
            DateTimeOffset.UtcNow,
            CorrelationId);
        repository.AddNoteAsync(Arg.Any<AddReconciliationNoteCommand>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var sut = new ReconciliationWorkflowService(repository);

        var result = await sut.AddNoteAsync(
            new AddReconciliationNoteCommand(ItemId, "review note", "review_note", null, CorrelationId),
            CancellationToken.None);

        result.Should().Be(expected);
        await repository.Received(1).AddNoteAsync(
            Arg.Is<AddReconciliationNoteCommand>(command =>
                command.ReconciliationItemId == ItemId &&
                command.NoteText == "review note" &&
                command.NoteType == "REVIEW_NOTE" &&
                command.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies financial-impact resolution requests are validated before persistence.
    /// </summary>
    [Fact]
    public async Task SubmitResolutionRequest_WhenAdjustmentRequiredWithoutFinancialImpact_Throws()
    {
        var sut = new ReconciliationWorkflowService(Substitute.For<IReconciliationWorkflowRepository>());

        var act = () => sut.SubmitResolutionRequestAsync(
            new SubmitReconciliationResolutionCommand(
                ItemId,
                "REQUEST_FINANCIAL_ADJUSTMENT",
                "reason",
                "NONE",
                AdjustmentRequired: true,
                "summary",
                "detail",
                "RESOLVED",
                null,
                CorrelationId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*POSSIBLE or DEFINITE*");
    }

    /// <summary>
    /// Verifies approval and rejection decisions are restricted to schema-supported values.
    /// </summary>
    [Fact]
    public async Task DecideResolutionRequest_WhenDecisionUnsupported_Throws()
    {
        var sut = new ReconciliationWorkflowService(Substitute.For<IReconciliationWorkflowRepository>());

        var act = () => sut.DecideResolutionRequestAsync(
            new DecideReconciliationResolutionCommand(
                Guid.NewGuid(),
                "MAYBE",
                "reason",
                "comment",
                null,
                CorrelationId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*APPROVED or REJECTED*");
    }

    /// <summary>
    /// Verifies query handlers delegate readback/listing without payment-truth mutation paths.
    /// </summary>
    [Fact]
    public async Task ReadQueries_WhenValid_DelegateToRepository()
    {
        var repository = Substitute.For<IReconciliationWorkflowRepository>();
        repository.ReadWorkflowHistoryAsync(Arg.Any<ReadReconciliationWorkflowHistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReconciliationWorkflowHistoryRecord>());
        repository.ListRunsAsync(Arg.Any<ListReconciliationRunsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReconciliationRunRecord>());
        repository.ListExceptionsAsync(Arg.Any<ListReconciliationExceptionsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReconciliationExceptionRecord>());
        var sut = new ReconciliationWorkflowService(repository);

        await sut.ReadWorkflowHistoryAsync(new ReadReconciliationWorkflowHistoryQuery(ItemId), CancellationToken.None);
        await sut.ListRunsAsync(new ListReconciliationRunsQuery(200), CancellationToken.None);
        await sut.ListExceptionsAsync(new ListReconciliationExceptionsQuery(200, "open", "LOW", null), CancellationToken.None);

        await repository.Received(1).ReadWorkflowHistoryAsync(
            Arg.Is<ReadReconciliationWorkflowHistoryQuery>(query => query.ReconciliationItemId == ItemId),
            Arg.Any<CancellationToken>());
        await repository.Received(1).ListRunsAsync(
            Arg.Is<ListReconciliationRunsQuery>(query => query.Limit == 100),
            Arg.Any<CancellationToken>());
        await repository.Received(1).ListExceptionsAsync(
            Arg.Is<ListReconciliationExceptionsQuery>(query => query.Limit == 100 && query.Status == "open"),
            Arg.Any<CancellationToken>());
    }
}
