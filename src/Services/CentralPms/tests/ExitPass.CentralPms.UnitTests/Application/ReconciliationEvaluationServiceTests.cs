using ExitPass.CentralPms.Application.Reconciliation;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Tests conservative reconciliation item evaluation rules.
/// </summary>
public sealed class ReconciliationEvaluationServiceTests
{
    private static readonly Guid ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    private static readonly Guid SourceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");
    private static readonly Guid TargetId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4");
    private static readonly Guid CorrelationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5");

    /// <summary>
    /// Verifies schema-supported evidence combinations are classified conservatively.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvaluationCases))]
    public async Task Evaluate_WhenGivenSupportedEvidence_ClassifiesExpectedItem(
        ReconciliationItemRecord item,
        string expectedStatus,
        string expectedMatchStatus)
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.ReadItemAsync(ItemId, Arg.Any<CancellationToken>()).Returns(item);
        repository.SaveEvaluationAsync(
                Arg.Any<EvaluateReconciliationItemCommand>(),
                Arg.Any<ReconciliationEvaluationDecision>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var decision = call.ArgAt<ReconciliationEvaluationDecision>(1);
                return Evaluation(item, decision);
            });
        var sut = new ReconciliationEvaluationService(repository);

        var result = await sut.EvaluateAsync(
            new EvaluateReconciliationItemCommand(ItemId, null, null, CorrelationId),
            CancellationToken.None);

        result.ItemStatus.Should().Be(expectedStatus);
        result.MatchStatus.Should().Be(expectedMatchStatus);
        await repository.Received(1).SaveEvaluationAsync(
            Arg.Any<EvaluateReconciliationItemCommand>(),
            Arg.Is<ReconciliationEvaluationDecision>(decision =>
                decision.ItemStatus == expectedStatus &&
                decision.MatchStatus == expectedMatchStatus),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies current evaluation readback does not mutate the item.
    /// </summary>
    [Fact]
    public async Task ReadEvaluation_WhenKnown_ReturnsCurrentStateWithoutSaving()
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.ReadItemAsync(ItemId, Arg.Any<CancellationToken>())
            .Returns(Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: 100m, actual: 100m) with
            {
                ItemStatus = "MATCHED",
                MatchStatus = "MATCH",
                VarianceAmount = 0m
            });
        var sut = new ReconciliationEvaluationService(repository);

        var result = await sut.ReadEvaluationAsync(new ReadReconciliationItemEvaluationQuery(ItemId), CancellationToken.None);

        result.MatchStatus.Should().Be("MATCH");
        await repository.DidNotReceive().SaveEvaluationAsync(
            Arg.Any<EvaluateReconciliationItemCommand>(),
            Arg.Any<ReconciliationEvaluationDecision>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies unknown items flow through the repository as deterministic not-found errors.
    /// </summary>
    [Fact]
    public async Task Evaluate_WhenItemMissing_PropagatesNotFound()
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.ReadItemAsync(ItemId, Arg.Any<CancellationToken>())
            .Returns<Task<ReconciliationItemRecord>>(_ => throw new ReconciliationItemNotFoundException(ItemId));
        var sut = new ReconciliationEvaluationService(repository);

        var act = () => sut.EvaluateAsync(
            new EvaluateReconciliationItemCommand(ItemId, null, null, CorrelationId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ReconciliationItemNotFoundException>();
    }

    /// <summary>
    /// Verifies empty runs complete deterministically with zero counts.
    /// </summary>
    [Fact]
    public async Task EvaluateRun_WhenRunHasNoItems_ReturnsZeroSummary()
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.RunExistsAsync(RunId, Arg.Any<CancellationToken>()).Returns(true);
        repository.ListRunItemIdsAsync(RunId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        var sut = new ReconciliationEvaluationService(repository);

        var result = await sut.EvaluateRunAsync(
            new EvaluateReconciliationRunCommand(RunId, null, null, CorrelationId),
            CancellationToken.None);

        result.TotalItems.Should().Be(0);
        result.EvaluatedItems.Should().Be(0);
        result.SkippedItems.Should().Be(0);
        await repository.DidNotReceive().SaveEvaluationAsync(
            Arg.Any<EvaluateReconciliationItemCommand>(),
            Arg.Any<ReconciliationEvaluationDecision>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies run evaluation reuses item-level classification and returns summary counts.
    /// </summary>
    [Fact]
    public async Task EvaluateRun_WhenRunHasEligibleItems_ReturnsExpectedSummaryCounts()
    {
        var matchedItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa11");
        var mismatchedItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa12");
        var missingSourceItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa13");
        var inconclusiveItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaa14");
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.RunExistsAsync(RunId, Arg.Any<CancellationToken>()).Returns(true);
        var matchedItem = Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: 100m, actual: 100m, itemId: matchedItemId);
        var mismatchedItem = Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: 100m, actual: 110m, itemId: mismatchedItemId);
        var missingSourceItem = Item("PROVIDER_TO_CORE", providerOutcomeId: null, paymentConfirmationId: TargetId, expected: 100m, actual: 100m, itemId: missingSourceItemId);
        var inconclusiveItem = Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: null, actual: 100m, itemId: inconclusiveItemId);
        repository.ListRunItemsAsync(RunId, Arg.Any<CancellationToken>())
            .Returns(new[] { matchedItem, mismatchedItem, missingSourceItem, inconclusiveItem });
        repository.ReadItemAsync(matchedItemId, Arg.Any<CancellationToken>()).Returns(matchedItem);
        repository.ReadItemAsync(mismatchedItemId, Arg.Any<CancellationToken>()).Returns(mismatchedItem);
        repository.ReadItemAsync(missingSourceItemId, Arg.Any<CancellationToken>()).Returns(missingSourceItem);
        repository.ReadItemAsync(inconclusiveItemId, Arg.Any<CancellationToken>()).Returns(inconclusiveItem);
        repository.SaveEvaluationAsync(
                Arg.Any<EvaluateReconciliationItemCommand>(),
                Arg.Any<ReconciliationEvaluationDecision>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.ArgAt<EvaluateReconciliationItemCommand>(0);
                var decision = call.ArgAt<ReconciliationEvaluationDecision>(1);
                var item = Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, itemId: command.ReconciliationItemId);
                return Evaluation(item, decision);
            });
        var sut = new ReconciliationEvaluationService(repository);

        var result = await sut.EvaluateRunAsync(
            new EvaluateReconciliationRunCommand(RunId, null, null, CorrelationId),
            CancellationToken.None);

        result.TotalItems.Should().Be(4);
        result.EvaluatedItems.Should().Be(4);
        result.MatchedItems.Should().Be(1);
        result.MismatchedItems.Should().Be(1);
        result.MissingSourceItems.Should().Be(1);
        result.MissingTargetItems.Should().Be(0);
        result.InconclusiveItems.Should().Be(1);
        result.SkippedItems.Should().Be(0);
    }

    /// <summary>
    /// Verifies unknown runs return deterministic not-found errors.
    /// </summary>
    [Fact]
    public async Task EvaluateRun_WhenRunMissing_ThrowsRunNotFound()
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.RunExistsAsync(RunId, Arg.Any<CancellationToken>()).Returns(false);
        var sut = new ReconciliationEvaluationService(repository);

        var act = () => sut.EvaluateRunAsync(
            new EvaluateReconciliationRunCommand(RunId, null, null, CorrelationId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ReconciliationRunNotFoundException>();
    }

    /// <summary>
    /// Verifies duplicate run evaluation is deterministic for the same underlying item state.
    /// </summary>
    [Fact]
    public async Task EvaluateRun_WhenRepeated_ReturnsSameSummary()
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.RunExistsAsync(RunId, Arg.Any<CancellationToken>()).Returns(true);
        var item = Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: 100m, actual: 100m);
        repository.ListRunItemsAsync(RunId, Arg.Any<CancellationToken>()).Returns(new[] { item });
        repository.ReadItemAsync(ItemId, Arg.Any<CancellationToken>()).Returns(item);
        repository.SaveEvaluationAsync(
                Arg.Any<EvaluateReconciliationItemCommand>(),
                Arg.Any<ReconciliationEvaluationDecision>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Evaluation(Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId), call.ArgAt<ReconciliationEvaluationDecision>(1)));
        var sut = new ReconciliationEvaluationService(repository);

        var first = await sut.EvaluateRunAsync(new EvaluateReconciliationRunCommand(RunId, null, null, CorrelationId), CancellationToken.None);
        var second = await sut.EvaluateRunAsync(new EvaluateReconciliationRunCommand(RunId, null, null, CorrelationId), CancellationToken.None);

        second.Should().BeEquivalentTo(first);
    }

    /// <summary>
    /// Verifies run evaluation summary readback counts current item states.
    /// </summary>
    [Fact]
    public async Task ReadRunEvaluationSummary_WhenRunExists_ReturnsCurrentSummary()
    {
        var repository = Substitute.For<IReconciliationEvaluationRepository>();
        repository.RunExistsAsync(RunId, Arg.Any<CancellationToken>()).Returns(true);
        repository.ListRunItemsAsync(RunId, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId) with { ItemStatus = "MATCHED", MatchStatus = "MATCH", VarianceAmount = 0m },
                Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId) with { ItemStatus = "MISMATCHED", MatchStatus = "AMOUNT_MISMATCH", VarianceAmount = 5m },
                Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId) with { ItemStatus = "PENDING", MatchStatus = "NOT_EVALUATED" }
            });
        var sut = new ReconciliationEvaluationService(repository);

        var result = await sut.ReadRunEvaluationSummaryAsync(
            new ReadReconciliationRunEvaluationSummaryQuery(RunId),
            CancellationToken.None);

        result.TotalItems.Should().Be(3);
        result.EvaluatedItems.Should().Be(2);
        result.MatchedItems.Should().Be(1);
        result.MismatchedItems.Should().Be(1);
        result.SkippedItems.Should().Be(1);
    }

    /// <summary>
    /// Verifies evaluation SQL remains bounded to reconciliation item evidence fields.
    /// </summary>
    [Fact]
    public void RepositorySql_DoesNotMutatePaymentProviderOrExitTruth()
    {
        var repository = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "Reconciliation",
            "ReconciliationEvaluationRepository.cs");

        Assert.Contains("UPDATE reconciliation.reconciliation_items", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE reconciliation.reconciliation_runs", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE core.payment_attempts", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE core.payment_confirmations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO core.exit_authorizations", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE payments.provider_outcomes", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE gates.gate_authorization_consumptions", repository, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Evaluation test cases for conservative matching.
    /// </summary>
    public static IEnumerable<object[]> EvaluationCases()
    {
        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: null, paymentConfirmationId: TargetId, expected: 100m, actual: 100m),
            "EXCEPTION",
            "MISSING_SOURCE"
        ];

        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: null, expected: 100m, actual: 100m),
            "EXCEPTION",
            "MISSING_TARGET"
        ];

        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: 100m, actual: 100m),
            "MATCHED",
            "MATCH"
        ];

        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: 100m, actual: 110m),
            "MISMATCHED",
            "AMOUNT_MISMATCH"
        ];

        yield return
        [
            Item("PROVIDER_TO_CORE", providerOutcomeId: SourceId, paymentConfirmationId: TargetId, expected: null, actual: 110m),
            "PENDING",
            "INCONCLUSIVE"
        ];
    }

    private static ReconciliationItemEvaluationRecord Evaluation(
        ReconciliationItemRecord item,
        ReconciliationEvaluationDecision decision) =>
        new(
            item.ReconciliationItemId,
            item.ReconciliationRunId,
            item.ComparisonBasis,
            decision.ItemStatus,
            decision.MatchStatus,
            decision.EvaluationClassification,
            decision.EvaluationReason,
            item.ExpectedAmount,
            item.ActualAmount,
            decision.VarianceAmount,
            decision.ExceptionReasonCode,
            ExceptionCreatedOrUpdated: false,
            "deferred",
            DateTimeOffset.UtcNow,
            CorrelationId);

    private static ReconciliationItemRecord Item(
        string comparisonBasis,
        Guid? providerOutcomeId = null,
        Guid? paymentConfirmationId = null,
        decimal? expected = null,
        decimal? actual = null,
        Guid? itemId = null) =>
        new(
            itemId ?? ItemId,
            RunId,
            MopsTransactionRecordId: null,
            ManualGateLogId: null,
            PaymentAttemptId: null,
            PaymentConfirmationId: paymentConfirmationId,
            ProviderOutcomeId: providerOutcomeId,
            TargetEntityType: null,
            TargetEntityId: null,
            ComparisonBasis: comparisonBasis,
            ItemStatus: "PENDING",
            MatchStatus: "NOT_EVALUATED",
            ExpectedAmount: expected,
            ActualAmount: actual,
            CurrencyCode: "PHP",
            VarianceAmount: null,
            ExceptionReasonCode: null,
            ResolvedAt: null,
            ResolvedByUserId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            CorrelationId: null);

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
